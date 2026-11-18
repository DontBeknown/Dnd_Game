using System;
using System.Collections.Generic;
using System.Linq;

namespace ASAL
{
    internal static class CombatSkills
    {
        internal static bool HasAction(Combatant caster, string skill) => caster != null && (caster.ActionsLeft > 0
            || skill == CombatRules.MultiShots && caster.MultiShotTargets.Length is > 0 and < 3);

        internal static bool TryUse(MatchToken[] tokens, CombatEncounter encounter, MatchToken token, Combatant caster,
            MatchCommand command, Skill skill, Skill focus, Func<int, int> roll, out string summary, out string report,
            out MatchRoll result)
        {
            summary = report = null;
            result = null;
            var name = skill.skillName;
            bool multi = name == CombatRules.MultiShots;
            bool followUp = multi && caster.MultiShotTargets.Length is > 0 and < 3;
            if (!HasAction(caster, name) || skill.skillType == SkillType.RacePassive)
                return false;

            var targets = Targets(tokens, encounter, token, caster, command.TargetId, name, skill.skillType);
            if (targets.Length == 0)
                return false;
            if (!followUp)
                caster.ActionsLeft--;
            if (multi)
                caster.MultiShotTargets = caster.MultiShotTargets.Append(targets[0].Id).ToArray();

            if (skill.skillType != SkillType.Damage || name == "Rage")
            {
                foreach (var target in targets)
                {
                    var state = encounter.Find(target.Id);
                    var duration = name switch
                    {
                        "Blessing" => 3,
                        "Rage" or "Ancestor Guidance" or "Amplify Magic" => 2,
                        CombatRules.Dodge or "Turn undead" => 1,
                        _ => -1
                    };
                    CombatRules.AddStatus(state, name, duration, token.Id);
                }
                summary = $"{token.Name} used {name}.";
                return true;
            }

            bool heal = name == "Heal";
            var ability = skill.associatedAbility?.FirstOrDefault()?.abilityName;
            var modifier = CombatRules.Modifier(token.Character, caster, ability);
            var sides = new List<int>();
            var faces = new List<int>();
            bool raging = !heal && CombatRules.Has(caster, "Rage");
            var hitDice = heal ? Array.Empty<Dice>() : skill.GetHitDiceLists(raging ? 1 : 0).ToArray();
            int hit = RollDice(hitDice, sides, faces, roll);

            // shortcut: Bound extra dice for exceptionally large saved levels, revisit if level 105+ is supported in the rules.
            var damage = skill.GetDamageDiceLists(Math.Min(token.Character.Level / 5, 20));
            bool focused = CombatRules.Has(caster, "Focus");
            if (!heal && focused)
            {
                if (targets.Any(target => target.Character.Race == "Human") && focus != null)
                    damage.AddRange(focus.GetDamageDiceLists(0));
                CombatRules.RemoveStatus(caster, "Focus");
            }
            long rawDamage = RollDice(damage, sides, faces, roll) + (long)modifier;
            if (raging)
                rawDamage += CombatRules.Modifier(token.Character, caster, "Strength");
            var changes = new List<string>();
            foreach (var target in targets)
            {
                var state = encounter.Find(target.Id);
                bool critical = name switch
                {
                    "Fireball" => target.Character.Race is "Slime" or "Half beast",
                    "Lightning Strike" => target.Character.Race is "Elf" or "Human",
                    "Ice shot" => target.Character.Race == "Orc",
                    _ => false
                };
                var amount = (int)Math.Clamp(heal ? rawDamage * token.Character.Level
                    : critical ? rawDamage + rawDamage / 2 : rawDamage, 0, int.MaxValue);
                bool dodged = !heal && amount > 0 && CombatRules.Has(state, CombatRules.Dodge);
                var change = CombatRules.ApplyHealth(state, heal ? amount : -amount, target.Character.MaxHp, !heal);
                if (name == "Ice shot" && !dodged && state.Hp > 0)
                    CombatRules.AddStatus(state, "Ice shot", 1, token.Id);
                if (dodged)
                    changes.Add($"{target.Name} dodged");
                else
                    changes.Add($"{target.Name} {state.Hp}/{target.Character.MaxHp} HP ({change:+#;-#;0}{(critical ? " critical" : "")})");
            }
            if (caster.Hp == 0)
                CombatRules.Advance(encounter, tokens);

            result = DiceManager.MakeRoll(command.CommandId, sides.ToArray(), faces.ToArray());
            summary = $"{token.Name} used {name}. {string.Join(", ", changes)}.";
            report = heal ? $"Healing roll: {rawDamage}." : $"Hit: {hit + (long)modifier}. Damage roll: {rawDamage}.";
            return true;
        }

        private static MatchToken[] Targets(MatchToken[] tokens, CombatEncounter encounter, MatchToken token, Combatant caster,
            string targetId, string skill, SkillType type)
        {
            if (type == SkillType.Buff || skill == "Rage")
                return skill == "Blessing" ? tokens.Where(target => target.Placed && encounter.Find(target.Id)?.Hp > 0
                    && CombatRules.IsFriendly(token, target)).ToArray() : new[] { token };

            var range = skill switch
            {
                "Slash" or "Circle slash" or "Bonk" => 1,
                "Roar" or "Turn undead" => 5,
                "Multi shots" or "Fireball" or "Holy Shot" => 10,
                "Single shot" or "Lightning Strike" or "Ice shot" => 15,
                _ => int.MaxValue
            };
            bool area = skill is "Circle slash" or "Roar" or "Turn undead";
            bool heal = skill == "Heal";
            var taunts = caster.Statuses.Where(status => status.Name == "Roar" && encounter.Find(status.SourceId)?.Hp > 0)
                .Select(status => status.SourceId).ToArray();
            return tokens.Where(target => target.Placed && encounter.Find(target.Id) != null
                && (heal || encounter.Find(target.Id).Hp > 0)
                && (area || target.Id == targetId)
                && (heal == CombatRules.IsFriendly(token, target))
                && (skill != "Turn undead" || target.Character.Race == "Zombie")
                && (skill != CombatRules.MultiShots || !caster.MultiShotTargets.Contains(target.Id))
                && (heal || taunts.Length == 0 || taunts.Contains(target.Id))
                && CombatRules.InRange(token, target, range)).ToArray();
        }

        private static int RollDice(IEnumerable<Dice> dice, List<int> sides, List<int> faces, Func<int, int> roll)
        {
            int total = 0;
            foreach (var die in dice)
            {
                var face = roll(die.diceType);
                sides.Add(die.diceType);
                faces.Add(face);
                total += face;
            }
            return total;
        }
    }
}
