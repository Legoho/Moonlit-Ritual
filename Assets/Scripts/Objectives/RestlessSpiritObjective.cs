using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Objective 4 (the surprise) - The Restless Spirit.
/// The ghost of a lost student wanders the board, drawn towards the summoning circles. Each round it
/// drifts closer. Heroines Study it with Lore (it counts as a Study target): each Success adds 1 Insight,
/// a Critical adds 1 more. At the required Insight they learn its name and lay it to rest.
/// If it reaches a summoning circle first, it is consumed and the objective fails.
/// Leans on Lore, so with the other three objectives every discipline has a reason to leave the Anchor.
/// </summary>
public class RestlessSpiritObjective : ObjectiveBase
{
    [Header("Spirit")]
    [SerializeField] private RestlessSpirit spiritPrefab;
    [Tooltip("Insight needed to lay it to rest.")]
    [SerializeField] private int insightNeeded = 6;
    [Tooltip("Tiles it drifts towards the nearest summoning circle each round.")]
    [SerializeField] private int driftPerRound = 1;
    [SerializeField] private float driftStepDuration = 0.35f;

    [Header("Placement (walking distance from the Anchor)")]
    [SerializeField] private int minDistance = 4;
    [SerializeField] private int maxDistance = 8;
    [Tooltip("Never closer than this to a spawner at the start.")]
    [SerializeField] private int minDistanceFromSpawners = 5;

    [Tooltip("Optional names; one is picked and revealed when it is laid to rest.")]
    [SerializeField] private List<string> studentNames = new() { "Annelise", "Margit", "Odette", "Sylvie", "Theodora" };

    private RestlessSpirit spirit;
    private int insight;
    private string revealedName;

    private void Reset()
    {
        title = "The Restless Spirit";
        description = "A lost student's spirit drifts towards the summoning circles. Study it with Lore to lay it to rest before it is consumed.";
        moonlightReward = 2;
        bonus = new ObjectiveBonus { healResolve = 1, shieldsForParty = 1 };
    }

    public override bool CanRun(ObjectiveManager manager) => spiritPrefab != null && manager.Spawners().Count > 0;

    public override string StatusLine
    {
        get
        {
            if (State == ObjectiveState.Completed) return $"{revealedName} is at rest";
            if (State == ObjectiveState.Failed) return "The spirit was consumed";
            int d = DistanceToCircle();
            return $"Insight {insight}/{insightNeeded}" + (d >= 0 ? $", {d} tiles from a summoning circle" : "");
        }
    }

    public override IEnumerable<ObjectiveUnit> Units =>
        spirit != null && IsActive ? new ObjectiveUnit[] { spirit } : Enumerable.Empty<ObjectiveUnit>();

    public RestlessSpirit Spirit => spirit;

    protected override void Setup()
    {
        insight = 0;
        revealedName = studentNames.Count > 0 ? studentNames[Random.Range(0, studentNames.Count)] : "the student";

        var tiles = Manager.FindSpreadTiles(1, minDistance, maxDistance, 0, minDistanceFromSpawners);
        if (tiles.Count == 0) tiles = Manager.FindSpreadTiles(1, minDistance, maxDistance, 0, 3);
        if (tiles.Count == 0) { Fail("There was nowhere for the spirit to appear."); return; }

        spirit = Instantiate(spiritPrefab, Grid.GridToWorld(tiles[0]), Quaternion.identity);
        if (!spirit.PlaceAt(tiles[0])) { Destroy(spirit.gameObject); spirit = null; Fail("It could not appear."); return; }
        spirit.Owner = this;
        Manager.Reserve(tiles[0]);
    }

    protected override void Cleanup()
    {
        if (spirit != null) { spirit.RemoveFromGrid(); Destroy(spirit.gameObject); }
        spirit = null;
    }

    /// <summary>Called by RitualManager when a heroine Studies the spirit.</summary>
    public void AddInsight(Heroine h, int amount)
    {
        if (!IsActive || spirit == null) return;
        insight = Mathf.Min(insightNeeded, insight + Mathf.Max(0, amount));
        if (insight < insightNeeded)
        {
            Announce($"{h.DisplayName} studies the spirit: Insight {insight}/{insightNeeded}.");
            Manager.Refresh();
            return;
        }

        Announce($"{h.DisplayName} remembers her name: {revealedName}. The spirit smiles and fades into the moonlight.");
        spirit.RemoveFromGrid();
        Destroy(spirit.gameObject, 0.5f);
        spirit = null;
        Complete();
    }

    public override void OnRoundStart(int round)
    {
        if (spirit == null || !IsActive) return;
        var path = PathToNearestCircle();
        if (path == null) return;

        var steps = path.Take(driftPerRound).ToList();
        if (steps.Count > 0) spirit.StartCoroutine(Drift(steps));
        else CheckConsumed();
    }

    private System.Collections.IEnumerator Drift(List<Vector2Int> steps)
    {
        foreach (var t in steps)
        {
            if (spirit == null || !Grid.IsFree(t)) break;
            yield return spirit.StepTo(t, driftStepDuration);
        }
        CheckConsumed();
        Manager.Refresh();
    }

    private void CheckConsumed()
    {
        if (spirit == null || !IsActive) return;
        if (DistanceToCircle() > 1) return;
        Announce("The summoning circle swallows the restless spirit...");
        spirit.RemoveFromGrid();
        Destroy(spirit.gameObject, 0.5f);
        spirit = null;
        Fail("The spirit was consumed by a summoning circle.");
    }

    private IEnumerable<MonsterSpawner> OpenCircles() => Manager.Spawners().Where(s => !s.isSealed);

    private int DistanceToCircle()
    {
        if (spirit == null) return -1;
        var tiles = OpenCircles().Select(s => s.Tile).ToList();
        return tiles.Count == 0 ? -1 : tiles.Min(t => Chebyshev(spirit.GridPosition, t));
    }

    /// <summary>Shortest path over free tiles to a tile next to an open summoning circle.</summary>
    private List<Vector2Int> PathToNearestCircle()
    {
        var targets = OpenCircles().Select(s => s.Tile).ToList();
        if (targets.Count == 0) return null;

        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var dist = Grid.Flood(spirit.GridPosition, -1, Grid.IsFree, cameFrom);
        Vector2Int best = spirit.GridPosition;
        int bestDist = int.MaxValue;
        foreach (var kv in dist)
        {
            if (kv.Value >= bestDist) continue;
            if (targets.Any(t => Chebyshev(kv.Key, t) <= 1)) { best = kv.Key; bestDist = kv.Value; }
        }
        if (bestDist == int.MaxValue || best == spirit.GridPosition) return new List<Vector2Int>();
        return GridManager.BuildPath(cameFrom, spirit.GridPosition, best);
    }
}
