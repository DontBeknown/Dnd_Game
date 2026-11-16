using System;
using UnityEngine;

namespace ASAL
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] private SignInPanel signInPanelPrefab;

        private RectTransform screenRoot;
        private SignInPanel panel;

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
            if (!signInPanelPrefab)
                throw new InvalidOperationException("Assign the SignInPanel prefab on UIManager.");

            signInPanelPrefab.ValidateBindings();
            screenRoot = root;
        }

        public void ShowSignIn()
        {
            EnsurePanel();
            panel.ShowLogin();
        }

        internal void ShowSignedIn(AccountProfile profile)
        {
            EnsurePanel();
            panel.ShowSignedIn(profile.DisplayName);
        }

        internal void RefreshAccountStatus(AuthManager auth)
        {
            if (panel)
                panel.Refresh(auth);
        }

        private void EnsurePanel()
        {
            if (panel)
                return;

            panel = Instantiate(signInPanelPrefab, screenRoot, false);
            panel.gameObject.SetActive(true);
        }

        internal void Shutdown()
        {
            if (panel)
                GameplayInstance.Release(panel.gameObject);

            panel = null;
            screenRoot = null;
        }
    }
}
