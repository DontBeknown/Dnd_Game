using System;
using TMPro;
using UnityEngine;
using Button = UnityEngine.UI.Button;

namespace ASAL
{
    public sealed class CharacterPickerLayout : CharacterFormLayout
    {
        [SerializeField] private TMP_Dropdown monsterRecords;
        [SerializeField] private Button create;
        [SerializeField] private Button createMonster;
        [SerializeField] private Button saveMonster;
        [SerializeField] private Button deleteMonster;
        [SerializeField] private TMP_Text levelValue;
        [SerializeField] private TMP_Text hpValue;
        [SerializeField] private TMP_Text remainingPoints;
        [SerializeField] private TMP_Text[] abilityLabels;
        [SerializeField] private TMP_Text[] scoreValues;
        [SerializeField] private TMP_Text[] modifierValues;
        [SerializeField] private Button[] increaseScores;
        [SerializeField] private Button[] decreaseScores;
        [SerializeField] private Button increaseLevel;
        [SerializeField] private Button decreaseLevel;
        [SerializeField] private Button reset;
        [SerializeField] private Button confirmSelection;

        private CharacterEditorPanel panel;
        private bool enemy;

        private Button CreateButton => enemy ? createMonster : create;
        internal override TMP_Dropdown Records => enemy ? monsterRecords : base.Records;
        internal override Button SaveButton => enemy ? saveMonster : base.SaveButton;
        internal override Button DeleteButton => enemy ? deleteMonster : base.DeleteButton;
        internal override bool UsesTemplateDraft => true;

        internal override void Bind(CharacterEditorPanel panel, bool enemy)
        {
            this.panel = panel;
            this.enemy = enemy;
            base.Records.gameObject.SetActive(!enemy);
            monsterRecords.gameObject.SetActive(enemy);
            (enemy ? create : createMonster).gameObject.SetActive(false);
            (enemy ? base.SaveButton : saveMonster).gameObject.SetActive(false);
            (enemy ? base.DeleteButton : deleteMonster).gameObject.SetActive(false);

            CreateButton.onClick.AddListener(panel.Save);
            confirmSelection.onClick.AddListener(panel.ChooseSaved);
            reset.onClick.AddListener(panel.ResetDraft);
            increaseLevel.onClick.AddListener(() => StepLevel(1));
            decreaseLevel.onClick.AddListener(() => StepLevel(-1));
            for (int index = 0; index < scoreValues.Length; index++)
            {
                abilityLabels[index].text = $"{Characters.Abilities[index].Label} :";
                int scoreIndex = index;
                increaseScores[index].onClick.AddListener(() => StepAbility(scoreIndex, 1));
                decreaseScores[index].onClick.AddListener(() => StepAbility(scoreIndex, -1));
            }
        }

        internal override void Render(CharacterDraft draft)
        {
            hpValue.text = $"HP : {draft.Hp}";
            levelValue.text = $"Level : {draft.Level}";
            remainingPoints.text = $"Ability Points : {draft.AbilityPoints}";
            for (int index = 0; index < scoreValues.Length; index++)
            {
                var ability = FindAbility(draft, index);
                scoreValues[index].text = ability.Points.ToString();
                modifierValues[index].text = CharacterManager.AbilityModifier(ability).ToString("+0;-0;0");
            }
        }

        internal override void SetControls(CharacterDraft draft, bool busy, bool hasRecord, bool canSelect, bool dirty)
        {
            bool canEdit = !busy;
            CreateButton.gameObject.SetActive(!hasRecord);
            CreateButton.interactable = !busy;
            SaveButton.gameObject.SetActive(hasRecord);
            DeleteButton.gameObject.SetActive(hasRecord);
            confirmSelection.gameObject.SetActive(hasRecord && canSelect);
            confirmSelection.interactable = canEdit && hasRecord && !dirty;
            reset.interactable = canEdit;
            increaseLevel.interactable = canEdit && draft.Level < int.MaxValue;
            decreaseLevel.interactable = canEdit && draft.Level > 1;
            for (int index = 0; index < scoreValues.Length; index++)
            {
                var ability = FindAbility(draft, index);
                increaseScores[index].interactable = canEdit && CharacterManager.CanAdjustAbility(draft, ability, 1);
                decreaseScores[index].interactable = canEdit && CharacterManager.CanAdjustAbility(draft, ability, -1);
            }
        }

        private void StepAbility(int index, int change)
        {
            var draft = panel.EditableDraft;
            if (draft == null)
                return;
            var ability = FindAbility(draft, index);
            if (CharacterManager.TryAdjustAbility(draft, ability, change))
                panel.UpdateDerivedStats();
        }

        private void StepLevel(int change)
        {
            var draft = panel.EditableDraft;
            if (draft == null)
                return;
            int next = (int)Math.Clamp((long)draft.Level + change, 1L, int.MaxValue);
            if (next == draft.Level)
                return;
            draft.Level = next;
            panel.UpdateDerivedStats();
        }
    }
}
