using System;
using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace ASAL
{
    public sealed class PhotonManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private const string SESSION_KEY = "asal2";
        private const string ACTOR_KEY = "dmActor";
        private const string USER_KEY = "dmUser";
        private const string PHASE_KEY = "roomPhase";
        private const string CHARACTER_KEY = "characterId";
        private const string CHARACTER_NAME_KEY = "characterName";
        private const string READY_KEY = "ready";
        private const byte KICK_EVENT = 42;

        private static readonly string[] SetupPlayerProperties =
            { SESSION_KEY, CHARACTER_KEY, CHARACTER_NAME_KEY, READY_KEY };
        private readonly Dictionary<string, DiscoveredRoom> rooms = new();
        private AccountProfile account;
        private bool switchingAccount;
        private bool wasInRoom;
        private bool busy;
        private string lobbyFailure;

        public static PhotonManager Instance { get; private set; }
        internal event Action Changed;
        internal event Action<Hashtable> RoomChanged;
        internal event Action<int, string> KickReceived;
        internal event Action<string> Failed;

        internal bool Discovering => PhotonNetwork.InLobby && !busy && !switchingAccount;
        internal bool Busy => busy || switchingAccount;
        internal bool ReturningToLobby { get; private set; }
        internal bool CreatedRoom { get; private set; }
        internal bool CanRetry => account != null && !Busy && !PhotonNetwork.InLobby && !PhotonNetwork.InRoom;
        internal IReadOnlyCollection<DiscoveredRoom> Rooms => rooms.Values;
        internal Room Current => account != null && !switchingAccount && wasInRoom
            ? PhotonNetwork.CurrentRoom : null;
        internal int LocalActor => PhotonNetwork.LocalPlayer.ActorNumber;
        internal string LocalUser => PhotonNetwork.LocalPlayer.UserId;
        internal string Session => Current?.CustomProperties[SESSION_KEY] as string;
        internal int DmActor => Current?.CustomProperties[ACTOR_KEY] is int actor ? actor : 0;
        internal string DmUser => Current?.CustomProperties[USER_KEY] as string;
        internal bool Started => Current?.CustomProperties[PHASE_KEY] is int phase && phase == (int)RoomPhase.Starting;
        internal bool IsMaster => PhotonNetwork.IsMasterClient;

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate PhotonManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            PhotonNetwork.NetworkingClient.OpResponseReceived += OnOperationResponse;
        }

        public override void OnDisable()
        {
            PhotonNetwork.NetworkingClient.OpResponseReceived -= OnOperationResponse;
            base.OnDisable();
        }

        internal void SetAccount(AccountProfile next)
        {
            if (account?.Uid == next?.Uid)
                return;

            account = next;
            rooms.Clear();
            PhotonNetwork.RemovePlayerCustomProperties(SetupPlayerProperties);
            if (switchingAccount || PhotonNetwork.NetworkClientState == ClientState.Disconnecting)
            {
                switchingAccount = true;
            }
            else if (PhotonNetwork.NetworkClientState is ClientState.PeerCreated or ClientState.Disconnected)
            {
                if (account != null)
                    Retry();
            }
            else
            {
                switchingAccount = true;
                PhotonNetwork.Disconnect();
            }
            Changed?.Invoke();
        }

        internal void Shutdown()
        {
            SetAccount(null);
        }

        internal void Retry()
        {
            if (!CanRetry)
                return;

            if (PhotonNetwork.IsConnectedAndReady)
            {
                JoinLobby();
                return;
            }

            PhotonNetwork.AutomaticallySyncScene = false;
            PhotonNetwork.AuthValues = new AuthenticationValues(account.Uid);
            PhotonNetwork.NickName = account.DisplayName;
            Begin(PhotonNetwork.ConnectUsingSettings(), "Could not connect to Photon.");
        }

        internal void Create(string roomName)
        {
            if (!Discovering)
                return;

            PhotonNetwork.RemovePlayerCustomProperties(SetupPlayerProperties);
            var options = new RoomOptions
            {
                MaxPlayers = 5,
                IsOpen = false,
                IsVisible = false,
                PublishUserId = true,
                BroadcastPropsChangeToAll = true,
                CustomRoomProperties = new Hashtable
                {
                    [SESSION_KEY] = Guid.NewGuid().ToString("N"),
                    [PHASE_KEY] = (int)RoomPhase.Setup
                },
                CustomRoomPropertiesForLobby = new[] { SESSION_KEY }
            };
            Begin(PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default), "Could not create the room.");
        }

        internal void Join(string name)
        {
            if (Discovering && rooms.ContainsKey(name))
            {
                PhotonNetwork.RemovePlayerCustomProperties(SetupPlayerProperties);
                Begin(PhotonNetwork.JoinRoom(name), "Could not join the room.");
            }
        }

        internal void Leave()
        {
            if (Current != null && !switchingAccount && !busy)
                Begin(PhotonNetwork.LeaveRoom(false), "Could not leave the room. Try again.");
        }

        internal bool PublishDm() => Current.SetCustomProperties(new Hashtable
        {
            [ACTOR_KEY] = LocalActor,
            [USER_KEY] = LocalUser
        });

        internal void SetDiscovery(bool open)
        {
            Current.IsOpen = open;
            Current.IsVisible = open;
        }

        internal bool PublishStart() => Current.SetCustomProperties(
            new Hashtable { [PHASE_KEY] = (int)RoomPhase.Starting },
            new Hashtable { [PHASE_KEY] = (int)RoomPhase.Setup });

        internal RoomMember[] ReadRoster()
        {
            var session = Session;
            return Current.Players.Values.OrderBy(player => player.ActorNumber).Select(player =>
            {
                var props = player.CustomProperties;
                var belongs = session != null && props[SESSION_KEY] as string == session;
                return new RoomMember(player.ActorNumber, player.UserId, player.NickName, !player.IsInactive,
                    belongs ? props[CHARACTER_KEY] as string : null,
                    belongs ? props[CHARACTER_NAME_KEY] as string : null,
                    belongs && props[READY_KEY] is bool ready && ready);
            }).ToArray();
        }

        internal bool PublishSelection(SelectedCharacter selection, bool ready) =>
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                [SESSION_KEY] = Session,
                [CHARACTER_KEY] = selection?.RecordId ?? string.Empty,
                [CHARACTER_NAME_KEY] = selection?.DisplayName ?? string.Empty,
                [READY_KEY] = ready
            });

        internal bool SendKick(int actor) => PhotonNetwork.RaiseEvent(KICK_EVENT, Session,
            new RaiseEventOptions { TargetActors = new[] { actor } }, SendOptions.SendReliable);

        public void OnEvent(EventData message)
        {
            if (!switchingAccount && Current != null && Session != null && message.Code == KICK_EVENT
                && message.CustomData is string session && session == Session
                && Current.Players.TryGetValue(message.Sender, out var sender) && !sender.IsInactive)
                KickReceived?.Invoke(sender.ActorNumber, sender.UserId);
        }

        public override void OnConnectedToMaster()
        {
            if (account != null && !switchingAccount)
                JoinLobby();
        }

        public override void OnJoinedLobby()
        {
            ReturningToLobby = false;
            Complete();
        }

        public override void OnCreatedRoom() => CreatedRoom = true;

        public override void OnLeftLobby()
        {
            rooms.Clear();
            Changed?.Invoke();
        }

        public override void OnJoinedRoom()
        {
            rooms.Clear();
            wasInRoom = true;
            Complete();
        }

        public override void OnLeftRoom()
        {
            wasInRoom = false;
            CreatedRoom = false;
            busy = true;
            ReturningToLobby = true;
            Changed?.Invoke();
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            var intentional = switchingAccount;
            switchingAccount = false;
            wasInRoom = false;
            CreatedRoom = false;
            ReturningToLobby = false;
            rooms.Clear();
            Complete();
            if (account != null)
            {
                if (intentional)
                    Retry();
                else
                    Failed?.Invoke(lobbyFailure ?? $"Photon disconnected ({cause}).");
            }
            lobbyFailure = null;
        }

        private void OnOperationResponse(OperationResponse response)
        {
            // This PUN version fires OnJoinedLobby even when JoinLobby returns an error.
            if (response.OperationCode != OperationCode.JoinLobby || response.ReturnCode == 0
                || account == null || switchingAccount)
                return;

            lobbyFailure = $"Could not enter room discovery ({response.ReturnCode}).";
            busy = true;
            PhotonNetwork.Disconnect();
            Changed?.Invoke();
        }

        public override void OnRoomListUpdate(List<RoomInfo> updates)
        {
            if (!PhotonNetwork.InLobby || switchingAccount)
                return;

            foreach (var info in updates)
            {
                if (info.RemovedFromList || !info.IsOpen || !info.IsVisible || info.PlayerCount >= info.MaxPlayers
                    || info.CustomProperties[SESSION_KEY] is not string)
                    rooms.Remove(info.Name);
                else
                    rooms[info.Name] = new DiscoveredRoom(info.Name, info.PlayerCount, info.MaxPlayers);
            }
            Changed?.Invoke();
        }

        public override void OnCreateRoomFailed(short code, string message) => OperationFailed(message);
        public override void OnJoinRoomFailed(short code, string message) => OperationFailed(message);
        public override void OnPlayerEnteredRoom(Player player) => RoomChanged?.Invoke(null);
        public override void OnPlayerLeftRoom(Player player) => RoomChanged?.Invoke(null);
        public override void OnMasterClientSwitched(Player player) => RoomChanged?.Invoke(null);
        public override void OnPlayerPropertiesUpdate(Player player, Hashtable properties) => RoomChanged?.Invoke(null);
        public override void OnRoomPropertiesUpdate(Hashtable properties) => RoomChanged?.Invoke(properties);

        private void JoinLobby()
        {
            if (!PhotonNetwork.InLobby && !wasInRoom)
                Begin(PhotonNetwork.JoinLobby(TypedLobby.Default), "Could not enter room discovery.");
        }

        private void Begin(bool sent, string failure)
        {
            busy = sent;
            if (!sent && Current == null)
                ReturningToLobby = false;
            Changed?.Invoke();
            if (!sent)
                Failed?.Invoke(failure);
        }

        private void Complete()
        {
            busy = false;
            Changed?.Invoke();
        }

        private void OperationFailed(string message)
        {
            Complete();
            Failed?.Invoke(message);
            if (!PhotonNetwork.InLobby && PhotonNetwork.IsConnectedAndReady && !busy && !switchingAccount)
                JoinLobby();
        }
    }
}
