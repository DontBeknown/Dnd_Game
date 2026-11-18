using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ASAL.PlayHudControls;

namespace ASAL
{
    public sealed class PlayInventoryHud : MonoBehaviour
    {
        [SerializeField] private GameObject inventoryPanel, itemPanel, inventoryRow, itemRow;
        [SerializeField] private Button inventory, items, closeInventory, closeItems;
        [SerializeField] private TMP_Dropdown recipientChoice;
        [SerializeField] private TMP_Text inventoryStatus, itemStatus;

        private readonly List<RoomMember> recipients = new();

        internal void Initialize(PlayHud hud)
        {
            inventoryPanel.SetActive(false);
            itemPanel.SetActive(false);
            inventoryRow.SetActive(false);
            itemRow.SetActive(false);
            inventory.onClick.AddListener(() => hud.ToggleModal(inventoryPanel, inventory, OpenInventory));
            items.onClick.AddListener(() => hud.ToggleModal(itemPanel, items, RenderInventory));
            closeInventory.onClick.AddListener(hud.CloseModal);
            closeItems.onClick.AddListener(hud.CloseModal);
            recipientChoice.onValueChanged.AddListener(_ => RenderInventory());
            InventoryManager.Instance.Changed += RenderInventory;
        }

        internal void Release()
        {
            if (InventoryManager.Instance)
                InventoryManager.Instance.Changed -= RenderInventory;
        }

        private void OpenInventory()
        {
            RenderInventory();
            InventoryManager.Instance.Reload();
        }

        internal void RefreshRoom(RoomManager room, bool isDm)
        {
            items.gameObject.SetActive(isDm);
            var previousActor = SelectedRecipient()?.Actor;
            recipients.Clear();
            if (isDm)
                recipients.AddRange(room.Roster.Where(member => member.Active && member.Actor != room.LocalActor));
            SetOptions(recipientChoice, recipients.Select(member => member.Name),
                recipients.FindIndex(member => member.Actor == previousActor));
            RenderInventory();
        }

        private RoomMember SelectedRecipient() => recipientChoice.value < recipients.Count ? recipients[recipientChoice.value] : null;

        private void RenderInventory()
        {
            var manager = InventoryManager.Instance;
            if (!manager)
                return;

            ClearRows(inventoryRow);
            foreach (var entry in manager.Items)
            {
                var row = AddRow(inventoryRow);
                SetText(Text(row, "Quantity"), entry.quantity.ToString());
                var icon = Find(row, "Icon").GetComponent<Image>();
                icon.sprite = Resources.Load<Sprite>(entry.iconPath ?? "");
                var button = Find(row, "ItemButton").GetComponent<Button>();
                button.onClick.AddListener(() => SetText(inventoryStatus, entry.itemName + ": " + entry.description));
                Find(row, "RemoveBT").SetActive(false);
            }

            ClearRows(itemRow);
            var recipient = SelectedRecipient();
            var operations = manager.Operations.Where(item => item.Actor == recipient?.Actor)
                .ToLookup(item => item.ItemId, StringComparer.Ordinal);
            foreach (var item in manager.Catalog)
            {
                var row = AddRow(itemRow);
                var operation = operations[item.itemID].LastOrDefault();
                var locked = operation?.Status == InventoryOperationStatus.Pending || operation?.CanRetry == true;
                SetText(Text(row, "Text"), item.itemName + (operation == null ? "" : "\n" + operation.Status
                    + (operation.Quantity.HasValue ? $" ({operation.Quantity.Value})" : "")));
                Find(row, "Icon").GetComponent<Image>().sprite = item.GetIcon();
                var add = Find(row, "ItemButton").GetComponent<Button>();
                var remove = Find(row, "RemoveBT").GetComponent<Button>();
                add.interactable = remove.interactable = recipient != null && !locked;
                add.onClick.AddListener(() => ChangeItem(recipient, item.itemID, 1));
                remove.onClick.AddListener(() => ChangeItem(recipient, item.itemID, -1));
                if (operation?.CanRetry == true)
                {
                    var retry = AddRow(itemRow);
                    SetText(Text(retry, "Text"), "Retry " + item.itemName);
                    Find(retry, "Icon").SetActive(false);
                    Find(retry, "RemoveBT").SetActive(false);
                    Find(retry, "ItemButton").GetComponent<Button>().onClick.AddListener(() => manager.Retry(operation.Id));
                }
            }

            var status = manager.Error ?? (manager.Loading ? "Loading inventory…" : manager.Items.Count == 0 ? "Inventory is empty." : "");
            SetText(inventoryStatus, status);
            SetText(itemStatus, recipient == null ? "No connected player is available."
                : operations.SelectMany(group => group).LastOrDefault()?.Error ?? "Choose an item to grant or remove.");
        }

        private void ChangeItem(RoomMember recipient, string itemId, int delta)
        {
            if (recipient == null || !InventoryManager.Instance.RequestChange(recipient.Actor, itemId, delta))
                SetText(itemStatus, "Could not send the inventory change.");
        }
    }
}
