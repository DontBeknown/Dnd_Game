using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Firebase;
using Firebase.Database;
using UnityEngine;

namespace ASAL
{
    public sealed class SaveManager : MonoBehaviour
    {
        private const string InventoryReceipts = "_asalInventoryOperations";

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

        internal Task<CharacterData[]> LoadCharactersAsync(string uid) => LoadCharactersAsync(uid, false);

        internal async Task<CharacterData[]> LoadCharactersAsync(string uid, bool enemy)
        {
            var snapshot = await CharacterRecords(uid, enemy).GetValueAsync();
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

        internal string NewCharacterId(string uid, bool enemy) => CharacterRecords(uid, enemy).Push().Key;

        internal Task SaveCharacterAsync(string uid, bool enemy, CharacterData record) =>
            CharacterRecords(uid, enemy).Child(CharacterId(record.firebaseKey)).UpdateChildrenAsync(CharacterUpdate(record));

        internal Task DeleteCharacterAsync(string uid, bool enemy, string id) =>
            CharacterRecords(uid, enemy).Child(CharacterId(id)).RemoveValueAsync();

        private DatabaseReference CharacterRecords(string uid, bool enemy) =>
            Records(enemy ? "enemy_data" : "character_data", uid);

        private static string CharacterId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(".#$[]/".ToCharArray()) >= 0 || id.Any(char.IsControl))
                throw new FormatException("The character ID is invalid.");
            return id;
        }

        private static Dictionary<string, object> CharacterUpdate(CharacterData record) => new()
        {
            ["characterName"] = record.characterName,
            ["raceName"] = record.raceName,
            ["characterClassName"] = record.characterClassName,
            ["level"] = record.level,
            ["HP"] = record.HP,
            ["abilityPoint"] = record.abilityPoint,
            ["abilityScorePoints"] = record.abilityScorePoints?.Select(ability => new Dictionary<string, object>
            {
                ["abilityScoreName"] = ability.abilityScoreName,
                ["abilityScorePoint"] = ability.abilityScorePoint,
                ["ablityModifierBonus"] = ability.ablityModifierBonus
            }).ToList(),
            ["skillData"] = record.skillData?.Select(skill => new Dictionary<string, object>
            {
                ["skillName"] = skill.skillName,
                ["skillType"] = skill.skillType
            }).ToList()
        };

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

        internal async Task<ItemData[]> LoadInventoryAsync(string uid)
        {
            var snapshot = await OwnerInventory(uid).GetValueAsync();
            var items = new List<ItemData>();
            foreach (var child in snapshot.Children)
            {
                if (!IsInventoryItemId(child.Key)
                    || !TryReadInventoryInteger(child.Child("quantity").Value, out var quantity) || quantity <= 0)
                    continue;

                var item = new ItemData(child.Key, child.Child("itemName").Value as string ?? child.Key,
                    child.Child("description").Value as string, quantity, child.Child("iconPath").Value as string,
                    child.Child("type").Value as string);
                items.Add(item);
            }
            return items.OrderBy(item => item.itemName, StringComparer.CurrentCulture)
                .ThenBy(item => item.itemID, StringComparer.Ordinal).ToArray();
        }

        internal async Task<int?> ApplyInventoryChangeAsync(string uid, string matchId, string operationId,
            ItemData item, int delta)
        {
            if (string.IsNullOrEmpty(matchId) || !Guid.TryParseExact(operationId, "N", out _)
                || !IsInventoryItemId(item?.itemID) || delta == 0)
                return null;

            var snapshot = await OwnerInventory(uid).RunTransaction(inventory =>
            {
                if (!inventory.HasChildren && inventory.Value != null)
                    return TransactionResult.Abort();

                var receipts = inventory.Child(InventoryReceipts);
                if ((!receipts.HasChildren && receipts.Value != null) || receipts.Child("itemID").Value != null)
                    return TransactionResult.Abort();

                var receipt = receipts.Child(operationId);
                if (receipt.HasChildren || receipt.Value != null)
                    return TransactionResult.Success(inventory);

                var entry = inventory.Child(item.itemID);
                bool exists = entry.HasChildren || entry.Value != null;
                object current = exists ? entry.Child("quantity").Value : 0;
                if (!TryChangeInventoryQuantity(current, delta, out var quantity))
                    // A no-op lets Firebase retry against server data when the local cache is stale.
                    return TransactionResult.Success(inventory);

                if (quantity == 0)
                    entry.Value = null;
                else if (exists)
                    entry.Child("quantity").Value = quantity;
                else
                    entry.Value = new Dictionary<string, object>
                    {
                        ["itemID"] = item.itemID, ["itemName"] = item.itemName,
                        ["description"] = item.description ?? string.Empty, ["quantity"] = quantity,
                        ["iconPath"] = item.iconPath ?? string.Empty, ["type"] = item.type
                    };

                receipt.Value = new Dictionary<string, object>
                {
                    ["matchId"] = matchId, ["itemId"] = item.itemID, ["delta"] = delta, ["quantity"] = quantity
                };
                return TransactionResult.Success(inventory);
            }, fireLocalEvents: false);

            var saved = snapshot?.Child(InventoryReceipts).Child(operationId).Value;
            return TryReadInventoryReceipt(saved, matchId, item.itemID, delta, out var result) ? result : null;
        }

        internal static bool IsInventoryItemId(string id) => !string.IsNullOrWhiteSpace(id)
            && id != InventoryReceipts && id.IndexOfAny(".#$[]/".ToCharArray()) < 0
            && !id.Any(char.IsControl) && Encoding.UTF8.GetByteCount(id) <= 768;

        private static bool TryChangeInventoryQuantity(object current, int delta, out int quantity)
        {
            quantity = 0;
            if (delta == 0 || !TryReadInventoryInteger(current, out var saved) || saved < 0)
                return false;
            long updated = (long)saved + delta;
            if (updated < 0 || updated > int.MaxValue)
                return false;
            quantity = (int)updated;
            return true;
        }

        private static bool TryReadInventoryReceipt(object value, string match, string item, int delta, out int quantity)
        {
            quantity = 0;
            if (value is not IDictionary receipt || !Equals(receipt["matchId"], match) || !Equals(receipt["itemId"], item)
                || !TryReadInventoryInteger(receipt["delta"], out var savedDelta) || savedDelta != delta
                || !TryReadInventoryInteger(receipt["quantity"], out var savedQuantity) || savedQuantity < 0)
                return false;
            quantity = savedQuantity;
            return true;
        }

        private static bool TryReadInventoryInteger(object value, out int integer)
        {
            integer = 0;
            switch (value)
            {
                case int number:
                    integer = number;
                    return true;
                case long number when number >= int.MinValue && number <= int.MaxValue:
                    integer = (int)number;
                    return true;
                case double number when number >= int.MinValue && number <= int.MaxValue && number == Math.Truncate(number):
                    integer = (int)number;
                    return true;
                default:
                    return false;
            }
        }

        private DatabaseReference OwnerInventory(string uid)
        {
            if (string.IsNullOrEmpty(uid) || AuthManager.Instance?.CurrentAccount?.Uid != uid)
                throw new InvalidOperationException("Inventory requires the signed-in owner.");
            return Records("inventory", uid);
        }

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
