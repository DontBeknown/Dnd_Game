using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ASAL.PlayHudControls;

namespace ASAL
{
    public sealed class PlayTurnHud : MonoBehaviour
    {
        [SerializeField] private GameObject turnPanel, combatFlag, explorationFlag, turnRow;
        [SerializeField] private Button turnList, changePhase, endTurn;

        private MatchSnapshot snapshot;
        private RoomManager room;

        internal void Initialize(RoomManager rooms)
        {
            room = rooms;
            turnPanel.SetActive(false);
            turnRow.SetActive(false);
            turnList.onClick.AddListener(() => turnPanel.SetActive(!turnPanel.activeSelf));
            changePhase.onClick.AddListener(() => CombatManager.Instance.ToggleEncounter());
            endTurn.onClick.AddListener(EndTurn);
        }

        internal void RefreshRole(bool isDm) => changePhase.gameObject.SetActive(isDm);

        private void EndTurn()
        {
            var active = snapshot?.Encounter?.ActiveTokenId;
            if (!string.IsNullOrEmpty(active))
                CombatManager.Instance.EndTurn(active);
        }

        internal void Apply(MatchSnapshot next)
        {
            var wasInCombat = snapshot?.Encounter != null;
            snapshot = next;
            var encounter = snapshot.Encounter;
            if ((encounter != null) != wasInCombat)
                turnPanel.SetActive(encounter != null);
            combatFlag.SetActive(encounter != null);
            explorationFlag.SetActive(encounter == null);
            turnList.gameObject.SetActive(encounter != null);
            endTurn.gameObject.SetActive(encounter != null);
            var activeId = encounter?.ActiveTokenId;
            endTurn.interactable = room.IsDm || next.Tokens.Any(token => token.Id == activeId && token.OwnerActor == room.LocalActor);
            ClearRows(turnRow);
            if (encounter != null)
            {
                foreach (var participant in encounter.TurnOrder)
                {
                    var token = next.Tokens.FirstOrDefault(item => item.Id == participant.TokenId);
                    if (token == null)
                        continue;
                    var row = AddRow(turnRow);
                    SetText(row.GetComponentInChildren<TMP_Text>(true), (activeId == token.Id ? "▶ " : "") +
                        token.Name + "  " + participant.Initiative +
                        (participant.Statuses.Length == 0 ? "" : "  " + string.Join(", ", participant.Statuses.Select(status => status.Name))));
                }
            }
        }
    }
}
