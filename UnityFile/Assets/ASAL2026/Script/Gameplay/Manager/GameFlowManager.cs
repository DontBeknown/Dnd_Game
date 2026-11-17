using System;
using UnityEngine;

namespace ASAL
{
    public sealed class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate GameFlowManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        internal void Begin()
        {
            RoomManager.Instance.StartRequested += OnStartRequested;
            RoomManager.Instance.MatchApplied += OnMatchApplied;
            AuthManager.Instance.AccountChanged += OnAccountChanged;
            AuthManager.Instance.StatusChanged += RefreshAccountStatus;
            UIManager.Instance.ShowSignIn();
            AuthManager.Instance.RetryStartup();
        }

        internal void Shutdown()
        {
            RoomManager.Instance.StartRequested -= OnStartRequested;
            RoomManager.Instance.MatchApplied -= OnMatchApplied;
            AuthManager.Instance.AccountChanged -= OnAccountChanged;
            AuthManager.Instance.StatusChanged -= RefreshAccountStatus;
        }

        private static void OnStartRequested(string session)
        {
            var room = RoomManager.Instance;
            if (room.IsDm && room.Session == session)
                room.Match.Start(UIManager.Instance.CaptureStartingMap());
        }

        private static void OnMatchApplied(MatchSnapshot snapshot) => UIManager.Instance.ShowPlay(snapshot);

        private static void OnAccountChanged(AccountProfile account)
        {
            RoomManager.Instance.SetAccount(account);
            if (account == null)
                UIManager.Instance.ShowSignIn();
            else
                UIManager.Instance.ShowLobby(account);

            RefreshAccountStatus();
        }

        private static void RefreshAccountStatus() =>
            UIManager.Instance.RefreshAccountStatus(AuthManager.Instance);
    }
}
