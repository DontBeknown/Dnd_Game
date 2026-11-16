using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Database;
using UnityEngine;

namespace ASAL
{
    public sealed class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        [SerializeField] private string databaseUrl;

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate SaveManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        internal void Initialize()
        {
            if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Assign the HTTPS test Realtime Database URL on SaveManager.");
        }

        internal async Task<AccountProfile> LoadProfileAsync(string uid)
        {
            var snapshot = await ProfileReference(uid).GetValueAsync();
            return ReadProfile(uid, snapshot);
        }

        internal async Task<AccountProfile> CreateProfileAsync(string uid, string name, string email)
        {
            var createdAt = DateTime.UtcNow.ToString("o");
            var snapshot = await ProfileReference(uid).RunTransaction(profile =>
            {
                if (profile.Value != null && profile.Value is not System.Collections.IDictionary)
                    return TransactionResult.Abort();

                if (string.IsNullOrWhiteSpace(profile.Child("displayName").Value as string))
                    profile.Child("displayName").Value = name;
                if (profile.Child("email").Value == null && !string.IsNullOrEmpty(email))
                    profile.Child("email").Value = email;
                if (profile.Child("createdAt").Value == null)
                    profile.Child("createdAt").Value = createdAt;

                return TransactionResult.Success(profile);
            }, fireLocalEvents: false);

            return ReadProfile(uid, snapshot)
                ?? throw new InvalidOperationException("The saved account profile has no display name.");
        }

        private DatabaseReference ProfileReference(string uid) =>
            FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, databaseUrl)
                .RootReference.Child("users").Child(uid);

        private static AccountProfile ReadProfile(string uid, DataSnapshot snapshot)
        {
            var name = snapshot?.Child("displayName").Value as string;
            return string.IsNullOrWhiteSpace(name) ? null : new AccountProfile(uid, name);
        }
    }
}
