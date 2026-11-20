using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Objective - Elite Guardian (GDD: Arcana and Lore).
/// A stronger enemy (+2 Resolve) guards a relic cache far from the Anchor. It never walks toward
/// the ritual on its own, but attacks anyone who comes into reach. Defeating it needs a big Arcana
/// roll or Solving its type first. Reward: a Relic from the cache.
/// </summary>
public class EliteGuardianObjective : ObjectiveBase
{
    [Header("Guardian")]
    [Tooltip("Monster prefabs (with MonsterHolder) that can be the guardian. One is picked at random.")]
    [SerializeField] private List<MonsterHolder> guardianPrefabs = new();
    [Tooltip("Extra Resolve on top of its normal (and Solved) Resolve. GDD: +2.")]
    [SerializeField] private int extraResolve = 2;
    [Tooltip("On: it holds its position. Off: it moves and hunts like a normal enemy.")]
    [SerializeField] private bool holdPosition = true;

    [Header("Relic cache (optional visual)")]
    [Tooltip("Shown next to the guardian until it is defeated, e.g. a glowing chest.")]
    [SerializeField] private GameObject cachePrefab;
    [SerializeField] private Vector3 cacheOffset = new Vector3(0.6f, 0f, 0.6f);

    [Header("Placement (walking distance from the Anchor)")]
    [SerializeField] private int minDistance = 6;
    [SerializeField] private int maxDistance = 12;

    private MonsterHolder guardian;
    private GameObject cache;
    private bool spawned;

    private void Reset()
    {
        title = "Elite Guardian";
        description = "An empowered shadow guards a relic cache. Defeat it: Solve it first, or land a big Arcana roll.";
        moonlightReward = 2;
        bonus = new ObjectiveBonus { relics = 1 };
    }

    public override bool CanRun(ObjectiveManager manager) => guardianPrefabs.Any(p => p != null);

    public override string StatusLine =>
        State == ObjectiveState.Completed ? "Guardian defeated, cache opened"
        : State == ObjectiveState.Failed ? "The guardian still stands"
        : guardian != null ? $"Defeat the {guardian.DisplayName} (Resolve {guardian.CurrentResolve})"
        : "Defeat the guardian";

    protected override void Setup()
    {
        spawned = false;
        var tiles = Manager.FindSpreadTiles(1, minDistance, maxDistance, 0, minDistFromSpawners: 2);
        if (tiles.Count == 0 && Manager.TryFindBehindEnemyLines(4, out var far)) tiles.Add(far);
        if (tiles.Count == 0) { Fail("There was no place for the guardian on this map."); return; }

        var tile = tiles[0];
        var prefabs = guardianPrefabs.Where(p => p != null).ToList();
        var prefab = prefabs[Random.Range(0, prefabs.Count)];
        guardian = Instantiate(prefab, Grid.GridToWorld(tile), prefab.transform.rotation);
        if (!guardian.PlaceAt(tile))
        {
            Destroy(guardian.gameObject);
            guardian = null;
            Fail("There was no place for the guardian on this map.");
            return;
        }
        Manager.Reserve(tile);
        guardian.MakeElite(extraResolve, holdPosition);
        Ritual.AddMonster(guardian);

        if (cachePrefab != null)
            cache = Instantiate(cachePrefab, guardian.transform.position + cacheOffset, Quaternion.identity);
        spawned = true;
    }

    private void Update()
    {
        // Defeated (by any means): its object is destroyed.
        if (IsActive && spawned && guardian == null)
        {
            if (cache != null) Destroy(cache);
            Complete();
        }
    }

    public override void OnEncounterEnd(bool won)
    {
        if (guardian != null) Fail("The guardian still stands.");
    }

    protected override void Cleanup()
    {
        spawned = false;
        if (guardian != null) guardian.Die();
        if (cache != null) Destroy(cache);
        guardian = null;
        cache = null;
    }
}
