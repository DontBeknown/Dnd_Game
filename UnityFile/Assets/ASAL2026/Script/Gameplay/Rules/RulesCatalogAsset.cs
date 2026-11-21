using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ASAL
{
    [CreateAssetMenu(menuName = "ASAL/Rules Catalog")]
    public sealed class RulesCatalogAsset : ScriptableObject
    {
        [SerializeField] private RaceRuleAsset[] races = Array.Empty<RaceRuleAsset>();
        [SerializeField] private ClassRuleAsset[] classes = Array.Empty<ClassRuleAsset>();
        [SerializeField] private AbilityRuleAsset[] abilities = Array.Empty<AbilityRuleAsset>();
        [SerializeField] private SkillRuleAsset[] skills = Array.Empty<SkillRuleAsset>();
        [SerializeField] private StatusRuleAsset[] statuses = Array.Empty<StatusRuleAsset>();
        [SerializeField] private Dice[] dice = Array.Empty<Dice>();

        internal RulesCatalog Build()
        {
            var raceAssets = Index(races, "race");
            var classAssets = Index(classes, "class");
            var abilityAssets = Index(abilities, "ability");
            var skillAssets = Index(skills, "skill");
            var statusAssets = Index(statuses, "status", allowEmpty: true);
            ValidateLegacySources(races.Select(item => item.Source), "race");
            ValidateLegacySources(classes.Select(item => item.Source), "class");
            ValidateLegacySources(abilities.Select(item => item.Source), "ability");

            var supported = new HashSet<int>();
            foreach (var die in dice)
            {
                if (!die || die.diceType is < 2 or > 20 || !supported.Add(die.diceType))
                    throw Invalid("dice", die ? die.name : null);
            }
            if (!supported.Contains(20) || !abilities.Any(item => item.Id == AbilityId.Constitution.Value))
                throw new InvalidOperationException("The ASAL rules catalog requires Constitution and a d20.");

            AbilityId AbilityKey(AbilityRuleAsset asset) => new(Resolve(abilityAssets, asset, "ability"));
            RaceId RaceKey(RaceRuleAsset asset) => new(Resolve(raceAssets, asset, "race"));
            SkillId[] Grants(IEnumerable<SkillRuleAsset> entries) => entries
                .Select(asset => new SkillId(Resolve(skillAssets, asset, "skill"))).Distinct().ToArray();

            DiceFormula Formula(DiceTermEntry[] terms, string id)
            {
                if (terms.Length > 20 || terms.Any(term => !term.Die || !supported.Contains(term.Die.diceType)
                    || term.Count is < 1 or > 20))
                    throw Invalid("dice formula", id);
                return new DiceFormula(terms.Select(term => new DiceTerm(term.Die.diceType, term.Count)));
            }

            SkillEffect Effect(SkillEffectEntry entry, string id)
            {
                if (entry.Kind == EffectKind.ApplyStatus)
                    return new ApplyStatusEffect(new StatusId(Resolve(statusAssets, entry.Status, "status")));
                if (entry.Kind is EffectKind.Damage or EffectKind.Heal && entry.AmountDice.Length > 0)
                {
                    var amount = Formula(entry.AmountDice, id);
                    var ability = entry.Ability ? AbilityKey(entry.Ability) : default;
                    if (entry.Kind == EffectKind.Heal)
                        return new HealingEffect(amount, ability);
                    return new DamageEffect(Formula(entry.HitDice, id), amount, ability, entry.CriticalRaces.Select(RaceKey));
                }
                throw Invalid("skill effect", id);
            }

            var raceDefinitions = races.Select(item => new RaceDefinition(new RaceId(item.Id), item.Source, item.Label,
                item.LegacyName, item.Adjustments
                    .GroupBy(entry => AbilityKey(entry.Ability))
                    .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Points)),
                Grants(item.Grants))).ToArray();
            var classDefinitions = classes.Select(item =>
            {
                if (item.BaseHp < 0)
                    throw Invalid("class base HP", item.Id);
                return new ClassDefinition(new ClassId(item.Id), item.Source, item.Label, item.LegacyName, item.BaseHp, Grants(item.Grants));
            }).ToArray();
            var abilityDefinitions = abilities.Select(item =>
                new AbilityDefinition(new AbilityId(item.Id), item.Source, item.Label, item.LegacyName)).ToArray();
            var skillDefinitions = skills.Select(item =>
            {
                bool passive = item.Category == SkillType.RacePassive;
                if (!Enum.IsDefined(typeof(SkillType), item.Category)
                    || !Enum.IsDefined(typeof(TargetMode), item.TargetMode) || item.Range < 0
                    || item.ActionCost != (passive ? 0 : 1) || item.MaxDistinctTargets < 1
                    || item.MaxDistinctTargets > 1 && (item.ActionCost == 0
                        || item.TargetMode is not (TargetMode.SelectedEnemy or TargetMode.SelectedAlly))
                    || passive != (item.Effects.Length == 0))
                    throw Invalid("skill policy", item.Id);

                var targets = new TargetPolicy(item.TargetMode, item.Range, item.RequiredRace ? RaceKey(item.RequiredRace) : default);
                var action = new ActionPolicy(item.ActionCost, item.MaxDistinctTargets);
                return new SkillDefinition(new SkillId(item.Id), item.Category, item.Detail, item.Label, item.LegacyName,
                    item.Icon, targets, action, item.Effects.Select(effect => Effect(effect, item.Id)).ToArray());
            }).ToArray();
            var statusDefinitions = statuses.Select(item =>
            {
                if (item.Duration < -1 || item.Duration == 0 || item.ExtraHitDicePerTerm is < 0 or > 20
                    || item.BonusDamageRace && item.BonusDamageDice.Length == 0)
                    throw Invalid("status policy", item.Id);

                Resolve(skillAssets, item.LabelSource, "status label skill");
                return new StatusDefinition(new StatusId(item.Id), item.Duration, item.ExpiresAtTargetTurn,
                    item.ExpiresAtSourceTurn, item.BlocksAction, item.Taunts, item.Dodge, item.AbilityPointBonus,
                    item.BonusAbility ? AbilityKey(item.BonusAbility) : default, item.ExtraHitDicePerTerm,
                    item.BonusDamageAbility ? AbilityKey(item.BonusDamageAbility) : default,
                    item.BonusDamageDice.Length == 0 ? null : Formula(item.BonusDamageDice, item.Id),
                    item.BonusDamageRace ? RaceKey(item.BonusDamageRace) : default, item.ConsumeOnAttack,
                    item.LabelSource.Label);
            }).ToArray();
            var portraits = new Dictionary<(RaceId, ClassId), (Texture, Sprite)>();
            foreach (var raceAsset in races)
            {
                var race = new RaceId(raceAsset.Id);
                foreach (var item in raceAsset.Artwork)
                {
                    var characterClass = item.CharacterClass ? new ClassId(Resolve(classAssets, item.CharacterClass, "class")) : default;
                    if ((!item.Portrait && !item.Token) || !portraits.TryAdd((race, characterClass), (item.Portrait, item.Token)))
                        throw Invalid("artwork", race.Value);
                }
            }

            return new RulesCatalog(raceDefinitions, classDefinitions, abilityDefinitions, skillDefinitions, statusDefinitions,
                Aliases(races, raceDefinitions), Aliases(classes, classDefinitions),
                Aliases(abilities, abilityDefinitions), Aliases(skills, skillDefinitions), portraits, supported);
        }

        private static HashSet<T> Index<T>(T[] entries, string kind, bool allowEmpty = false) where T : RuleAsset
        {
            if (!allowEmpty && entries.Length == 0)
                throw Invalid(kind, null);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var assets = new HashSet<T>();
            foreach (var entry in entries)
            {
                if (!entry || !ValidId(entry.Id) || entry is NamedRuleAsset named && string.IsNullOrWhiteSpace(named.Label)
                    || !ids.Add(entry.Id))
                    throw Invalid(kind, entry ? entry.Id : null);
                assets.Add(entry);
            }
            return assets;
        }

        private static void ValidateLegacySources<T>(IEnumerable<T> entries, string kind) where T : UnityEngine.Object
        {
            var sources = new HashSet<T>();
            foreach (var entry in entries)
                if (!entry || !sources.Add(entry))
                    throw Invalid("legacy " + kind + " source", entry ? entry.name : null);
        }

        private static Dictionary<string, T> Aliases<T>(NamedRuleAsset[] entries, T[] definitions)
        {
            var aliases = new Dictionary<string, T>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                foreach (var alias in entry.Aliases.Prepend(entry.LegacyName))
                    if (string.IsNullOrWhiteSpace(alias) || !aliases.TryAdd(alias, definitions[index]))
                        throw Invalid("legacy alias", alias);
            }
            return aliases;
        }

        private static string Resolve<T>(HashSet<T> assets, T asset, string kind) where T : RuleAsset =>
            asset && assets.Contains(asset) ? asset.Id : throw Invalid(kind + " reference", asset ? asset.name : null);

        private static bool ValidId(string id) => !string.IsNullOrEmpty(id)
            && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

        private static InvalidOperationException Invalid(string kind, string value) =>
            new($"Invalid or duplicate {kind} in the ASAL rules catalog: {value ?? "(missing)"}.");
    }
}
