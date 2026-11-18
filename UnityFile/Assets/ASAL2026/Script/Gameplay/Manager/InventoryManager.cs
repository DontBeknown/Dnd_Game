using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ASAL
{
    public sealed class InventoryManager : MonoBehaviour
    {
        private const float AcknowledgementTimeout = 10f;
        private const string ChangeMessage = "inventory";
        private const string AcknowledgementMessage = "inventoryAck";

        [SerializeField] private ItemDatabase itemDatabase;

        private readonly Dictionary<string, InventoryOperation> operations = new(StringComparer.Ordinal);
        private PhotonManager photon;
        private RoomManager room;
        private AuthManager auth;
        private SaveManager saves;
        private string loadedMatch;
        private int roomVersion;
        private int loadVersion;

        public static InventoryManager Instance { get; private set; }
        internal event Action Changed;
        internal IReadOnlyList<Item> Catalog { get; private set; } = Array.Empty<Item>();
        internal IReadOnlyList<ItemData> Items { get; private set; } = Array.Empty<ItemData>();
        internal IReadOnlyCollection<InventoryOperation> Operations => operations.Values;
        internal bool Loading { get; private set; }
        internal string Error { get; private set; }

        private string CurrentMatch => room.Phase == RoomPhase.Playing && room.CanLeave
            ? room.Match?.Committed?.MatchId : null;

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate InventoryManager.");
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
            room = RoomManager.Instance;
            auth = AuthManager.Instance;
            saves = SaveManager.Instance;
            var catalog = itemDatabase ? itemDatabase : Resources.Load<ItemDatabase>("Items/itemDatabase");
            Catalog = catalog && catalog.allItems != null
                ? catalog.allItems.Where(item => item && SaveManager.IsInventoryItemId(item.itemID)).ToArray()
                : Array.Empty<Item>();
            photon.MatchReceived += Receive;
            room.MatchApplied += OnMatchApplied;
            auth.AccountChanged += OnAccountChanged;
        }

        internal void Shutdown()
        {
            photon.MatchReceived -= Receive;
            room.MatchApplied -= OnMatchApplied;
            auth.AccountChanged -= OnAccountChanged;
            ClearRoom();
        }

        internal void ClearRoom()
        {
            roomVersion++;
            loadedMatch = null;
            Loading = false;
            Error = null;
            Items = Array.Empty<ItemData>();
            operations.Clear();
            Changed?.Invoke();
        }

        internal void Reload()
        {
            string match = CurrentMatch;
            string uid = auth.CurrentAccount?.Uid;
            if (string.IsNullOrEmpty(match) || string.IsNullOrEmpty(uid))
                return;

            int request = ++loadVersion;
            int version = roomVersion;
            Loading = true;
            Error = null;
            Changed?.Invoke();
            _ = LoadAsync(uid, match, version, request);
        }

        internal bool RequestChange(int recipientActor, string itemId, int quantityDelta)
        {
            string match = CurrentMatch;
            if (string.IsNullOrEmpty(match) || !room.IsDm || quantityDelta == 0 || FindItem(itemId) == null)
                return false;

            var recipient = room.Roster.FirstOrDefault(member => member.Actor == recipientActor && member.Active
                && member.Actor != photon.LocalActor && !string.IsNullOrEmpty(member.UserId));
            if (recipient == null)
                return false;

            var operation = new InventoryOperation(Guid.NewGuid().ToString("N"), recipient.Actor, recipient.UserId,
                match, itemId, quantityDelta);
            operations.Add(operation.Id, operation);
            Send(operation);
            return true;
        }

        internal bool Retry(string operationId)
        {
            if (string.IsNullOrEmpty(operationId) || !operations.TryGetValue(operationId, out var operation)
                || !operation.CanRetry || !room.IsDm || CurrentMatch != operation.Match
                || !room.Roster.Any(member => member.Active && operation.MatchesRecipient(member.Actor, member.UserId)))
                return false;

            Send(operation);
            return true;
        }

        private void OnMatchApplied(MatchSnapshot snapshot)
        {
            if (loadedMatch == snapshot.MatchId)
                return;
            loadedMatch = snapshot.MatchId;
            Reload();
        }

        private void OnAccountChanged(AccountProfile account) => ClearRoom();

        private Item FindItem(string itemId) => Catalog.FirstOrDefault(item => item.itemID == itemId);

        private bool IsCurrent(string uid, string match, int version) =>
            version == roomVersion && auth.CurrentAccount?.Uid == uid && CurrentMatch == match;

        private async Task LoadAsync(string uid, string match, int version, int request)
        {
            ItemData[] loaded = null;
            try { loaded = await saves.LoadInventoryAsync(uid); }
            catch (Exception) { }

            if (!IsCurrent(uid, match, version) || request != loadVersion)
                return;

            Loading = false;
            if (loaded == null)
                Error = "Could not load your inventory. Try again.";
            else
                Items = loaded;
            Changed?.Invoke();
        }

        private void Send(InventoryOperation operation)
        {
            operation.Start(Time.unscaledTime + AcknowledgementTimeout);
            if (!photon.SendMatch(new[] { operation.Actor }, ChangeMessage, operation.Match,
                    operation.Id, operation.ItemId, operation.Delta))
                operation.Uncertain("Could not send the inventory change. Retry to confirm it.");
            Changed?.Invoke();
        }

        private void Receive(int actor, string user, object[] data)
        {
            if (data.Length != 5 || data[0] is not string message || data[1] is not string match
                || match != CurrentMatch || match != room.Session || data[2] is not string operationId
                || !Guid.TryParseExact(operationId, "N", out _))
                return;

            if (message == ChangeMessage && room.IsDmIdentity(actor, user) && !room.IsDm
                && data[3] is string itemId && data[4] is int delta)
            {
                string uid = auth.CurrentAccount?.Uid;
                if (!string.IsNullOrEmpty(uid) && uid == photon.LocalUser)
                    _ = ApplyAsync(uid, match, actor, user, operationId, FindItem(itemId), delta);
            }
            else if (message == AcknowledgementMessage && room.IsDm && data[3] is int quantity && quantity >= -1
                && data[4] is bool uncertain && operations.TryGetValue(operationId, out var operation)
                && operation.Match == match && operation.MatchesRecipient(actor, user)
                && operation.Status != InventoryOperationStatus.Confirmed)
            {
                if (quantity >= 0)
                    operation.Confirm(quantity);
                else if (uncertain)
                    operation.Reject("The recipient's save failed. Retry to check or apply the same operation.", canRetry: true);
                else
                    operation.Reject("The recipient rejected the item or resulting quantity.");
                Changed?.Invoke();
            }
        }

        private async Task ApplyAsync(string uid, string match, int dmActor, string dmUser,
            string operationId, Item item, int delta)
        {
            int version = roomVersion;
            int? quantity = null;
            bool uncertain = false;
            if (item && delta != 0)
            {
                try { quantity = await saves.ApplyInventoryChangeAsync(uid, match, operationId, ItemData.FromItem(item), delta); }
                catch (Exception) { uncertain = true; }
            }

            if (!IsCurrent(uid, match, version) || !room.IsDmIdentity(dmActor, dmUser))
                return;

            photon.SendMatch(new[] { dmActor }, AcknowledgementMessage, match, operationId, quantity ?? -1, uncertain);
            if (quantity.HasValue)
                Reload();
        }

        private void Update()
        {
            bool changed = false;
            foreach (var operation in operations.Values)
            {
                if (operation.Status != InventoryOperationStatus.Pending || operation.CanRetry
                    || Time.unscaledTime < operation.Deadline)
                    continue;
                operation.Uncertain("The recipient has not confirmed the save. Retry to check it.");
                changed = true;
            }
            if (changed)
                Changed?.Invoke();
        }
    }

    internal enum InventoryOperationStatus { Pending, Confirmed, Failed }

    internal sealed class InventoryOperation
    {
        private readonly string recipientUser;

        internal InventoryOperation(string id, int actor, string recipientUser, string match, string itemId, int delta)
        {
            Id = id;
            Actor = actor;
            this.recipientUser = recipientUser;
            Match = match;
            ItemId = itemId;
            Delta = delta;
        }

        internal string Id { get; }
        internal int Actor { get; }
        internal string Match { get; }
        internal string ItemId { get; }
        internal int Delta { get; }
        internal InventoryOperationStatus Status { get; private set; }
        internal int? Quantity { get; private set; }
        internal string Error { get; private set; }
        internal bool CanRetry { get; private set; }
        internal float Deadline { get; private set; }

        internal bool MatchesRecipient(int actor, string user) => Actor == actor && recipientUser == user;

        internal void Start(float deadline)
        {
            Status = InventoryOperationStatus.Pending;
            Error = null;
            CanRetry = false;
            Deadline = deadline;
        }

        internal void Uncertain(string error)
        {
            Status = InventoryOperationStatus.Pending;
            Error = error;
            CanRetry = true;
        }

        internal void Confirm(int quantity)
        {
            Status = InventoryOperationStatus.Confirmed;
            Quantity = quantity;
            Error = null;
            CanRetry = false;
        }

        internal void Reject(string error, bool canRetry = false)
        {
            Status = InventoryOperationStatus.Failed;
            Error = error;
            CanRetry = canRetry;
        }
    }
}
