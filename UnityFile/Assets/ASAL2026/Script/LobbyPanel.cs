using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class LobbyPanel : MonoBehaviour
    {
        [SerializeField] private TMP_InputField roomName;
        [SerializeField] private GameObject titleView;
        [SerializeField] private GameObject findRoomView;
        [SerializeField] private GameObject createRoomView;
        [SerializeField] private TMP_InputField accountName;
        [SerializeField] private GameObject statusView;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button openFindRoom;
        [SerializeField] private Button openCreateRoom;
        [SerializeField] private Button backFromFindRoom;
        [SerializeField] private Button backFromCreateRoom;
        [SerializeField] private Button create;
        [SerializeField] private Button retry;
        [SerializeField] private Button signOut;
        [SerializeField] private RectTransform content;
        [SerializeField] private LobbyRoomRow rowPrefab;

        private readonly Dictionary<string, LobbyRoomRow> rows = new();
        private View view;

        private enum View { Title, FindRoom, CreateRoom }

        private void Awake()
        {
            create.onClick.AddListener(CreateRoom);
            openFindRoom.onClick.AddListener(ShowFindRoom);
            openCreateRoom.onClick.AddListener(ShowCreateRoom);
            backFromFindRoom.onClick.AddListener(ShowTitle);
            backFromCreateRoom.onClick.AddListener(ShowTitle);
            retry.onClick.AddListener(RoomManager.Instance.Retry);
            signOut.onClick.AddListener(AuthManager.Instance.SignOut);
        }

        internal void Render(RoomManager model, string displayName)
        {
            accountName.SetTextWithoutNotify(displayName);
            RenderView(model);
            var names = new HashSet<string>(model.Rooms.Select(room => room.Name));
            foreach (var name in rows.Keys.Where(name => !names.Contains(name)).ToArray())
            {
                rows[name].gameObject.SetActive(false);
                Destroy(rows[name].gameObject);
                rows.Remove(name);
            }

            var index = 0;
            foreach (var room in model.Rooms.OrderBy(room => room.Name, System.StringComparer.Ordinal))
            {
                if (!rows.TryGetValue(room.Name, out var row))
                {
                    row = Instantiate(rowPrefab, content, false);
                    rows.Add(room.Name, row);
                }
                row.transform.SetSiblingIndex(index++);
                row.Bind(room, model.Discovering);
            }

            var events = EventSystem.current;
            if (events && (!events.currentSelectedGameObject
                || !events.currentSelectedGameObject.transform.IsChildOf(transform)
                || !events.currentSelectedGameObject.activeInHierarchy))
                SelectFirstControl(model);
        }

        internal void ResetView()
        {
            view = View.Title;
            roomName.SetTextWithoutNotify(string.Empty);
        }

        internal void PrepareReturnToDiscovery()
        {
            view = View.FindRoom;
            roomName.SetTextWithoutNotify(string.Empty);
        }

        private void RenderView(RoomManager model)
        {
            bool connected = model.Discovering;
            titleView.SetActive(view == View.Title);
            findRoomView.SetActive(view == View.FindRoom);
            createRoomView.SetActive(view == View.CreateRoom);
            openFindRoom.interactable = connected;
            openCreateRoom.interactable = connected;
            create.interactable = connected;
            roomName.interactable = connected;
            retry.gameObject.SetActive(model.CanRetry);
            status.text = model.Error ?? (connected
                ? (view == View.FindRoom && model.Rooms.Count == 0 ? "No open rooms." : string.Empty)
                : model.CanRetry ? "Room discovery is unavailable."
                : model.ReturningToLobby ? "Returning to room discovery…" : "Connecting to room discovery…");
            statusView.SetActive(!string.IsNullOrEmpty(status.text));
        }

        private void ShowTitle() => ShowView(View.Title);
        private void ShowFindRoom() => ShowView(View.FindRoom);
        private void ShowCreateRoom() => ShowView(View.CreateRoom);

        private void ShowView(View next)
        {
            view = next;
            var model = RoomManager.Instance;
            RenderView(model);
            SelectFirstControl(model);
        }

        private void SelectFirstControl(RoomManager model)
        {
            Selectable control = view switch
            {
                View.FindRoom => backFromFindRoom,
                View.CreateRoom => model.Discovering ? (Selectable)roomName : backFromCreateRoom,
                _ => model.Discovering ? openCreateRoom : signOut
            };
            control.Select();
        }

        private void CreateRoom() => RoomManager.Instance.Create(roomName.text);
    }
}
