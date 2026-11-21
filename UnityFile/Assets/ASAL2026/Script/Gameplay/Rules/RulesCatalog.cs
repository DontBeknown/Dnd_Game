using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace ASAL
{
    internal sealed class RaceDefinition
    {
        internal RaceDefinition(RaceId id, Race source, string label, string legacyName,
            Dictionary<AbilityId, int> abilityAdjustments, SkillId[] skills)
        {
            Id = id;
            Source = source;
            Label = label;
            LegacyName = legacyName;
            AbilityAdjustments = new ReadOnlyDictionary<AbilityId, int>(abilityAdjustments);
            Skills = Array.AsReadOnly(skills);
        }

        internal RaceId Id { get; }
        internal Race Source { get; }
        internal string Label { get; }
        internal string LegacyName { get; }
        internal IReadOnlyDictionary<AbilityId, int> AbilityAdjustments { get; }
        internal IReadOnlyList<SkillId> Skills { get; }
    }

    internal sealed class ClassDefinition
    {
        internal ClassDefinition(ClassId id, CharacterClass source, string label, string legacyName, int baseHp, SkillId[] skills)
        {
            Id = id;
            Source = source;
            Label = label;
            LegacyName = legacyName;
            BaseHp = baseHp;
            Skills = Array.AsReadOnly(skills);
        }

        internal ClassId Id { get; }
        internal CharacterClass Source { get; }
        internal string Label { get; }
        internal string LegacyName { get; }
        internal int BaseHp { get; }
        internal IReadOnlyList<SkillId> Skills { get; }
    }

    internal sealed class AbilityDefinition
    {
        internal AbilityDefinition(AbilityId id, AbilityScore source, string label, string legacyName)
        {
            Id = id;
            Source = source;
            Label = label;
            LegacyName = legacyName;
        }

        internal AbilityId Id { get; }
        internal AbilityScore Source { get; }
        internal string Label { get; }
        internal string LegacyName { get; }
    }

    internal sealed class SkillDefinition
    {
        internal SkillDefinition(SkillId id, SkillType category, string detail, string label, string legacyName,
            Texture icon, TargetPolicy targeting, ActionPolicy action, SkillEffect[] effects)
        {
            Id = id;
            Label = label;
            LegacyName = legacyName;
            Detail = detail;
            Category = category;
            Icon = icon;
            Targeting = targeting;
            Action = action;
            Effects = Array.AsReadOnly(effects);
            HitDiceText = FormatDice(effects.Select(effect => effect.HitDiceText));
            DamageDiceText = FormatDice(effects.Select(effect => effect.DamageDiceText));
        }

        internal SkillId Id { get; }
        internal string Label { get; }
        internal string LegacyName { get; }
        internal string Detail { get; }
        internal SkillType Category { get; }
        internal Texture Icon { get; }
        internal TargetPolicy Targeting { get; }
        internal ActionPolicy Action { get; }
        internal IReadOnlyList<SkillEffect> Effects { get; }
        internal bool IsPassive => Category == SkillType.RacePassive;
        internal string HitDiceText { get; }
        internal string DamageDiceText { get; }

        private static string FormatDice(IEnumerable<string> expressions)
        {
            var combined = string.Join(" + ", expressions.Where(text => text != "-"));
            return combined.Length == 0 ? "-" : combined;
        }
    }

    internal sealed class RulesCatalog
    {
        private readonly Dictionary<RaceId, RaceDefinition> races;
        private readonly Dictionary<ClassId, ClassDefinition> classes;
        private readonly Dictionary<AbilityId, AbilityDefinition> abilities;
        private readonly Dictionary<SkillId, SkillDefinition> skills;
        private readonly Dictionary<StatusId, StatusDefinition> statuses;
        private readonly Dictionary<string, RaceDefinition> raceAliases;
        private readonly Dictionary<string, ClassDefinition> classAliases;
        private readonly Dictionary<string, AbilityDefinition> abilityAliases;
        private readonly Dictionary<string, SkillDefinition> skillAliases;
        private readonly Dictionary<Race, RaceDefinition> raceSources;
        private readonly Dictionary<CharacterClass, ClassDefinition> classSources;
        private readonly Dictionary<AbilityScore, AbilityDefinition> abilitySources;
        private readonly Dictionary<(RaceId, ClassId), (Texture Portrait, Sprite Token)> artwork;

        internal RulesCatalog(RaceDefinition[] raceDefinitions, ClassDefinition[] classDefinitions,
            AbilityDefinition[] abilityDefinitions, SkillDefinition[] skillDefinitions, StatusDefinition[] statusDefinitions,
            Dictionary<string, RaceDefinition> raceAliases, Dictionary<string, ClassDefinition> classAliases,
            Dictionary<string, AbilityDefinition> abilityAliases, Dictionary<string, SkillDefinition> skillAliases,
            Dictionary<(RaceId, ClassId), (Texture, Sprite)> artwork, HashSet<int> diceSides)
        {
            races = raceDefinitions.ToDictionary(item => item.Id);
            classes = classDefinitions.ToDictionary(item => item.Id);
            abilities = abilityDefinitions.ToDictionary(item => item.Id);
            skills = skillDefinitions.ToDictionary(item => item.Id);
            statuses = statusDefinitions.ToDictionary(item => item.Id);
            this.raceAliases = raceAliases;
            this.classAliases = classAliases;
            this.abilityAliases = abilityAliases;
            this.skillAliases = skillAliases;
            raceSources = raceDefinitions.ToDictionary(item => item.Source);
            classSources = classDefinitions.ToDictionary(item => item.Source);
            abilitySources = abilityDefinitions.ToDictionary(item => item.Source);
            this.artwork = artwork;
            DiceSides = Array.AsReadOnly(diceSides.OrderBy(side => side).ToArray());
            Races = Array.AsReadOnly(raceDefinitions);
            Classes = Array.AsReadOnly(classDefinitions);
            Abilities = Array.AsReadOnly(abilityDefinitions);
        }

        internal IReadOnlyList<RaceDefinition> Races { get; }
        internal IReadOnlyList<ClassDefinition> Classes { get; }
        internal IReadOnlyList<AbilityDefinition> Abilities { get; }
        internal IReadOnlyCollection<int> DiceSides { get; }

        internal static RulesCatalog Load()
        {
            var asset = Resources.Load<RulesCatalogAsset>("Rules/MainRules");
            if (!asset)
                throw new InvalidOperationException("The ASAL rules catalog asset is missing.");
            return asset.Build();
        }

        internal RaceDefinition Race(RaceId id) => races.GetValueOrDefault(id);
        internal ClassDefinition Class(ClassId id) => classes.GetValueOrDefault(id);
        internal AbilityDefinition Ability(AbilityId id) => abilities.GetValueOrDefault(id);
        internal SkillDefinition Skill(SkillId id) => skills.GetValueOrDefault(id);
        internal StatusDefinition Status(StatusId id) => statuses.GetValueOrDefault(id);
        internal RaceDefinition LegacyRace(string alias) => alias == null ? null : raceAliases.GetValueOrDefault(alias);
        internal ClassDefinition LegacyClass(string alias) => alias == null ? null : classAliases.GetValueOrDefault(alias);
        internal AbilityDefinition LegacyAbility(string alias) => alias == null ? null : abilityAliases.GetValueOrDefault(alias);
        internal SkillDefinition LegacySkill(string alias) => alias == null ? null : skillAliases.GetValueOrDefault(alias);
        internal RaceDefinition RaceFromSource(Race source) => source ? raceSources.GetValueOrDefault(source) : null;
        internal ClassDefinition ClassFromSource(CharacterClass source) => source ? classSources.GetValueOrDefault(source) : null;
        internal AbilityDefinition AbilityFromSource(AbilityScore source) => source ? abilitySources.GetValueOrDefault(source) : null;

        internal Texture Portrait(RaceId race, ClassId characterClass) =>
            artwork.GetValueOrDefault((race, characterClass)).Portrait ?? artwork.GetValueOrDefault((race, default)).Portrait;

        internal Sprite TokenArt(RaceId race, ClassId characterClass) =>
            artwork.GetValueOrDefault((race, characterClass)).Token ?? artwork.GetValueOrDefault((race, default)).Token;
    }
}
