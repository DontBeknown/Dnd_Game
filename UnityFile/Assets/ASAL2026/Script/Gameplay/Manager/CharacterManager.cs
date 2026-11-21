using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ASAL
{
    public sealed class CharacterManager : MonoBehaviour
    {
        public static CharacterManager Instance { get; private set; }

        private string selectedTokenId;

        internal RulesCatalog Catalog { get; private set; }
        internal IReadOnlyList<RaceDefinition> Races => Catalog.Races;
        internal IReadOnlyList<ClassDefinition> Classes => Catalog.Classes;
        internal IReadOnlyList<AbilityDefinition> Abilities => Catalog.Abilities;
        private Character heroTemplate;
        private Character enemyTemplate;

        internal void Register()
        {
            if (Instance)
                throw new InvalidOperationException("Duplicate CharacterManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (Instance == this)
                Instance = null;
        }

        internal void Initialize(RulesCatalog catalog)
        {
            Catalog = catalog;
            heroTemplate = Resources.Load<Character>("TemplateCharacter");
            enemyTemplate = Resources.Load<Character>("TemplateEnemy");
            ValidateTemplate(heroTemplate);
            ValidateTemplate(enemyTemplate);
        }

        internal Task<CharacterDraft[]> LoadAsync(AccountProfile account, bool enemy)
        {
            CheckAccess(account, enemy);
            return SaveManager.Instance.LoadCharactersAsync(account.Uid, enemy);
        }

        internal Task SaveAsync(AccountProfile account, bool enemy, CharacterDraft record)
        {
            CheckAccess(account, enemy);
            if (record.RepairError != null)
                throw new FormatException(record.RepairError);
            record.Name = record.Name?.Trim();
            if (Project(record) == null || record.AbilityPoints < 0)
                throw new FormatException("Enter a name, positive level and HP, and valid ability scores.");
            record.RecordId ??= SaveManager.Instance.NewCharacterId(account.Uid, enemy);
            return SaveManager.Instance.SaveCharacterAsync(account.Uid, enemy, record);
        }

        internal Task DeleteAsync(AccountProfile account, bool enemy, string id)
        {
            CheckAccess(account, enemy);
            return SaveManager.Instance.DeleteCharacterAsync(account.Uid, enemy, id);
        }

        internal MatchCharacter Project(CharacterDraft record)
        {
            if (record == null || record.RepairError != null)
                return null;

            var projection = new MatchCharacter
            {
                Name = record.Name, RaceId = record.RaceId.Value, ClassId = record.ClassId.Value,
                MaxHp = record.Hp, Level = record.Level,
                Abilities = record.AbilityScores.Select(score => new MatchAbility
                {
                    AbilityId = score.Id.Value, Points = score.Points, Bonus = score.Bonus
                }).ToArray(),
                SkillIds = record.SkillIds.Select(skill => skill.Value).ToArray()
            };
            return ValidProjection(projection) ? projection : null;
        }

        internal bool ValidProjection(MatchCharacter character)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.Name) || character.Name.Length > 64
                || character.MaxHp < 1 || character.Level < 1 || character.SkillIds == null
                || character.Abilities == null || character.Abilities.Length != Abilities.Count
                || character.Abilities.Any(score => score == null || score.Points < 0
                    || string.IsNullOrEmpty(score.AbilityId) || Catalog.Ability(new AbilityId(score.AbilityId)) == null)
                || character.Abilities.Select(score => score.AbilityId).Distinct().Count() != Abilities.Count
                || string.IsNullOrEmpty(character.RaceId) || string.IsNullOrEmpty(character.ClassId))
                return false;

            var race = Catalog.Race(new RaceId(character.RaceId));
            var characterClass = Catalog.Class(new ClassId(character.ClassId));
            if (race == null || characterClass == null)
                return false;
            var availableSkills = GrantedSkills(race, characterClass);
            return character.SkillIds.Length <= availableSkills.Length
                && character.SkillIds.All(id => availableSkills.Contains(new SkillId(id)))
                && character.SkillIds.Distinct().Count() == character.SkillIds.Length;
        }

        internal static SkillId[] GrantedSkills(RaceDefinition race, ClassDefinition characterClass) =>
            characterClass.Skills.Concat(race.Skills).Distinct().ToArray();

        internal CharacterDraft NewDraft() => new()
        {
            Level = 1,
            Hp = 1,
            AbilityScores = Abilities.Select(ability => new CharacterAbilityScore(ability.Id)).ToList()
        };

        internal CharacterDraft NewRoomDraft(bool enemy)
        {
            var template = enemy ? enemyTemplate : heroTemplate;
            var draft = new CharacterDraft
            {
                Name = template.characterName,
                Level = template.level,
                AbilityPoints = template.abilityPoint,
                AbilityScores = Abilities.Select(ability =>
                {
                    var score = template.abilityScorepoints.Find(item => item.abilityScore == ability.Source);
                    return new CharacterAbilityScore(ability.Id, score?.ablityModifierBonus ?? 0)
                    {
                        Points = score?.abilityScorePoint ?? 10
                    };
                }).ToList()
            };
            SetCatalog(draft, Catalog.RaceFromSource(template.race), Catalog.ClassFromSource(template.characterClass));
            return draft;
        }

        private void ValidateTemplate(Character template)
        {
            if (!template || Catalog.RaceFromSource(template.race) == null || Catalog.ClassFromSource(template.characterClass) == null
                || template.abilityScorepoints == null || template.abilityScorepoints.Any(score =>
                    score == null || Catalog.AbilityFromSource(score.abilityScore) == null)
                || template.abilityScorepoints.Select(score => score.abilityScore).Distinct().Count() != template.abilityScorepoints.Count)
                throw new InvalidOperationException("A character template references missing or duplicate catalog definitions.");
        }

        internal static bool CanAdjustAbility(CharacterDraft draft, CharacterAbilityScore score, int change) =>
            score != null && (change == 1
                ? draft.AbilityPoints > 0 && score.Points < int.MaxValue
                : change == -1 && score.Points > 0 && draft.AbilityPoints < int.MaxValue);

        internal static bool TryAdjustAbility(CharacterDraft draft, CharacterAbilityScore score, int change)
        {
            if (!CanAdjustAbility(draft, score, change))
                return false;
            score.Points += change;
            draft.AbilityPoints -= change;
            return true;
        }

        internal static int AbilityModifier(CharacterAbilityScore score) => score == null ? 0
            : AbilityScoreValue.CalculateModifier(score.Points, score.Bonus);

        internal bool CanChangeRace(CharacterDraft draft, RaceDefinition race)
        {
            var previous = draft.RaceId.Value == null ? null : Catalog.Race(draft.RaceId);
            return draft.AbilityScores.All(ability => RaceAdjustedScore(ability, previous, race) is >= 0 and <= int.MaxValue);
        }

        internal static int CalculateHp(CharacterDraft draft, int baseHp)
        {
            var constitution = draft.Score(AbilityId.Constitution);
            return (int)Math.Clamp(baseHp + (4L + AbilityModifier(constitution)) * draft.Level, 1L, int.MaxValue);
        }

        internal void SetCatalog(CharacterDraft draft, RaceDefinition race, ClassDefinition characterClass)
        {
            bool raceChanged = draft.RaceId != race.Id;
            if (!raceChanged && draft.ClassId == characterClass.Id)
                return;

            if (raceChanged)
            {
                var previous = draft.RaceId.Value == null ? null : Catalog.Race(draft.RaceId);
                if (!CanChangeRace(draft, race))
                    throw new InvalidOperationException("Adjust your ability scores before changing to this race.");
                foreach (var ability in draft.AbilityScores)
                    ability.Points = (int)RaceAdjustedScore(ability, previous, race);
            }

            draft.RaceId = race.Id;
            draft.ClassId = characterClass.Id;
            draft.SkillIds = GrantedSkills(race, characterClass);
            draft.Hp = CalculateHp(draft, characterClass.BaseHp);
        }

        private static int Bonus(RaceDefinition race, AbilityId ability) =>
            race != null && race.AbilityAdjustments.TryGetValue(ability, out var bonus) ? bonus : 0;

        private static long RaceAdjustedScore(CharacterAbilityScore ability, RaceDefinition previous, RaceDefinition next) =>
            (long)ability.Points + Bonus(next, ability.Id) - Bonus(previous, ability.Id);

        internal static Vector3 BoardToWorld(Grid grid, Vector2 position)
        {
            var origin = grid.GetCellCenterWorld(Vector3Int.zero);
            var xStride = grid.GetCellCenterWorld(Vector3Int.right) - origin;
            var yStride = grid.GetCellCenterWorld(Vector3Int.up) - origin;
            return origin + xStride * position.x + yStride * position.y;
        }

        internal static Vector2 WorldToBoard(Grid grid, Vector3 position)
        {
            var origin = grid.GetCellCenterLocal(Vector3Int.zero);
            var offset = grid.transform.InverseTransformPoint(position) - origin;
            var xStride = grid.GetCellCenterLocal(Vector3Int.right) - origin;
            var yStride = grid.GetCellCenterLocal(Vector3Int.up) - origin;
            return new Vector2(Vector3.Dot(offset, xStride) / xStride.sqrMagnitude,
                Vector3.Dot(offset, yStride) / yStride.sqrMagnitude);
        }

        internal void SetSelected(string id)
        {
            var views = PhotonManager.Instance?.TokenViews;
            if (selectedTokenId != null && views != null && views.TryGetValue(selectedTokenId, out var previous) && previous)
                previous.SetSelected(false);
            selectedTokenId = id;
            if (id != null && views != null && views.TryGetValue(id, out var current) && current)
                current.SetSelected(true);
        }

        internal void ClearViews() => SetSelected(null);

        private static void CheckAccess(AccountProfile account, bool enemy)
        {
            if (account == null || !ReferenceEquals(AuthManager.Instance?.CurrentAccount, account))
                throw new InvalidOperationException("Sign in to manage saved records.");
            if (enemy && RoomManager.Instance is not ({ IsDm: true, CanEditRoom: true } or { IsDm: true, CanLeave: true, Phase: RoomPhase.Playing }))
                throw new InvalidOperationException("Only the room DM can manage enemy templates.");
        }
    }
}

