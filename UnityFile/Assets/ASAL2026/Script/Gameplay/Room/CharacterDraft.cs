using System.Collections.Generic;
using System.Linq;

namespace ASAL
{
    internal sealed class CharacterDraft
    {
        internal string RecordId { get; set; }
        internal string Name { get; set; }
        internal RaceId RaceId { get; set; }
        internal ClassId ClassId { get; set; }
        internal int Level { get; set; }
        internal int Hp { get; set; }
        internal int AbilityPoints { get; set; }
        internal string RepairError { get; set; }
        internal List<CharacterAbilityScore> AbilityScores { get; set; } = new();
        internal IReadOnlyList<SkillId> SkillIds { get; set; } = System.Array.Empty<SkillId>();

        internal CharacterAbilityScore Score(AbilityId abilityId) =>
            AbilityScores.Find(score => score.Id == abilityId);

        internal CharacterDraft Copy() => new()
        {
            RecordId = RecordId,
            Name = Name,
            RaceId = RaceId,
            ClassId = ClassId,
            Level = Level,
            Hp = Hp,
            AbilityPoints = AbilityPoints,
            RepairError = RepairError,
            AbilityScores = AbilityScores.Select(score => new CharacterAbilityScore(score.Id, score.Bonus)
            {
                Points = score.Points
            }).ToList(),
            SkillIds = SkillIds
        };
    }

    internal sealed class CharacterAbilityScore
    {
        internal CharacterAbilityScore(AbilityId id, int bonus = 0)
        {
            Id = id;
            Bonus = bonus;
        }

        internal AbilityId Id { get; }
        internal int Bonus { get; }
        internal int Points { get; set; } = 10;
    }
}
