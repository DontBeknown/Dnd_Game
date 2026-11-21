using System;
using UnityEngine;

namespace ASAL
{
    [CreateAssetMenu(menuName = "ASAL/Rules/Skill")]
    public sealed class SkillRuleAsset : NamedRuleAsset
    {
        [SerializeField] private SkillType category;
        [TextArea] [SerializeField] private string detail;
        [SerializeField] private Texture icon;
        [SerializeField] private TargetMode targetMode;
        [SerializeField] private int range = int.MaxValue;
        [SerializeField] private RaceRuleAsset requiredRace;
        [SerializeField] private int actionCost = 1;
        [SerializeField] private int maxDistinctTargets = 1;
        [SerializeField] private SkillEffectEntry[] effects = Array.Empty<SkillEffectEntry>();

        internal SkillType Category => category;
        internal string Detail => detail;
        internal Texture Icon => icon;
        internal TargetMode TargetMode => targetMode;
        internal int Range => range;
        internal RaceRuleAsset RequiredRace => requiredRace;
        internal int ActionCost => actionCost;
        internal int MaxDistinctTargets => maxDistinctTargets;
        internal SkillEffectEntry[] Effects => effects;
    }

    internal enum EffectKind { Damage, Heal, ApplyStatus }

    [Serializable]
    internal sealed class SkillEffectEntry
    {
        [SerializeField] private EffectKind kind;
        [SerializeField] private AbilityRuleAsset ability;
        [SerializeField] private DiceTermEntry[] hitDice = Array.Empty<DiceTermEntry>();
        [SerializeField] private DiceTermEntry[] amountDice = Array.Empty<DiceTermEntry>();
        [SerializeField] private RaceRuleAsset[] criticalRaces = Array.Empty<RaceRuleAsset>();
        [SerializeField] private StatusRuleAsset status;

        internal EffectKind Kind => kind;
        internal AbilityRuleAsset Ability => ability;
        internal DiceTermEntry[] HitDice => hitDice;
        internal DiceTermEntry[] AmountDice => amountDice;
        internal RaceRuleAsset[] CriticalRaces => criticalRaces;
        internal StatusRuleAsset Status => status;
    }

    [Serializable]
    internal sealed class DiceTermEntry
    {
        [SerializeField] private Dice die;
        [SerializeField] private int count = 1;

        internal Dice Die => die;
        internal int Count => count;
    }
}
