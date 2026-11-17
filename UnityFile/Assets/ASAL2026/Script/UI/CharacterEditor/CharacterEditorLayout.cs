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
                abilityLabels[index].text = Characters.Abilities[index].abilityName;
        }

        internal override void LoadDraft(CharacterData draft) =>
            level.SetTextWithoutNotify(draft.level.ToString());

        internal override bool PrepareCatalogChange(CharacterData draft, Race nextRace)
        {
            if (int.TryParse(level.text, out var value) && value > 0)
                draft.level = value;
            ReadScores(draft);
            return true;
        }

        internal override bool ReadForSave(CharacterData draft)
        {
            if (!int.TryParse(level.text, out var savedLevel) || !int.TryParse(hp.text, out var savedHp) || !ReadScores(draft))
                return false;

            draft.level = savedLevel;
            draft.HP = savedHp;
            return true;
        }

        internal override void Render(CharacterData draft)
        {
            hp.SetTextWithoutNotify(draft.HP.ToString());
            for (int index = 0; index < scores.Length; index++)
                scores[index].SetTextWithoutNotify((FindAbility(draft, index)?.abilityScorePoint ?? 0).ToString());
        }

        internal override void SetControls(CharacterData draft, bool busy, bool hasRecord, bool canSelect, bool dirty)
        {
            level.interactable = hp.interactable = !busy;
            foreach (var score in scores)
                score.interactable = !busy;
        }

        private bool ReadScores(CharacterData draft)
        {
            var values = new int[scores.Length];
            for (int index = 0; index < scores.Length; index++)
                if (!int.TryParse(scores[index].text, out values[index]) || values[index] < 0 || values[index] > 99)
                    return false;

            for (int index = 0; index < scores.Length; index++)
            {
                var ability = FindAbility(draft, index);
                if (ability == null)
                {
                    if (values[index] == 0)
                        continue;
                    ability = new CharacterData.AbilityScorePointsData
                    {
                        abilityScoreName = Characters.Abilities[index].abilityName
                    };
                    draft.abilityScorePoints.Add(ability);
                }
                ability.abilityScorePoint = values[index];
            }
            return true;
        }
    }
}
