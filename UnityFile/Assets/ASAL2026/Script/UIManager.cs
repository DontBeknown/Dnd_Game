using System;
using UnityEngine;

namespace ASAL
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] private LobbyPanel lobbyPrefab;
        [SerializeField] private RoomSetupPanel roomPrefab;

        private SignInPanel signInPanel;
        private LobbyPanel authoredLobby;
        private LobbyPanel lobbyPanel;
        private RoomSetupPanel roomPanel;
        private RectTransform screenRoot;
        private RoomManager rooms;
        private AccountProfile account;

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate UIManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        internal void Initialize(RectTransform root)
        {
            var panels = root.GetComponentsInChildren<SignInPanel>(true);
            if (panels.Length != 1)
                throw new InvalidOperationException($"Expected exactly one SignInPanel under the screen root, found {panels.Length}.");

            panels[0].ValidateBindings();
            signInPanel = panels[0];
            screenRoot = root;
            authoredLobby = root.GetComponentInChildren<LobbyPanel>(true);
            if (authoredLobby)
                authoredLobby.gameObject.SetActive(false);
        }

        public void ShowSignIn()
        {
            UnbindLobby();
            signInPanel.gameObject.SetActive(true);
            signInPanel.ShowLogin();
        }

        internal void ShowLobby(AccountProfile profile)
        {
            if ((!authoredLobby && !lobbyPrefab) || !roomPrefab)
                throw new InvalidOperationException("Assign the lobby and room setup prefabs on UIManager.");

            signInPanel.gameObject.SetActive(false);
            account = profile;
            rooms = RoomManager.Instance;
            rooms.Changed += RefreshLobby;
            lobbyPanel = authoredLobby ? authoredLobby : Instantiate(lobbyPrefab, screenRoot, false);
            lobbyPanel.ResetView();
            RefreshLobby();
        }

        private void RefreshLobby()
        {
            if (rooms.InRoom)
            {
                if (lobbyPanel.gameObject.activeSelf)
                    lobbyPanel.PrepareReturnToDiscovery();
                lobbyPanel.gameObject.SetActive(false);
                if (!roomPanel)
                    roomPanel = Instantiate(roomPrefab, screenRoot, false);
                roomPanel.Render(rooms);
                return;
            }

            if (roomPanel)
            {
                roomPanel.gameObject.SetActive(false);
                Destroy(roomPanel.gameObject);
                roomPanel = null;
            }
            lobbyPanel.gameObject.SetActive(true);
            lobbyPanel.Render(rooms, account.DisplayName);
        }

        private void UnbindLobby()
        {
            if (rooms)
                rooms.Changed -= RefreshLobby;
            rooms = null;
            account = null;
            if (lobbyPanel)
            {
                lobbyPanel.gameObject.SetActive(false);
                if (lobbyPanel != authoredLobby)
                    Destroy(lobbyPanel.gameObject);
            }
            if (roomPanel)
            {
                roomPanel.gameObject.SetActive(false);
                Destroy(roomPanel.gameObject);
            }
            lobbyPanel = null;
            roomPanel = null;
        }

        internal void RefreshAccountStatus(AuthManager auth)
        {
            if (signInPanel)
                signInPanel.Refresh(auth);
        }

        internal void Shutdown()
        {
            UnbindLobby();
            signInPanel = null;
            authoredLobby = null;
            screenRoot = null;
        }
    }
}
