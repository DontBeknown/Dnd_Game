using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ASAL
{
    public sealed class ChatManager : MonoBehaviour
    {
        private const int HistoryLimit = 100;
        private const int MessageLimit = 500;
        private readonly List<string> history = new();
        private readonly HashSet<string> appliedLogEntries = new(StringComparer.Ordinal);
        private PhotonManager photon;
        private RoomManager room;
        private string matchId;

        public static ChatManager Instance { get; private set; }
        internal event Action Changed;
        internal IReadOnlyList<string> History => history;

        internal void Register()
        {
            if (Instance)
                throw new InvalidOperationException("Duplicate ChatManager.");
            Instance = this;
        }

        internal void Unregister() => Instance = null;

        internal void Initialize()
        {
            photon = PhotonManager.Instance;
            room = RoomManager.Instance;
            photon.MatchReceived += Receive;
            room.Changed += RefreshRoom;
            room.MatchApplied += Apply;
        }

        internal void Shutdown()
        {
            photon.MatchReceived -= Receive;
            room.Changed -= RefreshRoom;
            room.MatchApplied -= Apply;
            ClearRoom();
        }

        internal bool Send(string input)
        {
            RefreshRoom();
            var message = Normalize(input);
            if (matchId == null || message == null)
                return false;

            var sender = room.Roster.FirstOrDefault(member => member.Active && member.Actor == room.LocalActor);
            if (sender == null || !photon.SendMatch(room.Roster.Where(member => member.Active && member.Actor != sender.Actor)
                    .Select(member => member.Actor).ToArray(), "chat", matchId, message))
                return false;

            Append(sender.Name + ": " + message);
            return true;
        }

        internal void AddPrivate(string entry)
        {
            RefreshRoom();
            if (matchId != null && room.IsDm)
                Append("[DM only] " + entry);
        }

        private void Receive(int actor, string user, object[] data)
        {
            if (data.Length != 3 || data[0] as string != "chat")
                return;
            RefreshRoom();
            if (matchId == null || data[1] as string != matchId || Normalize(data[2] as string) is not string message)
                return;
            var sender = room.Roster.FirstOrDefault(member => member.Active && member.Actor == actor && member.UserId == user);
            if (sender != null && actor != room.LocalActor)
                Append(sender.Name + ": " + message);
        }

        private void Apply(MatchSnapshot snapshot)
        {
            RefreshRoom();
            if (matchId != snapshot.MatchId || snapshot.PublicLog == null)
                return;

            foreach (var entry in snapshot.PublicLog)
                if (appliedLogEntries.Add(entry.Id))
                    Append(entry.Text);
            appliedLogEntries.IntersectWith(snapshot.PublicLog.Select(entry => entry.Id));
        }

        private void RefreshRoom()
        {
            var current = room.Phase == RoomPhase.Playing ? room.Session : null;
            if (current == matchId)
                return;
            ClearRoom();
            matchId = current;
        }

        private void ClearRoom()
        {
            matchId = null;
            history.Clear();
            appliedLogEntries.Clear();
            Changed?.Invoke();
        }

        private void Append(string entry)
        {
            if (history.Count == HistoryLimit)
                history.RemoveAt(0);
            history.Add(entry);
            Changed?.Invoke();
        }

        private static string Normalize(string text)
        {
            var trimmed = text?.Trim();
            return string.IsNullOrEmpty(trimmed) || trimmed.Length > MessageLimit ? null : trimmed;
        }
    }
}
