using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ASAL.PlayHudControls;

namespace ASAL
{
    public sealed class PlayMapHud : MonoBehaviour
    {
        [SerializeField] private GameObject mapPanel, enemyPanel, enemyRow;
        [SerializeField] private Button enemyList, loadMap;
        [SerializeField] private TMP_Dropdown mapChoice;

        private PlayHud hud;
        private PlayPanel play;
        private RoomManager room;

        internal void Initialize(PlayHud owner, PlayPanel panel, RoomManager rooms)
        {
            hud = owner;
            play = panel;
            room = rooms;
            enemyPanel.SetActive(false);
            enemyRow.SetActive(false);
            enemyList.onClick.AddListener(() => hud.ToggleModal(enemyPanel, enemyList));
            loadMap.onClick.AddListener(() => play.LoadSavedMap(mapChoice.value));
        }

        internal void RefreshRole(bool isDm)
        {
            mapPanel.SetActive(isDm);
            enemyList.gameObject.SetActive(isDm);
        }

        internal void SetMaps(IEnumerable<string> names, int selection, bool busy)
        {
            SetOptions(mapChoice, names, selection);
            mapChoice.interactable &= !busy && room.IsDm;
            loadMap.interactable = mapChoice.interactable;
        }

        internal void SetEnemies(IEnumerable<CharacterDraft> templates)
        {
            ClearRows(enemyRow);
            foreach (var template in templates)
            {
                var row = AddRow(enemyRow);
                SetText(Text(row, "EnemyNameText"), template.Name);
                SetText(Text(row, "EnemyLevelText"), "Level " + template.Level);
                SetText(Text(row, "EnemyHP"), "HP " + template.Hp);
                row.GetComponentInChildren<Button>(true).onClick.AddListener(() =>
                {
                    hud.CloseModal();
                    play.StartEnemyPlacement(template);
                });
            }
        }
    }
}
