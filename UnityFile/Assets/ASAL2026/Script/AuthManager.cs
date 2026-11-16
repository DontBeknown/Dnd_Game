using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

namespace ASAL
{
    public sealed class AuthManager : MonoBehaviour
    {
        public static AuthManager Instance { get; private set; }

        // Firebase Auth is shared by roots even while a destroyed root's SDK request finishes.
        private static Task drainingRequest = Task.CompletedTask;

        private FirebaseAuth auth;
        private TaskCompletionSource<bool> request;
        private FirebaseUser pendingUser;
        private string pendingName;
        private int generation;
        private bool initialized;

        public AccountProfile CurrentAccount { get; private set; }
        public event Action<AccountProfile> AccountChanged;

        internal event Action StatusChanged;
        internal bool IsReady => auth != null;
        internal bool IsBusy => request != null;
        internal bool NeedsProfile => pendingUser != null;
        internal string PendingName => pendingName;
        internal string Error { get; private set; }

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate AuthManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        internal void Initialize() => initialized = true;

        internal void Shutdown()
        {
            if (!initialized)
                return;

            initialized = false;
            if (request != null)
                drainingRequest = request.Task;

            InvalidateSession();
        }

        internal void RetryStartup()
        {
            if (IsReady)
                return;

            var previous = drainingRequest;
            RunRequest(async version =>
            {
                await previous;
                if (version != generation)
                    return;

                var dependencies = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (version != generation)
                    return;
                if (dependencies != DependencyStatus.Available)
                    throw new InvalidOperationException($"Firebase dependencies: {dependencies}");

                var session = FirebaseAuth.DefaultInstance;
                session.SignOut();
                auth = session;
            }, "Could not start Firebase. Close this message to retry.");
        }

        public void SignIn(string email, string password)
        {
            if (!CanSubmitCredentials() || !ValidateCredentials(email, password))
                return;

            RunRequest(async version =>
            {
                var result = await auth.SignInWithEmailAndPasswordAsync(email.Trim(), password);
                if (version != generation)
                    return;

                pendingUser = result.User;
                await LoadOrCreateProfile(version);
            }, "Sign-in failed. Check your credentials and try again.");
        }

        public void Register(string name, string email, string password, string confirmation)
        {
            if (!CanSubmitCredentials() || !ValidateCredentials(email, password))
                return;
            if (string.IsNullOrWhiteSpace(name))
            {
                SetError("Enter a display name.");
                return;
            }
            if (password != confirmation)
            {
                SetError("Passwords do not match.");
                return;
            }

            RunRequest(async version =>
            {
                var result = await auth.CreateUserWithEmailAndPasswordAsync(email.Trim(), password);
                if (version != generation)
                    return;

                pendingUser = result.User;
                pendingName = name.Trim();
                await LoadOrCreateProfile(version);
            }, "Registration failed. Check your email and password and try again.");
        }

        public void RetryProfile(string name)
        {
            if (!initialized || IsBusy || !NeedsProfile)
                return;

            pendingName = name?.Trim();
            RunRequest(LoadOrCreateProfile, "Could not load or save your profile. Try again.");
        }

        public void SignOut()
        {
            InvalidateSession();
            AccountChanged?.Invoke(null);
            StatusChanged?.Invoke();
        }

        private void InvalidateSession()
        {
            generation++;
            CurrentAccount = null;
            pendingUser = null;
            pendingName = null;
            Error = null;
            SignOutFirebase();
        }

        private void SignOutFirebase()
        {
            try { auth?.SignOut(); }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                auth = null;
            }
        }

        private bool CanSubmitCredentials() =>
            initialized && IsReady && !IsBusy && !NeedsProfile && CurrentAccount == null;

        private bool ValidateCredentials(string email, string password)
        {
            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrEmpty(password))
                return true;

            SetError("Enter your email and password.");
            return false;
        }

        private async Task LoadOrCreateProfile(int version)
        {
            var user = pendingUser;
            var name = pendingName;
            var profile = await SaveManager.Instance.LoadProfileAsync(user.UserId);
            if (version != generation)
                return;

            if (profile == null && string.IsNullOrWhiteSpace(name))
            {
                SetError("Enter a display name to finish setting up your account.");
                return;
            }

            if (profile == null)
                profile = await SaveManager.Instance.CreateProfileAsync(user.UserId, name, user.Email);

            PublishAccount(version, profile);
        }

        private void PublishAccount(int version, AccountProfile profile)
        {
            if (version != generation)
                return;

            pendingUser = null;
            pendingName = null;
            CurrentAccount = profile;
            AccountChanged?.Invoke(profile);
        }

        private async void RunRequest(Func<int, Task> operation, string failureMessage)
        {
            if (!initialized || IsBusy)
                return;

            int version = generation;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            request = completion;
            Error = null;

            // Calls originate on Unity's main thread and await resumes on its synchronization context.
            try
            {
                StatusChanged?.Invoke();
                await operation(version);
            }
            catch (Exception exception)
            {
                if (version == generation)
                {
                    Debug.LogWarning($"Account request failed ({exception.GetType().Name}).", this);
                    Error = NeedsProfile ? "Could not load or save your profile. Try again." : failureMessage;
                }
            }
            finally
            {
                if (version != generation)
                    SignOutFirebase();

                request = null;
                completion.TrySetResult(true);
                if (initialized)
                    StatusChanged?.Invoke();
            }
        }

        private void SetError(string message)
        {
            Error = message;
            StatusChanged?.Invoke();
        }
    }
}
