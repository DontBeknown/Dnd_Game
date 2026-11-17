using System;
using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Realtime;
using UnityEngine;

namespace ASAL
{
    public sealed class RoomManager : MonoBehaviour
    {
        private PhotonManager photon;
        private Room room;
        private RoomMember[] roster = Array.Empty<RoomMember>();
        private SelectedCharacter selection;
        private bool selectedReady;
        private bool waitingForDm;
        private bool openingDiscovery;
        private bool waitingForStart;
        private bool confirmedOpenFlag;
        private bool confirmedVisibleFlag;
        private float deadline;

        public static RoomManager Instance { get; private set; }
        public RoomPhase Phase { get; private set; }
        public event Action<string> StartRequested;

        internal event Action Changed;
        internal string Error { get; private set; }
        internal bool Discovering => photon.Discovering;
        internal bool CanRetry => photon.CanRetry;
        internal bool ReturningToLobby => photon.ReturningToLobby;
        internal bool InRoom => room != null;
        internal string RoomName => room?.Name;
        internal bool IsDm => InRoom && IsDmIdentity(photon.LocalActor, photon.LocalUser);
        internal bool CanEditRoom => InRoom && Phase == RoomPhase.Setup && !waitingForDm
            && !openingDiscovery && !waitingForStart && !photon.Busy;
        internal bool CanLeave => InRoom && !photon.Busy;
        internal bool LocalReady => selectedReady;
        internal bool HasSelection => selection != null;
        internal string SelectedCharacterId => selection?.RecordId;
        internal IReadOnlyList<RoomMember> Roster => roster;
        internal IReadOnlyCollection<DiscoveredRoom> Rooms => photon.Rooms;

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate RoomManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        internal void Initialize()
        {
            photon = PhotonManager.Instance;
            photon.Changed += OnTransportChanged;
            photon.Failed += ShowError;
        }

        internal void Shutdown()
        {
            UnbindRoom();
            photon.Changed -= OnTransportChanged;
            photon.Failed -= ShowError;
        }

        internal void SetAccount(AccountProfile account)
        {
            UnbindRoom();
            Error = null;
            photon.SetAccount(account);
        }

        public void Create(string name)
        {
            if (!Discovering)
                return;

            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 64)
            {
                ShowError("Enter a room name up to 64 characters.");
                return;
            }
            Error = null;
            photon.Create(name);
        }

        public void Join(string name)
        {
            if (!Discovering || string.IsNullOrEmpty(name))
                return;
            Error = null;
            photon.Join(name);
        }

        public void Retry()
        {
            Error = null;
            photon.Retry();
        }

        public void Leave()
        {
            if (!CanLeave)
                return;
            if (IsDm)
                photon.SetDiscovery(false);
            photon.Leave();
        }

        public void SelectCharacter(SelectedCharacter value)
        {
            if (!CanEditRoom || IsDm)
                return;
            if (value != null && (string.IsNullOrWhiteSpace(value.RecordId)
                || string.IsNullOrWhiteSpace(value.DisplayName)))
            {
                ShowError("The selected character needs a saved ID and display name.");
                return;
            }

            bool ready = RoomRules.KeepReady(selection?.RecordId, selection?.DisplayName,
                value?.RecordId, value?.DisplayName, selectedReady);
            PublishSelection(value, ready);
        }

        public void SetReady(bool ready)
        {
            if (!CanEditRoom || IsDm)
                return;
            if (ready && selection == null)
            {
                ShowError("Select a character before marking ready.");
                return;
            }
            PublishSelection(selection, ready);
        }

        public bool CanStartMatch() => CanEditRoom && RosterAllowsStart();

        public void StartMatch()
        {
            if (!CanEditRoom || !IsDm)
                return;
            if (!RosterAllowsStart())
            {
                ShowError("Every active player needs a selected character and must be ready.");
                return;
            }

            Error = null;
            waitingForStart = true;
            RequestDiscovery(false);
            Changed?.Invoke();
        }

        public void Kick(int actor)
        {
            if (!CanEditRoom || !AllowsKick(photon.LocalActor, photon.LocalUser, actor))
                return;
            if (!photon.SendKick(actor))
                ShowError("Could not remove the player.");
        }

        internal bool IsDmIdentity(int actor, string user) =>
            RoomRules.IsDm(actor, user, photon.DmActor, photon.DmUser);

