using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ASAL
{
    internal sealed class MatchSession
    {
        private const int ChunkBytes = 8192;
        private const int MaxChunks = 128;
        private const float ProposalTimeout = 12f;
        private const float ResyncInterval = 3f;

        private readonly PhotonManager photon;
        private readonly RoomManager room;
        private readonly Dictionary<string, BuildingObjectBase> catalog;
        private readonly Dictionary<string, Transfer> transfers = new();
        private readonly Dictionary<string, MatchSnapshot> staged = new();
        private readonly HashSet<string> commands = new();
        private readonly Dictionary<int, float> snapshotRequests = new();
        private Proposal pending;
        private float nextResync;
        private string lastResyncProposal;

        internal MatchSnapshot Committed { get; private set; }
        internal event Action<MatchSnapshot> Applied;

        internal MatchSession(PhotonManager photon, RoomManager room)
        {
            this.photon = photon;
            this.room = room;
            catalog = MapManager.LoadBuildables();
            photon.MatchReceived += Receive;
        }

        internal void Dispose()
        {
            photon.MatchReceived -= Receive;
            pending?.Completion.TrySetCanceled();
            pending = null;
            transfers.Clear();
            staged.Clear();
            commands.Clear();
            snapshotRequests.Clear();
            Committed = null;
        }

        internal void Start(TilemapDataWrapper map)
        {
            if (!room.IsDm || pending != null)
                return;
            var snapshot = new MatchSnapshot
            {
                MatchId = photon.Session, Revision = 1, Map = map, MapRevision = map == null ? 0 : 1,
                Roster = room.Roster.Where(member => member.Active).Select(member => new MatchMember
                {
                    Actor = member.Actor, Name = member.Name,
                    RecordId = member.CharacterId, CharacterName = member.CharacterName
                }).ToArray(),
                Tokens = Array.Empty<MatchToken>()
            };
            try
            {
                Stage(snapshot, null);
            }
            catch (FormatException error)
            {
                room.StartStagingFailed(error.Message);
            }
        }

        internal Task PublishAsync(TilemapDataWrapper map)
        {
            if (!room.IsDm || Committed == null || pending != null || photon.MatchRevision != Committed.Revision)
                throw new FormatException("Wait for the current match update before publishing.");
            var next = Committed.Copy();
            next.Revision++;
            next.MapRevision++;
            next.Map = map;
            foreach (var token in next.Tokens)
                token.Placed = token.Placed && MatchSnapshot.IsWalkable(map, token.Cell);
            return Stage(next, null).Completion.Task;
        }

        internal void Submit(MatchCommand command, MatchToken enemy = null)
        {
            if (!room.CanLeave || Committed == null || photon.MatchRevision != Committed.Revision)
                return;
            command.MatchId = Committed.MatchId;
            command.ExpectedRevision = Committed.Revision;
            command.CommandId = Guid.NewGuid().ToString("N");
            if (room.IsDm)
                HandleCommand(photon.LocalActor, photon.LocalUser, command, enemy);
            else if (!photon.SendMatch(new[] { photon.DmActor }, "command", photon.Session, JsonUtility.ToJson(command)))
                room.SetMatchStatus("Could not send the token action. Try again.");
        }

        internal void Refresh()
        {
            if (!room.CanLeave)
                return;

            var revision = photon.MatchRevision;
            var proposal = photon.MatchProposal;
            var snapshot = FindCommitted(staged, Committed?.Revision ?? 0, revision, proposal);
            if (snapshot != null)
            {
                Committed = snapshot;
                staged.Clear();
                transfers.Clear();
                lastResyncProposal = null;
                if (pending != null && pending.Revision <= revision)
                {
                    var completed = pending;
                    pending = null;
                    if (completed.Id == proposal)
                        completed.Completion.TrySetResult(true);
                    else
                        completed.Completion.TrySetException(new FormatException("Another update committed first. Try again."));
                }
                Applied?.Invoke(snapshot);
            }
            TryCommit();

            if (!room.IsDm && revision > (Committed?.Revision ?? 0)
                && (proposal != lastResyncProposal || Time.unscaledTime >= nextResync))
            {
                lastResyncProposal = proposal;
                nextResync = Time.unscaledTime + ResyncInterval;
                photon.SendMatch(new[] { photon.DmActor }, "snapshot", photon.Session);
            }
        }

        private static MatchSnapshot FindCommitted(Dictionary<string, MatchSnapshot> candidates, int appliedRevision,
            int committedRevision, string proposal) =>
            committedRevision > appliedRevision && !string.IsNullOrEmpty(proposal)
                && candidates.TryGetValue(proposal, out var snapshot) && snapshot.Revision == committedRevision
                ? snapshot : null;

        internal void Tick()
        {
            Refresh();
            if (pending == null)
                return;
            if (Time.unscaledTime >= pending.Expires)
                FailProposal("Photon did not confirm the update. Try again.");
            else if (!pending.CommitSent && pending.Fallback != null && Time.unscaledTime >= pending.Expires - 9f)
            {
                // A peer missing the delta baseline can still stage the complete result.
                var full = pending.Fallback;
                pending.Fallback = null;
                Send(Peers(), pending.Id, pending.Revision, "full", full);
            }
        }

        private Proposal Stage(MatchSnapshot snapshot, MatchDelta delta)
        {
            Validate(snapshot);
            var full = Pack(JsonUtility.ToJson(snapshot));
            var data = delta == null ? full : Pack(JsonUtility.ToJson(delta));
            var id = Guid.NewGuid().ToString("N");
            staged.Add(id, snapshot);
            var proposal = new Proposal(id, snapshot.Revision, Time.unscaledTime + ProposalTimeout);
            // Only publication has a caller awaiting the result.
            proposal.Completion.Task.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            pending = proposal;
            if (delta != null)
                proposal.Fallback = full;
            if (!Send(Peers(), id, snapshot.Revision, delta == null ? "full" : "delta", data))
                FailProposal("Could not send the match update. Try again.");
            else
                TryCommit();
            return proposal;
        }

        private void TryCommit()
        {
            if (pending == null || pending.CommitSent || !room.IsDm || !room.CanLeave)
                return;
            var peers = Peers();
            if (peers.Length > 0 && !peers.Any(pending.Acknowledged.Contains))
                return;
            if (photon.CommitMatch(pending.Revision, pending.Id))
                pending.CommitSent = true;
            else
                FailProposal("Could not commit the match update. Try again.");
        }

        private int[] Peers() => room.Roster.Where(member => member.Active && member.Actor != photon.LocalActor)
            .Select(member => member.Actor).ToArray();

        private void FailProposal(string message)
        {
            var failed = pending;
            pending = null;
            // Retain the staged candidate if a commit request is already in flight.
            if (!failed.CommitSent)
                staged.Remove(failed.Id);
            failed.Completion.TrySetException(new FormatException(message));
            if (failed.Revision == 1)
                room.StartStagingFailed(message);
            else
                room.SetMatchStatus(message);
        }

        private void Receive(int actor, string user, object[] data)
        {
            if (!room.CanLeave || data.Length < 2 || data[0] is not string kind || data[1] is not string session
                || session != photon.Session)
                return;

            try
            {
                switch (kind)
                {
                    case "chunk" when room.IsDmIdentity(actor, user):
                        ReceiveChunk(data);
                        break;
                    case "ack" when room.IsDm && pending != null && data.Length == 4
                        && data[2] is string id && id == pending.Id && data[3] is int revision && revision == pending.Revision:
                        pending.Acknowledged.Add(actor);
                        TryCommit();
                        break;
                    case "snapshot" when room.IsDm && Committed != null && photon.MatchRevision == Committed.Revision:
                        if (snapshotRequests.TryGetValue(actor, out var deadline) && Time.unscaledTime < deadline)
                            break;
                        snapshotRequests[actor] = Time.unscaledTime + ResyncInterval - 0.25f;
                        Send(new[] { actor }, photon.MatchProposal, Committed.Revision, "full", Pack(JsonUtility.ToJson(Committed)));
                        break;
                    case "command" when room.IsDm && data.Length == 3 && data[2] is string json && json.Length <= 2048:
                        HandleCommand(actor, user, JsonUtility.FromJson<MatchCommand>(json), null);
                        break;
                    case "rejected" when room.IsDmIdentity(actor, user):
                        room.SetMatchStatus("Token action rejected. Check the destination or try again.");
                        break;
                }
            }
            catch (Exception error) when (error is FormatException or ArgumentException)
            {
                // Discard malformed transient messages. The commit check requests a fresh snapshot.
            }
        }

        private void HandleCommand(int actor, string user, MatchCommand command, MatchToken enemy)
        {
            bool dm = room.IsDmIdentity(actor, user);
            if (Committed == null || pending != null || photon.MatchRevision != Committed.Revision
                || !room.Roster.Any(member => member.Active && member.Actor == actor && member.UserId == user)
                || command == null || commands.Contains(command.CommandId))
            {
                Reject(actor);
                return;
            }

            if (!Committed.TryApply(command, actor, dm, out var token))
            {
                if (!dm || actor != photon.LocalActor || enemy == null || command.Action != MatchAction.Place
                    || command.TokenId != enemy.Id || command.RecordId != enemy.RecordId
                    || command.MatchId != Committed.MatchId || command.ExpectedRevision != Committed.Revision
                    || !Guid.TryParseExact(command.CommandId, "N", out _)
                    || !Guid.TryParseExact(enemy.Id, "N", out _) || Committed.Tokens.Any(item => item.Id == enemy.Id)
                    || !MatchSnapshot.IsWalkable(Committed.Map, command.Cell))
                {
                    Reject(actor);
                    return;
                }
                token = enemy.At(command.Cell);
            }
            commands.Add(command.CommandId);
            var delta = new MatchDelta { MatchId = Committed.MatchId, Revision = Committed.Revision + 1, Token = token };
            try { Stage(delta.Apply(Committed), delta); }
            catch (FormatException error) { room.SetMatchStatus(error.Message); }
        }

        private void Reject(int actor)
        {
            if (actor == photon.LocalActor)
                room.SetMatchStatus("Token action rejected. Check the destination or try again.");
            else
                photon.SendMatch(new[] { actor }, "rejected", photon.Session);
        }

        private static byte[] Pack(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length == 0 || bytes.Length > ChunkBytes * MaxChunks)
                throw new FormatException("This match update exceeds the transfer limit.");
            return bytes;
        }

        private bool Send(int[] peers, string id, int revision, string form, byte[] bytes)
        {
            if (peers.Length == 0)
                return true;

            int count = (bytes.Length + ChunkBytes - 1) / ChunkBytes;
            for (int index = 0; index < count; index++)
            {
                var chunk = bytes.AsSpan(index * ChunkBytes, Math.Min(ChunkBytes, bytes.Length - index * ChunkBytes)).ToArray();
                if (!photon.SendMatch(peers, "chunk", photon.Session, id, revision, form, index, count, chunk))
                    return false;
            }
            return true;
        }

        private void ReceiveChunk(object[] data)
        {
            if (data.Length != 8 || data[2] is not string id || !Guid.TryParseExact(id, "N", out _)
                || data[3] is not int revision || revision <= (Committed?.Revision ?? 0) || revision > photon.MatchRevision + 1
                || data[4] is not string form || form is not ("full" or "delta")
                || data[5] is not int index || data[6] is not int count || data[7] is not byte[] bytes
                || count < 1 || count > MaxChunks || index < 0 || index >= count || bytes.Length == 0
                || bytes.Length > ChunkBytes || (index < count - 1 && bytes.Length != ChunkBytes))
                return;

            var key = id + form;
            if (!transfers.TryGetValue(key, out var transfer))
            {
                if (transfers.Count >= 4)
                    transfers.Clear();
                transfer = new Transfer(revision, count);
                transfers.Add(key, transfer);
            }
            if (transfer.Revision != revision || transfer.Chunks.Length != count || !transfer.Add(index, bytes))
                return;

            transfers.Remove(key);
            var json = Encoding.UTF8.GetString(transfer.Chunks.SelectMany(chunk => chunk).ToArray());
            MatchSnapshot snapshot;
            if (form == "delta")
            {
                var delta = JsonUtility.FromJson<MatchDelta>(json);
                if (Committed == null || delta?.Token == null || revision != Committed.Revision + 1
                    || delta.MatchId != photon.Session || delta.Revision != revision)
                    return;
                snapshot = delta.Apply(Committed);
            }
            else
                snapshot = JsonUtility.FromJson<MatchSnapshot>(json);

            if (snapshot?.Revision != revision || snapshot.MatchId != photon.Session)
                return;
            Validate(snapshot);
            if (staged.Count >= 4)
                staged.Clear();
            staged[id] = snapshot;
            if (revision > photon.MatchRevision)
                photon.SendMatch(new[] { photon.DmActor }, "ack", photon.Session, id, revision);
            Refresh();
        }

        private void Validate(MatchSnapshot snapshot)
        {
            if (snapshot.Roster == null || snapshot.Tokens == null || snapshot.MapRevision < 0
                || snapshot.MapRevision > snapshot.Revision
                || snapshot.Roster.Length > 5 || snapshot.Roster.Any(member => member == null || member.Actor < 1)
                || snapshot.Roster.Select(member => member.Actor).Distinct().Count() != snapshot.Roster.Length
                || snapshot.Tokens.Any(token => token == null || string.IsNullOrEmpty(token.Id))
                || snapshot.Tokens.Select(token => token.Id).Distinct().Count() != snapshot.Tokens.Length)
                throw new FormatException("Invalid match snapshot.");

            if (snapshot.MapRevision == 0)
                snapshot.Map = null;
            else if (snapshot.Map?.tilemapDataList == null
                || snapshot.Map.tilemapDataList.Any(layer => layer == null || string.IsNullOrEmpty(layer.key)
                    || layer.tiles == null || layer.tiles.Any(tile => tile == null || tile.position.z != 0
                        || string.IsNullOrEmpty(tile.guidForBuildable)
                        || !catalog.TryGetValue(tile.guidForBuildable, out var item) || item is BuildingTool))
                || !snapshot.Map.tilemapDataList.Where(layer => layer.key == "Tilemap_Floor")
                    .SelectMany(layer => layer.tiles).Any(tile => MatchSnapshot.IsWalkable(snapshot.Map, tile.position)))
                throw new FormatException("A playable map needs a walkable floor and valid tiles.");

            if (snapshot.Tokens.Any(token => token.Placed && !MatchSnapshot.IsWalkable(snapshot.Map, token.Cell)))
                throw new FormatException("A token is outside the playable map.");
        }

        private sealed class Proposal
        {
            internal readonly string Id;
            internal readonly int Revision;
            internal readonly float Expires;
            internal readonly HashSet<int> Acknowledged = new();
            internal readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool CommitSent;
            internal byte[] Fallback;

            internal Proposal(string id, int revision, float expires)
            {
                Id = id;
                Revision = revision;
                Expires = expires;
            }
        }

        private sealed class Transfer
        {
            internal readonly int Revision;
            internal readonly byte[][] Chunks;
            private int remaining;

            internal Transfer(int revision, int count)
            {
                Revision = revision;
                remaining = count;
                Chunks = new byte[count][];
            }

            internal bool Add(int index, byte[] bytes)
            {
                if (Chunks[index] != null)
                    return false;
                Chunks[index] = bytes;
                return --remaining == 0;
            }
        }
    }

}
