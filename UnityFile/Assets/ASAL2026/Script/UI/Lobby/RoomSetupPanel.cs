using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class RoomSetupPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text roomName;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Sprite readySprite;
        [SerializeField] private Sprite notReadySprite;
        [SerializeField] private Button leave;
        [SerializeField] private Button start;
        [SerializeField] private Button ready;
        [SerializeField] private Button selectCharacter;
        [SerializeField] private Button mapEditor;
        [SerializeField] private RectTransform content;
        [SerializeField] private RoomMemberRow rowPrefab;

        private readonly Dictionary<int, RoomMemberRow> rows = new();

        private void Awake()
        {
            var rooms = RoomManager.Instance;
            leave.onClick.AddListener(rooms.Leave);
            start.onClick.AddListener(rooms.StartMatch);
            ready.onClick.AddListener(() => rooms.SetReady(!rooms.LocalReady));
            selectCharacter.onClick.AddListener(UIManager.Instance.OpenCharacterPicker);
            mapEditor.onClick.AddListener(UIManager.Instance.OpenMapEditor);
        }

        internal void FocusFeature(bool isDm)
        {
            var button = isDm ? mapEditor : selectCharacter;
            if (button.isActiveAndEnabled && button.interactable)
                button.Select();
        }

        internal void Render(RoomManager model)
        {
            roomName.text = model.RoomName;
            leave.interactable = model.CanLeave;
            start.gameObject.SetActive(model.IsDm && model.Phase == RoomPhase.Setup);
            start.interactable = model.CanEditRoom;
            ready.gameObject.SetActive(!model.IsDm && model.Phase == RoomPhase.Setup);
            ready.interactable = model.CanEditRoom && model.HasSelection;
            selectCharacter.gameObject.SetActive(!model.IsDm && model.Phase == RoomPhase.Setup);
            mapEditor.gameObject.SetActive(model.IsDm && model.Phase == RoomPhase.Setup);
            selectCharacter.interactable = mapEditor.interactable = model.CanEditRoom;
            ready.image.sprite = model.LocalReady ? readySprite : notReadySprite;
            status.text = model.Error ?? (model.Phase == RoomPhase.Starting
                ? "Waiting for the initial match snapshot."
                : !model.CanLeave ? "Leaving room…"
                : !model.CanEditRoom ? "Updating room…"
                : !model.IsDm && !model.HasSelection ? "Select a saved character to ready up."
                : string.Empty);
            var actors = new HashSet<int>(model.Roster.Select(player => player.Actor));
            foreach (var actor in rows.Keys.Where(actor => !actors.Contains(actor)).ToArray())
            {
                rows[actor].gameObject.SetActive(false);
                Destroy(rows[actor].gameObject);
                rows.Remove(actor);
            }

            foreach (var player in model.Roster)
            {
                if (!rows.TryGetValue(player.Actor, out var row))
                {
                    row = Instantiate(rowPrefab, content, false);
                    rows.Add(player.Actor, row);
                }
                row.Bind(player, model.IsDmIdentity(player.Actor, player.UserId),
                    model.IsDm && model.CanEditRoom);
            }

            var events = EventSystem.current;
            if (events && leave.interactable && (!events.currentSelectedGameObject
                || !events.currentSelectedGameObject.transform.IsChildOf(transform)
                || !events.currentSelectedGameObject.activeInHierarchy))
                leave.Select();
        }
    }
}
