using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Runs the Optional Objectives (GDD 9.4) of an encounter.
/// Put objective components (LostRelicObjective, RescueCiviliansObjective, ...) on child objects of this
/// object. Each encounter it picks up to Max Per Encounter of them, places them on the map,
/// forwards combat events to them, and pays out bonuses and Moonlight.
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Tooltip("GDD 9.4: never more than two per encounter.")]
    [SerializeField] private int maxPerEncounter = 2;
    [Tooltip("On: picks randomly among the candidates each encounter. Off: the first ones in the list.")]
    [SerializeField] private bool pickRandomly = true;
    [Tooltip("Possible objectives. Leave empty to use every objective on this object's children.")]
    [SerializeField] private List<ObjectiveBase> candidates = new();

    private readonly List<ObjectiveBase> active = new();
    private readonly HashSet<Vector2Int> reserved = new();
    private Dictionary<Vector2Int, int> anchorDistance;

    public IReadOnlyList<ObjectiveBase> Active => active;
    public RitualManager Ritual { get; private set; }
    public TurnManager Turns { get; private set; }
    public RitualAnchor Anchor { get; private set; }
    public GridManager Grid => GridManager.Instance;

    /// <summary>Raised when objectives start, complete or fail (for the tracker UI).</summary>
    public event Action Changed;

    /// <summary>Objective units enemies may attack. Empty when there is no ObjectiveManager.</summary>
    public static IEnumerable<GridUnit> EnemyTargets =>
        Instance != null ? Instance.active.Where(o => o.IsActive).SelectMany(o => o.EnemyTargets) : Enumerable.Empty<GridUnit>();

    /// <summary>All objective units on the board (for clicks and action targeting).</summary>
    public static IEnumerable<ObjectiveUnit> Units =>
        Instance != null ? Instance.active.SelectMany(o => o.Units).Where(u => u != null && u.IsOnGrid) : Enumerable.Empty<ObjectiveUnit>();

    private void Awake()
    {
        Instance = this;
        if (candidates.Count == 0) candidates.AddRange(GetComponentsInChildren<ObjectiveBase>(true));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // =====================================================================
    // Lifecycle (called by RitualManager)
    // =====================================================================

    public void BeginEncounter(RitualManager ritual)
    {
        Ritual = ritual;
        Turns = FindAnyObjectByType<TurnManager>();
        Anchor = FindAnyObjectByType<RitualAnchor>();

        foreach (var o in active) o.End();
        active.Clear();
        reserved.Clear();
        anchorDistance = Anchor != null ? Grid.Flood(Anchor.GridPosition, -1, Grid.InBounds) : new();

        var pool = candidates.Where(c => c != null && c.enabled && c.CanRun(this)).ToList();
        if (pickRandomly) pool = pool.OrderBy(_ => UnityEngine.Random.value).ToList();

        foreach (var o in pool.Take(maxPerEncounter))
        {
            active.Add(o);
            o.Begin(this, ritual);
            string reward = o.bonus.Describe();
            Ritual.Announce($"Optional objective: {o.title}. {o.description}" +
                            $" Reward: {o.moonlightReward} Moonlight{(reward.Length > 0 ? ", " + reward : "")}.");
        }
        Changed?.Invoke();
    }

    public void RoundStart(int round) { foreach (var o in Running()) o.OnRoundStart(round); Changed?.Invoke(); }
    public void HeroineMoved(Heroine h, IReadOnlyList<Vector2Int> path) { foreach (var o in Running()) o.OnHeroineMoved(h, path); Changed?.Invoke(); }
    public void EnemyPhaseEnd() { foreach (var o in Running()) o.OnEnemyPhaseEnd(); Changed?.Invoke(); }
    public void EncounterEnd(bool won) { foreach (var o in Running()) o.OnEncounterEnd(won); Changed?.Invoke(); }
    public void Refresh() => Changed?.Invoke();

    private List<ObjectiveBase> Running() => active.Where(o => o.IsActive).ToList();

    /// <summary>Finds a summoning circle the heroine could seal with Occultism from this tile.</summary>
    public static SummoningCircle FindSealableCircle(Vector2Int heroineTile)
    {
        if (Instance == null) return null;
        foreach (var o in Instance.Running().OfType<StopSummoningObjective>())
        {
            var c = o.CircleNear(heroineTile);
            if (c != null) return c;
        }
        return null;
    }

    // =====================================================================
    // Results
    // =====================================================================

    internal void ReportCompleted(ObjectiveBase o)
    {
        ApplyBonus(o.bonus);
        if (RunManager.Instance != null) RunManager.Instance.AddMoonlight(o.moonlightReward);
        string reward = o.bonus.Describe();
        Ritual.Announce($"OBJECTIVE COMPLETE: {o.title}! +{o.moonlightReward} Moonlight{(reward.Length > 0 ? ", " + reward : "")}.");
        Ritual.RefreshDisplay();
        Changed?.Invoke();
    }

    internal void ReportFailed(ObjectiveBase o, string reason)
    {
        Ritual.Announce($"Objective failed: {o.title}. {reason}");
        Changed?.Invoke();
    }

    private void ApplyBonus(ObjectiveBonus b)
    {
        var r = Ritual.Ritual;
        if (b.restoreTurnCapacity > 0)
            r.turnCapacity = Mathf.Min(r.StartingTurnCapacity, r.turnCapacity + b.restoreTurnCapacity);
        if (b.stability > 0) r.Stabilize(b.stability);
        if (Turns != null)
        {
            foreach (var h in Turns.InCombat)
            {
                h.resolve = Mathf.Min(h.maxResolve, h.resolve + b.healResolve);
                h.shields += b.shieldsForParty;
            }
        }
        if (b.insightAllEnemyTypes > 0) 
            Ritual.GrantInsightToAllTypes(b.insightAllEnemyTypes);
        for (int i = 0; i < b.relics && RunManager.Instance != null; i++)
        {
            var relic = RunManager.Instance.GrantRandomRelic();
            if (relic != null) Ritual.Announce($"Relic found: {relic.displayName}. {relic.TooltipText()}");
        }
    }

    // =====================================================================
    // Placement helpers for objectives
    // =====================================================================

    /// <summary>Walking distance from the Anchor (ignoring units). -1 if unreachable.</summary>
    public int DistanceFromAnchor(Vector2Int t)
    {
        if (anchorDistance.TryGetValue(t, out int d)) return d;
        // Spawn cells may not be walkable themselves: use their best walkable neighbour.
        int best = int.MaxValue;
        foreach (var dir in GridManager.Directions)
            if (anchorDistance.TryGetValue(t + dir, out int n)) best = Mathf.Min(best, n + 1);
        return best == int.MaxValue ? -1 : best;
    }

    /// <summary>Free, reachable, not yet claimed by another objective.</summary>
    public bool IsPlaceable(Vector2Int t) => anchorDistance.ContainsKey(t) && Grid.IsFree(t) && !reserved.Contains(t);

    public IEnumerable<Vector2Int> PlaceableTiles() => anchorDistance.Keys.Where(IsPlaceable);

    public void Reserve(Vector2Int t) => reserved.Add(t);

    public List<MonsterSpawner> Spawners() =>
        FindObjectsByType<MonsterSpawner>(FindObjectsSortMode.None).Where(s => s != null && s.isActiveAndEnabled).ToList();

    /// <summary>
    /// A tile "behind enemy lines": near the spawner farthest from the Anchor, on the far side of it.
    /// Falls back to the farthest reachable tile.
    /// </summary>
    public bool TryFindBehindEnemyLines(int searchRadius, out Vector2Int tile)
    {
        var tiles = PlaceableTiles().ToList();
        tile = default;
        if (tiles.Count == 0) return false;

        var spawner = Spawners().OrderByDescending(s => DistanceFromAnchor(s.Tile)).FirstOrDefault();
        IEnumerable<Vector2Int> pick = tiles;
        if (spawner != null)
        {
            int spawnerDist = DistanceFromAnchor(spawner.Tile);
            var behind = tiles.Where(t => GridManager.Distance(t, spawner.Tile) <= searchRadius
                                          && DistanceFromAnchor(t) >= spawnerDist).ToList();
            if (behind.Count > 0) pick = behind;
        }
        // Farthest from the Anchor, with a little randomness among the top few.
        tile = pick.OrderByDescending(DistanceFromAnchor).Take(3).OrderBy(_ => UnityEngine.Random.value).First();
        return true;
    }

    /// <summary>Random tiles at a walking distance between min and max from the Anchor, at least minSpacing apart.</summary>
    public List<Vector2Int> FindSpreadTiles(int count, int minDist, int maxDist, int minSpacing, int minDistFromSpawners = 0)
    {
        var spawnerTiles = Spawners().Select(s => s.Tile).ToList();
        var pool = PlaceableTiles()
            .Where(t => { int d = DistanceFromAnchor(t); return d >= minDist && d <= maxDist; })
            .Where(t => spawnerTiles.All(s => GridManager.Distance(t, s) >= minDistFromSpawners))
            .OrderBy(_ => UnityEngine.Random.value)
            .ToList();

        var result = new List<Vector2Int>();
        foreach (var t in pool)
        {
            if (result.Count >= count) break;
            if (result.All(r => GridManager.Distance(r, t) >= minSpacing)) result.Add(t);
        }
        return result;
    }
}
