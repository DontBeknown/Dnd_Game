using TMPro;
using UnityEngine;

namespace ASAL
{
    public sealed class CharacterEditorLayout : CharacterFormLayout
    {
        [SerializeField] private TMP_InputField level;
        [SerializeField] private TMP_InputField hp;
        [SerializeField] private TMP_InputField[] scores;
        [SerializeField] private TMP_Text[] abilityLabels;

        internal override void Bind(CharacterEditorPanel panel, bool enemy)
        {
            for (int index = 0; index < scores.Length; index++)
                abilityLabels[index].text = Characters.Abilities[index].Label;
        }

        internal override void LoadDraft(CharacterDraft draft) =>
            level.SetTextWithoutNotify(draft.Level.ToString());

        internal override bool PrepareCatalogChange(CharacterDraft draft)
        {
            if (int.TryParse(level.text, out var value) && value > 0)
                draft.Level = value;
            return ReadScores(draft);
        }

        internal override bool ReadForSave(CharacterDraft draft)
        {
            if (!int.TryParse(level.text, out var savedLevel) || !int.TryParse(hp.text, out var savedHp) || !ReadScores(draft))
                return false;

            draft.Level = savedLevel;
            draft.Hp = savedHp;
            return true;
        }

        internal override void Render(CharacterDraft draft)
        {
            hp.SetTextWithoutNotify(draft.Hp.ToString());
            for (int index = 0; index < scores.Length; index++)
                scores[index].SetTextWithoutNotify(FindAbility(draft, index).Points.ToString());
        }

        internal override void SetControls(CharacterDraft draft, bool busy, bool hasRecord, bool canSelect, bool dirty)
        {
            level.interactable = hp.interactable = !busy;
            foreach (var score in scores)
                score.interactable = !busy;
        }

        private bool ReadScores(CharacterDraft draft)
        {
            var values = new int[scores.Length];
            for (int index = 0; index < scores.Length; index++)
                if (!int.TryParse(scores[index].text, out values[index]) || values[index] < 0 || values[index] > 99)
                    return false;

            for (int index = 0; index < scores.Length; index++)
                FindAbility(draft, index).Points = values[index];
            return true;
        }
    }
}
