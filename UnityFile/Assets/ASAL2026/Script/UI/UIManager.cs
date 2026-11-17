using System;
using UnityEngine;

namespace ASAL
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }
        
        [SerializeField] private GameObject mainBackGround;
        [SerializeField] private LobbyPanel lobbyPrefab;
        [SerializeField] private RoomSetupPanel roomPrefab;
        [SerializeField] private CharacterPickerPanel characterPickerPrefab;
        [SerializeField] private MapEditorPanel mapEditorPrefab;

        private SignInPanel signInPanel;
        private LobbyPanel authoredLobby;
        private LobbyPanel lobbyPanel;
        private RoomSetupPanel roomPanel;
        private CharacterPickerPanel characterPicker;
        private MapEditorPanel mapEditor;
        private RectTransform screenRoot;
        private RoomManager rooms;
        private AccountProfile account;
        private readonly TabTransition screens = new();

        internal bool IsScreenTransitioning => screens.IsPlaying;

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
            screens.Show(signInPanel.gameObject);
            signInPanel.ShowLogin();
        }

        internal void ShowLobby(AccountProfile profile)
        {
            if ((!authoredLobby && !lobbyPrefab) || !roomPrefab)
                throw new InvalidOperationException("Assign the lobby and room setup prefabs on UIManager.");

            UnbindLobby();
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
                if (screens.Current == lobbyPanel.gameObject)
                    lobbyPanel.PrepareReturnToDiscovery();
                if (!roomPanel)
                    roomPanel = Instantiate(roomPrefab, screenRoot, false);
                if (rooms.Phase != RoomPhase.Setup)
                    ClearFeatures();
                if (!characterPicker && (!mapEditor || screens.Current != mapEditor.gameObject))
                {
                    screens.Show(roomPanel.gameObject);
                    roomPanel.Render(rooms);
                }
                return;
            }

            ClearFeatures();
            var previousRoom = roomPanel;
            roomPanel = null;
            screens.Show(lobbyPanel.gameObject, retirePrevious: () =>
            {
                if (previousRoom)
                    Destroy(previousRoom.gameObject);
            });
            lobbyPanel.Render(rooms, account.DisplayName);
        }

        internal void OpenCharacterPicker()
        {
            if (!rooms || !rooms.CanEditRoom || rooms.IsDm || characterPicker)
                return;
            characterPicker = Instantiate(characterPickerPrefab, screenRoot, false);
            screens.Show(characterPicker.gameObject, () => characterPicker.Open(account, rooms.SelectedCharacterId, value =>
            {
                if (!rooms || !rooms.CanEditRoom || rooms.IsDm)
                    return;
                rooms.SelectCharacter(value);
                CloseCharacterPicker();
            }, CloseCharacterPicker));
        }

        internal void OpenMapEditor()
        {
            if (!rooms || !rooms.CanEditRoom || !rooms.IsDm)
                return;
            if (!mapEditor)
            {
                mapEditor = Instantiate(mapEditorPrefab, screenRoot, false);
                mapEditor.Initialize(account, CloseMapEditor);
            }
            screens.Show(mapEditor.gameObject, retirePrevious: () => {
                mapEditor.Open();
                mainBackGround.SetActive(false);
                });
        }

        private void CloseCharacterPicker()
        {
            var previousPicker = characterPicker;
            previousPicker.enabled = false;
            characterPicker = null;
            screens.Show(roomPanel.gameObject, retirePrevious: () => Destroy(previousPicker.gameObject));
            RestoreRoomFocus();
        }

        private void CloseMapEditor()
        {
            mapEditor.enabled = false;
            screens.Show(roomPanel.gameObject, retirePrevious: () => {
                mainBackGround.SetActive(true);
                });
            RestoreRoomFocus();
        }

        private void RestoreRoomFocus()
        {
            RefreshLobby();
            if (roomPanel && rooms.InRoom)
                roomPanel.FocusFeature(rooms.IsDm);
        }

        private void ClearFeatures()
        {
            if (characterPicker || mapEditor && mapEditor.gameObject.activeSelf)
                screens.Finish();
            if (characterPicker)
            {
                characterPicker.gameObject.SetActive(false);
                Destroy(characterPicker.gameObject);
                characterPicker = null;
            }
            if (mapEditor)
            {
                mapEditor.Release();
                mapEditor.gameObject.SetActive(false);
                Destroy(mapEditor.gameObject);
                mapEditor = null;
            }
        }

        private void UnbindLobby()
        {
            screens.Finish();
            ClearFeatures();
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
