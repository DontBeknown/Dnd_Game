using UnityEngine;

namespace ASAL
{
    [CreateAssetMenu(menuName = "ASAL/Rules/Ability")]
    public sealed class AbilityRuleAsset : NamedRuleAsset
    {
        [Tooltip("Legacy ability asset used by existing character templates.")] [SerializeField] private AbilityScore source;

        internal AbilityScore Source => source;
    }
}
