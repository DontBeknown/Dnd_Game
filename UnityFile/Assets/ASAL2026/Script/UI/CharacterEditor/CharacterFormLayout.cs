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
        internal virtual bool UsesTemplateDraft => false;

        internal abstract void Bind(CharacterEditorPanel panel, bool enemy);
        internal virtual void LoadDraft(CharacterDraft draft) { }
        internal virtual bool PrepareCatalogChange(CharacterDraft draft) => true;
        internal virtual bool ReadForSave(CharacterDraft draft) => true;
        internal abstract void Render(CharacterDraft draft);
        internal abstract void SetControls(CharacterDraft draft, bool busy, bool hasRecord, bool canSelect, bool dirty);

        internal static CharacterAbilityScore FindAbility(CharacterDraft draft, int index) =>
            draft.Score(Characters.Abilities[index].Id);
    }
}
