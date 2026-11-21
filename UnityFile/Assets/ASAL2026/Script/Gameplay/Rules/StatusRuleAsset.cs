using System;
using UnityEngine;

namespace ASAL
{
    [CreateAssetMenu(menuName = "ASAL/Rules/Status")]
    public sealed class StatusRuleAsset : RuleAsset
    {
        [SerializeField] private SkillRuleAsset labelSource;
        [SerializeField] private int duration = -1;
        [SerializeField] private bool expiresAtTargetTurn;
        [SerializeField] private bool expiresAtSourceTurn;
        [SerializeField] private bool blocksAction;
        [SerializeField] private bool taunts;
        [SerializeField] private bool dodge;
        [SerializeField] private int abilityPointBonus;
        [SerializeField] private AbilityRuleAsset bonusAbility;
        [SerializeField] private int extraHitDicePerTerm;
        [SerializeField] private AbilityRuleAsset bonusDamageAbility;
        [SerializeField] private DiceTermEntry[] bonusDamageDice = Array.Empty<DiceTermEntry>();
        [SerializeField] private RaceRuleAsset bonusDamageRace;
        [SerializeField] private bool consumeOnAttack;

        internal SkillRuleAsset LabelSource => labelSource;
        internal int Duration => duration;
        internal bool ExpiresAtTargetTurn => expiresAtTargetTurn;
        internal bool ExpiresAtSourceTurn => expiresAtSourceTurn;
        internal bool BlocksAction => blocksAction;
        internal bool Taunts => taunts;
        internal bool Dodge => dodge;
        internal int AbilityPointBonus => abilityPointBonus;
        internal AbilityRuleAsset BonusAbility => bonusAbility;
        internal int ExtraHitDicePerTerm => extraHitDicePerTerm;
        internal AbilityRuleAsset BonusDamageAbility => bonusDamageAbility;
        internal DiceTermEntry[] BonusDamageDice => bonusDamageDice;
        internal RaceRuleAsset BonusDamageRace => bonusDamageRace;
        internal bool ConsumeOnAttack => consumeOnAttack;
    }
}
