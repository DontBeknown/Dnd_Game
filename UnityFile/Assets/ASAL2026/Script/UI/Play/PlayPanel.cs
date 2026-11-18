using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class PlayPanel : MonoBehaviour
    {
        [SerializeField] private Camera playCamera;
        [SerializeField] private Transform world;
        [SerializeField] private Grid grid;
        [SerializeField] private Transform mapRoot;
        [SerializeField] private Transform tokenRoot;
        [SerializeField] private TokenView tokenPrefab;
        [SerializeField] private TMP_Dropdown tokenChoices;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button mapEditor;
        [SerializeField] private Button enemies;
        [SerializeField] private Button leave;
        [SerializeField] private PlayHud hud;

        private readonly Dictionary<string, Tilemap> layers = new();
        private readonly List<MatchToken> choices = new();
        private Dictionary<string, BuildingObjectBase> catalog;
        private CharacterData[] templates = Array.Empty<CharacterData>();
        private MatchSnapshot snapshot;
        private RoomManager room;
        private AccountProfile account;
        private string matchId;
        private string templateError;
        private string mapNotice;
        private string notice;
        private KeyValuePair<string, string>[] savedMaps = Array.Empty<KeyValuePair<string, string>>();
        private int mapIndexRequest;
        private bool publishingMap;
        private int templateRequest;
        private int mapRevision = -1;

        internal void Initialize(RoomManager rooms, AccountProfile owner)
        {
            room = rooms;
            account = owner;
            matchId = rooms.Session;
            world.SetParent(null, false);
            world.gameObject.SetActive(false);
            catalog = MapManager.LoadBuildables();
            mapEditor.onClick.AddListener(UIManager.Instance.OpenMapEditor);
            enemies.onClick.AddListener(() => UIManager.Instance.OpenRecordEditor(true, enemies));
            if (!hud)
                leave.onClick.AddListener(rooms.Leave);
            tokenChoices.captionText.richText = tokenChoices.itemText.richText = false;
            status.richText = false;
            mapEditor.gameObject.SetActive(rooms.IsDm);
            enemies.gameObject.SetActive(rooms.IsDm);
            hud?.Initialize(this, rooms);
            RefreshEnemies();
            RefreshMaps();
        }

        internal void Apply(MatchSnapshot next)
        {
            snapshot = next;
            if (mapRevision != next.MapRevision)
            {
                RenderMap(next.Map);
                FrameMap(next.Map);
                mapRevision = next.MapRevision;
            }
            CharacterManager.Instance.Reconcile(next, tokenPrefab, tokenRoot, grid);
            RenderChoices();
            hud?.Apply(next);
            RefreshStatus();
        }

        internal void FocusMapEditor()
        {
            mapEditor.Select();
            RefreshMaps();
        }

        internal async void RefreshEnemies()
        {
            if (!room.IsDm)
                return;
            var request = ++templateRequest;
            try
            {
                var records = await CharacterManager.Instance.LoadAsync(account, true);
                if (!IsCurrent(request))
                    return;
                templates = records;
                templateError = null;
                RenderChoices();
                hud?.SetEnemies(records);
            }
            catch (Exception)
            {
                if (IsCurrent(request))
                    templateError = "Could not load enemy templates. Reopen the template editor to retry.";
            }

            if (IsCurrent(request))
                RefreshStatus();
        }

        private bool IsCurrent(int request) => this && request == templateRequest && account != null
            && ReferenceEquals(account, AuthManager.Instance?.CurrentAccount) && matchId == room.Session;

        private bool Current => this && account != null && ReferenceEquals(account, AuthManager.Instance?.CurrentAccount)
            && room.IsDm && matchId == room.Session && room.Phase == RoomPhase.Playing;

        private async void RefreshMaps()
        {
            if (!hud || !room.IsDm)
                return;
            var request = ++mapIndexRequest;
            try
            {
                var maps = await SaveManager.Instance.LoadMapIndexAsync(account.Uid);
                if (Current && request == mapIndexRequest)
                {
                    savedMaps = maps;
                    hud.SetMaps(savedMaps.Select(entry => entry.Value), 0, publishingMap);
                }
            }
            catch (Exception)
            {
                if (Current && request == mapIndexRequest)
                {
                    mapNotice = "Could not load saved maps. Open MapMaker to retry.";
                    RefreshStatus();
                }
            }
        }

        internal async void LoadSavedMap(int selection)
        {
            if (!Current || publishingMap || selection < 0 || selection >= savedMaps.Length)
                return;
            var entry = savedMaps[selection];
            publishingMap = true;
            hud.SetMaps(savedMaps.Select(map => map.Value), selection, true);
            mapNotice = "Loading " + entry.Value + "…";
            RefreshStatus();
            try
            {
                var map = await SaveManager.Instance.LoadMapAsync(account.Uid, entry.Key);
                if (Current)
                    await room.Match.PublishAsync(map);
                if (Current)
                    mapNotice = null;
            }
            catch (Exception error)
            {
                if (Current)
                    mapNotice = error is FormatException ? error.Message : "Could not load the saved map.";
            }
            finally
            {
                publishingMap = false;
                if (Current)
                {
                    hud.SetMaps(savedMaps.Select(map => map.Value), selection, false);
                    RefreshStatus();
                }
            }
        }

        internal void SelectEnemy(string templateId)
        {
            var index = choices.FindIndex(token => token.Id == "template-" + templateId);
            if (index >= 0)
            {
                tokenChoices.value = index;
                tokenChoices.Select();
            }
        }

        internal void ShowNotice(string message)
        {
            notice = message;
            RefreshStatus();
        }

        internal void RefreshStatus()
        {
            leave.interactable = room.CanLeave;
            hud?.RefreshRoom();
            status.text = room.Error ?? notice ?? mapNotice ?? templateError ?? (snapshot?.Map == null
                ? room.IsDm ? "Open MapMaker and load a saved map." : "Waiting for the DM to load a map."
                : "Click a floor cell to place or move the selected token. Right-click a token for details.");
        }

        internal void Release()
        {
            hud?.Release();
            account = null;
            CharacterManager.Instance.ClearViews();
        }

        private void OnEnable() => world.gameObject.SetActive(true);

        private void OnDisable()
        {
            if (world)
                world.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (world && !world.IsChildOf(transform))
                Destroy(world.gameObject);
        }

        private void FrameMap(TilemapDataWrapper map)
        {
            if (map == null)
                return;
            var floor = map.tilemapDataList.Where(layer => layer.key == "Tilemap_Floor").SelectMany(layer => layer.tiles);
            var min = new Vector3Int(floor.Min(tile => tile.position.x), floor.Min(tile => tile.position.y), 0);
            var max = new Vector3Int(floor.Max(tile => tile.position.x), floor.Max(tile => tile.position.y), 0);
            var low = grid.GetCellCenterWorld(min);
            var high = grid.GetCellCenterWorld(max);
            var center = (low + high) * 0.5f;
            playCamera.transform.position = new Vector3(center.x, center.y, playCamera.transform.position.z);
            playCamera.orthographicSize = Mathf.Clamp(Mathf.Max(high.y - low.y,
                (high.x - low.x) / playCamera.aspect) * 0.5f + 2, 4, 40);
        }

        private void RenderMap(TilemapDataWrapper map)
        {
            foreach (var layer in layers.Values)
                layer.ClearAllTiles();

            if (map == null)
                return;

            foreach (var layer in map.tilemapDataList)
            {
                if (!layers.TryGetValue(layer.key, out var tilemap))
                {
                    var layerObject = new GameObject(layer.key, typeof(Tilemap), typeof(TilemapRenderer));
                    layerObject.transform.SetParent(mapRoot, false);
                    tilemap = layerObject.GetComponent<Tilemap>();
                    layers.Add(layer.key, tilemap);
                }

                foreach (var tile in layer.tiles)
                {
                    var item = catalog[tile.guidForBuildable];
                    tilemap.SetTile(tile.position, item.TileBase);
                    tilemap.GetComponent<TilemapRenderer>().sortingOrder = item.Category ? item.Category.SortingOrder : -2;
                }
            }
        }

        private void RenderChoices()
        {
            if (snapshot == null)
                return;
            var previous = choices.Count > tokenChoices.value ? choices[tokenChoices.value].Id : null;
            choices.Clear();
            foreach (var member in snapshot.Roster)
            {
                if (string.IsNullOrEmpty(member.RecordId) || (!room.IsDm && member.Actor != room.LocalActor))
                    continue;
                var id = MatchSnapshot.PlayerTokenId(member.Actor);
                choices.Add(Array.Find(snapshot.Tokens, token => token.Id == id) ?? new MatchToken
                {
                    Id = id, OwnerActor = member.Actor, RecordId = member.RecordId, Name = member.CharacterName
                });
            }
            if (room.IsDm)
            {
                choices.AddRange(snapshot.Tokens.Where(token => token.OwnerActor == 0));
                choices.AddRange(templates.Select(template => new MatchToken
                {
                    Id = "template-" + template.firebaseKey, RecordId = template.firebaseKey, Name = template.characterName
                }));
            }

            tokenChoices.ClearOptions();
            tokenChoices.AddOptions(choices.Select(token => (token.Placed ? "Move " : "Place ") + token.Name).ToList());
            tokenChoices.SetValueWithoutNotify(Math.Max(0, choices.FindIndex(token => token.Id == previous)));
            tokenChoices.RefreshShownValue();
            tokenChoices.interactable = choices.Count > 0 && snapshot.Map != null;
        }

        private void Update()
        {
            if (snapshot == null || UIManager.Instance.IsScreenTransitioning || !room.CanLeave)
                return;
            var input = InputManager.Instance;
            if ((input.BoardCancel || input.BoardDismiss) && hud && hud.IsTargeting)
            {
                hud.CancelTarget();
                return;
            }
            if (input.BoardDismiss && hud && hud.BlocksBoard)
            {
                hud.Dismiss();
                return;
            }
            if (hud && hud.BlocksBoard)
                return;
            playCamera.transform.position += (Vector3)(input.BoardPan * (playCamera.orthographicSize * Time.unscaledDeltaTime));
            if (EventSystem.current.IsPointerOverGameObject())
                return;

            playCamera.orthographicSize = Mathf.Clamp(playCamera.orthographicSize - input.BoardZoom, 2f, 40f);
            if (!input.BoardClick && !input.BoardCancel)
                return;

            var cell = grid.WorldToCell(playCamera.ScreenToWorldPoint(input.BoardPointer));
            cell.z = 0;
            if (hud && (hud.IsTargeting || input.BoardCancel))
            {
                var hit = Array.Find(snapshot.Tokens, item => item.Placed && item.Cell == cell);
                if (hud.IsTargeting)
                    hud.AcceptTarget(hit);
                else
                    hud.Inspect(hit);
                return;
            }
            if (!input.BoardClick || !tokenChoices.interactable)
                return;

            var token = choices[tokenChoices.value];
            var command = new MatchCommand
            {
                Action = token.Placed ? MatchAction.Move : MatchAction.Place,
                TokenId = token.Id,
                OwnerActor = token.OwnerActor,
                RecordId = token.RecordId,
                Cell = cell
            };
            MatchToken enemy = null;
            if (token.OwnerActor == 0 && token.Id.StartsWith("template-", StringComparison.Ordinal))
            {
                var template = templates.FirstOrDefault(record => record.firebaseKey == token.RecordId);
                if (template == null)
                    return;
                enemy = new MatchToken
                {
                    Id = Guid.NewGuid().ToString("N"), RecordId = token.RecordId, Name = token.Name,
                    Character = CharacterManager.Instance.Project(template)
                };
                command.TokenId = enemy.Id;
            }
            room.Match.Submit(command, enemy);
        }
    }
}

