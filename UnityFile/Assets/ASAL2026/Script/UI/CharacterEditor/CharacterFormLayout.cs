using TMPro;
using UnityEngine;

namespace ASAL
{
    public abstract class CharacterFormLayout : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown records;
        [SerializeField] private UnityEngine.UI.Button save;
        [SerializeField] private UnityEngine.UI.Button delete;

        protected static CharacterManager Characters => CharacterManager.Instance;
        internal virtual TMP_Dropdown Records => records;
        internal virtual UnityEngine.UI.Button SaveButton => save;
        internal virtual UnityEngine.UI.Button DeleteButton => delete;
        internal virtual bool RequiresSavedRecord => false;

        internal abstract void Bind(CharacterEditorPanel panel, bool enemy);
        internal virtual void LoadDraft(CharacterData draft) { }
        internal virtual bool PrepareCatalogChange(CharacterData draft, Race nextRace) => true;
        internal virtual bool ReadForSave(CharacterData draft) => true;
        internal abstract void Render(CharacterData draft);
        internal abstract void SetControls(CharacterData draft, bool busy, bool hasRecord, bool canSelect, bool dirty);

        internal static CharacterData.AbilityScorePointsData FindAbility(CharacterData draft, int index) =>
            draft.abilityScorePoints.Find(item => item.abilityScoreName == Characters.Abilities[index].abilityName);
    }
}
