using UnityEngine;

/// <summary>
/// The central ritual tile (GDD 5.3). Occupies its tile, so enemies must stand next to it
/// (or within their attack range) to hit it. Heroines Channel and Stabilize from adjacent tiles.
/// </summary>
public class RitualAnchor : GridUnit
{
    [Header("Runtime")]
    [Tooltip("From Guard. Each Shield blocks 1 Turn Capacity loss until the next player phase.")]
    public int shields;

    protected override string GetDisplayName() => "Ritual Anchor";

    public bool IsAdjacent(Vector2Int tile) => GridManager.Distance(tile, GridPosition) == 1;
}
