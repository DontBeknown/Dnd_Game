using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class MapEditorPanel : MonoBehaviour
    {
        [SerializeField] private MapManager draft;
        [SerializeField] private Transform world;
        [SerializeField] private Transform palette;
        [SerializeField] private GameObject categoryPrefab;
        [SerializeField] private Button tilePrefab;
        [SerializeField] private TMP_InputField mapName;
        [SerializeField] private TMP_Dropdown maps;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button back;
        [SerializeField] private Button load;
        [SerializeField] private Button save;
        [SerializeField] private Button delete;
        [SerializeField] private Button publish;

        private KeyValuePair<string, string>[] entries = Array.Empty<KeyValuePair<string, string>>();
        private AccountProfile account;
        private BuildingObjectBase brush;
        private Vector3Int? stroke;
        private Vector3Int? previewCell;
        private string draftName = string.Empty;
        private bool busy;
        private bool indexLoaded;
        private bool confirmLoad;
        private bool confirmDelete;

        private string SelectedId => maps.value == 0 ? null : entries[maps.value - 1].Key;
        private bool Current => this && account != null && ReferenceEquals(AuthManager.Instance?.CurrentAccount, account);

        internal void Initialize(AccountProfile owner, Action close)
        {
            account = owner;
            world.SetParent(null, false);
            world.position = new Vector3(10000, 0, 0);
            draft.Initialize();
            BuildPalette();
            back.onClick.AddListener(() => close());
            load.onClick.AddListener(Load);
            save.onClick.AddListener(Save);
            delete.onClick.AddListener(Delete);
            publish.onClick.AddListener(() => Run("Publishing map…", async () =>
            {
                if (!CanPublish)
                    return;
                await RoomManager.Instance.Match.PublishAsync(draft.Capture());
                if (Current)
                    status.text = "Map published.";
            }));
            maps.onValueChanged.AddListener(_ =>
            {
                confirmLoad = confirmDelete = false;
                status.text = string.Empty;
                RenderControls();
            });
            mapName.onValueChanged.AddListener(_ => RenderControls());
            RenderIndex(null);
        }

        internal TilemapDataWrapper CaptureStartingMap()
        {
            var map = draft.Capture();
            return map.tilemapDataList.Where(layer => layer.key == "Tilemap_Floor")
                .SelectMany(layer => layer.tiles).Any(tile => MatchSnapshot.IsWalkable(map, tile.position)) ? map : null;
        }

        private bool CanPublish => RoomManager.Instance is { IsDm: true, Phase: RoomPhase.Playing };

        internal void Open()
        {
            enabled = true;
            RenderControls();
            back.Select();
            if (!indexLoaded)
                Run("Loading saved maps…", () => RefreshIndex(null));
        }

        internal void Release() => account = null;

        private void OnEnable()
        {
            if (world)
                world.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            stroke = null;
            ResetPreview();
            if (world)
                world.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (world && !world.IsChildOf(transform))
                Destroy(world.gameObject);
        }

        private void BuildPalette()
        {
            foreach (var group in draft.Buildables.Where(item => item.UICategory)
                .GroupBy(item => item.UICategory).OrderBy(group => group.Key.SiblingIndex))
            {
                var category = Instantiate(categoryPrefab, palette, false);
                category.GetComponentInChildren<TMP_Text>().text = group.Key.name;
                category.GetComponentInChildren<Image>().color = group.Key.BackgroundColor;
                var items = category.transform.Find("Items");
                foreach (var item in group)
                {
                    var button = Instantiate(tilePrefab, items, false);
                    button.name = item.name;
                    var label = button.GetComponentInChildren<TMP_Text>();
                    label.text = item is BuildingTool ? "Erase" : item.name;
                    if (item.TileBase is UnityEngine.Tilemaps.Tile tile)
                        button.image.sprite = tile.sprite;
                    button.onClick.AddListener(() =>
                    {
                        brush = item;
                        ResetPreview();
                        status.text = label.text;
                    });
                }
            }
        }

        private void Update()
        {
            if (busy || !Current || UIManager.Instance.IsScreenTransitioning)
                return;
            var input = InputManager.Instance;
            bool overUi = EventSystem.current.IsPointerOverGameObject();
            bool typing = EventSystem.current.currentSelectedGameObject
                && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>()?.isFocused == true;
            if (!stroke.HasValue)
                draft.Pan(typing ? Vector2.zero : input.EditorPan, overUi ? 0 : input.EditorZoom);

            if (overUi || input.EditorCancelStroke)
            {
                stroke = null;
                if (previewCell.HasValue)
                    ResetPreview();
                if (input.EditorCancelStroke && !overUi)
                    brush = null;
                return;
            }

            if (!brush)
                return;
            var cell = draft.CellAt(input.EditorPointer);
            if (input.EditorStrokeStarted)
                stroke = cell;
            if (stroke.HasValue && input.EditorStrokeEnded)
            {
                draft.Paint(brush, stroke.Value, cell, false);
                stroke = null;
                previewCell = null;
            }
            else if (previewCell != cell || input.EditorStrokeStarted)
            {
                draft.Paint(brush, stroke ?? cell, cell, true);
                previewCell = cell;
            }
        }

        private void ResetPreview()
        {
            previewCell = null;
            draft.ClearPreview();
        }

        private void Load()
        {
            if ((draft.Dirty || mapName.text != draftName) && !confirmLoad)
            {
                confirmLoad = true;
                status.text = "Unsaved edits will be discarded. Press again to continue.";
                return;
            }
            confirmLoad = false;
            var id = SelectedId;
            if (id == null)
            {
                draft.Replace(null, null);
                mapName.text = draftName = string.Empty;
                status.text = "New draft.";
                return;
            }
            var name = entries[maps.value - 1].Value;
            Run("Loading map…", async () =>
            {
                var body = await SaveManager.Instance.LoadMapAsync(account.Uid, id);
                if (!Current)
                    return;
                draft.Replace(id, body);
                mapName.text = draftName = name;
                status.text = "Map loaded.";
            });
        }

        private void Save()
        {
            var name = mapName.text.Trim();
            var id = draft.DraftId ?? Guid.NewGuid().ToString();
            Run("Saving map…", async () =>
            {
                var body = draft.Capture();
                await SaveManager.Instance.SaveMapAsync(account.Uid, id, name, body);
                if (!Current)
                    return;
                draft.MarkSaved(id);
                mapName.text = draftName = name;
                entries = entries.Where(entry => entry.Key != id)
                    .Append(new KeyValuePair<string, string>(id, name)).OrderBy(entry => entry.Value)
                    .ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
                RenderIndex(id);
                status.text = "Map saved.";
            });
        }

        private void Delete()
        {
            if (!confirmDelete)
            {
                confirmDelete = true;
                status.text = "Press Delete again to remove the saved map.";
                return;
            }
            var id = SelectedId;
            Run("Deleting map…", async () =>
            {
                await SaveManager.Instance.DeleteMapAsync(account.Uid, id);
                if (!Current)
                    return;
                entries = entries.Where(entry => entry.Key != id).ToArray();
                if (draft.DraftId == id)
                    draft.ForgetSave();
                RenderIndex(null);
                status.text = "Saved map deleted.";
            });
        }

        private async System.Threading.Tasks.Task RefreshIndex(string selectedId)
        {
            var result = await SaveManager.Instance.LoadMapIndexAsync(account.Uid);
            if (!Current)
                return;
            entries = result;
            indexLoaded = true;
            RenderIndex(selectedId);
            status.text = entries.Length == 0 ? "No saved maps. Paint and save a new draft." : string.Empty;
        }

        private void RenderIndex(string selectedId)
        {
            maps.ClearOptions();
            maps.AddOptions(new[] { "New map" }.Concat(entries.Select(entry => entry.Value)).ToList());
            maps.SetValueWithoutNotify(Array.FindIndex(entries, entry => entry.Key == selectedId) + 1);
            maps.RefreshShownValue();
            RenderControls();
        }

        private void RenderControls()
        {
            publish.gameObject.SetActive(CanPublish);
            publish.interactable = !busy && CanPublish;
            maps.interactable = !busy && indexLoaded;
            load.interactable = !busy && indexLoaded;
            mapName.interactable = !busy;
            save.interactable = !busy && indexLoaded && !string.IsNullOrWhiteSpace(mapName.text);
            delete.interactable = !busy && indexLoaded && SelectedId != null;
        }

        private async void Run(string message, Func<System.Threading.Tasks.Task> action)
        {
            if (!Current || busy)
                return;
            busy = true;
            confirmLoad = confirmDelete = false;
            stroke = null;
            ResetPreview();
            status.text = message;
            RenderControls();
            try
            {
                await action();
            }
            catch (Exception error)
            {
                if (Current)
                    status.text = error is FormatException ? error.Message : "Map operation failed. Try again.";
            }
            finally
            {
                busy = false;
                if (Current)
                {
                    RenderControls();
                    if (isActiveAndEnabled)
                    {
                        var selected = EventSystem.current.currentSelectedGameObject;
                        if (!selected || selected.GetComponent<Selectable>()?.IsInteractable() != true)
                            back.Select();
                    }
                }
            }
        }
    }
}
