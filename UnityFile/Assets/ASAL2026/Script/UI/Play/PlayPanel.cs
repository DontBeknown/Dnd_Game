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
        private const float DragThresholdPixels = 6f;
        private const float CollisionSkin = 0.002f;
        private const string WallLayer = "Tilemap_Wall";

        [SerializeField] private Camera playCamera;
        [SerializeField] private Transform world;
        [SerializeField] private Grid grid;
        [SerializeField] private Transform mapRoot;
        [SerializeField] private Transform tokenRoot;
        [SerializeField] private TokenView tokenPrefab;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button mapEditor;
        [SerializeField] private Button enemies;
        [SerializeField] private Button leave;
        [SerializeField] private PlayHud hud;

        private readonly Dictionary<string, Tilemap> layers = new();
        private readonly List<Collider2D> overlaps = new();
        private readonly List<RaycastHit2D> sweepHits = new();
        private readonly ContactFilter2D boardFilter = new() { useTriggers = false };
        private Dictionary<string, BuildingObjectBase> catalog;
        private TilemapCollider2D wallCollider;
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
        private int placementStartedFrame = -1;
        private MatchToken enemyTemplate;
        private TokenView preview;
        private string pressedTokenId;
        private Vector2 pressPointer;
        private Vector2 dragOffset;
        private bool dragging;
        private string selectedTokenId;

        internal TokenView NetworkTokenPrefab => tokenPrefab;
        internal bool BoardVisible => isActiveAndEnabled && account != null;

        internal void Initialize(RoomManager rooms, AccountProfile owner)
        {
            room = rooms;
            account = owner;
            matchId = rooms.Session;
            mapEditor.onClick.AddListener(UIManager.Instance.OpenMapEditor);
            enemies.onClick.AddListener(() => UIManager.Instance.OpenRecordEditor(true, enemies));
            if (!hud)
                leave.onClick.AddListener(rooms.Leave);
            status.richText = false;
            mapEditor.gameObject.SetActive(rooms.IsDm);
            enemies.gameObject.SetActive(rooms.IsDm);
            hud?.Initialize(this, rooms);
            SetBoardVisible(BoardVisible);
        }

        internal void BeginPlay()
        {
            RefreshEnemies();
            RefreshMaps();
        }

        internal void Apply(MatchSnapshot next)
        {
            snapshot = next;
            PrepareBoard(next.Map, next.MapRevision);
            SetBoardVisible(BoardVisible);
            hud?.Apply(next);
            RefreshStatus();
        }

        internal void PrepareBoard(TilemapDataWrapper map, int revision)
        {
            if (mapRevision == revision)
                return;

            ClearDrag();
            catalog ??= MapManager.LoadBuildables();
            if (world.IsChildOf(transform))
                world.SetParent(null, false);
            world.gameObject.SetActive(true);
            RenderMap(map);
            FrameMap(map);
            mapRevision = revision;
            SetBoardVisible(BoardVisible);
            Physics2D.SyncTransforms();
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
                templateError = null;
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

        internal void StartEnemyPlacement(CharacterData template)
        {
            if (!Current)
                return;
            ClearBoardInteraction();
            var character = CharacterManager.Instance.Project(template);
            if (character == null)
            {
                ShowNotice("This enemy template has invalid gameplay data. Edit the template before placing it.");
                return;
            }
            enemyTemplate = new MatchToken
            {
                RecordId = template.firebaseKey, Name = character.Name, Character = character, Hp = character.MaxHp
            };
            placementStartedFrame = Time.frameCount;
            BindPreview(enemyTemplate);
            ShowNotice(null);
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
            status.text = room.Error ?? notice ?? mapNotice ?? templateError ?? (enemyTemplate != null
                ? "Click an empty spot to place " + enemyTemplate.Name + ". Right-click to stop."
                : "Drag a token you control to move it. Click it to see details.");
        }

        internal void Release()
        {
            ClearBoardInteraction();
            hud?.Release();
            account = null;
            SetBoardVisible(false);
        }

        private void OnEnable() => SetBoardVisible(BoardVisible);

        private void OnDisable()
        {
            ClearBoardInteraction();
            SetSelectedToken(null);
            SetBoardVisible(false);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                ClearDrag();
        }

        private void OnDestroy()
        {
            if (world && !world.IsChildOf(transform))
            {
                if (Application.isPlaying)
                    Destroy(world.gameObject);
                else
                    DestroyImmediate(world.gameObject);
            }
        }

        private void SetBoardVisible(bool visible)
        {
            if (playCamera)
                playCamera.enabled = visible;
            foreach (var layer in layers.Values)
                layer.GetComponent<TilemapRenderer>().enabled = visible;
            if (PhotonManager.Instance)
                foreach (var view in PhotonManager.Instance.TokenViews.Values)
                    if (view)
                        view.SetBoardVisible(visible);
        }

        private void FrameMap(TilemapDataWrapper map)
        {
            var tiles = map?.tilemapDataList.SelectMany(layer => layer.tiles).ToArray();
            var min = tiles is { Length: > 0 }
                ? new Vector3Int(tiles.Min(tile => tile.position.x), tiles.Min(tile => tile.position.y), 0)
                : Vector3Int.zero;
            var max = tiles is { Length: > 0 }
                ? new Vector3Int(tiles.Max(tile => tile.position.x), tiles.Max(tile => tile.position.y), 0)
                : Vector3Int.zero;
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

            foreach (var layer in map?.tilemapDataList ?? Enumerable.Empty<TilemapData>())
            {
                if (!layers.TryGetValue(layer.key, out var tilemap))
                {
                    var layerObject = new GameObject(layer.key, typeof(Tilemap), typeof(TilemapRenderer));
                    layerObject.transform.SetParent(mapRoot, false);
                    tilemap = layerObject.GetComponent<Tilemap>();
                    layers.Add(layer.key, tilemap);
                    if (layer.key == WallLayer)
                        wallCollider = layerObject.AddComponent<TilemapCollider2D>();
                }

                var renderer = tilemap.GetComponent<TilemapRenderer>();
                foreach (var tile in layer.tiles)
                {
                    var item = catalog[tile.guidForBuildable];
                    tilemap.SetTile(tile.position, item.TileBase);
                    renderer.sortingOrder = item.Category ? item.Category.SortingOrder : -2;
                    if (layer.key == WallLayer)
                        tilemap.SetColliderType(tile.position, Tile.ColliderType.Grid);
                }
            }
            if (wallCollider)
                wallCollider.ProcessTilemapChanges();
        }

        internal static bool CanSelectToken(int ownerActor, int localActor, bool isDm) =>
            isDm || (localActor > 0 && ownerActor == localActor);

        internal Vector3 ToWorld(Vector2 position) => CharacterManager.BoardToWorld(grid, position);
        internal Vector2 ToBoard(Vector3 position) => CharacterManager.WorldToBoard(grid, position);

        internal bool IsOpen(Vector2 position, string ignoreTokenId = null)
        {
            if (!IsFinite(position) || !tokenPrefab || !tokenPrefab.Footprint)
                return false;

            Physics2D.SyncTransforms();
            Physics2D.OverlapCircle(ToWorld(position), Radius(tokenPrefab.Footprint), boardFilter, overlaps);
            return !overlaps.Any(hit => BlocksToken(hit, ignoreTokenId));
        }

        internal bool TryMoveToken(string id, Vector2 requested, out Vector2 accepted)
        {
            accepted = default;
            if (!PhotonManager.Instance || id == null
                || !PhotonManager.Instance.TokenViews.TryGetValue(id, out var view) || !view)
                return false;
            accepted = view.BoardPosition;
            if (!IsFinite(requested) || !view.Footprint || !view.Footprint.isActiveAndEnabled)
                return false;
            if (requested == accepted)
                return true;

            Physics2D.SyncTransforms();
            Vector2 start = view.Footprint.bounds.center;
            Vector2 target = ToWorld(requested);
            var direction = target - start;
            var distance = direction.magnitude;
            if (distance <= 0f)
                return true;

            direction /= distance;
            Physics2D.CircleCast(start, Radius(view.Footprint), direction, boardFilter, sweepHits, distance);
            var travel = distance;
            foreach (var hit in sweepHits)
                if (BlocksToken(hit.collider, id))
                    travel = Mathf.Min(travel, Mathf.Max(0f, hit.distance - CollisionSkin));
            if (travel <= 0f)
                return true;
            accepted = travel < distance ? ToBoard(start + direction * travel) : requested;
            view.SetBoardPosition(ToWorld(accepted), accepted);
            Physics2D.SyncTransforms();
            return true;
        }

        private static bool IsFinite(Vector2 position) => float.IsFinite(position.x) && float.IsFinite(position.y)
            && Mathf.Abs(position.x) <= 1000000f && Mathf.Abs(position.y) <= 1000000f;

        private static float Radius(CircleCollider2D footprint)
        {
            var scale = footprint.transform.lossyScale;
            return footprint.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }

        private bool BlocksToken(Collider2D hit, string ignored) =>
            hit == wallCollider || (ViewFor(hit) is TokenView view && view.TokenId != ignored);

        private static TokenView ViewFor(Collider2D hit) =>
            hit && PhotonManager.Instance && hit.TryGetComponent<TokenView>(out var view)
            && !string.IsNullOrEmpty(view.TokenId) && PhotonManager.Instance.TokenViews.TryGetValue(view.TokenId, out var registered)
            && registered == view ? view : null;

        internal void SetSelectedToken(string id)
        {
            var photon = PhotonManager.Instance;
            if (photon && selectedTokenId != null && photon.TokenViews.TryGetValue(selectedTokenId, out var previous) && previous)
                previous.SetSelected(false);
            selectedTokenId = id;
            if (photon && id != null && photon.TokenViews.TryGetValue(id, out var current) && current)
                current.SetSelected(true);
        }

        // The EventSystem resolves this frame's UI pointer before the board handles it.
        private void LateUpdate()
        {
            if (snapshot == null || UIManager.Instance.IsScreenTransitioning)
                return;
            if (!room.CanLeave)
            {
                ClearBoardInteraction();
                return;
            }

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
            {
                ClearDrag();
                if (preview)
                    preview.gameObject.SetActive(false);
                return;
            }
            if (input.BoardCancel || input.BoardDismiss)
            {
                ClearBoardInteraction();
                ShowNotice(null);
                return;
            }

            playCamera.transform.position += (Vector3)(input.BoardPan * (playCamera.orthographicSize * Time.unscaledDeltaTime));
            var pointer = input.BoardPointer;
            if (!playCamera.pixelRect.Contains(pointer) || EventSystem.current?.IsPointerOverGameObject() == true)
            {
                if (input.BoardReleased)
                    ClearDrag();
                if (preview)
                    preview.gameObject.SetActive(false);
                return;
            }

            playCamera.orthographicSize = Mathf.Clamp(playCamera.orthographicSize - input.BoardZoom, 2f, 40f);
            if (!input.BoardClick && enemyTemplate == null && pressedTokenId == null)
                return;

            var depth = tokenRoot.position.z - playCamera.transform.position.z;
            var board = ToBoard(playCamera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, depth)));

            if (hud && hud.IsTargeting)
            {
                if (input.BoardClick)
                    hud.AcceptTarget(TokenAt(board)?.Token);
                return;
            }
            if (enemyTemplate != null)
            {
                UpdateEnemyPlacement(board, input.BoardClick && placementStartedFrame != Time.frameCount);
                return;
            }

            if (input.BoardClick)
            {
                ClearDrag();
                var hitView = TokenAt(board);
                var hit = hitView?.Token;
                if (hit == null || !CanSelectToken(hit.OwnerActor, room.LocalActor, room.IsDm))
                    return;
                pressedTokenId = hit.Id;
                pressPointer = pointer;
                dragOffset = hitView.BoardPosition - board;
                SetSelectedToken(hit.Id);
            }
            if (pressedTokenId == null)
                return;

            var photon = PhotonManager.Instance;
            if (!photon || !photon.TokenViews.TryGetValue(pressedTokenId, out var view) || !view || view.Token is not { } token
                || !CanSelectToken(token.OwnerActor, room.LocalActor, room.IsDm)
                || (!input.BoardHeld && !input.BoardReleased))
            {
                ClearDrag();
                return;
            }

            if (!dragging && (pointer - pressPointer).sqrMagnitude >= DragThresholdPixels * DragThresholdPixels)
            {
                dragging = true;
                room.Match.BeginDrag(token.Id);
            }

            if (dragging)
            {
                if (input.BoardReleased)
                {
                    room.Match.EndDrag(token.Id, board + dragOffset);
                    ClearDrag(false);
                }
                else
                    room.Match.DragTo(token.Id, board + dragOffset);
                return;
            }

            if (!input.BoardReleased)
                return;
            ClearDrag();
            if (hud && hud.Inspect(token))
                SetSelectedToken(token.Id);
        }

        private TokenView TokenAt(Vector2 board)
        {
            Physics2D.SyncTransforms();
            Physics2D.OverlapPoint(ToWorld(board), boardFilter, overlaps);
            foreach (var hit in overlaps)
                if (ViewFor(hit) is TokenView view && view.Token != null)
                    return view;
            return null;
        }

        private void UpdateEnemyPlacement(Vector2 board, bool clicked)
        {
            if (!room.IsDm)
            {
                ClearBoardInteraction();
                ShowNotice(null);
                return;
            }

            var available = IsOpen(board);
            MovePreview(board, available);
            if (!clicked)
                return;
            if (!available)
            {
                ShowNotice("Choose a spot clear of tokens and walls.");
                return;
            }
            if (!room.Match.CanSubmit)
            {
                ShowNotice("Wait for the current match update before placing another enemy.");
                return;
            }

            var enemy = new MatchToken
            {
                Id = Guid.NewGuid().ToString("N"), RecordId = enemyTemplate.RecordId,
                Name = enemyTemplate.Name, Character = enemyTemplate.Character, Hp = enemyTemplate.Hp
            };
            ShowNotice(null);
            room.Match.Submit(new MatchCommand
            {
                Action = MatchAction.Place, TokenId = enemy.Id, RecordId = enemy.RecordId, Position = board
            }, enemy);
        }

        private void BindPreview(MatchToken token)
        {
            if (!preview)
                preview = Instantiate(tokenPrefab, tokenRoot, false);
            preview.Bind(token, ToWorld(token.Position));
            preview.SetPreview(true);
            preview.gameObject.SetActive(false);
        }

        private void MovePreview(Vector2 position, bool selected)
        {
            preview.transform.position = ToWorld(position);
            preview.SetSelected(selected);
            preview.gameObject.SetActive(true);
        }

        private void ClearDrag(bool cancel = true)
        {
            if (pressedTokenId == null)
                return;
            if (dragging && cancel)
                room?.Match?.CancelDrag(pressedTokenId);
            pressedTokenId = null;
            dragging = false;
            SetSelectedToken(null);
        }

        private void ClearBoardInteraction()
        {
            ClearDrag();
            enemyTemplate = null;
            if (preview)
                preview.gameObject.SetActive(false);
        }
    }
}

