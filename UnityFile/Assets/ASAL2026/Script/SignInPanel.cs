using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class SignInPanel : MonoBehaviour
    {
        [SerializeField] private GameObject loginView;
        [SerializeField] private TMP_InputField loginEmail;
        [SerializeField] private TMP_InputField loginPassword;
        [SerializeField] private Button loginSubmit;
        [SerializeField] private Button openRegistration;
        [SerializeField] private Button loginQuit;

        [SerializeField] private GameObject registrationView;
        [SerializeField] private TMP_InputField registrationName;
        [SerializeField] private TMP_InputField registrationEmail;
        [SerializeField] private TMP_InputField registrationPassword;
        [SerializeField] private TMP_InputField registrationConfirmation;
        [SerializeField] private Button registrationSubmit;
        [SerializeField] private Button openLogin;

        [SerializeField] private GameObject recoveryView;
        [SerializeField] private TMP_InputField recoveryName;
        [SerializeField] private Button recoverySubmit;

        [SerializeField] private ErrorPanel errorPanelPrefab;

        private ErrorPanel errorPanel;
        private bool showingRecovery;

        private void Awake()
        {
            BindButton(loginSubmit, SubmitLogin);
            BindButton(registrationSubmit, SubmitRegistration);
            BindButton(recoverySubmit, SubmitProfile);
            BindButton(openRegistration, ShowRegistration);
            BindButton(openLogin, ShowLogin);
            BindButton(loginQuit, Quit);
        }

        private void BindButton(Button button, UnityAction action)
        {
            if (!button)
                return;

            var click = button.onClick;
            for (var i = 0; i < click.GetPersistentEventCount(); i++)
            {
                if (click.GetPersistentTarget(i) == this
                    && click.GetPersistentMethodName(i) == action.Method.Name
                    && click.GetPersistentListenerState(i) != UnityEventCallState.Off)
                {
                    return;
                }
            }

            click.AddListener(action);
        }

        internal void ValidateBindings()
        {
            if (!loginView || !loginEmail || !loginPassword || !loginSubmit
                || !registrationView || !registrationName || !registrationEmail || !registrationPassword
                || !registrationConfirmation || !registrationSubmit
                || !recoveryView || !recoveryName || !recoverySubmit
                || !errorPanelPrefab || !errorPanelPrefab.errorText || !errorPanelPrefab.closeErrorButton)
            {
                throw new InvalidOperationException("Assign all SignInPanel Inspector fields.");
            }
        }

        public void ShowLogin()
        {
            ClearPasswords();
            recoveryName.text = string.Empty;
            ShowView(loginView, loginEmail);
        }

        public void ShowRegistration()
        {
            ClearPasswords();
            ShowView(registrationView, registrationName);
        }

        public void SubmitLogin() =>
            AuthManager.Instance.SignIn(loginEmail.text, loginPassword.text);

        public void SubmitRegistration() =>
            AuthManager.Instance.Register(
                registrationName.text, registrationEmail.text,
                registrationPassword.text, registrationConfirmation.text);

        public void SubmitProfile() => AuthManager.Instance.RetryProfile(recoveryName.text);
        public void Quit() => Application.Quit();

        internal void Refresh(AuthManager auth)
        {
            if (auth.NeedsProfile && !showingRecovery)
            {
                ClearPasswords();
                recoveryName.text = auth.PendingName ?? string.Empty;
                ShowView(recoveryView, recoveryName);
            }

            bool canSubmit = auth.IsReady && !auth.IsBusy && !auth.NeedsProfile;
            loginSubmit.interactable = canSubmit;
            registrationSubmit.interactable = canSubmit;
            recoverySubmit.interactable = auth.NeedsProfile && !auth.IsBusy;
            if (string.IsNullOrEmpty(auth.Error))
            {
                if (errorPanel)
                    errorPanel.gameObject.SetActive(false);
                return;
            }

            if (!errorPanel)
            {
                errorPanel = Instantiate(errorPanelPrefab, transform, false);
                errorPanel.errorText.richText = false;
                BindButton(errorPanel.closeErrorButton, CloseError);
            }

            errorPanel.errorText.text = auth.Error;
            if (!errorPanel.gameObject.activeSelf)
                errorPanel.gameObject.SetActive(true);
            errorPanel.closeErrorButton.Select();
        }

        private void CloseError()
        {
            errorPanel.gameObject.SetActive(false);
            AuthManager.Instance.RetryStartup();
        }

        private void ShowView(GameObject view, Selectable firstControl)
        {
            showingRecovery = view == recoveryView;
            loginView.SetActive(view == loginView);
            registrationView.SetActive(view == registrationView);
            recoveryView.SetActive(showingRecovery);
            if (firstControl.isActiveAndEnabled && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(firstControl.gameObject);
        }

        private void ClearPasswords()
        {
            loginPassword.text = string.Empty;
            registrationPassword.text = string.Empty;
            registrationConfirmation.text = string.Empty;
        }
    }
}
