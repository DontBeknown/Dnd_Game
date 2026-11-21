using System;
using UnityEngine;

namespace ASAL
{
    public abstract class RuleAsset : ScriptableObject
    {
        [SerializeField, Tooltip("Permanent ID used in saved records and matches.")] private string id;

        internal string Id => id;
    }

    public abstract class NamedRuleAsset : RuleAsset
    {
        [SerializeField] private string label;
        [Tooltip("Original name written for legacy save readers.")] [SerializeField] private string legacyName;
        [SerializeField] private string[] aliases = Array.Empty<string>();

        internal string Label => label;
        internal string LegacyName => legacyName;
        internal string[] Aliases => aliases;
    }
}
