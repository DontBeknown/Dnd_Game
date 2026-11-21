using System;
using UnityEngine;

namespace ASAL
{
    [CreateAssetMenu(menuName = "ASAL/Rules/Class")]
    public sealed class ClassRuleAsset : NamedRuleAsset
    {
        [Tooltip("Legacy class asset used by existing character templates.")] [SerializeField] private CharacterClass source;
        [SerializeField] private int baseHp;
        [SerializeField] private SkillRuleAsset[] grants = Array.Empty<SkillRuleAsset>();

        internal CharacterClass Source => source;
        internal int BaseHp => baseHp;
        internal SkillRuleAsset[] Grants => grants;
    }
}
