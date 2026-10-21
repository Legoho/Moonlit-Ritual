using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The tactical board (GDD 5.1): tile coordinates, occupancy, orthogonal pathfinding,
/// tile clicks and optional tile highlights. Tiles are not GameObjects; they are just coordinates.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    public static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    [Header("Source of the board")]
    [Tooltip("If set (or found in the scene), the grid copies MapManager's generated map: same cells, " +
             "same cell size, walkable = Map_So.isWalkable. Grid tile (x, y) = map (column, row).")]
    public MapManager mapManager;
    [Tooltip("Only used when there is no MapManager: parent of hand-placed tile colliders to fit the grid to.")]
    public Transform tileRoot;

    /// <summary>False until the board is known. With a MapManager, RitualManager calls SyncFromMap after the map is generated.</summary>
    public bool IsReady { get; private set; }
    public bool UsesMap => mapManager != null;

    [Header("Board (GDD 5.1) - overwritten when Tile Root is set")]
    public int width = 20;
    public int height = 20;
    [Tooltip("Size of ONE cell. A 3x3 start tile should be 3 x cellSize wide.")]
    public float cellSize = 1f;
    [Tooltip("World position of the bottom-left OUTER CORNER of tile (0,0) - not the board centre. " +
             "Y = height units stand at.")]
    public Vector3 origin = Vector3.zero;
    [Tooltip("On: tiles lie on the XZ plane (3D isometric). Off: tiles lie on the XY plane (2D).")]
    public bool useXZPlane = true;

    [Header("Clicks")]
    [Tooltip("Logs every board click and the tile it maps to.")]
    [SerializeField] private bool debugClicks = true;

    // Cells covered by a tile collider (only used when tileRoot is set).
    private HashSet<Vector2Int> tileCells;

    [Header("Tile highlights (optional)")]
    [Tooltip("A flat quad or sprite without a collider. Pooled and recolored per use.")]
    [SerializeField] private GameObject tileHighlightPrefab;

    private readonly List<GameObject> highlightPool = new();
    private int activeHighlights;

    private readonly Dictionary<Vector2Int, GridUnit> occupants = new();
    private RitualManager ritualManager;
    private Camera cam;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Two GridManagers in the scene; destroying the second one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (mapManager == null) mapManager = FindAnyObjectByType<MapManager>();
        if (mapManager == null)
        {
            FitToTiles(true);
            IsReady = true;
        }
        // Otherwise wait: MapManager generates in its Start, and RitualManager calls SyncFromMap afterwards.
    }

    // ---------- Reading MapManager ----------

    public static Vector2Int MapCellToTile(Vector2Int rowCol) => new Vector2Int(rowCol.y, rowCol.x);

    /// <summary>
    /// Copies the generated map. Call after MapManager has generated (and again if it regenerates).
    /// Ritual cells always count as walkable so heroines can stand around the Anchor.
    /// </summary>
    public bool SyncFromMap(bool log = true)
    {
        if (mapManager == null || mapManager.Rows <= 0 || mapManager.Columns <= 0) return false;

        cellSize = mapManager.CellSize;
        width = mapManager.Columns;
        height = mapManager.Rows;
        useXZPlane = true;

        // MapManager puts cell (row, col) CENTRES at mapCenter + ((col - cc) * size, 0, (row - cr) * size).
        // origin is the outer corner of tile (0,0), so step back half a cell.
        int centerRow = mapManager.Rows / 2;
        int centerCol = mapManager.Columns / 2;
        origin = mapManager.MapCenter - new Vector3((centerCol + 0.5f) * cellSize, 0f, (centerRow + 0.5f) * cellSize);

        tileCells = new HashSet<Vector2Int>();
        for (int r = 0; r < mapManager.Rows; r++)
        {
            for (int c = 0; c < mapManager.Columns; c++)
            {
                Map_So cell = mapManager.map[r, c];
                if (cell == null) continue;
                if (cell.isWalkable || cell.category == MapCellType.Ritual)
                    tileCells.Add(new Vector2Int(c, r));
            }
        }

        occupants.Clear();
        IsReady = true;

        if (log)
            Debug.Log($"[Grid] Synced from MapManager: {width}x{height} cells of {cellSize}, " +
                      $"{tileCells.Count} walkable, origin {origin}.", this);
        return true;
    }

    /// <summary>Grid tiles of every map cell of this category (e.g. Spawn, Ritual).</summary>
    public List<Vector2Int> TilesOfCategory(MapCellType category)
    {
        var tiles = new List<Vector2Int>();
        if (mapManager == null) return tiles;
        foreach (var rowCol in mapManager.GetCellsOfCategory(category))
            tiles.Add(MapCellToTile(rowCol));
        return tiles;
    }

    /// <summary>The middle cell of the 3x3 ritual circle (where the Anchor goes).</summary>
    public Vector2Int RitualCenterTile =>
        mapManager != null ? new Vector2Int(mapManager.Columns / 2, mapManager.Rows / 2) : new Vector2Int(width / 2, height / 2);

    private void Start()
    {
        ritualManager = FindAnyObjectByType<RitualManager>();
        cam = Camera.main;
    }

    // ---------- Fitting the grid to the tile colliders ----------

    /// <summary>
    /// Reads every collider under tileRoot. The combined footprint becomes the board, its top surface
    /// becomes the height units stand at, and each collider marks the cells it covers as walkable.
    /// </summary>
    public void FitToTiles(bool log)
    {
        if (tileRoot == null) { tileCells = null; return; }

        var colliders = tileRoot.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            Debug.LogWarning("[Grid] Tile Root has no colliders; using the manual board settings.", this);
            tileCells = null;
            return;
        }

        Bounds all = colliders[0].bounds;
        foreach (var c in colliders) all.Encapsulate(c.bounds);

        float minA = all.min.x, maxA = all.max.x;
        float minB = useXZPlane ? all.min.z : all.min.y;
        float maxB = useXZPlane ? all.max.z : all.max.y;
        float top = useXZPlane ? all.max.y : all.min.z;

        origin = useXZPlane ? new Vector3(minA, top, minB) : new Vector3(minA, minB, top);
        width = Mathf.Max(1, Mathf.RoundToInt((maxA - minA) / cellSize));
        height = Mathf.Max(1, Mathf.RoundToInt((maxB - minB) / cellSize));

        tileCells = new HashSet<Vector2Int>();
        foreach (var c in colliders)
        {
            Bounds b = c.bounds;
            // Shrink slightly so a collider that ends exactly on a cell edge doesn't claim the next cell.
            float pad = cellSize * 0.1f;
            Vector2Int min = WorldToGridUnchecked(b.min + Vector3.one * pad);
            Vector2Int max = WorldToGridUnchecked(b.max - Vector3.one * pad);
            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                    tileCells.Add(new Vector2Int(x, y));
        }

        if (log)
            Debug.Log($"[Grid] Fitted to {colliders.Length} tile colliders: {width}x{height} cells, " +
                      $"{tileCells.Count} walkable, origin {origin}.", this);
    }

    // ---------- Coordinates ----------

    public bool InBounds(Vector2Int t) =>
        t.x >= 0 && t.y >= 0 && t.x < width && t.y < height && (tileCells == null || tileCells.Contains(t));

    /// <summary>Orthogonal (Manhattan) tile distance, used for movement and ranges (GDD 5.1).</summary>
    public static int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    /// <summary>
    /// Ability range: straight steps cost 1, diagonal steps cost 1.5.
    /// Example: 2 right + 2 up = 2 diagonals = 3.0, 3 right + 1 up = 1 diagonal + 2 straight = 3.5.
    /// </summary>
    public static float RangeDistance(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return straight + diagonal * 1.5f;
    }

    public Vector3 GridToWorld(Vector2Int t)
    {
        float a = (t.x + 0.5f) * cellSize;
        float b = (t.y + 0.5f) * cellSize;
        return origin + (useXZPlane ? new Vector3(a, 0f, b) : new Vector3(a, b, 0f));
    }

    public Vector2Int WorldToGrid(Vector3 world) => WorldToGridUnchecked(world);

    private Vector2Int WorldToGridUnchecked(Vector3 world)
    {
        Vector3 local = world - origin;
        float b = useXZPlane ? local.z : local.y;
        return new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(b / cellSize));
    }

    // ---------- Occupancy ----------

    public GridUnit GetOccupant(Vector2Int t) => occupants.TryGetValue(t, out var u) ? u : null;
    public bool IsFree(Vector2Int t) => InBounds(t) && !occupants.ContainsKey(t);

    /// <summary>Moves a unit's occupancy to tile t. Fails if t is out of bounds or taken by another unit.</summary>
    public bool Register(GridUnit unit, Vector2Int t)
    {
        if (!InBounds(t)) return false;
        if (occupants.TryGetValue(t, out var other) && other != unit) return false;
        Unregister(unit);
        occupants[t] = unit;
        return true;
    }

    public void Unregister(GridUnit unit)
    {
        if (unit.IsOnGrid && occupants.TryGetValue(unit.GridPosition, out var o) && o == unit)
            occupants.Remove(unit.GridPosition);
    }

    /// <summary>Closest free tile to start (by tile distance, ignoring walls), within maxRadius.</summary>
    public bool TryFindNearestFree(Vector2Int start, int maxRadius, out Vector2Int result)
    {
        var dist = Flood(start, maxRadius, InBounds);
        Vector2Int best = start;
        int bestDist = int.MaxValue;
        foreach (var kv in dist)
        {
            if (kv.Value < bestDist && IsFree(kv.Key))
            {
                best = kv.Key;
                bestDist = kv.Value;
            }
        }
        result = best;
        return bestDist != int.MaxValue;
    }

    // ---------- Pathfinding (orthogonal BFS) ----------

    /// <summary>
    /// Breadth-first flood from start. Returns step distance per reached tile (start = 0).
    /// maxSteps &lt; 0 means unlimited. Fill cameFrom to rebuild paths with BuildPath.
    /// </summary>
    public Dictionary<Vector2Int, int> Flood(Vector2Int start, int maxSteps, Func<Vector2Int, bool> isWalkable,
        Dictionary<Vector2Int, Vector2Int> cameFrom = null)
    {
        var dist = new Dictionary<Vector2Int, int> { { start, 0 } };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int d = dist[current];
            if (maxSteps >= 0 && d >= maxSteps) continue;

            foreach (var dir in Directions)
            {
                var next = current + dir;
                if (dist.ContainsKey(next) || !InBounds(next) || !isWalkable(next)) continue;
                dist[next] = d + 1;
                if (cameFrom != null) cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }
        return dist;
    }

    /// <summary>Path from start to end (excluding start, including end).</summary>
    public static List<Vector2Int> BuildPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int start, Vector2Int end)
    {
        var path = new List<Vector2Int>();
        var current = end;
        while (current != start)
        {
            path.Add(current);
            current = cameFrom[current];
        }
        path.Reverse();
        return path;
    }

    // ---------- Input: tile clicks ----------

    private void Update()
    {
        if (ritualManager == null || cam == null) return;

        if (Input.GetMouseButtonDown(1))
        {
            ritualManager.CancelAction();
            return;
        }
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out var hit))
        {
            // Clicks on units are handled by GridUnit.OnMouseDown.
            if (hit.collider.GetComponentInParent<GridUnit>() != null) return;

            // The exact surface point that was clicked. Height is ignored by WorldToGrid, so this works
            // for 1x1 tiles, the 3x3 start tile and one big floor alike.
            Vector3 point = hit.point - hit.normal * 0.01f;
            ReportTileClick(WorldToGrid(point), hit.collider.name);
            return;
        }

        var hit2D = Physics2D.GetRayIntersection(ray);
        if (hit2D.collider != null && hit2D.collider.GetComponentInParent<GridUnit>() != null) return;

        // No collider under the mouse: fall back to the flat board plane.
        var plane = useXZPlane ? new Plane(Vector3.up, origin) : new Plane(Vector3.forward, origin);
        if (plane.Raycast(ray, out float enter))
            ReportTileClick(WorldToGrid(ray.GetPoint(enter)), "board plane");
    }

    private void ReportTileClick(Vector2Int tile, string source)
    {
        if (debugClicks) Debug.Log($"[Grid] Click on {source} -> tile {tile} (in bounds: {InBounds(tile)})");
        if (InBounds(tile)) ritualManager.OnTileClicked(tile);
    }

    // ---------- Highlights ----------

    public void ShowHighlights(IEnumerable<Vector2Int> tiles, Color color)
    {
        ClearHighlights();
        if (tileHighlightPrefab == null) return;

        Vector3 lift = useXZPlane ? Vector3.up * 0.01f : Vector3.back * 0.01f;
        foreach (var t in tiles)
        {
            GameObject h;
            if (activeHighlights < highlightPool.Count)
            {
                h = highlightPool[activeHighlights];
            }
            else
            {
                h = Instantiate(tileHighlightPrefab, transform);
                highlightPool.Add(h);
            }
            activeHighlights++;
            h.transform.position = GridToWorld(t) + lift;
            h.SetActive(true);

            var sprite = h.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) sprite.color = color;
            else
            {
                var r = h.GetComponentInChildren<Renderer>();
                if (r != null) r.material.color = color;
            }
        }
    }

    public void ClearHighlights()
    {
        for (int i = 0; i < activeHighlights; i++)
            highlightPool[i].SetActive(false);
        activeHighlights = 0;
    }

    // ---------- Editor gizmos ----------

    private Vector3 Corner(float x, float y) =>
        origin + (useXZPlane ? new Vector3(x * cellSize, 0f, y * cellSize) : new Vector3(x * cellSize, y * cellSize, 0f));

    private void OnDrawGizmosSelected()
    {
        // Preview in the editor so you can check the lines sit on your tile edges.
        if (!Application.isPlaying)
        {
            if (mapManager == null) mapManager = FindAnyObjectByType<MapManager>();
            bool synced = false;
            try { synced = mapManager != null && SyncFromMap(false); }
            catch (Exception) { /* map data not built yet in edit mode */ }
            if (!synced) FitToTiles(false);
        }

        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        for (int x = 0; x <= width; x++) Gizmos.DrawLine(Corner(x, 0), Corner(x, height));
        for (int y = 0; y <= height; y++) Gizmos.DrawLine(Corner(0, y), Corner(width, y));

        // Tile (0,0) in green, so you can see where the grid starts.
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(GridToWorld(Vector2Int.zero), Vector3.one * cellSize * 0.5f);

        // Cells inside the rectangle but not covered by any tile, in red.
        if (tileCells != null)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (!tileCells.Contains(new Vector2Int(x, y)))
                        Gizmos.DrawCube(GridToWorld(new Vector2Int(x, y)), Vector3.one * cellSize * 0.3f);
        }
    }

    private void OnDrawGizmos()
    {

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        foreach (var t in occupants.Keys)
            Gizmos.DrawWireCube(GridToWorld(t), Vector3.one * cellSize * 0.9f);
    }
}
