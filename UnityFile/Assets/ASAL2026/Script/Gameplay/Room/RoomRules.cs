using System.Collections.Generic;
using System.Linq;

namespace ASAL
{
    public sealed class SelectedCharacter
    {
        public SelectedCharacter(string recordId, string displayName)
        {
            RecordId = recordId;
            DisplayName = displayName;
        }

        public string RecordId { get; }
        public string DisplayName { get; }
    }

    public sealed class RoomMember
    {
        public RoomMember(int actor, string userId, string name, bool active,
            string characterId, string characterName, bool ready)
        {
            Actor = actor;
            UserId = userId;
            Name = name;
            Active = active;
            CharacterId = characterId;
            CharacterName = characterName;
            Ready = ready;
        }

        public int Actor { get; }
        public string UserId { get; }
        public string Name { get; }
        public bool Active { get; }
        public string CharacterId { get; }
        public string CharacterName { get; }
        public bool Ready { get; }
    }

    public sealed class DiscoveredRoom
    {
        public DiscoveredRoom(string name, int count, int capacity)
        {
            Name = name;
            Count = count;
            Capacity = capacity;
        }

        public string Name { get; }
        public int Count { get; }
        public int Capacity { get; }
    }

    public enum RoomPhase { None, Setup, Starting, Playing }

    public static class RoomRules
    {
        public static bool IsDm(int actor, string user, int dmActor, string dmUser) =>
            actor > 0 && actor == dmActor && !string.IsNullOrEmpty(user) && user == dmUser;

        public static bool CanStart(int actor, string user, int dmActor, string dmUser,
            IReadOnlyList<RoomMember> roster) =>
            IsDm(actor, user, dmActor, dmUser) && roster.All(player =>
                !player.Active || IsDm(player.Actor, player.UserId, dmActor, dmUser)
                || (player.Ready && !string.IsNullOrWhiteSpace(player.CharacterId)));

        public static bool CanKick(int actor, string user, int dmActor, string dmUser,
            int target, bool targetPresent) =>
            IsDm(actor, user, dmActor, dmUser) && target > 0 && target != dmActor && targetPresent;

        public static bool KeepReady(string oldId, string oldName, string newId, string newName, bool ready) =>
            ready && newId != null && oldId == newId && oldName == newName;
    }
}
