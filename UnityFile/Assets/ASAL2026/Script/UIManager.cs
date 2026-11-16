using System;
using UnityEngine;

namespace ASAL
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private SignInPanel signInPanel;

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
        }

        public void ShowSignIn()
        {
            signInPanel.ShowLogin();
        }

        internal void ShowSignedIn(AccountProfile profile)
        {
            signInPanel.ShowSignedIn(profile.DisplayName);
        }

        internal void RefreshAccountStatus(AuthManager auth)
        {
            if (signInPanel)
                signInPanel.Refresh(auth);
        }

        internal void Shutdown() => signInPanel = null;
    }
}
