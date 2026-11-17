using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ASAL
{
    public sealed class CharacterSkillHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private enum CatalogKind { Race, Class }

        [SerializeField] private CatalogKind catalog;
        [SerializeField] private GameObject detailPanel;
        [SerializeField] private TMP_Text header;
        [SerializeField] private Transform content;
        [SerializeField] private SkillListItem rowPrefab;

        private readonly List<SkillListItem> rows = new();
        private TMP_Dropdown dropdown;

        private void Awake()
        {
            dropdown = GetComponent<TMP_Dropdown>();
            Debug.Assert(detailPanel && header && content && rowPrefab, "Assign the skill preview references.", this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            var characters = CharacterManager.Instance;
            if (!characters || !detailPanel || !header || !content || !rowPrefab)
                return;

            int index = dropdown.value;
            if (catalog == CatalogKind.Race && characters.Races != null && index >= 0 && index < characters.Races.Length)
            {
                var race = characters.Races[index];
                Show(race.raceName, race.raceSkills);
            }
            else if (catalog == CatalogKind.Class && characters.Classes != null && index >= 0 && index < characters.Classes.Length)
            {
                var characterClass = characters.Classes[index];
                Show(characterClass.className, characterClass.classSkills);
            }
        }

        public void OnPointerExit(PointerEventData eventData) => Hide();

        private void OnDisable() => Hide();

        private void Show(string title, List<Skill> skills)
        {
            foreach (var row in rows)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();

            detailPanel.SetActive(true);
            header.text = title;
            if (skills == null)
                return;

            foreach (var skill in skills)
            {
                if (!skill)
                    continue;
                var row = Instantiate(rowPrefab, content);
                row.SetUp(skill.skillName, skill.detail);
                rows.Add(row);
            }
        }

        private void Hide()
        {
            if (detailPanel)
                detailPanel.SetActive(false);
        }
    }
}
