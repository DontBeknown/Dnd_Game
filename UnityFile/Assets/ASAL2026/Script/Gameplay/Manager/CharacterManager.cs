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

        private readonly Dictionary<string, TokenView> views = new();
        private readonly Stack<TokenView> available = new();

        internal Race[] Races { get; private set; }
        internal CharacterClass[] Classes { get; private set; }
        internal AbilityScore[] Abilities { get; private set; }
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

        internal void Initialize()
        {
            Races = Resources.LoadAll<Race>("Races").OrderBy(item => item.raceName).ToArray();
            Classes = Resources.LoadAll<CharacterClass>("Classes").OrderBy(item => item.className).ToArray();
            Abilities = Resources.LoadAll<AbilityScore>("AbilityScores").OrderBy(item => item.abilityName).ToArray();
            heroTemplate = Resources.Load<Character>("TemplateCharacter");
            enemyTemplate = Resources.Load<Character>("TemplateEnemy");
        }

        internal Task<CharacterData[]> LoadAsync(AccountProfile account, bool enemy)
        {
            CheckAccess(account, enemy);
            return SaveManager.Instance.LoadCharactersAsync(account.Uid, enemy);
        }

        internal Task SaveAsync(AccountProfile account, bool enemy, CharacterData record)
        {
            CheckAccess(account, enemy);
            record.characterName = record.characterName?.Trim();
            if (string.IsNullOrEmpty(record.characterName) || record.characterName.Length > 64
                || record.level < 1 || record.HP < 1 || record.abilityPoint < 0)
                throw new FormatException("Enter a name, positive level and HP, and valid ability scores.");
            if (!Races.Any(item => item.raceName == record.raceName)
                || !Classes.Any(item => item.className == record.characterClassName))
                throw new FormatException("Select an existing race and class.");
            record.firebaseKey ??= SaveManager.Instance.NewCharacterId(account.Uid, enemy);
            return SaveManager.Instance.SaveCharacterAsync(account.Uid, enemy, record);
        }

        internal Task DeleteAsync(AccountProfile account, bool enemy, string id)
        {
            CheckAccess(account, enemy);
            return SaveManager.Instance.DeleteCharacterAsync(account.Uid, enemy, id);
        }

        internal MatchCharacter Project(CharacterData record)
        {
            if (record == null)
                return null;

            var race = Array.Find(Races, item => item.raceName == record.raceName);
            var characterClass = Array.Find(Classes, item => item.className == record.characterClassName);
            var skills = CatalogSkills(race, characterClass);
            var projection = new MatchCharacter
            {
                Name = record.characterName, Race = record.raceName, Class = record.characterClassName,
                MaxHp = record.HP, Level = record.level,
                Abilities = Abilities.Select(ability =>
                {
                    var score = record.abilityScorePoints?.Find(item => item?.abilityScoreName == ability.abilityName);
                    return new MatchAbility { Name = ability.abilityName, Points = score?.abilityScorePoint ?? 10,
                        Bonus = score?.ablityModifierBonus ?? 0 };
                }).ToArray(),
                Skills = record.skillData is { Count: > 0 }
                    ? skills.Intersect(record.skillData.Select(item => item?.skillName)).ToArray() : skills
            };
            return ValidProjection(projection) ? projection : null;
        }

        internal bool ValidProjection(MatchCharacter character)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.Name) || character.Name.Length > 64
                || character.MaxHp < 1 || character.Level < 1 || character.Skills == null
                || character.Abilities == null || character.Abilities.Length != Abilities.Length
                || character.Abilities.Any(score => score == null || score.Points < 0
                    || !Abilities.Any(ability => ability.abilityName == score.Name))
                || character.Abilities.Select(score => score.Name).Distinct().Count() != Abilities.Length)
                return false;

            var race = Array.Find(Races, item => item.raceName == character.Race);
            var characterClass = Array.Find(Classes, item => item.className == character.Class);
            if (!race || !characterClass)
                return false;
            var availableSkills = CatalogSkills(race, characterClass);
            return character.Skills.Length <= availableSkills.Length && character.Skills.All(availableSkills.Contains)
                && character.Skills.Distinct().Count() == character.Skills.Length;
        }

        private static string[] CatalogSkills(Race race, CharacterClass characterClass) =>
            (characterClass?.classSkills ?? new List<Skill>()).Concat(race?.raceSkills ?? new List<Skill>())
                .Where(skill => skill).Select(skill => skill.skillName).Distinct().ToArray();

        internal CharacterData NewDraft() => new()
        {
            level = 1,
            HP = 1,
            abilityScorePoints = Abilities.Select(ability => new CharacterData.AbilityScorePointsData
            {
                abilityScoreName = ability.abilityName, abilityScorePoint = 10
            }).ToList(),
            skillData = new List<CharacterData.SkillData>()
        };

        internal CharacterData NewRoomDraft(bool enemy)
        {
            var template = enemy ? enemyTemplate : heroTemplate;
            var draft = new CharacterData
            {
                characterName = template.characterName,
                level = template.level,
                abilityPoint = template.abilityPoint,
                abilityScorePoints = template.abilityScorepoints.Select(score => new CharacterData.AbilityScorePointsData
                {
                    abilityScoreName = score.abilityScore.abilityName,
                    abilityScorePoint = score.abilityScorePoint,
                    ablityModifierBonus = score.ablityModifierBonus
                }).ToList()
            };
            SetCatalog(draft, template.race, template.characterClass);
            return draft;
        }

        internal static bool CanAdjustAbility(CharacterData draft, CharacterData.AbilityScorePointsData score, int change)
        {
            int value = score?.abilityScorePoint ?? 0;
            return change == 1 ? draft.abilityPoint > 0 && value < int.MaxValue
                : change == -1 && value > 0 && draft.abilityPoint < int.MaxValue;
        }

        internal static bool TryAdjustAbility(CharacterData draft, CharacterData.AbilityScorePointsData score, int change)
        {
            if (score == null || !CanAdjustAbility(draft, score, change))
                return false;
            score.abilityScorePoint += change;
            draft.abilityPoint -= change;
            return true;
        }

        internal static int AbilityModifier(CharacterData.AbilityScorePointsData score) => score == null ? 0
            : (int)Math.Clamp((long)Math.Floor((score.abilityScorePoint - 10L) / 2.0)
                + score.ablityModifierBonus, int.MinValue, int.MaxValue);

        internal bool CanChangeRace(CharacterData draft, Race race)
        {
            var previous = Array.Find(Races, item => item.raceName == draft.raceName);
            return draft.abilityScorePoints.All(ability => RaceAdjustedScore(ability, previous, race) is >= 0 and <= int.MaxValue);
        }

        internal static int CalculateHp(CharacterData draft, int baseHp)
        {
            var constitution = draft.abilityScorePoints.Find(ability => ability.abilityScoreName == "Constitution");
            return (int)Math.Clamp(baseHp + (4L + AbilityModifier(constitution)) * draft.level, 1L, int.MaxValue);
        }

        internal void SetCatalog(CharacterData draft, Race race, CharacterClass characterClass)
        {
            bool raceChanged = draft.raceName != race.raceName;
            bool classChanged = draft.characterClassName != characterClass.className;
            if (!raceChanged && !classChanged)
                return;

            if (raceChanged)
            {
                var previous = Array.Find(Races, item => item.raceName == draft.raceName);
                foreach (var ability in draft.abilityScorePoints)
                {
                    ability.abilityScorePoint = (int)RaceAdjustedScore(ability, previous, race);
                }
            }

            draft.raceName = race.raceName;
            draft.characterClassName = characterClass.className;
            draft.skillData = (characterClass.classSkills ?? new List<Skill>())
                .Concat(race.raceSkills ?? new List<Skill>()).Where(skill => skill).Distinct()
                .Select(skill => new CharacterData.SkillData { skillName = skill.skillName, skillType = skill.skillType.ToString() })
                .ToList();
            draft.HP = CalculateHp(draft, characterClass.baseHP);
        }

        private static int Bonus(Race race, string ability) => race?.abilityScoreModifiers?
            .Where(item => item.abilityScore && item.abilityScore.abilityName == ability).Sum(item => item.modifierPoints) ?? 0;

        private static long RaceAdjustedScore(CharacterData.AbilityScorePointsData ability, Race previous, Race next) =>
            (long)ability.abilityScorePoint + Bonus(next, ability.abilityScoreName) - Bonus(previous, ability.abilityScoreName);

        internal void Reconcile(MatchSnapshot snapshot, TokenView prefab, Transform root, Grid grid)
        {
            var placed = snapshot.Tokens.Where(token => token.Placed).Select(token => token.Id).ToHashSet();
            foreach (var id in views.Keys.Where(id => !placed.Contains(id)).ToArray())
            {
                var view = views[id];
                views.Remove(id);
                view.gameObject.SetActive(false);
                available.Push(view);
            }

            foreach (var token in snapshot.Tokens.Where(token => token.Placed))
            {
                if (!views.TryGetValue(token.Id, out var view))
                {
                    view = available.Count == 0 ? Instantiate(prefab, root, false) : available.Pop();
                    views.Add(token.Id, view);
                }
                view.Bind(token, grid.GetCellCenterWorld(token.Cell));
                view.gameObject.SetActive(true);
            }
        }

        internal void ClearViews()
        {
            foreach (var view in views.Values.Concat(available))
                if (view)
                    Destroy(view.gameObject);
            views.Clear();
            available.Clear();
        }

        private static void CheckAccess(AccountProfile account, bool enemy)
        {
            if (account == null || !ReferenceEquals(AuthManager.Instance?.CurrentAccount, account))
                throw new InvalidOperationException("Sign in to manage saved records.");
            if (enemy && RoomManager.Instance is not ({ IsDm: true, CanEditRoom: true } or { IsDm: true, CanLeave: true, Phase: RoomPhase.Playing }))
                throw new InvalidOperationException("Only the room DM can manage enemy templates.");
        }
    }
}

