using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ASAL
{
    public sealed class MapManager : MonoBehaviour
    {
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap defaultMap;
        [SerializeField] private Tilemap previewMap;
        [SerializeField] private Camera editorCamera;
        [SerializeField] private BuildingCategory[] categories;

        private readonly Dictionary<string, Tilemap> layers = new(StringComparer.Ordinal);
        private readonly Dictionary<BuildingCategory, Tilemap> categoryLayers = new();
        private readonly Dictionary<Tilemap, Dictionary<Vector3Int, string>> placedTiles = new();
        private Dictionary<string, BuildingObjectBase> buildables;
        private Tilemap[] eraseOrder;

        internal IEnumerable<BuildingObjectBase> Buildables => buildables.Values;
        internal string DraftId { get; private set; }
        internal bool Dirty { get; private set; }

        internal void Initialize()
        {
            BuildCatalog();
            AddLayer(defaultMap);
            foreach (var category in categories)
            {
                var layer = new GameObject("Tilemap_" + category.name);
                layer.transform.SetParent(grid.transform, false);
                var map = layer.AddComponent<Tilemap>();
                layer.AddComponent<TilemapRenderer>().sortingOrder = category.SortingOrder;
                categoryLayers.Add(category, map);
                AddLayer(map);
            }

            eraseOrder = layers.Values.OrderByDescending(map => map.GetComponent<TilemapRenderer>().sortingOrder).ToArray();
            editorCamera.depth = 100;
        }

        private void AddLayer(Tilemap map)
        {
            layers.Add(map.name, map);
            placedTiles.Add(map, new Dictionary<Vector3Int, string>());
        }

        private void BuildCatalog() => buildables = LoadBuildables();

        internal static Dictionary<string, BuildingObjectBase> LoadBuildables() =>
            Resources.LoadAll<BuildingObjectBase>("Map/Scriptables/Buildables")
                .Where(item => item.TileBase).OrderBy(item => item.name, StringComparer.Ordinal)
                .ToDictionary(item => item.name, StringComparer.Ordinal);

        internal Vector3Int CellAt(Vector2 position)
        {
            var cell = grid.WorldToCell(editorCamera.ScreenToWorldPoint(position));
            cell.z = 0;
            return cell;
        }

        internal void Pan(Vector2 movement, float scroll)
        {
            editorCamera.transform.position += (Vector3)(movement * (editorCamera.orthographicSize * Time.unscaledDeltaTime));
            editorCamera.orthographicSize = Mathf.Clamp(editorCamera.orthographicSize - scroll, 2, 40);
        }

        internal void ClearPreview() => previewMap.ClearAllTiles();

        internal void Paint(BuildingObjectBase item, Vector3Int start, Vector3Int end, bool preview)
        {
            ClearPreview();
            if (!item)
                return;
            if (item is BuildingTool)
            {
                if (!preview)
                {
                    var top = eraseOrder.FirstOrDefault(map => map.HasTile(end));
                    if (top)
                    {
                        top.SetTile(end, null);
                        placedTiles[top].Remove(end);
                        Dirty = true;
                    }
                }
                return;
            }

            if (item.PlaceType == PlaceType.Line)
            {
                if (Mathf.Abs(end.x - start.x) >= Mathf.Abs(end.y - start.y))
                    end.y = start.y;
                else
                    end.x = start.x;
            }
            else if (item.PlaceType != PlaceType.Rectangle)
                start = end;

            var map = preview ? previewMap : LayerFor(item);
            for (int y = Mathf.Min(start.y, end.y); y <= Mathf.Max(start.y, end.y); y++)
            for (int x = Mathf.Min(start.x, end.x); x <= Mathf.Max(start.x, end.x); x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (item.PlacementRestrictions != null && item.PlacementRestrictions.Any(category =>
                    category && categoryLayers.TryGetValue(category, out var restricted) && restricted.HasTile(cell)))
                    continue;
                if (preview)
                    map.SetTile(cell, item.TileBase);
                else if (!placedTiles[map].TryGetValue(cell, out var id) || id != item.name)
                {
                    map.SetTile(cell, item.TileBase);
                    placedTiles[map][cell] = item.name;
                    Dirty = true;
                }
            }
        }

        private Tilemap LayerFor(BuildingObjectBase item) =>
            item.Category && categoryLayers.TryGetValue(item.Category, out var layer) ? layer : defaultMap;

        internal TilemapDataWrapper Capture()
        {
            var data = new List<TilemapData>();
            foreach (var pair in layers)
            {
                var layer = new TilemapData { key = pair.Key };
                foreach (var tile in placedTiles[pair.Value])
                    layer.tiles.Add(new TileInfo(tile.Key, tile.Value));
                data.Add(layer);
            }
            return new TilemapDataWrapper(data);
        }

        internal void Replace(string id, TilemapDataWrapper body)
        {
            var tiles = new List<(Tilemap Map, Vector3Int Cell, BuildingObjectBase Item)>();
            if (body != null)
            {
                foreach (var layer in body.tilemapDataList)
                {
                    if (layer == null || string.IsNullOrWhiteSpace(layer.key))
                        throw new FormatException("The saved map contains an invalid layer.");
                    // The legacy save also included BuildPreview. Its contents were never part of the draft.
                    if (layer.key == previewMap.name)
                        continue;
                    if (!layers.TryGetValue(layer.key, out var map))
                        throw new FormatException("The saved map contains an unknown layer.");
                    if (layer.tiles == null)
                        continue;
                    foreach (var tile in layer.tiles)
                    {
                        if (tile == null || string.IsNullOrWhiteSpace(tile.guidForBuildable)
                            || !buildables.TryGetValue(tile.guidForBuildable, out var item) || item is BuildingTool)
                            throw new FormatException("The saved map contains an unknown tile.");
                        tiles.Add((map, tile.position, item));
                    }
                }
            }

            foreach (var layer in layers.Values)
            {
                layer.ClearAllTiles();
                placedTiles[layer].Clear();
            }
            ClearPreview();
            foreach (var tile in tiles)
            {
                tile.Map.SetTile(tile.Cell, tile.Item.TileBase);
                placedTiles[tile.Map][tile.Cell] = tile.Item.name;
            }
            MarkSaved(id);
        }

        internal void ForgetSave()
        {
            DraftId = null;
            Dirty = true;
        }

        internal void MarkSaved(string id)
        {
            DraftId = id;
            Dirty = false;
        }
    }
}
