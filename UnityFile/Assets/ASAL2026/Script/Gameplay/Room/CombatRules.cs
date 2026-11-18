using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ASAL
{
    [Serializable]
    internal sealed class MatchCharacter
    {
        public string Name;
        public string Race;
        public string Class;
        public int Level;
        public int MaxHp;
        public MatchAbility[] Abilities;
        public string[] Skills;
    }

    [Serializable]
    internal sealed class MatchAbility
    {
        public string Name;
        public int Points;
        public int Bonus;
    }

    [Serializable]
    internal sealed class CombatEncounter
    {
        public string ActiveTokenId;
        public Combatant[] TurnOrder;
        public int Round;

        internal Combatant Find(string tokenId) => Array.Find(TurnOrder, entry => entry.TokenId == tokenId);
        internal CombatEncounter Copy()
        {
            var clone = JsonUtility.FromJson<CombatEncounter>(JsonUtility.ToJson(this));
            if (string.IsNullOrEmpty(clone.ActiveTokenId))
                clone.ActiveTokenId = null;
            return clone;
        }
    }

    [Serializable]
    internal sealed class Combatant
    {
        public string TokenId;
        public int Initiative;
        public int Hp;
        public int ActionsLeft;
        public int MoveLeft;
        public string[] MultiShotTargets = Array.Empty<string>();
        public CombatStatus[] Statuses = Array.Empty<CombatStatus>();
    }

    [Serializable]
    internal sealed class CombatStatus
    {
        public string Name;
        public int Turns;
        public string SourceId;
    }

    internal static class CombatRules
    {
        internal const string Dodge = "Warrior’s sense";
        internal const string MultiShots = "Multi shots";
        private static readonly Vector3Int[] Neighbors = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };

        internal static CombatEncounter StartEncounter(MatchToken[] tokens, Func<int, int> roll)
        {
            var order = tokens.Where(token => token.Placed)
                .Select(token => new Combatant { TokenId = token.Id, Initiative = roll(20), Hp = token.Hp })
                .OrderByDescending(entry => entry.Initiative).ThenBy(entry => entry.TokenId, StringComparer.Ordinal).ToArray();
            var active = Array.Find(order, entry => entry.Hp > 0);
            if (active == null)
                return null;
            var encounter = new CombatEncounter { TurnOrder = order, Round = 1 };
            BeginTurn(encounter, active, tokens);
            return encounter;
        }

        internal static void Advance(CombatEncounter encounter, MatchToken[] tokens)
        {
            var active = encounter.Find(encounter.ActiveTokenId);
            int previous = Array.IndexOf(encounter.TurnOrder, active);
            if (active != null)
            {
                active.ActionsLeft = 0;
                active.MoveLeft = 0;
                active.MultiShotTargets = Array.Empty<string>();
            }
            for (int step = 1; step <= encounter.TurnOrder.Length; step++)
            {
                int index = (previous + step) % encounter.TurnOrder.Length;
                var next = encounter.TurnOrder[index];
                if (next.Hp <= 0 || !tokens.Any(token => token.Id == next.TokenId && token.Placed))
                    continue;
                if (index <= previous && encounter.Round < int.MaxValue)
                    encounter.Round++;
                BeginTurn(encounter, next, tokens);
                return;
            }
            encounter.ActiveTokenId = null;
        }

        private static void BeginTurn(CombatEncounter encounter, Combatant next, MatchToken[] tokens)
        {
            encounter.ActiveTokenId = next.TokenId;
            foreach (var entry in encounter.TurnOrder)
            {
                entry.Statuses = entry.Statuses.Where(status => status.Name != "Roar" || status.SourceId != next.TokenId).ToArray();
            }
            next.Statuses = next.Statuses.Where(status => status.Turns != 0 && status.Name != Dodge).ToArray();
            next.ActionsLeft = Has(next, "Turn undead") ? 0 : 1;
            next.MoveLeft = (Array.Find(tokens, token => token.Id == next.TokenId)?.Character?.Skills?.Contains("Size matter") == true ? 7 : 6)
                - (Has(next, "Ice shot") ? 2 : 0);
            foreach (var status in next.Statuses)
                if (status.Turns > 0)
                    status.Turns--;
        }

        internal static bool CanAct(CombatEncounter encounter, MatchToken token, int actor, bool dm) =>
            encounter?.ActiveTokenId != null && token?.Placed == true && encounter.ActiveTokenId == token.Id
            && encounter.Find(token.Id)?.Hp > 0 && (dm ? token.OwnerActor == 0 : token.OwnerActor == actor);

        internal static int MovementCost(TilemapDataWrapper map, Vector3Int origin, Vector3Int destination, int allowance)
        {
            var direct = Math.Abs((long)origin.x - destination.x) + Math.Abs((long)origin.y - destination.y);
            if (direct == 0 || direct > allowance)
                return -1;
            var queue = new Queue<(Vector3Int Cell, int Steps)>();
            var visited = new HashSet<Vector3Int> { origin };
            queue.Enqueue((origin, 0));
            while (queue.Count > 0)
            {
                var (cell, steps) = queue.Dequeue();
                foreach (var direction in Neighbors)
                {
                    var next = cell + direction;
                    if (!visited.Add(next) || !MatchSnapshot.IsWalkable(map, next))
                        continue;
                    if (next == destination)
                        return steps + 1;
                    if (steps + 1 < allowance)
                        queue.Enqueue((next, steps + 1));
                }
            }
            return -1;
        }

        internal static int ApplyHealth(Combatant target, int amount, int maxHp, bool attack)
        {
            if (attack && amount < 0 && Has(target, Dodge))
            {
                RemoveStatus(target, Dodge);
                return 0;
            }
            var previous = target.Hp;
            target.Hp = ClampHp(previous, amount, maxHp);
            return target.Hp - previous;
        }

        internal static int ClampHp(int hp, int amount, int maxHp) => (int)Math.Clamp((long)hp + amount, 0, maxHp);

        internal static int Modifier(MatchCharacter character, Combatant caster, string ability)
        {
            var score = Array.Find(character.Abilities, value => value.Name == ability);
            if (score == null)
                return 0;
            long points = score.Points;
            if (Has(caster, "Blessing")) points++;
            if (ability == "Dexterity" && Has(caster, "Ancestor Guidance")) points += 2;
            if (ability == "Intelligence" && Has(caster, "Amplify Magic")) points += 2;
            long modifier = (long)Math.Floor((points - 10) / 2.0) + score.Bonus;
            return (int)Math.Clamp(modifier, int.MinValue, int.MaxValue);
        }

        internal static bool Has(Combatant target, string status) => target?.Statuses?.Any(item => item.Name == status) == true;

        internal static void RemoveStatus(Combatant target, string name) =>
            target.Statuses = target.Statuses.Where(status => status.Name != name).ToArray();

        internal static void AddStatus(Combatant target, string name, int turns, string source = null)
        {
            RemoveStatus(target, name);
            target.Statuses = target.Statuses.Append(new CombatStatus { Name = name, Turns = turns, SourceId = source }).ToArray();
        }

        internal static bool IsFriendly(MatchToken first, MatchToken second) => (first.OwnerActor == 0) == (second.OwnerActor == 0);

        internal static bool InRange(MatchToken caster, MatchToken target, int range) =>
            Math.Abs((long)caster.Cell.x - target.Cell.x) + Math.Abs((long)caster.Cell.y - target.Cell.y) <= range;
    }
}
