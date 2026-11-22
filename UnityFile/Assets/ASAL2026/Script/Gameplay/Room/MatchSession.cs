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
        private const int MaxMatchMessageLength = 8192;
        private const int MaxCombatMessageLength = 128 * 1024;
        private const float StartTimeout = 12f;
        private const float ResyncInterval = 3f;
        private const float DragInterval = 0.05f;
        private const float DragTimeout = 3f;
        private const string Rejected = "Match action rejected. Check the current turn, target, or destination.";

        private readonly PhotonManager photon;
        private readonly RoomManager room;
        private readonly Dictionary<string, BuildingObjectBase> catalog;
        private readonly Dictionary<int, (string RecordId, MatchCharacter Character)> characters = new();
        private readonly Dictionary<string, Drag> drags = new();
        private readonly Dictionary<int, float> mapRequests = new();
        private readonly HashSet<string> commands = new();
        private readonly Dictionary<string, int> deferredHealth = new();
        private readonly List<MatchLogEntry> publicLog = new();
        private MatchMember[] starting;
        private MatchMember[] members;
        private CombatEncounter encounter;
        private TilemapDataWrapper map;
        private int mapRevision;
        private int combatRevision;
        private float startDeadline;
        private float nextMapRequest;
        private bool playRequested;
        private bool dirty;
        private Transfer mapTransfer;
        private string mapTransferId;
        private string localRecordId;
        private MatchCharacter localCharacter;
        private float nextProjection;
        private bool projectionLoading;
        private bool projectionInvalid;
        private string localDragId;
        private string localDragToken;
        private float nextDrag;
        private int dragStep;
        private bool disposed;

        internal MatchSnapshot Committed { get; private set; }
        internal bool CanSubmit => room.CanLeave && photon.Playing && Committed != null;
        internal event Action<MatchSnapshot> Applied;
        internal event Action<AttackFeedback, int> AttackResolved;

        internal MatchSession(PhotonManager photon, RoomManager room)
        {
            this.photon = photon;
            this.room = room;
            catalog = MapManager.LoadBuildables();
            photon.MatchReceived += Receive;
            photon.TokenViewsChanged += OnTokenViewsChanged;
        }

        internal void Dispose()
        {
            disposed = true;
            photon.MatchReceived -= Receive;
            photon.TokenViewsChanged -= OnTokenViewsChanged;
            Committed = null;
        }

        internal void Start(TilemapDataWrapper initialMap)
        {
            if (!room.IsDm || room.Phase != RoomPhase.Starting)
                return;
            try
            {
                if (initialMap != null)
                    ValidateMap(initialMap);
                map = initialMap;
                mapRevision = map == null ? 0 : 1;
                starting = ReadMembers();
                startDeadline = Time.unscaledTime + StartTimeout;
                room.SetMatchStatus("Loading player characters…");
                TryStartPlay();
            }
            catch (FormatException error)
            {
                FailStart(error.Message);
            }
        }

        private MatchMember[] ReadMembers() => room.Roster.Where(member => member.Active).OrderBy(member => member.Actor)
            .Select(member => new MatchMember
            {
                Actor = member.Actor, Name = member.Name, RecordId = member.CharacterId, CharacterName = member.CharacterName
            }).ToArray();

        private void TryStartPlay()
        {
            if (starting == null || room.Phase != RoomPhase.Starting || !room.CanLeave || photon.Playing)
                return;
            if (Time.unscaledTime >= startDeadline)
            {
                FailStart(playRequested ? "Photon did not confirm the match start."
                    : "Could not load every player character. Check the selected characters and try again.");
                return;
            }
            if (playRequested)
                return;

            var players = starting.Where(member => member.Actor != room.DmActor).ToArray();
            if (players.Any(member => !characters.TryGetValue(member.Actor, out var saved)
                || saved.RecordId != member.RecordId || saved.Character.Name != member.CharacterName
                || !room.Roster.Any(player => player.Actor == member.Actor && player.Active && player.Ready
                    && player.CharacterId == member.RecordId && player.CharacterName == member.CharacterName)))
                return;

            try
            {
                var mapBytes = map == null ? null : PackMap(map);
                var board = UIManager.Instance.EnsurePlayBoard(map, mapRevision);
                var center = new MatchSnapshot { Map = map }.BoardCenter();
                foreach (var member in players)
                {
                    var character = characters[member.Actor].Character;
                    Vector2 position = FindOpenPosition(board, center);
                    var token = new MatchToken
                    {
                        Id = MatchSnapshot.PlayerTokenId(member.Actor), OwnerActor = member.Actor,
                        RecordId = member.RecordId, Name = character.Name, Character = character,
                        Hp = character.MaxHp, Placed = true, Position = position
                    };
                    if (!photon.SpawnToken(token, board.ToWorld(position)))
                        throw new FormatException("Could not spawn a player token. Try starting again.");
                }
                if (mapBytes != null && !SendMap(Peers(), mapRevision, mapBytes))
                    throw new FormatException("Could not send the starting map. Try starting again.");
                if (!photon.PublishPlay(mapRevision))
                    throw new FormatException("Could not start the match. Try again.");
                members = starting;
                playRequested = true;
            }
            catch (FormatException error)
            {
                FailStart(error.Message);
            }
        }

        private void FailStart(string message)
        {
            starting = null;
            foreach (var id in photon.TokenViews.Keys.ToArray())
                photon.DespawnToken(id);
            room.StartStagingFailed(message);
        }

        internal Task PublishAsync(TilemapDataWrapper nextMap)
        {
            if (!room.IsDm || !CanSubmit)
                throw new FormatException("Wait for the match to start before publishing a map.");

            ValidateMap(nextMap);
            var bytes = PackMap(nextMap);
            int nextRevision = mapRevision + 1;
            var board = UIManager.Instance.EnsurePlayBoard(nextMap, nextRevision);
            var occupied = photon.TokenViews.Values.OrderBy(view => view.Token.Id, StringComparer.Ordinal).ToArray();
            var relocations = new List<(TokenView View, Vector2 Original)>();

            try
            {
                foreach (var view in occupied)
                {
                    var token = view.Token;
                    if (board.IsOpen(token.Position, token.Id))
                        continue;
                    Vector2 point = FindOpenPosition(board, token.Position);
                    relocations.Add((view, token.Position));
                    view.SetBoardPosition(board.ToWorld(point), point);
                }
                if (!SendMap(Peers(), nextRevision, bytes))
                    throw new FormatException("Could not send the map. Try again.");
            }
            catch (FormatException)
            {
                UIManager.Instance.EnsurePlayBoard(map, mapRevision);
                foreach (var (view, original) in relocations)
                    view.SetBoardPosition(board.ToWorld(original), original);
                throw;
            }

            map = nextMap;
            mapRevision = nextRevision;
            dirty = true;
            Refresh();
            return Task.CompletedTask;
        }

        private static Vector2 FindOpenPosition(PlayPanel board, Vector2 center)
        {
            if (board.IsOpen(center))
                return center;
            for (int ring = 1; ring <= 256; ring++)
            {
                for (int offset = -ring; offset <= ring; offset++)
                {
                    var lower = center + new Vector2(offset, -ring);
                    if (board.IsOpen(lower))
                        return lower;
                    var upper = center + new Vector2(offset, ring);
                    if (board.IsOpen(upper))
                        return upper;
                    if (Math.Abs(offset) == ring)
                        continue;
                    var left = center + new Vector2(-ring, offset);
                    if (board.IsOpen(left))
                        return left;
                    var right = center + new Vector2(ring, offset);
                    if (board.IsOpen(right))
                        return right;
                }
            }
            throw new FormatException("There is no clear space near the token.");
        }

        private void OnTokenViewsChanged() => dirty = true;

        internal void Refresh()
        {
            if (!room.CanLeave || !photon.Playing || mapRevision < photon.InitialMapRevision)
                return;
            if (Committed == null)
            {
                members ??= ReadMembers();
                if (room.Roster.Any(member => member.Active && member.Actor != room.DmActor
                    && !photon.TokenViews.ContainsKey(MatchSnapshot.PlayerTokenId(member.Actor))))
                    return;
                starting = null;
            }
            else if (!dirty)
                return;

            dirty = false;
            foreach (var item in deferredHealth.ToArray())
            {
                if (photon.TokenViews.TryGetValue(item.Key, out var view))
                {
                    if (item.Value <= view.Token.Character.MaxHp)
                        view.UpdateHealth(item.Value);
                    deferredHealth.Remove(item.Key);
                }
            }
            Committed = new MatchSnapshot
            {
                MatchId = room.Session, Revision = combatRevision, MapRevision = mapRevision, Map = map,
                Roster = members, Tokens = photon.TokenViews.Values.Select(view => view.Token).ToArray(),
                Encounter = encounter, PublicLog = publicLog.ToArray()
            };
            Applied?.Invoke(Committed);
        }

        internal void Tick()
        {
            if (room.Phase == RoomPhase.Setup)
            {
                starting = members = null;
                map = null;
                mapRevision = 0;
                mapTransfer = null;
                mapTransferId = null;
                playRequested = false;
                characters.Clear();
                localRecordId = null;
                return;
            }
            ShareLocalCharacter();
            TryStartPlay();
            if (!room.IsDm && photon.Playing && mapRevision < photon.InitialMapRevision && Time.unscaledTime >= nextMapRequest)
            {
                nextMapRequest = Time.unscaledTime + ResyncInterval;
                photon.SendMatch(new[] { room.DmActor }, "map-request", room.Session);
            }
            Refresh();

            foreach (var drag in drags.ToArray())
            {
                if (Time.unscaledTime >= drag.Value.Expires || !photon.TokenViews.ContainsKey(drag.Key)
                    || !room.Roster.Any(member => member.Active && member.Actor == drag.Value.Actor))
                    drags.Remove(drag.Key);
            }
        }

        private void ShareLocalCharacter()
        {
            if (!room.CanLeave || room.IsDm || room.Phase != RoomPhase.Starting || !room.LocalReady
                || string.IsNullOrEmpty(room.SelectedCharacterId))
                return;

            if (localRecordId != room.SelectedCharacterId)
            {
                localRecordId = room.SelectedCharacterId;
                localCharacter = null;
                projectionInvalid = false;
                nextProjection = 0;
            }
            if (projectionLoading || Time.unscaledTime < nextProjection)
                return;
            if (localCharacter == null && !projectionInvalid)
                LoadLocalCharacter(localRecordId, AuthManager.Instance.CurrentAccount);
            else if (room.DmActor > 0)
            {
                var body = projectionInvalid ? string.Empty : JsonUtility.ToJson(localCharacter);
                photon.SendMatch(new[] { room.DmActor }, "character", room.Session, localRecordId,
                    body.Length <= MaxMatchMessageLength ? body : string.Empty);
                nextProjection = Time.unscaledTime + ResyncInterval;
            }
        }

        private async void LoadLocalCharacter(string recordId, AccountProfile account)
        {
            projectionLoading = true;
            try
            {
                var records = await CharacterManager.Instance.LoadAsync(account, false);
                if (IsCurrentProjection(recordId, account))
                {
                    var record = Array.Find(records, candidate => candidate?.RecordId == recordId);
                    localCharacter = CharacterManager.Instance.Project(record);
                    projectionInvalid = localCharacter == null;
                    if (projectionInvalid)
                        room.SetMatchStatus(record?.RepairError
                            ?? "The selected character has invalid or missing gameplay data. Select a saved character again.");
                }
            }
            catch (Exception)
            {
                if (IsCurrentProjection(recordId, account))
                    room.SetMatchStatus("Could not load the selected character for play. Retrying.");
            }
            finally
            {
                projectionLoading = false;
                if (IsCurrentProjection(recordId, account) && localCharacter == null && !projectionInvalid)
                    nextProjection = Time.unscaledTime + ResyncInterval;
            }
        }

        private bool IsCurrentProjection(string recordId, AccountProfile account) => !disposed
            && room.Phase == RoomPhase.Starting && recordId == localRecordId
            && ReferenceEquals(account, AuthManager.Instance?.CurrentAccount);


        internal void Submit(MatchCommand command, MatchToken enemy = null)
        {
            if (!CanSubmit)
            {
                room.SetMatchStatus("Wait for the match to start.");
                return;
            }
            command.MatchId = room.Session;
            command.ExpectedRevision = combatRevision;
            command.CommandId = Guid.NewGuid().ToString("N");
            if (room.IsDm)
                HandleCommand(photon.LocalActor, photon.LocalUser, command, enemy);
            else if (!photon.SendMatch(new[] { room.DmActor }, "command", room.Session, JsonUtility.ToJson(command)))
                room.SetMatchStatus("Could not send the match action. Try again.");
        }

        internal void BeginDrag(string id)
        {
            if (!CanSubmit)
                return;
            localDragToken = id;
            localDragId = Guid.NewGuid().ToString("N");
            nextDrag = 0;
            dragStep = 0;
            SendDrag("drag-begin", id, Vector2.zero);
        }

        internal void DragTo(string id, Vector2 point)
        {
            if (id != localDragToken || (!room.IsDm && Time.unscaledTime < nextDrag))
                return;
            nextDrag = Time.unscaledTime + DragInterval;
            SendDrag("drag-move", id, point);
        }

        internal void EndDrag(string id, Vector2 point)
        {
            FinishDrag("drag-end", id, point);
        }

        internal void CancelDrag(string id)
        {
            FinishDrag("drag-cancel", id, Vector2.zero);
        }

        private void FinishDrag(string kind, string id, Vector2 point)
        {
            if (id != localDragToken)
                return;
            SendDrag(kind, id, point);
            localDragToken = localDragId = null;
        }

        private void SendDrag(string kind, string id, Vector2 point)
        {
            if (!CanSubmit)
                return;
            int step = dragStep++;
            if (room.IsDm)
            {
                HandleDrag(photon.LocalActor, photon.LocalUser, kind, id, localDragId, step, point);
                return;
            }
            var payload = new object[] { kind, room.Session, id, localDragId, step, point };
            if (kind == "drag-move")
                photon.SendDrag(room.DmActor, payload);
            else if (!photon.SendMatch(new[] { room.DmActor }, payload))
                room.SetMatchStatus("Could not send the token movement.");
        }

        internal static bool CanDragToken(int actor, bool dm, int ownerActor) =>
            actor > 0 && (dm || ownerActor == actor);

        private void HandleDrag(int actor, string user, string kind, string id, string dragId, int step, Vector2 point)
        {
            bool dm = room.IsDmIdentity(actor, user);
            if (!CanSubmit || !IsActive(actor, user) || !Guid.TryParseExact(dragId, "N", out _)
                || !photon.TokenViews.TryGetValue(id, out var view) || !CanDragToken(actor, dm, view.Token.OwnerActor))
                return;
            if (kind == "drag-begin")
            {
                if (step == 0 && (dm || !drags.TryGetValue(id, out var existing) || existing.Actor == actor))
                    drags[id] = new Drag(actor, dragId, step, Time.unscaledTime + DragTimeout);
                return;
            }
            if (!drags.TryGetValue(id, out var drag) || drag.Actor != actor || drag.Id != dragId || step <= drag.Step)
                return;

            if (kind is "drag-end" or "drag-cancel")
                drags.Remove(id);
            else
                drags[id] = new Drag(actor, dragId, step, Time.unscaledTime + DragTimeout);

            if (kind is "drag-move" or "drag-end")
                UIManager.Instance.EnsurePlayBoard(map, mapRevision).TryMoveToken(id, point, out _);
        }

        private bool IsActive(int actor, string user) =>
            room.Roster.Any(member => member.Active && member.Actor == actor && member.UserId == user);

        private void Receive(int actor, string user, object[] data)
        {
            if (!room.CanLeave || data.Length < 2 || data[0] is not string kind || data[1] is not string session
                || session != room.Session)
                return;

            bool dm = room.IsDmIdentity(actor, user);
            try
            {
                switch (kind)
                {
                    case "map" when dm:
                        ReceiveMap(data);
                        break;
                    case "map-request" when room.IsDm && CanSubmit && map != null && IsActive(actor, user):
                        if (!mapRequests.TryGetValue(actor, out var deadline) || Time.unscaledTime >= deadline)
                        {
                            mapRequests[actor] = Time.unscaledTime + ResyncInterval;
                            SendMap(new[] { actor }, mapRevision, PackMap(map));
                        }
                        break;
                    case "combat" when dm && data.Length == 3 && data[2] is string combat
                        && combat.Length <= MaxCombatMessageLength:
                        ApplyCombat(JsonUtility.FromJson<CombatUpdate>(combat));
                        break;
                    case "command" when room.IsDm && data.Length == 3 && data[2] is string json
                        && json.Length <= MaxMatchMessageLength:
                        HandleCommand(actor, user, JsonUtility.FromJson<MatchCommand>(json), null);
                        break;
                    case "character" when room.IsDm && room.Phase == RoomPhase.Starting && data.Length == 4
                        && data[2] is string recordId && data[3] is string body && body.Length <= MaxMatchMessageLength:
                        ReceiveCharacter(actor, user, recordId, body.Length == 0 ? null : JsonUtility.FromJson<MatchCharacter>(body));
                        break;
                    case "drag-begin" or "drag-move" or "drag-end" or "drag-cancel" when room.IsDm
                        && data.Length == 6 && data[2] is string id && data[3] is string dragId
                        && data[4] is int step && data[5] is Vector2 point:
                        HandleDrag(actor, user, kind, id, dragId, step, point);
                        break;
                    case "rejected" when dm:
                        room.SetMatchStatus(Rejected);
                        break;
                }
            }
            catch (Exception error) when (error is FormatException or ArgumentException)
            {
                // Drop malformed room messages without applying partial state.
            }
        }

        private void ReceiveCharacter(int actor, string user, string recordId, MatchCharacter character)
        {
            var member = room.Roster.FirstOrDefault(player => player.Active && player.Ready && player.Actor == actor
                && player.UserId == user && player.CharacterId == recordId);
            if (starting == null || member == null || room.IsDmIdentity(actor, user))
                return;
            if (member.CharacterName != character?.Name || !CharacterManager.Instance.ValidProjection(character))
            {
                starting = null;
                room.StartStagingFailed($"Could not load {member.Name}'s selected character. Ask them to select a saved character again.");
                return;
            }
            characters[actor] = (recordId, character);
            TryStartPlay();
        }


        private void HandleCommand(int actor, string user, MatchCommand command, MatchToken enemy)
        {
            if (!CanSubmit || !IsActive(actor, user) || command == null || command.MatchId != room.Session
                || command.ExpectedRevision != combatRevision || !Guid.TryParseExact(command.CommandId, "N", out _)
                || commands.Contains(command.CommandId))
            {
                Reject(actor);
                return;
            }

            Refresh();
            bool dm = room.IsDmIdentity(actor, user);
            MatchLogEntry privateReport = null;
            MatchDelta delta;
            if (command.Action == MatchAction.Place)
            {
                if (!dm || actor != photon.LocalActor || enemy == null || enemy.OwnerActor != 0
                    || command.TokenId != enemy.Id || command.RecordId != enemy.RecordId
                    || !Guid.TryParseExact(enemy.Id, "N", out _) || photon.TokenViews.ContainsKey(enemy.Id)
                    || !CharacterManager.Instance.ValidProjection(enemy.Character)
                    || !UIManager.Instance.EnsurePlayBoard(map, mapRevision).IsOpen(command.Position))
                {
                    Reject(actor);
                    return;
                }
                enemy = enemy.At(command.Position);
                enemy.Hp = enemy.Character.MaxHp;
                delta = new MatchDelta { Token = enemy };
                CombatManager.Instance.ResolvePlacement(Committed, command, delta);
            }
            else if (!CombatManager.Instance.TryResolve(Committed, command, actor, dm, out delta, out privateReport))
            {
                Reject(actor);
                return;
            }

            bool placing = command.Action == MatchAction.Place;
            var health = delta.ChangedTokens ?? Array.Empty<MatchToken>();
            if (!placing && delta.Token != null)
                health = health.Append(delta.Token).ToArray();

            bool hasCombatUpdate = delta.EncounterChanged || delta.Log != null || health.Length > 0;
            var update = hasCombatUpdate ? new CombatUpdate
            {
                Revision = combatRevision + 1, EncounterChanged = delta.EncounterChanged, Encounter = delta.Encounter,
                Health = health.Select(token => new HealthChange { Id = token.Id, Hp = token.Hp }).ToArray(),
                Log = delta.Log, Attack = delta.Attack
            } : null;
            var payload = update == null ? null : JsonUtility.ToJson(update);
            if (payload?.Length > MaxCombatMessageLength)
            {
                Reject(actor);
                return;
            }

            if (placing && !photon.SpawnToken(enemy, UIManager.Instance.EnsurePlayBoard(map, mapRevision).ToWorld(command.Position)))
            {
                Reject(actor);
                return;
            }
            if (update != null && !photon.SendMatch(Peers(), "combat", room.Session, payload))
            {
                if (placing)
                    photon.DespawnToken(enemy.Id);
                room.SetMatchStatus("Could not send the combat update. Try again.");
                return;
            }
            commands.Add(command.CommandId);
            if (delta.RemovedTokenId != null)
                photon.DespawnToken(delta.RemovedTokenId);
            if (update != null)
                ApplyCombat(update);
            Refresh();
            if (privateReport != null)
                DiceManager.Instance.ReportPrivate(privateReport.Text, privateReport.Roll);
        }

        private void ApplyCombat(CombatUpdate update)
        {
            if (update?.Revision != combatRevision + 1)
                return;
            var nextEncounter = update.Encounter?.Round > 0 ? update.Encounter : null;
            var log = string.IsNullOrEmpty(update.Log?.Id) ? null : update.Log;
            if (nextEncounter != null && !IsValidEncounter(nextEncounter)
                || log != null && (!Guid.TryParseExact(log.Id, "N", out _) || string.IsNullOrEmpty(log.Text)
                    || !ValidRoll(log.Roll, log.Id))
                || update.Health?.Any(change => change == null || string.IsNullOrEmpty(change.Id) || change.Hp < 0
                    || photon.TokenViews.TryGetValue(change.Id, out var view) && change.Hp > view.Token.Character.MaxHp) == true)
                return;

            combatRevision = update.Revision;
            foreach (var change in update.Health ?? Array.Empty<HealthChange>())
            {
                if (photon.TokenViews.TryGetValue(change.Id, out var view))
                    view.UpdateHealth(change.Hp);
                else
                    deferredHealth[change.Id] = change.Hp;
            }
            if (update.EncounterChanged)
            {
                if (nextEncounter != null && string.IsNullOrEmpty(nextEncounter.ActiveTokenId))
                    nextEncounter.ActiveTokenId = null;
                encounter = nextEncounter;
            }
            if (log != null)
            {
                if (string.IsNullOrEmpty(log.Roll?.Id))
                    log.Roll = null;
                if (publicLog.Count >= 40)
                    publicLog.RemoveAt(0);
                publicLog.Add(log);
            }
            dirty = true;
            if (!string.IsNullOrEmpty(update.Attack?.AttackerId) && update.Attack.Targets is { Length: > 0 })
                AttackResolved?.Invoke(update.Attack, update.Revision);
        }

        private static bool IsValidEncounter(CombatEncounter value) =>
            value.TurnOrder != null && value.TurnOrder.All(entry => entry != null
                && !string.IsNullOrEmpty(entry.TokenId) && entry.Hp >= 0 && entry.Initiative is >= 1 and <= 20
                && entry.ActionsLeft is >= 0 and <= 1 && entry.Statuses != null
                && entry.Statuses.All(status => status != null && status.Turns >= -1
                    && CharacterManager.Instance.Catalog.Status(new StatusId(status.StatusId)) != null)
                && entry.Statuses.Select(status => status.StatusId).Distinct().Count() == entry.Statuses.Length
                && ValidFollowUps(entry))
            && value.TurnOrder.Select(entry => entry.TokenId).Distinct().Count() == value.TurnOrder.Length
            && (string.IsNullOrEmpty(value.ActiveTokenId) || value.Find(value.ActiveTokenId)?.Hp > 0);

        private static bool ValidFollowUps(Combatant entry)
        {
            var targets = entry.FollowUpTargets;
            if (targets == null)
                return false;
            if (targets.Length == 0)
                return string.IsNullOrEmpty(entry.FollowUpSkillId);

            var skill = CharacterManager.Instance.Catalog.Skill(new SkillId(entry.FollowUpSkillId));
            return skill != null && skill.Action.MaxDistinctTargets > 1 && targets.Length <= skill.Action.MaxDistinctTargets
                && targets.All(id => !string.IsNullOrEmpty(id)) && targets.Distinct().Count() == targets.Length;
        }

        private static bool ValidRoll(MatchRoll roll, string id) => roll == null || string.IsNullOrEmpty(roll.Id)
            || roll.Id == id && roll.Sides is { Length: > 0 } && roll.Faces != null
            && roll.Sides.Length == roll.Faces.Length
            && roll.Sides.Zip(roll.Faces, (sides, face) => DiceManager.Instance.Supports(sides)
                && face >= 1 && face <= sides).All(valid => valid)
            && roll.Faces.Sum() == roll.Total;

        private void Reject(int actor)
        {
            if (actor == photon.LocalActor)
                room.SetMatchStatus(Rejected);
            else
                photon.SendMatch(new[] { actor }, "rejected", room.Session);
        }

        private int[] Peers() => room.Roster.Where(member => member.Active && member.Actor != photon.LocalActor)
            .Select(member => member.Actor).ToArray();

        private static byte[] PackMap(TilemapDataWrapper nextMap)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(nextMap));
            if (bytes.Length == 0 || bytes.Length > ChunkBytes * MaxChunks)
                throw new FormatException("The map exceeds the transfer limit.");
            return bytes;
        }

        private bool SendMap(int[] peers, int revision, byte[] bytes)
        {
            if (peers.Length == 0)
                return true;
            var id = Guid.NewGuid().ToString("N");
            int count = (bytes.Length + ChunkBytes - 1) / ChunkBytes;
            for (int index = 0; index < count; index++)
            {
                var chunk = bytes.AsSpan(index * ChunkBytes, Math.Min(ChunkBytes, bytes.Length - index * ChunkBytes)).ToArray();
                if (!photon.SendMatch(peers, "map", room.Session, id, revision, index, count, chunk))
                    return false;
            }
            return true;
        }

        private void ReceiveMap(object[] data)
        {
            if (room.Phase == RoomPhase.Setup || data.Length != 7 || data[2] is not string id
                || !Guid.TryParseExact(id, "N", out _) || data[3] is not int revision || revision <= mapRevision
                || data[4] is not int index || data[5] is not int count || data[6] is not byte[] bytes
                || count < 1 || count > MaxChunks || index < 0 || index >= count
                || bytes.Length == 0 || bytes.Length > ChunkBytes || index < count - 1 && bytes.Length != ChunkBytes)
                return;
            if (mapTransferId != id)
            {
                if (mapTransfer != null && mapTransfer.Revision > revision)
                    return;
                mapTransfer = new Transfer(revision, count);
                mapTransferId = id;
            }
            var transfer = mapTransfer;
            if (transfer.Revision != revision || transfer.Chunks.Length != count || !transfer.Add(index, bytes))
                return;
            mapTransfer = null;
            mapTransferId = null;

            var nextMap = JsonUtility.FromJson<TilemapDataWrapper>(
                Encoding.UTF8.GetString(transfer.Chunks.SelectMany(chunk => chunk).ToArray()));
            ValidateMap(nextMap);
            mapRevision = revision;
            map = nextMap;
            dirty = true;
        }

        private void ValidateMap(TilemapDataWrapper nextMap)
        {
            if (nextMap?.tilemapDataList == null || nextMap.tilemapDataList.Any(layer => layer == null
                || string.IsNullOrEmpty(layer.key) || layer.tiles == null || layer.tiles.Any(tile => tile == null
                    || tile.position.z != 0 || string.IsNullOrEmpty(tile.guidForBuildable)
                    || !catalog.TryGetValue(tile.guidForBuildable, out var item) || item is BuildingTool)))
                throw new FormatException("The map contains invalid tiles.");
        }

        [Serializable]
        private sealed class CombatUpdate
        {
            public int Revision;
            public HealthChange[] Health;
            public bool EncounterChanged;
            public CombatEncounter Encounter;
            public MatchLogEntry Log;
            public AttackFeedback Attack;
        }

        [Serializable]
        private sealed class HealthChange
        {
            public string Id;
            public int Hp;
        }

        private readonly struct Drag
        {
            internal readonly int Actor;
            internal readonly string Id;
            internal readonly int Step;
            internal readonly float Expires;

            internal Drag(int actor, string id, int step, float expires)
            {
                Actor = actor;
                Id = id;
                Step = step;
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
