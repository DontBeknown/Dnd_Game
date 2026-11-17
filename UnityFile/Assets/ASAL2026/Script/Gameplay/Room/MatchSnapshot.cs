using System;
using System.Linq;
using UnityEngine;

namespace ASAL
{
    [Serializable]
    internal sealed class MatchSnapshot
    {
        public string MatchId;
        public int Revision;
        public int MapRevision;
        public TilemapDataWrapper Map;
        public MatchMember[] Roster;
        public MatchToken[] Tokens;

        internal static bool IsWalkable(TilemapDataWrapper map, Vector3Int cell) =>
            cell.z == 0 && HasTile(map, "Tilemap_Floor", cell) && !HasTile(map, "Tilemap_Wall", cell);

        private static bool HasTile(TilemapDataWrapper map, string layer, Vector3Int cell) =>
            map?.tilemapDataList?.Any(entry => entry.key == layer && entry.tiles.Any(tile => tile.position == cell)) == true;

        internal MatchSnapshot Copy() => JsonUtility.FromJson<MatchSnapshot>(JsonUtility.ToJson(this));

        internal bool TryApply(MatchCommand command, int actor, bool dm, out MatchToken result)
        {
            result = null;
            if (command == null || command.MatchId != MatchId || command.ExpectedRevision != Revision
                || !Guid.TryParseExact(command.CommandId, "N", out _) || !IsWalkable(Map, command.Cell))
                return false;

            var token = Array.Find(Tokens, item => item.Id == command.TokenId);
            if (command.Action == MatchAction.Move && token?.Placed != true)
                return false;
            if (token == null && command.Action == MatchAction.Place)
            {
                var member = Array.Find(Roster, item => item.Actor == command.OwnerActor && item.RecordId == command.RecordId);
                if (member == null || command.TokenId != PlayerTokenId(member.Actor))
                    return false;
                token = new MatchToken { Id = command.TokenId, OwnerActor = member.Actor,
                    RecordId = member.RecordId, Name = member.CharacterName };
            }

            if (token == null || (!dm && token.OwnerActor != actor)
                || (command.Action == MatchAction.Place && token.Placed)
                || command.Action is not (MatchAction.Place or MatchAction.Move))
                return false;

            result = token.At(command.Cell);
            return true;
        }

        internal static string PlayerTokenId(int actor) => "player-" + actor;
    }

    [Serializable]
    internal sealed class MatchMember
    {
        public int Actor;
        public string Name;
        public string RecordId;
        public string CharacterName;
    }

    [Serializable]
    internal sealed class MatchToken
    {
        public string Id;
        public int OwnerActor;
        public string RecordId;
        public string Name;
        public bool Placed;
        public Vector3Int Cell;

        internal MatchToken At(Vector3Int cell) => new()
        {
            Id = Id, OwnerActor = OwnerActor, RecordId = RecordId, Name = Name, Placed = true, Cell = cell
        };
    }

    internal enum MatchAction { Place, Move }

    [Serializable]
    internal sealed class MatchCommand
    {
        public string MatchId;
        public string CommandId;
        public int ExpectedRevision;
        public MatchAction Action;
        public string TokenId;
        public int OwnerActor;
        public string RecordId;
        public Vector3Int Cell;
    }

    [Serializable]
    internal sealed class MatchDelta
    {
        public string MatchId;
        public int Revision;
        public MatchToken Token;

        internal MatchSnapshot Apply(MatchSnapshot committed) => new()
        {
            MatchId = MatchId,
            Revision = Revision,
            MapRevision = committed.MapRevision,
            Map = committed.Map,
            Roster = committed.Roster,
            Tokens = committed.Tokens.Where(item => item.Id != Token.Id).Append(Token).ToArray()
        };
    }

}
