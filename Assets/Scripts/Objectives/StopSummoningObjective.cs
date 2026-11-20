using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>One spawner turned into a summoning circle that can be sealed with Occultism.</summary>
public class SummoningCircle
{
    public MonsterSpawner spawner;
    public int seal;
    public int sealNeeded;
    public GameObject visual;
    public StopSummoningObjective owner;

    public bool IsSealed => seal >= sealNeeded;
    public Vector2Int Tile => spawner.Tile;

    /// <summary>Adds Occultism Successes to the seal. Returns true if this sealed it.</summary>
    public bool AddSeal(Heroine h, int amount) => owner.AddSeal(this, h, amount);
}

/// <summary>
/// Objective 3 - Stop the Summoning Rituals.
/// Every spawner is a summoning circle. A heroine standing next to one (diagonals count) uses
/// Occultism on it instead of the Anchor: each Success adds 1 to its seal (Criticals add the Channel
/// crit bonus). A sealed circle stops spawning for the rest of the encounter.
/// Sealing energy does not go through the ritual's Stability limit.
/// </summary>
public class StopSummoningObjective : ObjectiveBase
{
    [Header("Seals")]
    [Tooltip("Occultism Successes needed to seal one circle.")]
    [SerializeField] private int sealPerCircle = 4;
    [Tooltip("How many circles must be sealed for the objective. 0 = all of them.")]
    [SerializeField] private int circlesRequired = 0;

    [Header("Visuals (optional)")]
    [Tooltip("Shown on every active summoning circle (e.g. a glowing red rune).")]
    [SerializeField] private GameObject circlePrefab;
    [Tooltip("Shown where a circle has been sealed.")]
    [SerializeField] private GameObject sealedPrefab;

    private readonly List<SummoningCircle> circles = new();
    private readonly List<GameObject> spawnedVisuals = new();

    private int Sealed => circles.Count(c => c.IsSealed);
    private int Needed => circlesRequired <= 0 ? circles.Count : Mathf.Min(circlesRequired, circles.Count);

    private void Reset()
    {
        title = "Stop the Summoning";
        description = "The shadows pour out of summoning circles. Seal them with Occultism while standing next to them.";
        moonlightReward = 2;
        bonus = new ObjectiveBonus { stability = 2 };
    }

    public override bool CanRun(ObjectiveManager manager) => manager.Spawners().Count > 0;

    public override string StatusLine
    {
        get
        {
            if (State == ObjectiveState.Completed) return "The summoning has stopped";
            var open = circles.Where(c => !c.IsSealed).Select(c => $"{c.seal}/{c.sealNeeded}");
            return $"{Sealed}/{Needed} circles sealed (open: {string.Join(", ", open)})";
        }
    }

    protected override void Setup()
    {
        circles.Clear();
        foreach (var s in Manager.Spawners())
        {
            var c = new SummoningCircle { spawner = s, sealNeeded = sealPerCircle, owner = this };
            if (circlePrefab != null)
            {
                c.visual = Instantiate(circlePrefab, Grid.GridToWorld(s.Tile), Quaternion.identity);
                spawnedVisuals.Add(c.visual);
            }
            circles.Add(c);
        }
    }

    protected override void Cleanup()
    {
        foreach (var c in circles) c.spawner.isSealed = false;
        foreach (var v in spawnedVisuals) if (v != null) Destroy(v);
        spawnedVisuals.Clear();
        circles.Clear();
    }

    /// <summary>An unsealed circle next to this tile (diagonals count), or null.</summary>
    public SummoningCircle CircleNear(Vector2Int tile) =>
        circles.Where(c => !c.IsSealed && Chebyshev(tile, c.Tile) <= 1).FirstOrDefault();

    internal bool AddSeal(SummoningCircle c, Heroine h, int amount)
    {
        if (c.IsSealed || !IsActive) return false;
        c.seal = Mathf.Min(c.sealNeeded, c.seal + Mathf.Max(0, amount));
        if (!c.IsSealed)
        {
            Announce($"{h.DisplayName} weakens the summoning circle: {c.seal}/{c.sealNeeded}.");
            Manager.Refresh();
            return false;
        }

        c.spawner.isSealed = true;
        if (c.visual != null) Destroy(c.visual);
        if (sealedPrefab != null) spawnedVisuals.Add(Instantiate(sealedPrefab, Grid.GridToWorld(c.Tile), Quaternion.identity));
        Announce($"{h.DisplayName} seals a summoning circle! No more shadows will come from it.");

        if (Sealed >= Needed) Complete();
        else Manager.Refresh();
        return true;
    }
}
