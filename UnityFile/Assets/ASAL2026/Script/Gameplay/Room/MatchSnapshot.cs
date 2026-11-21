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
        public CombatEncounter Encounter;
        public MatchLogEntry[] PublicLog = Array.Empty<MatchLogEntry>();

        internal Vector2 BoardCenter()
        {
            var tiles = Map?.tilemapDataList?.Where(layer => layer?.tiles != null)
                .SelectMany(layer => layer.tiles).Where(tile => tile != null).ToArray();
            if (tiles == null || tiles.Length == 0)
                return Vector2.zero;
            return new Vector2((tiles.Min(tile => tile.position.x) + (float)tiles.Max(tile => tile.position.x)) * 0.5f,
                (tiles.Min(tile => tile.position.y) + (float)tiles.Max(tile => tile.position.y)) * 0.5f);
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
        public Vector2 Position;
        public MatchCharacter Character;
        public int Hp;

        internal MatchToken At(Vector2 position) => new()
        {
            Id = Id, OwnerActor = OwnerActor, RecordId = RecordId, Name = Name, Placed = true, Position = position,
            Character = Character, Hp = Hp
        };
    }

    internal enum MatchAction { Place = 0, ToggleEncounter = 2, EndTurn, UseSkill, ChangeHp, RemoveToken, RollAbility, RollDice }

    [Serializable]
    internal sealed class MatchCommand
    {
        public string MatchId;
        public string CommandId;
        public int ExpectedRevision;
        public MatchAction Action;
        public string TokenId;
        public string RecordId;
        public Vector2 Position;
        public string TargetId;
        public string Skill;
        public string Ability;
        public int Amount;
        public int[] Dice;
    }

    internal sealed class MatchDelta
    {
        public MatchToken Token;
        public MatchToken[] ChangedTokens;
        public string RemovedTokenId;
        public bool EncounterChanged;
        public CombatEncounter Encounter;
        public MatchLogEntry Log;
    }
}
