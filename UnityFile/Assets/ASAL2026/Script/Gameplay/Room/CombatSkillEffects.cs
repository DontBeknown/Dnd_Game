using System;
using System.Collections.Generic;
using System.Linq;

namespace ASAL
{
    internal sealed class StatusDefinition
    {
        internal StatusDefinition(StatusId id, int duration, bool expiresAtTargetTurn = false,
            bool expiresAtSourceTurn = false, bool blocksAction = false, bool taunts = false, bool dodge = false,
            int abilityPointBonus = 0, AbilityId bonusAbility = default, int extraHitDicePerTerm = 0,
            AbilityId bonusDamageAbility = default, DiceFormula bonusDamageDice = null, RaceId bonusDamageRace = default,
            bool consumeOnAttack = false, string label = null)
        {
            Id = id;
            Duration = duration;
            ExpiresAtTargetTurn = expiresAtTargetTurn;
            ExpiresAtSourceTurn = expiresAtSourceTurn;
            BlocksAction = blocksAction;
            Taunts = taunts;
            Dodge = dodge;
            AbilityPointBonus = abilityPointBonus;
            BonusAbility = bonusAbility;
            ExtraHitDicePerTerm = extraHitDicePerTerm;
            BonusDamageAbility = bonusDamageAbility;
            BonusDamageDice = bonusDamageDice;
            BonusDamageRace = bonusDamageRace;
            ConsumeOnAttack = consumeOnAttack;
            Label = label ?? id.Value;
        }

        internal StatusId Id { get; }
        internal int Duration { get; }
        internal bool ExpiresAtTargetTurn { get; }
        internal bool ExpiresAtSourceTurn { get; }
        internal bool BlocksAction { get; }
        internal bool Taunts { get; }
        internal bool Dodge { get; }
        internal int AbilityPointBonus { get; }
        internal AbilityId BonusAbility { get; }
        internal int ExtraHitDicePerTerm { get; }
        internal AbilityId BonusDamageAbility { get; }
        internal DiceFormula BonusDamageDice { get; }
        internal RaceId BonusDamageRace { get; }
        internal bool ConsumeOnAttack { get; }
        internal string Label { get; }
    }

    internal sealed class SkillResolution
    {
        private Dictionary<string, AttackTargetFeedback> attackTargets;

        internal SkillResolution(RulesCatalog rules, MatchToken actor, Combatant caster, CombatEncounter encounter,
            MatchToken[] targets, Func<int, int> roll)
        {
            Rules = rules;
            Actor = actor;
            Caster = caster;
            Encounter = encounter;
            Targets = targets;
            Dice = new DiceRoll(roll);
        }

        internal RulesCatalog Rules { get; }
        internal MatchToken Actor { get; }
        internal Combatant Caster { get; }
        internal CombatEncounter Encounter { get; }
        internal MatchToken[] Targets { get; }
        internal DiceRoll Dice { get; }
        internal List<string> HealthChanges { get; } = new();
        internal List<string> Reports { get; } = new();

        internal int Modifier(AbilityId ability) => CombatRules.Modifier(Rules, Actor.Character, Caster, ability);

        // shortcut: Bound extra dice for exceptionally large saved levels, revisit if level 105+ is supported.
        internal int ExtraDamageDicePerTerm => Math.Min(Actor.Character.Level / 5, 20);

        internal AttackFeedback GetAttackFeedback() => attackTargets == null ? null : new AttackFeedback
        {
            AttackerId = Actor.Id,
            Targets = Targets.Where(target => attackTargets.ContainsKey(target.Id))
                .OrderBy(target => (target.Position - Actor.Position).sqrMagnitude)
                .ThenBy(target => target.Id, StringComparer.Ordinal)
                .Select(target => attackTargets[target.Id]).ToArray()
        };

