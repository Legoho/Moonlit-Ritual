using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Objective - Defend the Position (GDD: Warding).
/// A ward-stone (or watchtower, barricade...) stands away from the Anchor. Enemies may target it,
/// telegraphed like any other intent. Heroines protect it by Guarding it, blocking paths, or
/// dealing with its attackers. While it stands, it shields the Anchor every round.
/// Completed if it is still standing when the ritual completes.
/// </summary>
public class DefendPositionObjective : ObjectiveBase
{
    [Header("Structure")]
    [Tooltip("Prefab with the Civilian component (give it a name like 'Ward-Stone'). It is the thing to defend.")]
    [SerializeField] private Civilian structurePrefab;
    [Tooltip("Resolve of the structure (overrides the prefab's Max Resolve).")]
    [SerializeField] private int structureResolve = 3;
    [Tooltip("While it stands, the Anchor gains this many Shields at the start of every round. 0 = none.")]
    [SerializeField] private int anchorShieldsEachRound = 1;
    [Tooltip("On: enemies may choose it as a target.")]
    [SerializeField] private bool enemiesTargetIt = true;

    [Header("Placement (walking distance from the Anchor)")]
    [SerializeField] private int minDistance = 4;
    [SerializeField] private int maxDistance = 7;

    private Civilian structure;

    private bool Standing => structure != null && !structure.IsLost;

    private void Reset()
    {
        title = "Defend the Position";
        description = "A ward-stone protects the circle. Keep it standing until the ritual is complete; while it stands, the Anchor gains 1 Shield each round.";
        moonlightReward = 1;
        bonus = new ObjectiveBonus();
    }

    public override bool CanRun(ObjectiveManager manager) => structurePrefab != null;

    public override string StatusLine =>
        State == ObjectiveState.Completed ? $"{StructureName} held"
        : State == ObjectiveState.Failed ? $"{StructureName} fell"
        : Standing ? $"Keep the {StructureName} standing (Resolve {structure.resolve}/{structure.maxResolve})"
        : $"Keep the {StructureName} standing";

    private string StructureName => structure != null ? structure.DisplayName : "ward-stone";

    public override IEnumerable<ObjectiveUnit> Units => Standing ? new[] { (ObjectiveUnit)structure } : Enumerable.Empty<ObjectiveUnit>();
    public override IEnumerable<GridUnit> EnemyTargets =>
        enemiesTargetIt && Standing && structure.IsOnGrid ? new[] { (GridUnit)structure } : Enumerable.Empty<GridUnit>();

    protected override void Setup()
    {
        var tiles = Manager.FindSpreadTiles(1, minDistance, maxDistance, 0, minDistFromSpawners: 2);
        if (tiles.Count == 0) { Fail("There was no place for it on this map."); return; }

        var tile = tiles[0];
        structure = Instantiate(structurePrefab, Grid.GridToWorld(tile), structurePrefab.transform.rotation);
        if (!structure.PlaceAt(tile))
        {
            Destroy(structure.gameObject);
            structure = null;
            Fail("There was no place for it on this map.");
            return;
        }
        Manager.Reserve(tile);
        structure.maxResolve = Mathf.Max(1, structureResolve);
        structure.resolve = structure.maxResolve;
        structure.Owner = this;
        structure.Lost += _ => Fail($"The {StructureName} has fallen.");
    }

    public override void OnRoundStart(int round)
    {
        if (!Standing || anchorShieldsEachRound <= 0 || Manager.Anchor == null) return;
        Manager.Anchor.shields += anchorShieldsEachRound;
        Announce($"The {StructureName} shields the Anchor: +{anchorShieldsEachRound} Shield.");
    }

    public override void OnEncounterEnd(bool won)
    {
        if (won && Standing) Complete();
        else if (Standing) Fail("The ritual failed.");
    }

    protected override void Cleanup()
    {
        if (structure != null) { structure.RemoveFromGrid(); Destroy(structure.gameObject); }
        structure = null;
    }
}
