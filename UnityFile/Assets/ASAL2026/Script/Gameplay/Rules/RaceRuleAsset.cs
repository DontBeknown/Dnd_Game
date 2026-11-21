using System;
using UnityEngine;

namespace ASAL
{
    [CreateAssetMenu(menuName = "ASAL/Rules/Race")]
    public sealed class RaceRuleAsset : NamedRuleAsset
    {
        [Tooltip("Legacy race asset used by existing character templates.")] [SerializeField] private Race source;
        [SerializeField] private RaceAdjustmentEntry[] adjustments = Array.Empty<RaceAdjustmentEntry>();
        [SerializeField] private SkillRuleAsset[] grants = Array.Empty<SkillRuleAsset>();
        [SerializeField] private RaceArtworkEntry[] artwork = Array.Empty<RaceArtworkEntry>();

        internal Race Source => source;
        internal RaceAdjustmentEntry[] Adjustments => adjustments;
        internal SkillRuleAsset[] Grants => grants;
        internal RaceArtworkEntry[] Artwork => artwork;
    }

    [Serializable]
    internal sealed class RaceAdjustmentEntry
    {
        [SerializeField] private AbilityRuleAsset ability;
        [SerializeField] private int points;

        internal AbilityRuleAsset Ability => ability;
        internal int Points => points;
    }

    [Serializable]
    internal sealed class RaceArtworkEntry
    {
        [Tooltip("Leave empty for the default artwork for this race.")] [SerializeField] private ClassRuleAsset characterClass;
        [SerializeField] private Texture portrait;
        [SerializeField] private Sprite token;

        internal ClassRuleAsset CharacterClass => characterClass;
        internal Texture Portrait => portrait;
        internal Sprite Token => token;
    }
}