        internal void ApplyHealth(MatchToken target, long amount, bool healing, bool critical = false)
        {
            var state = Encounter.Find(target.Id);
            var points = (int)Math.Clamp(amount, 0, int.MaxValue);
            var change = CombatRules.ApplyHealth(Rules, state, healing ? points : -points, target.Character.MaxHp, out var dodged);
            if (!healing)
            {
                attackTargets ??= new Dictionary<string, AttackTargetFeedback>();
                if (!attackTargets.TryGetValue(target.Id, out var feedback))
                {
                    feedback = new AttackTargetFeedback { TokenId = target.Id };
                    attackTargets.Add(target.Id, feedback);
                }
                feedback.Damage = (int)Math.Min(int.MaxValue, (long)feedback.Damage - change);
                feedback.Dodged = feedback.Damage == 0 && (feedback.Dodged || dodged);
            }
            HealthChanges.Add(dodged ? $"{target.Name} dodged"
                : $"{target.Name} {state.Hp}/{target.Character.MaxHp} HP ({change:+#;-#;0}{(critical ? " critical" : "")})");
        }
    }

    internal abstract class SkillEffect
    {
        internal virtual string HitDiceText => "-";
        internal virtual string DamageDiceText => "-";
        internal abstract void Apply(SkillResolution resolution);
    }

    internal sealed class DamageEffect : SkillEffect
    {
        private readonly DiceFormula hit;
        private readonly DiceFormula damage;
        private readonly AbilityId ability;
        private readonly HashSet<RaceId> criticalRaces;

        internal DamageEffect(DiceFormula hit, DiceFormula damage, AbilityId ability, IEnumerable<RaceId> criticalRaces)
        {
            this.hit = hit;
            this.damage = damage;
            this.ability = ability;
            this.criticalRaces = criticalRaces.ToHashSet();
        }

        internal override string HitDiceText => hit.DisplayText;
        internal override string DamageDiceText => damage.DisplayText;

        internal override void Apply(SkillResolution resolution)
        {
            var statuses = CombatRules.Statuses(resolution.Rules, resolution.Caster).ToArray();
            var modifier = resolution.Modifier(ability);
            var hitTotal = resolution.Dice.Roll(hit, statuses.Sum(status => status.ExtraHitDicePerTerm)) + (long)modifier;
            long rawDamage = resolution.Dice.Roll(damage, resolution.ExtraDamageDicePerTerm) + (long)modifier;

            foreach (var status in statuses)
            {
                if (status.BonusDamageDice != null && (status.BonusDamageRace.Value == null
                    || resolution.Targets.Any(target => target.Character.RaceId == status.BonusDamageRace.Value)))
                    rawDamage += resolution.Dice.Roll(status.BonusDamageDice);
                if (status.ConsumeOnAttack)
                    CombatRules.RemoveStatus(resolution.Caster, status.Id);
            }
            foreach (var status in statuses)
            {
                if (status.BonusDamageAbility.Value != null)
                    rawDamage += resolution.Modifier(status.BonusDamageAbility);
            }

            foreach (var target in resolution.Targets)
            {
                bool critical = criticalRaces.Contains(new RaceId(target.Character.RaceId));
                resolution.ApplyHealth(target, critical ? rawDamage + rawDamage / 2 : rawDamage, healing: false, critical: critical);
            }
            resolution.Reports.Add($"Hit: {hitTotal}. Damage roll: {rawDamage}.");
        }
    }

    internal sealed class HealingEffect : SkillEffect
    {
        private readonly DiceFormula dice;
        private readonly AbilityId ability;

        internal HealingEffect(DiceFormula dice, AbilityId ability)
        {
            this.dice = dice;
            this.ability = ability;
        }

        internal override void Apply(SkillResolution resolution)
        {
            long rawHealing = resolution.Dice.Roll(dice, resolution.ExtraDamageDicePerTerm) + (long)resolution.Modifier(ability);
            foreach (var target in resolution.Targets)
                resolution.ApplyHealth(target, rawHealing * resolution.Actor.Character.Level, healing: true);
            resolution.Reports.Add($"Healing roll: {rawHealing}.");
        }
    }

    internal sealed class ApplyStatusEffect : SkillEffect
    {
        private readonly StatusId status;

        internal ApplyStatusEffect(StatusId status) => this.status = status;

        internal override void Apply(SkillResolution resolution)
        {
            var definition = resolution.Rules.Status(status);
            foreach (var target in resolution.Targets)
                CombatRules.AddStatus(resolution.Encounter.Find(target.Id), definition, resolution.Actor.Id);
        }
    }
}
