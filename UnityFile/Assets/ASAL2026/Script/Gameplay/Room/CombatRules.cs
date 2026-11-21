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
        public string RaceId;
        public string ClassId;
        public int Level;
        public int MaxHp;
        public MatchAbility[] Abilities;
        public string[] SkillIds;
    }

    [Serializable]
    internal sealed class MatchAbility
    {
        public string AbilityId;
        public int Points;
        public int Bonus;

        internal AbilityScoreValue Value => new(new AbilityId(AbilityId), Points, Bonus);
    }

    internal readonly struct AbilityScoreValue
    {
        internal AbilityScoreValue(AbilityId id, int points, int bonus)
        {
            Id = id;
            Points = points;
            Bonus = bonus;
        }

        internal AbilityId Id { get; }
        internal int Points { get; }
        internal int Bonus { get; }
        internal int Modifier(long adjustment) => CalculateModifier(Points + adjustment, Bonus);

        internal static int CalculateModifier(long points, int bonus) =>
            (int)Math.Clamp((long)Math.Floor((points - 10) / 2.0) + bonus, int.MinValue, int.MaxValue);
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
        public string FollowUpSkillId;
        public string[] FollowUpTargets = Array.Empty<string>();
        public CombatStatus[] Statuses = Array.Empty<CombatStatus>();
    }

    [Serializable]
    internal sealed class CombatStatus
    {
        public string StatusId;
        public int Turns;
        public string SourceId;
    }

    internal static class CombatRules
    {
        internal static CombatEncounter StartEncounter(RulesCatalog rules, MatchToken[] tokens, Func<int, int> roll)
        {
            var order = tokens.Where(token => token.Placed)
                .Select(token => new Combatant { TokenId = token.Id, Initiative = roll(20), Hp = token.Hp })
                .OrderByDescending(entry => entry.Initiative).ThenBy(entry => entry.TokenId, StringComparer.Ordinal).ToArray();
            var active = Array.Find(order, entry => entry.Hp > 0);
            if (active == null)
                return null;
            var encounter = new CombatEncounter { TurnOrder = order, Round = 1 };
            BeginTurn(rules, encounter, active);
            return encounter;
        }

        internal static void Advance(RulesCatalog rules, CombatEncounter encounter, MatchToken[] tokens)
        {
            var active = encounter.Find(encounter.ActiveTokenId);
            int previous = Array.IndexOf(encounter.TurnOrder, active);
            if (active != null)
            {
                active.ActionsLeft = 0;
                active.FollowUpSkillId = null;
                active.FollowUpTargets = Array.Empty<string>();
            }
            for (int step = 1; step <= encounter.TurnOrder.Length; step++)
            {
                int index = (previous + step) % encounter.TurnOrder.Length;
                var next = encounter.TurnOrder[index];
                if (next.Hp <= 0 || !tokens.Any(token => token.Id == next.TokenId && token.Placed))
                    continue;
                if (index <= previous && encounter.Round < int.MaxValue)
                    encounter.Round++;
                BeginTurn(rules, encounter, next);
                return;
            }
            encounter.ActiveTokenId = null;
        }

        private static void BeginTurn(RulesCatalog rules, CombatEncounter encounter, Combatant next)
        {
            encounter.ActiveTokenId = next.TokenId;
            RemoveSourceStatuses(rules, encounter, next.TokenId);
            next.Statuses = next.Statuses.Where(status => status.Turns != 0
                && rules.Status(new StatusId(status.StatusId))?.ExpiresAtTargetTurn != true).ToArray();
            next.ActionsLeft = Statuses(rules, next).Any(status => status.BlocksAction) ? 0 : 1;
            foreach (var status in next.Statuses)
                if (status.Turns > 0)
                    status.Turns--;
        }

        internal static void RemoveSourceStatuses(RulesCatalog rules, CombatEncounter encounter, string sourceId)
        {
            foreach (var entry in encounter.TurnOrder)
            {
                entry.Statuses = entry.Statuses.Where(status => status.SourceId != sourceId
                    || rules.Status(new StatusId(status.StatusId))?.ExpiresAtSourceTurn != true).ToArray();
            }
        }

        internal static bool CanAct(CombatEncounter encounter, MatchToken token, int actor, bool dm) =>
            encounter?.ActiveTokenId != null && token?.Placed == true && encounter.ActiveTokenId == token.Id
            && encounter.Find(token.Id)?.Hp > 0 && (dm ? token.OwnerActor == 0 : token.OwnerActor == actor);

        internal static int ApplyHealth(RulesCatalog rules, Combatant target, int amount, int maxHp, out bool dodged)
        {
            var dodge = amount < 0 ? Statuses(rules, target).FirstOrDefault(status => status.Dodge) : null;
            dodged = dodge != null;
            if (dodged)
            {
                RemoveStatus(target, dodge.Id);
                return 0;
            }
            var previous = target.Hp;
            target.Hp = ClampHp(previous, amount, maxHp);
            return target.Hp - previous;
        }

        internal static int ClampHp(int hp, int amount, int maxHp) => (int)Math.Clamp((long)hp + amount, 0, maxHp);

        internal static int Modifier(RulesCatalog rules, MatchCharacter character, Combatant caster, AbilityId ability)
        {
            var score = character.Abilities?.FirstOrDefault(value => value.AbilityId == ability.Value);
            if (score == null)
                return 0;
            var adjustment = Statuses(rules, caster)
                .Where(status => status.BonusAbility.Value == null || status.BonusAbility.Equals(ability))
                .Sum(status => (long)status.AbilityPointBonus);
            return score.Value.Modifier(adjustment);
        }

        internal static IEnumerable<StatusDefinition> Statuses(RulesCatalog rules, Combatant target) =>
            (target?.Statuses ?? Array.Empty<CombatStatus>())
                .Select(status => rules.Status(new StatusId(status.StatusId))).Where(definition => definition != null);

        internal static void RemoveStatus(Combatant target, StatusId id) =>
            target.Statuses = target.Statuses.Where(status => status.StatusId != id.Value).ToArray();

        internal static void AddStatus(Combatant target, StatusDefinition status, string source)
        {
            RemoveStatus(target, status.Id);
            target.Statuses = target.Statuses.Append(new CombatStatus
                { StatusId = status.Id.Value, Turns = status.Duration, SourceId = source }).ToArray();
        }

        internal static bool IsFriendly(MatchToken first, MatchToken second) => (first.OwnerActor == 0) == (second.OwnerActor == 0);

        internal static bool InRange(MatchToken caster, MatchToken target, int range) =>
            Math.Abs((double)caster.Position.x - target.Position.x) + Math.Abs((double)caster.Position.y - target.Position.y) <= range;
    }
}
