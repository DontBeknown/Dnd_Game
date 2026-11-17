using System;
using System.Collections.Generic;
using System.Linq;
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

        internal async Task<CharacterData[]> LoadCharactersAsync(string uid)
        {
            var snapshot = await Records("character_data", uid).GetValueAsync();
            var records = new List<CharacterData>();
            foreach (var child in snapshot.Children)
            {
                var record = ReadCharacter(child.Key, child.GetRawJsonValue());
                if (record != null)
                    records.Add(record);
            }
            return records.OrderBy(record => record.characterName, StringComparer.CurrentCulture)
                .ThenBy(record => record.firebaseKey, StringComparer.Ordinal).ToArray();
        }

        internal async Task<KeyValuePair<string, string>[]> LoadMapIndexAsync(string uid)
        {
            var snapshot = await Records("tilemap_data", uid).Child("keys").GetValueAsync();
            return snapshot.Children
                .Select(child => new KeyValuePair<string, string>(child.Key, child.Child("name").Value as string))
                .Where(entry => entry.Key != "keys" && !string.IsNullOrWhiteSpace(entry.Value))
                .OrderBy(entry => entry.Value, StringComparer.CurrentCulture)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        }

        internal async Task<TilemapDataWrapper> LoadMapAsync(string uid, string id)
        {
            var snapshot = await Records("tilemap_data", uid).Child(MapId(id)).GetValueAsync();
            return ReadMapBody(snapshot.GetRawJsonValue());
        }

        internal Task SaveMapAsync(string uid, string id, string name, TilemapDataWrapper body) =>
            Records("tilemap_data", uid).UpdateChildrenAsync(MapUpdate(id, name, body));

        internal Task DeleteMapAsync(string uid, string id) =>
            Records("tilemap_data", uid).UpdateChildrenAsync(MapUpdate(id, null, null));

        private static CharacterData ReadCharacter(string id, string json)
        {
            var record = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<CharacterData>(json);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(record?.characterName))
                return null;
            record.firebaseKey = id;
            return record;
        }

        private static string MapId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "keys" || id.IndexOfAny(".#$[]/".ToCharArray()) >= 0)
                throw new FormatException("The map ID is invalid.");
            return id;
        }

        private static TilemapDataWrapper ReadMapBody(string json)
        {
            var body = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<TilemapDataWrapper>(json);
            if (body?.tilemapDataList == null)
                throw new FormatException("The saved map is missing its layers.");
            return body;
        }

        private static Dictionary<string, object> MapUpdate(string id, string name, TilemapDataWrapper body) =>
            new()
            {
                ["keys/" + MapId(id)] = name == null ? null : new Dictionary<string, object> { ["name"] = name },
                [id] = name == null ? null : new Dictionary<string, object>
                {
                    ["tilemapDataList"] = body.tilemapDataList.Select(layer => new Dictionary<string, object>
                    {
                        ["key"] = layer.key,
                        ["tiles"] = layer.tiles.Select(tile => new Dictionary<string, object>
                        {
                            ["guidForBuildable"] = tile.guidForBuildable,
                            ["position"] = new Dictionary<string, object>
                            {
                                ["x"] = tile.position.x,
                                ["y"] = tile.position.y,
                                ["z"] = tile.position.z
                            }
                        }).ToList()
                    }).ToList()
                }
            };

        private DatabaseReference ProfileReference(string uid) => Records("users", uid);

        private DatabaseReference Records(string collection, string uid) =>
            FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, databaseUrl)
                .RootReference.Child(collection).Child(uid);

        private static AccountProfile ReadProfile(string uid, DataSnapshot snapshot)
        {
            var name = snapshot?.Child("displayName").Value as string;
            return string.IsNullOrWhiteSpace(name) ? null : new AccountProfile(uid, name);
        }
    }
}
