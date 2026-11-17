using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class RoomMemberRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text readyStatusText;
        [SerializeField] private Button kickButton;
        [SerializeField] private GameObject readyFlag;
        [SerializeField] private GameObject notReadyFlag;
        [SerializeField] private GameObject playerBG;
        [SerializeField] private GameObject dmBG;
        [SerializeField] private Color dmColor = new Color(0.8f, 0.8f, 1f);
        [SerializeField] private Color playerColor = new Color(1f, 1f, 1f);

        private int actor;

        private void Awake() => kickButton.onClick.AddListener(Kick);

        internal void Bind(RoomMember player, bool dm, bool canKick)
        {
            actor = player.Actor;
            string name = player.Name ?? "Player";
            if (dm)
                name += " (DM)";
            else if (!string.IsNullOrEmpty(player.CharacterName))
                name += $" ({player.CharacterName})";

            playerNameText.text = name;
            readyStatusText.text = dm ? string.Empty : player.Ready ? "Ready" : "Not Ready";
            playerNameText.color = dm ? dmColor : playerColor;
            readyFlag.SetActive(!dm && player.Ready);
            notReadyFlag.SetActive(!dm && !player.Ready);
            playerBG.SetActive(!dm);
            dmBG.SetActive(dm);
            kickButton.gameObject.SetActive(canKick && !dm && player.Active);
            gameObject.SetActive(true);
        }

        private void Kick() => RoomManager.Instance.Kick(actor);
    }
}