        private void OnTransportChanged()
        {
            if (room != photon.Current)
            {
                UnbindRoom();
                if (photon.Current != null)
                    BindRoom();
            }
            Changed?.Invoke();
        }

        private void BindRoom()
        {
            room = photon.Current;
            photon.RoomChanged += OnRoomChanged;
            photon.KickReceived += OnKickReceived;
            Phase = RoomPhase.Setup;
            waitingForDm = photon.CreatedRoom && photon.Session != null && photon.DmActor == 0;
            if (waitingForDm)
            {
                deadline = Time.unscaledTime + 10f;
                if (!photon.PublishDm())
                    AbortRoom("Could not initialize the room.");
            }
            else
            {
                OnRoomChanged(null);
            }
        }

        private void UnbindRoom()
        {
            photon.RoomChanged -= OnRoomChanged;
            photon.KickReceived -= OnKickReceived;
            room = null;
            roster = Array.Empty<RoomMember>();
            selection = null;
            selectedReady = false;
            waitingForDm = false;
            openingDiscovery = false;
            waitingForStart = false;
            confirmedOpenFlag = confirmedVisibleFlag = false;
            Phase = RoomPhase.None;
        }

        private void OnRoomChanged(Hashtable properties)
        {
            if (room == null || room != photon.Current || photon.Busy)
                return;

            roster = photon.ReadRoster();
            if (waitingForDm && IsDm)
            {
                waitingForDm = false;
                RequestDiscovery(true);
            }
            if (!waitingForDm && (photon.Session == null || !roster.Any(player =>
                player.Active && IsDmIdentity(player.Actor, player.UserId))))
            {
                AbortRoom("The DM left the room.");
                return;
            }
            if (photon.Started && Phase == RoomPhase.Setup)
            {
                waitingForStart = false;
                Phase = RoomPhase.Starting;
                StartRequested?.Invoke(photon.Session);
            }
            else if ((openingDiscovery || waitingForStart) && properties != null)
            {
                if (properties[GamePropertyKey.IsOpen] is bool open)
                    confirmedOpenFlag = open == openingDiscovery;
                if (properties[GamePropertyKey.IsVisible] is bool visible)
                    confirmedVisibleFlag = visible == openingDiscovery;

                if (confirmedOpenFlag && confirmedVisibleFlag)
                {
                    if (openingDiscovery)
                        openingDiscovery = false;
                    else
                        TryPublishStart();
                }
            }
            Changed?.Invoke();
        }

        private void RequestDiscovery(bool open)
        {
            openingDiscovery = open;
            confirmedOpenFlag = confirmedVisibleFlag = false;
            deadline = Time.unscaledTime + 10f;
            photon.SetDiscovery(open);
        }

        private void TryPublishStart()
        {
            confirmedOpenFlag = confirmedVisibleFlag = false;
            if (RosterAllowsStart() && photon.PublishStart())
                return;

            waitingForStart = false;
            RequestDiscovery(true);
            ShowError("Could not start. Check the roster and try again.");
        }

        private void Update()
        {
            if ((waitingForDm || openingDiscovery || waitingForStart)
                && Time.unscaledTime >= deadline && !photon.Busy)
                AbortRoom("Photon did not confirm the room update.");
        }

        private void AbortRoom(string message)
        {
            if (photon.IsMaster)
                photon.SetDiscovery(false);
            photon.Leave();
            ShowError(message);
        }

        private void OnKickReceived(int sender, string user)
        {
            if (Phase == RoomPhase.Setup && AllowsKick(sender, user, photon.LocalActor))
            {
                photon.Leave();
                ShowError("The DM removed you from the room.");
            }
        }

        private bool RosterAllowsStart() =>
            RoomRules.CanStart(photon.LocalActor, photon.LocalUser, photon.DmActor, photon.DmUser, roster);

        private bool AllowsKick(int sender, string user, int target) =>
            RoomRules.CanKick(sender, user, photon.DmActor, photon.DmUser, target,
                roster.Any(player => player.Active && player.Actor == target));

        private void PublishSelection(SelectedCharacter value, bool ready)
        {
            if (!photon.PublishSelection(value, ready))
            {
                ShowError("Could not update readiness.");
                return;
            }
            selection = value;
            selectedReady = ready;
            Error = null;
            Changed?.Invoke();
        }

        private void ShowError(string message)
        {
            Error = message;
            Changed?.Invoke();
        }
    }
}
