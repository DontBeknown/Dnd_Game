using System;
using System.Linq;

namespace ASAL
{
    internal enum TargetMode { Self, AllAllies, SelectedEnemy, AreaEnemies, SelectedAlly }

    internal sealed class TargetPolicy
    {
        internal TargetPolicy(TargetMode mode, int range, RaceId requiredRace = default)
        {
            Mode = mode;
            Range = range;
            RequiredRace = requiredRace;
        }

        internal TargetMode Mode { get; }
        internal int Range { get; }
        internal RaceId RequiredRace { get; }
        internal bool NeedsTarget => Mode is TargetMode.SelectedEnemy or TargetMode.SelectedAlly;

        internal MatchToken[] Select(RulesCatalog rules, MatchToken[] tokens, CombatEncounter encounter,
            MatchToken token, Combatant caster, string targetId)
        {
            bool MatchesRace(MatchToken target) => RequiredRace.Value == null || target.Character?.RaceId == RequiredRace.Value;
            if (Mode == TargetMode.Self)
                return MatchesRace(token) ? new[] { token } : Array.Empty<MatchToken>();

            bool allies = Mode is TargetMode.SelectedAlly or TargetMode.AllAllies;
            var taunts = allies ? Array.Empty<string>() : caster.Statuses
                .Where(status => rules.Status(new StatusId(status.StatusId))?.Taunts == true
                    && encounter.Find(status.SourceId)?.Hp > 0)
                .Select(status => status.SourceId).ToArray();
            return tokens.Where(target =>
            {
                if (!target.Placed || encounter.Find(target.Id) is not { } state)
                    return false;
                return (Mode == TargetMode.SelectedAlly || state.Hp > 0)
                    && (!NeedsTarget || target.Id == targetId)
                    && (allies == CombatRules.IsFriendly(token, target))
                    && MatchesRace(target)
                    && (taunts.Length == 0 || taunts.Contains(target.Id))
                    && CombatRules.InRange(token, target, Range);
            }).ToArray();
        }
    }

    internal sealed class ActionPolicy
    {
        internal ActionPolicy(int cost, int maxDistinctTargets = 1)
        {
            Cost = cost;
            MaxDistinctTargets = maxDistinctTargets;
        }

        internal int Cost { get; }
        internal int MaxDistinctTargets { get; }

        internal bool IsFollowUp(Combatant caster, SkillId skill) => MaxDistinctTargets > 1
            && caster.FollowUpSkillId == skill.Value && caster.FollowUpTargets.Length > 0
            && caster.FollowUpTargets.Length < MaxDistinctTargets;

        internal void Spend(Combatant caster, SkillId skill, MatchToken[] targets, bool followUp)
        {
            if (!followUp)
                caster.ActionsLeft -= Cost;
            if (MaxDistinctTargets <= 1)
                return;
            caster.FollowUpSkillId = skill.Value;
            caster.FollowUpTargets = followUp ? caster.FollowUpTargets.Append(targets[0].Id).ToArray() : new[] { targets[0].Id };
        }
    }

    internal static class CombatSkills
    {
        internal static bool HasAction(Combatant caster, SkillDefinition skill) =>
            caster != null && skill != null && !skill.IsPassive
            && (caster.ActionsLeft >= skill.Action.Cost || skill.Action.IsFollowUp(caster, skill.Id));

        internal static bool TryUse(RulesCatalog rules, MatchToken[] tokens, CombatEncounter encounter, MatchToken token,
            Combatant caster, MatchCommand command, SkillDefinition skill, Func<int, int> roll, out string summary,
            out string report, out MatchRoll result, out AttackFeedback attack)
        {
            summary = report = null;
            result = null;
            attack = null;
            if (!HasAction(caster, skill))
                return false;

            bool followUp = skill.Action.IsFollowUp(caster, skill.Id);
            var targets = skill.Targeting.Select(rules, tokens, encounter, token, caster, command.TargetId);
            if (targets.Length == 0 || (followUp && caster.FollowUpTargets.Contains(targets[0].Id)))
                return false;

            skill.Action.Spend(caster, skill.Id, targets, followUp);
            var resolution = new SkillResolution(rules, token, caster, encounter, targets, roll);
            foreach (var effect in skill.Effects)
                effect.Apply(resolution);

            if (caster.Hp == 0)
                CombatRules.Advance(rules, encounter, tokens);

            summary = $"{token.Name} used {skill.Label}.";
            if (resolution.HealthChanges.Count > 0)
                summary += $" {string.Join(", ", resolution.HealthChanges)}.";
            report = resolution.Reports.Count == 0 ? null : string.Join(" ", resolution.Reports);
            result = resolution.Dice.ToResult(command.CommandId);
            attack = resolution.GetAttackFeedback();
            return true;
        }
    }
}
