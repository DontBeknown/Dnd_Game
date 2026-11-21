using System;
using UnityEngine;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }
        
        [SerializeField] private GameObject mainBackGround;
        [SerializeField] private LobbyPanel lobbyPrefab;
        [SerializeField] private RoomSetupPanel roomPrefab;
        [SerializeField] private CharacterEditorPanel characterPickerPrefab;
        [SerializeField] private MapEditorPanel mapEditorPrefab;
        [SerializeField] private PlayPanel playPrefab;

        private SignInPanel signInPanel;
        private LobbyPanel authoredLobby;
        private LobbyPanel lobbyPanel;
        private RoomSetupPanel roomPanel;
        private CharacterEditorPanel characterPicker;
        private MapEditorPanel mapEditor;
        private CharacterEditorPanel recordEditor;
        private PlayPanel play;
        private bool playStarted;
        private Button editorReturnFocus;
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
            PhotonManager.RegisterTokenPrefab(playPrefab.NetworkTokenPrefab);
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
                if (rooms.Phase != RoomPhase.Setup && characterPicker)
                    RetireCharacterPicker();
                if (recordEditor && recordEditor.OpeningPhase != rooms.Phase)
                {
                    recordEditor.Release();
                    recordEditor.gameObject.SetActive(false);
                    Destroy(recordEditor.gameObject);
                    recordEditor = null;
                    editorReturnFocus = null;
                }
                if (screens.Current == lobbyPanel.gameObject)
                    lobbyPanel.PrepareReturnToDiscovery();
                if (!roomPanel)
                    roomPanel = Instantiate(roomPrefab, screenRoot, false);
                if (play && rooms.Phase == RoomPhase.Playing)
                {
                    play.RefreshStatus();
                    return;
                }
                if (!characterPicker && !recordEditor && (!mapEditor || screens.Current != mapEditor.gameObject))
                {
                    screens.Show(roomPanel.gameObject);
                    roomPanel.Render(rooms);
                }
                return;
            }

            if (roomPanel || play)
                ClearFeatures();
            if (recordEditor)
                return;
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
            if (!rooms || !rooms.CanEditRoom || rooms.IsDm || characterPicker || recordEditor)
                return;
            if (rooms.LocalReady)
            {
                rooms.SetReady(false);
                if (rooms.LocalReady)
                    return;
            }
            characterPicker = Instantiate(characterPickerPrefab, screenRoot, false);
            screens.Show(characterPicker.gameObject, () => characterPicker.Open(account, false, CloseCharacterPicker,
                rooms.SelectedCharacterId, value =>
            {
                if (!rooms.SelectCharacter(value))
                    return false;
                CloseCharacterPicker();
                return true;
            }, value => rooms.SelectedCharacterId != value.RecordId || rooms.SelectCharacter(value)));
        }

        internal TilemapDataWrapper CaptureStartingMap() => mapEditor ? mapEditor.CaptureStartingMap() : null;

        internal void ShowPlay(MatchSnapshot snapshot)
        {
            var board = GetPlayBoard();
            board.Apply(snapshot);
            if (!playStarted)
            {
                playStarted = true;
                board.BeginPlay();
                screens.Show(board.gameObject, retirePrevious: () => mainBackGround.SetActive(false));
            }
        }

        internal PlayPanel EnsurePlayBoard(TilemapDataWrapper map, int revision)
        {
            var board = GetPlayBoard();
            board.PrepareBoard(map, revision);
            return board;
        }

        private PlayPanel GetPlayBoard()
        {
            if (!play)
            {
                play = Instantiate(playPrefab, screenRoot, false);
                play.Initialize(rooms, account);
            }
            return play;
        }

        internal void DiscardPreparedPlay()
        {
            if (!playStarted)
                ClearPlay();
        }

        private void ClearPlay()
        {
            if (play)
            {
                play.Release();
                play.gameObject.SetActive(false);
                Destroy(play.gameObject);
                play = null;
            }
            playStarted = false;
        }

        internal void OpenRecordEditor(bool enemy, Button focus)
        {
            if (!rooms || recordEditor || characterPicker || (enemy
                ? !rooms.IsDm || (!rooms.CanEditRoom && rooms.Phase != RoomPhase.Playing)
                : rooms.InRoom))
                return;

            editorReturnFocus = focus;
            recordEditor = Instantiate(characterPickerPrefab, screenRoot, false);
            screens.Show(recordEditor.gameObject, () => recordEditor.Open(account, enemy, CloseRecordEditor));
        }

        private void CloseRecordEditor()
        {
            var previous = recordEditor;
            recordEditor = null;
            previous.Release();
            var target = playStarted ? play.gameObject : rooms.InRoom ? roomPanel.gameObject : lobbyPanel.gameObject;
            screens.Show(target, retirePrevious: () =>
            {
                Destroy(previous.gameObject);
                if (editorReturnFocus && editorReturnFocus.IsInteractable())
                    editorReturnFocus.Select();
                editorReturnFocus = null;
            });
            if (play)
                play.RefreshEnemies();
            RefreshLobby();
        }

        internal void OpenMapEditor()
        {
            if (!rooms || !rooms.IsDm || (!rooms.CanEditRoom && rooms.Phase != RoomPhase.Playing))
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
            previousPicker.Release();
            characterPicker = null;
            screens.Show(roomPanel.gameObject, retirePrevious: () => Destroy(previousPicker.gameObject));
            RestoreRoomFocus();
        }

        private void RetireCharacterPicker()
        {
            if (!characterPicker)
                return;
            characterPicker.Release();
            characterPicker.gameObject.SetActive(false);
            Destroy(characterPicker.gameObject);
            characterPicker = null;
        }

        private void CloseMapEditor()
        {
            mapEditor.enabled = false;
            var target = playStarted ? play.gameObject : roomPanel.gameObject;
            screens.Show(target, retirePrevious: () =>
            {
                mainBackGround.SetActive(!playStarted);
                if (playStarted)
                    play.FocusMapEditor();
                else
                    RestoreRoomFocus();
            });
        }

        private void RestoreRoomFocus()
        {
            RefreshLobby();
            if (roomPanel && rooms.InRoom)
                roomPanel.FocusFeature(rooms.IsDm);
        }

        private void ClearFeatures()
        {
            screens.Finish();
            if (recordEditor)
            {
                recordEditor.Release();
                recordEditor.gameObject.SetActive(false);
                Destroy(recordEditor.gameObject);
                recordEditor = null;
                editorReturnFocus = null;
            }
            ClearPlay();
            mainBackGround.SetActive(true);
            RetireCharacterPicker();
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
