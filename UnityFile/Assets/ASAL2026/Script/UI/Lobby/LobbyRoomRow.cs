using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class LobbyRoomRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text roomName;
        [SerializeField] private TMP_Text playerNumber;
        [SerializeField] private Button join;

        private string room;

        private void Awake() => join.onClick.AddListener(Join);

        internal void Bind(DiscoveredRoom entry, bool canJoin)
        {
            room = entry.Name;
            roomName.text = room;
            playerNumber.text = $"{entry.Count} / {entry.Capacity}";
            join.interactable = canJoin;
            gameObject.SetActive(true);
        }

        private void Join() => RoomManager.Instance.Join(room);
    }
}
