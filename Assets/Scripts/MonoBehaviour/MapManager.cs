using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    public Map map = new Map();

    [Header("Cell palette")]
    [Tooltip("Every Map_So the generator may use. Several per category = random variants, picked by generationWeight.")]
    [SerializeField] private Map_So[] cellPalette;

    [Header("Kit settings")]
    [Tooltip("Footprint of ONE floor tile in the prefab's own units (before scaling).")]
    [SerializeField] private float tileSize = 1f;
    [Tooltip("The Dark Academia models need 2x.")]
    [SerializeField] private float modelScale = 2f;
    [Tooltip("Extra Y rotation if the wall mesh doesn't face +Z by default.")]
    [SerializeField] private float wallRotationOffset = 0f;
    [Tooltip("Moves each wall in its OWN local space, as a fraction of one cell. " +
             "X = along the wall (use ±0.5 if the pivot is at one end), " +
             "Z = toward/away from the room (small values fix walls sitting too far in or out).")]
    [SerializeField] private Vector3 wallPivotOffset = Vector3.zero;

    [Header("Placement")]
    [Tooltip("World position of the map's middle cell.")]
    [SerializeField] private Vector3 mapCenter = Vector3.zero;

    [Header("Generation")]
    [SerializeField] private bool generateOnStart = true;
    [Range(0.1f, 0.9f)]
    [SerializeField] private float fillPercent = 0.5f;
    [Min(0)]
    [SerializeField] private int spawnCount = 2;
    [Range(0f, 0.5f)]
    [SerializeField] private float hazardChance = 0.05f;
    [Tooltip("0 = random every time. Any other value gives the same map each time.")]
    [SerializeField] private int seed = 0;

    private GameObject[,] mapTiles;   // one container object per cell
    private Transform mapRoot;
    private System.Random rng;

    public float CellSize => tileSize * modelScale;
    public Vector3 MapCenter => mapCenter;
    public int Rows => map.rows;
    public int Columns => map.columns;

    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int( 1, 0), // +row (+Z)
        new Vector2Int(-1, 0), // -row (-Z)
        new Vector2Int( 0, 1), // +col (+X)
        new Vector2Int( 0,-1), // -col (-X)
    };

    private void Start()
    {
        if (generateOnStart)
            Generate();
        else
            RebuildVisuals();   // build whatever map is already stored (e.g. hand-painted)
    }

    [ContextMenu("Generate Random Map")]
    public void Generate()
    {
        rng = seed == 0 ? new System.Random() : new System.Random(seed);
        ClearMapVisuals();
        CreateMap();
        GenerateMapVisuals();
    }

    [ContextMenu("Rebuild Visuals From Map Data")]
    public void RebuildVisuals()
    {
        rng ??= new System.Random(seed == 0 ? System.Environment.TickCount : seed);
        ClearMapVisuals();
        GenerateMapVisuals();
    }

    // =====================================================================
    // DATA: decide the category of every cell, then pick a Map_So for it
    // =====================================================================

    private void CreateMap()
    {
        int rows = map.rows;
        int cols = map.columns;
        map.Clear(rows, cols);

        // Floor = 0 in your enum, so a new array is all Floor. Fill with Empty first.
        MapCellType[,] layout = new MapCellType[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                layout[r, c] = MapCellType.Empty;

        CarveFloor(layout);
        PlaceRitualCircle(layout);
        PlaceWalls(layout);
        PlaceOnRandomFloor(layout, MapCellType.Spawn, spawnCount);
        PlaceHazards(layout);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                map[r, c] = PickWeighted(layout[r, c]);
    }

    // Drunkard's walk: one connected area, so every floor cell is reachable.
    // Stays 1 cell away from the border so there's always room for walls.
    private void CarveFloor(MapCellType[,] layout)
    {
        int rows = map.rows, cols = map.columns;
        int row = rows / 2, col = cols / 2;
        int interior = (rows - 2) * (cols - 2);
        int target = Mathf.Max(1, Mathf.RoundToInt(interior * fillPercent));
        int carved = 0;
        int safety = target * 50;

        while (carved < target && safety-- > 0)
        {
            if (layout[row, col] != MapCellType.Floor)
            {
                layout[row, col] = MapCellType.Floor;
                carved++;
            }

            Vector2Int dir = Directions[rng.Next(Directions.Length)];
            row = Mathf.Clamp(row + dir.x, 1, rows - 2);
            col = Mathf.Clamp(col + dir.y, 1, cols - 2);
        }
    }

    // The 3x3 block around the centre cell is reserved for the ritual circle.
    // All 9 cells are marked so nothing else (spawns, hazards) lands there,
    // but only the middle one spawns the big prefab (see GenerateMapVisuals).
    private void PlaceRitualCircle(MapCellType[,] layout)
    {
        int centerRow = map.rows / 2;
        int centerCol = map.columns / 2;

        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
                layout[centerRow + dr, centerCol + dc] = MapCellType.Ritual;
    }

    private bool IsRitualCenter(int row, int column)
    {
        return row == map.rows / 2 && column == map.columns / 2;
    }

    // Any Empty cell touching a floor or ritual cell becomes a Wall cell.
    private void PlaceWalls(MapCellType[,] layout)
    {
        for (int r = 0; r < map.rows; r++)
        {
            for (int c = 0; c < map.columns; c++)
            {
                if (layout[r, c] != MapCellType.Empty)
                    continue;

                foreach (Vector2Int dir in Directions)
                {
                    int nr = r + dir.x, nc = c + dir.y;
                    if (map.InBounds(nr, nc) &&
                        (layout[nr, nc] == MapCellType.Floor || layout[nr, nc] == MapCellType.Ritual))
                    {
                        layout[r, c] = MapCellType.Wall;
                        break;
                    }
                }
            }
        }
    }

    private void PlaceOnRandomFloor(MapCellType[,] layout, MapCellType type, int count)
    {
        List<Vector2Int> floors = CollectCells(layout, MapCellType.Floor);
        for (int i = 0; i < count && floors.Count > 0; i++)
        {
            int pick = rng.Next(floors.Count);
            Vector2Int cell = floors[pick];
            layout[cell.x, cell.y] = type;
            floors.RemoveAt(pick);
        }
    }

    private void PlaceHazards(MapCellType[,] layout)
    {
        foreach (Vector2Int cell in CollectCells(layout, MapCellType.Floor))
        {
            if (rng.NextDouble() < hazardChance)
                layout[cell.x, cell.y] = MapCellType.Hazard;
        }
    }

    private List<Vector2Int> CollectCells(MapCellType[,] layout, MapCellType type)
    {
        var result = new List<Vector2Int>();
        for (int r = 0; r < map.rows; r++)
            for (int c = 0; c < map.columns; c++)
                if (layout[r, c] == type)
                    result.Add(new Vector2Int(r, c));
        return result;
    }

    // Weighted random pick among palette entries of one category.
    private Map_So PickWeighted(MapCellType category)
    {
        if (cellPalette == null) return null;

        float total = 0f;
        foreach (Map_So so in cellPalette)
            if (so != null && so.category == category && so.generationWeight > 0f)
                total += so.generationWeight;

        if (total <= 0f)
        {
            if (category != MapCellType.Empty)
                Debug.LogWarning($"MapManager: no Map_So with category {category} in the palette.");
            return null;
        }

        float roll = (float)rng.NextDouble() * total;
        foreach (Map_So so in cellPalette)
        {
            if (so == null || so.category != category || so.generationWeight <= 0f)
                continue;
            roll -= so.generationWeight;
            if (roll <= 0f)
                return so;
        }
        return null;
    }

    // =====================================================================
    // VISUALS: read the Map and spawn prefabs (works for random OR hand-made maps)
    // =====================================================================

    public void GenerateMapVisuals()
    {
        EnsureRoot();
        mapTiles = new GameObject[map.rows, map.columns];

        for (int i = 0; i < map.rows; i++)
        {
            for (int j = 0; j < map.columns; j++)
            {
                Map_So cell = map[i, j];
                if (cell == null)
                    continue;

                GameObject container = new GameObject($"Cell_{i}_{j}_{cell.category}");
                container.transform.SetParent(mapRoot, false);
                container.transform.position = CellToWorld(i, j);
                mapTiles[i, j] = container;

                switch (cell.category)
                {
                    case MapCellType.Wall:
                        SpawnWallFaces(i, j, cell, container.transform);
                        break;

                    case MapCellType.Ritual:
                        // One 3x3 prefab, placed only by the middle cell.
                        // The other 8 ritual cells keep their data but spawn nothing.
                        if (IsRitualCenter(i, j))
                            Spawn(cell.prefab, CellToWorld(i, j), Quaternion.identity, container.transform);
                        break;

                    case MapCellType.Spawn:
                    case MapCellType.Hazard:
                        // These are complete tiles of their own: no floor underneath.
                        Spawn(cell.prefab, CellToWorld(i, j), RandomQuarterTurn(), container.transform);
                        break;

                    default: // Floor, and Empty if you gave it a prefab
                        Spawn(cell.prefab, CellToWorld(i, j), Quaternion.identity, container.transform);
                        break;
                }
            }
        }
    }

    // A wall cell draws a wall face on each side that touches a walkable cell,
    // sitting on the shared edge and facing into the room.
    private void SpawnWallFaces(int row, int col, Map_So wall, Transform parent)
    {
        Vector3 center = CellToWorld(row, col);

        foreach (Vector2Int dir in Directions)
        {
            int nr = row + dir.x, nc = col + dir.y;
            if (!map.InBounds(nr, nc))
                continue;

            Map_So neighbour = map[nr, nc];
            // Only face real room cells: never another wall, never anything non-walkable.
            if (neighbour == null || neighbour.category == MapCellType.Wall || !neighbour.isWalkable)
                continue;

            Vector3 towardRoom = new Vector3(dir.y, 0f, dir.x);
            Vector3 pos = center + towardRoom * (CellSize * 0.5f);
            Quaternion rot = Quaternion.LookRotation(towardRoom) *
                             Quaternion.Euler(0f, wallRotationOffset, 0f);
            pos += rot * (wallPivotOffset * CellSize); // compensate for off-centre pivots
            Spawn(wall.prefab, pos, rot, parent);
        }
    }

    public void ClearMapVisuals()
    {
        if (mapRoot == null)
        {
            Transform existing = transform.Find("MapRoot");
            if (existing == null) return;
            mapRoot = existing;
        }

        for (int i = mapRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = mapRoot.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child); // so the context menu works in edit mode
        }

        mapTiles = null;
    }

    // =====================================================================
    // QUERIES for other systems (e.g. RitualManager spawning monsters)
    // =====================================================================

    // The centre cell (where CarveFloor starts) sits exactly at mapCenter in WORLD space,
    // no matter where the MapManager GameObject (or its parents) are.
    public Vector3 CellToWorld(int row, int column)
    {
        int centerRow = map.rows / 2;
        int centerColumn = map.columns / 2;
        return mapCenter + new Vector3(
            (column - centerColumn) * CellSize,
            0f,
            (row - centerRow) * CellSize);
    }

    public List<Vector2Int> GetCellsOfCategory(MapCellType category)
    {
        var result = new List<Vector2Int>();
        for (int r = 0; r < map.rows; r++)
            for (int c = 0; c < map.columns; c++)
                if (map[r, c] != null && map[r, c].category == category)
                    result.Add(new Vector2Int(r, c));
        return result;
    }

    public bool TryGetRandomCellPosition(MapCellType category, out Vector3 worldPosition)
    {
        List<Vector2Int> cells = GetCellsOfCategory(category);
        if (cells.Count == 0)
        {
            worldPosition = Vector3.zero;
            return false;
        }
        Vector2Int cell = cells[Random.Range(0, cells.Count)];
        worldPosition = CellToWorld(cell.x, cell.y);
        return true;
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private void EnsureRoot()
    {
        if (mapRoot != null) return;
        Transform existing = transform.Find("MapRoot");
        mapRoot = existing != null ? existing : new GameObject("MapRoot").transform;
        mapRoot.SetParent(transform, false);
    }

    private GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        if (prefab == null) return null;

        Quaternion randomRotation;
        float random = Random.Range(0f,1f);
        if (random < 0.35f)
            randomRotation = Quaternion.Euler(rotation.eulerAngles.x, rotation.eulerAngles.y + 180f, rotation.eulerAngles.z);
        else
            randomRotation = rotation;
        // Instantiate without a parent first, then re-parent keeping the world position,
        // so a moved/scaled MapManager can't shift or stretch the map.
        GameObject instance = Instantiate(prefab, position, rotation);
        instance.transform.localScale = prefab.transform.localScale * modelScale;
        instance.transform.localRotation = randomRotation;
        instance.transform.SetParent(parent, true);
        return instance;
    }

    private Quaternion RandomQuarterTurn()
    {
        return Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
    }
}
