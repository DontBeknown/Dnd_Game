using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using UnityEngine;

namespace ASAL
{
    public sealed partial class SaveManager
    {
        private const int CharacterSchemaVersion = 2;

        private RulesCatalog rulesCatalog;

        internal Task<CharacterDraft[]> LoadCharactersAsync(string uid) => LoadCharactersAsync(uid, false);

        internal async Task<CharacterDraft[]> LoadCharactersAsync(string uid, bool enemy)
        {
            var snapshot = await CharacterRecords(uid, enemy).GetValueAsync();
            return snapshot.Children.Select(child => ReadCharacter(child.Key, child.GetRawJsonValue(), rulesCatalog))
                .Where(record => record != null)
                .OrderBy(record => record.Name, StringComparer.CurrentCulture)
                .ThenBy(record => record.RecordId, StringComparer.Ordinal).ToArray();
        }

        internal string NewCharacterId(string uid, bool enemy) => CharacterRecords(uid, enemy).Push().Key;

        internal Task SaveCharacterAsync(string uid, bool enemy, CharacterDraft record) =>
            CharacterRecords(uid, enemy).Child(CharacterId(record.RecordId)).UpdateChildrenAsync(CharacterUpdate(record, rulesCatalog));

        internal Task DeleteCharacterAsync(string uid, bool enemy, string id) =>
            CharacterRecords(uid, enemy).Child(CharacterId(id)).RemoveValueAsync();

        private DatabaseReference CharacterRecords(string uid, bool enemy) =>
            Records(enemy ? "enemy_data" : "character_data", uid);

        private static string CharacterId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(".#$[]/".ToCharArray()) >= 0 || id.Any(char.IsControl))
                throw new FormatException("The character ID is invalid.");
            return id;
        }

        private static CharacterDraft ReadCharacter(string id, string json, RulesCatalog catalog)
        {
            var record = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<CharacterRecord>(json);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(record?.characterName))
                return null;

            var race = catalog.Race(new RaceId(record.raceId));
            var characterClass = catalog.Class(new ClassId(record.classId));
            bool legacy = record.schemaVersion < CharacterSchemaVersion
                || record.schemaVersion == CharacterSchemaVersion && race != null && characterClass != null && HasLegacyCollections(record);
            if (legacy)
            {
                race = catalog.LegacyRace(record.raceName);
                characterClass = catalog.LegacyClass(record.characterClassName);
            }
            var draft = new CharacterDraft
            {
                RecordId = id,
                Name = record.characterName,
                RaceId = race?.Id ?? default,
                ClassId = characterClass?.Id ?? default,
                Level = record.level,
                Hp = record.HP,
                AbilityPoints = record.abilityPoint
            };
            if (record.schemaVersion < 0 || record.schemaVersion > CharacterSchemaVersion)
                draft.RepairError = "This saved record uses an unsupported character format.";
            else if (race == null)
                draft.RepairError = "This saved record references an unknown race and needs repair.";
            else if (characterClass == null)
                draft.RepairError = "This saved record references an unknown class and needs repair.";

            var scores = new Dictionary<AbilityId, CharacterAbilityScore>();
            foreach (var score in record.abilityScorePoints ?? Array.Empty<AbilityRecord>())
            {
                var ability = legacy ? catalog.LegacyAbility(score?.abilityScoreName) : catalog.Ability(new AbilityId(score?.abilityId));
                if (ability == null || !scores.TryAdd(ability.Id, new CharacterAbilityScore(ability.Id, score.ablityModifierBonus)
                    { Points = score.abilityScorePoint }))
                    draft.RepairError ??= "This saved record has an unknown or duplicate ability and needs repair.";
            }
            foreach (var ability in catalog.Abilities)
                draft.AbilityScores.Add(scores.TryGetValue(ability.Id, out var score) ? score : new CharacterAbilityScore(ability.Id));

            if (race == null || characterClass == null)
                return draft;
            var grants = CharacterManager.GrantedSkills(race, characterClass);
            if (legacy && record.skillData is not { Length: > 0 })
            {
                draft.SkillIds = grants;
                return draft;
            }

            var skills = new HashSet<SkillId>();
            foreach (var item in record.skillData ?? Array.Empty<SkillRecord>())
            {
                var skill = legacy ? catalog.LegacySkill(item?.skillName) : catalog.Skill(new SkillId(item?.skillId));
                bool grantedAndUnique = skill != null && grants.Contains(skill.Id) && skills.Add(skill.Id);
                if (!legacy && !grantedAndUnique)
                    draft.RepairError ??= "This saved record has an unknown, duplicate, or ungranted skill and needs repair.";
            }
            draft.SkillIds = grants.Where(skills.Contains).ToArray();
            return draft;
        }

        private static bool HasLegacyCollections(CharacterRecord record) =>
            // Older Main rewrites both collections and legacy labels while leaving the top-level IDs in place.
            record.abilityScorePoints is { Length: > 0 } && record.abilityScorePoints.All(score =>
                score is { abilityId: null, abilityScorePoint: >= 0 } && !string.IsNullOrWhiteSpace(score.abilityScoreName))
            && (record.skillData == null || record.skillData.All(skill =>
                skill is { skillId: null } && !string.IsNullOrWhiteSpace(skill.skillName)));

        private static Dictionary<string, object> CharacterUpdate(CharacterDraft record, RulesCatalog catalog)
        {
            if (record.RepairError != null)
                throw new FormatException(record.RepairError);

            var race = catalog.Race(record.RaceId);
            var characterClass = catalog.Class(record.ClassId);
            return new Dictionary<string, object>
            {
                ["schemaVersion"] = CharacterSchemaVersion,
                ["characterName"] = record.Name,
                ["raceId"] = record.RaceId.Value,
                ["raceName"] = race.LegacyName,
                ["classId"] = record.ClassId.Value,
                ["characterClassName"] = characterClass.LegacyName,
                ["level"] = record.Level,
                ["HP"] = record.Hp,
                ["abilityPoint"] = record.AbilityPoints,
                ["abilityScorePoints"] = record.AbilityScores.Select(score => new Dictionary<string, object>
                {
                    ["abilityId"] = score.Id.Value,
                    ["abilityScoreName"] = catalog.Ability(score.Id).LegacyName,
                    ["abilityScorePoint"] = score.Points,
                    ["ablityModifierBonus"] = score.Bonus
                }).ToList(),
                ["skillData"] = record.SkillIds.Select(id =>
                {
                    var skill = catalog.Skill(id);
                    return new Dictionary<string, object>
                    {
                        ["skillId"] = id.Value,
                        ["skillName"] = skill.LegacyName,
                        ["skillType"] = skill.Category.ToString()
                    };
                }).ToList()
            };
        }

        [Serializable]
        private sealed class CharacterRecord
        {
            public int schemaVersion;
            public string characterName;
            public string raceId;
            public string raceName;
            public string classId;
            public string characterClassName;
            public int level;
            public int HP;
            public int abilityPoint;
            public AbilityRecord[] abilityScorePoints;
            public SkillRecord[] skillData;
        }

        [Serializable]
        private sealed class AbilityRecord
        {
            public string abilityId;
            public string abilityScoreName;
            public int abilityScorePoint;
            public int ablityModifierBonus;
        }

        [Serializable]
        private sealed class SkillRecord
        {
            public string skillId;
            public string skillName;
        }
    }
}
