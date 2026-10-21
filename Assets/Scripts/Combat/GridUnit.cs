using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.VirtualTexturing;

/// <summary>
/// Anything that occupies one tile: heroines, monsters and the Ritual Anchor.
/// Needs a Collider (or Collider2D) for clicks.
/// </summary>
public abstract class GridUnit : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("On: the start tile is read from where the object sits in the scene. Off: uses Grid Position.")]
    [SerializeField] protected bool snapFromTransform = true;
    [SerializeField] protected Vector2Int gridPosition;
    [Tooltip("Added to the tile centre, e.g. to lift a sprite above the floor.")]
    [SerializeField] protected Vector3 visualOffset = Vector3.zero;

    public Vector2Int GridPosition => gridPosition;
    public bool IsOnGrid { get; private set; }
    public string DisplayName => GetDisplayName();

    protected GridManager Grid => GridManager.Instance;
    protected RitualManager ritualManager;

    protected virtual string GetDisplayName() => name;

    protected virtual void Awake()
    {
        ritualManager = FindAnyObjectByType<RitualManager>();
    }

    protected virtual void Start()
    {
        // Spawned units are placed by their spawner before Start runs.
        // On a generated map, RitualManager places the Anchor and heroines once the map exists.
        if (!IsOnGrid && Grid != null && Grid.IsReady) PlaceOnGridAtStart();
    }

    private void PlaceOnGridAtStart()
    {
        if (Grid == null)
        {
            Debug.LogError($"{name}: no GridManager in the scene.");
            return;
        }

        var tile = snapFromTransform ? Grid.WorldToGrid(transform.position - visualOffset) : gridPosition;
        if (!Grid.IsFree(tile))
        {
            if (Grid.TryFindNearestFree(tile, 5, out var free))
            {
                Debug.LogWarning($"{name}: tile {tile} is taken or off the board, using {free}.");
                tile = free;
            }
            else
            {
                Debug.LogError($"{name}: no free tile near {tile}.");
                return;
            }
        }
        PlaceAt(tile);
    }

    /// <summary>Puts the unit on tile t. Fails if the tile is taken or off the board.</summary>
    public bool PlaceAt(Vector2Int t, bool snapTransform = true)
    {
        if (Grid == null || !Grid.Register(this, t)) return false;
        gridPosition = t;
        IsOnGrid = true;
        if (snapTransform) transform.position = Grid.GridToWorld(t) + visualOffset;
        return true;
    }

    /// <summary>Moves one tile with a short slide. Occupancy updates immediately.</summary>
    public IEnumerator StepTo(Vector2Int t, float duration)
    {
        Vector3 from = transform.position;
        if (!PlaceAt(t, false)) yield break;

        Vector3 to = Grid.GridToWorld(t) + visualOffset;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        transform.position = to;
    }

    public void RemoveFromGrid()
    {
        if (Grid != null && IsOnGrid) Grid.Unregister(this);
        IsOnGrid = false;
    }

    protected virtual void OnDestroy()
    {
        RemoveFromGrid();
    }

    protected virtual void OnMouseDown()
    {
        if (ritualManager != null) ritualManager.OnUnitClicked(this);
    }
}
