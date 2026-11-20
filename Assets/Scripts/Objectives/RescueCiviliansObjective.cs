using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Objective 2 - Save the Civilians (students, cats, dogs...).
/// Civilians hide around the board, between the Anchor and the spawners. The shadows may hunt them
/// (they become targets for enemy intents). Heroines protect them by blocking paths, killing or pushing
/// enemies, or Guarding them with Warding. Completed if enough are still safe when the ritual finishes.
/// </summary>
public class RescueCiviliansObjective : ObjectiveBase
{
    [Header("Civilians")]
    [Tooltip("Prefabs with the Civilian component (student, cat, dog...). One is picked at random per civilian.")]
    [SerializeField] private List<Civilian> civilianPrefabs = new();
    [SerializeField] private int count = 3;
    [Tooltip("How many must survive for the objective to succeed.")]
    [SerializeField] private int requiredSurvivors = 2;
    [Tooltip("Extra Moonlight for every survivor above the required number.")]
    [SerializeField] private int moonlightPerExtraSurvivor = 1;

    [Header("Placement (walking distance from the Anchor)")]
    [SerializeField] private int minDistance = 4;
    [SerializeField] private int maxDistance = 9;
    [SerializeField] private int minSpacing = 3;

    [Header("Rules")]
    [Tooltip("On: enemies may choose civilians as targets, like heroines.")]
    [SerializeField] private bool enemiesHuntCivilians = true;

    private readonly List<Civilian> civilians = new();
    private int baseMoonlight;

    private int Alive => civilians.Count(c => c != null && !c.IsLost);

    private void Reset()
    {
        title = "Save the Civilians";
        description = "Students and their pets are hiding from the shadows. Keep them alive until the ritual is complete.";
        moonlightReward = 2;
        bonus = new ObjectiveBonus { restoreTurnCapacity = 1 };
    }

    public override bool CanRun(ObjectiveManager manager) => civilianPrefabs.Any(p => p != null);

    public override string StatusLine =>
        State == ObjectiveState.Completed ? $"{Alive}/{civilians.Count} civilians saved"
        : State == ObjectiveState.Failed ? "Too many civilians were lost"
        : $"{Alive}/{civilians.Count} civilians safe (need {requiredSurvivors})";

    public override IEnumerable<ObjectiveUnit> Units => civilians.Where(c => c != null && !c.IsLost);
    public override IEnumerable<GridUnit> EnemyTargets =>
        enemiesHuntCivilians ? civilians.Where(c => c != null && !c.IsLost && c.IsOnGrid) : Enumerable.Empty<GridUnit>();

    protected override void Setup()
    {
        civilians.Clear();
        baseMoonlight = baseMoonlight == 0 ? moonlightReward : baseMoonlight;
        moonlightReward = baseMoonlight;

        var prefabs = civilianPrefabs.Where(p => p != null).ToList();
        foreach (var tile in Manager.FindSpreadTiles(count, minDistance, maxDistance, minSpacing, minDistFromSpawners: 2))
        {
            var prefab = prefabs[Random.Range(0, prefabs.Count)];
            var c = Instantiate(prefab, Grid.GridToWorld(tile), Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f));
            if (!c.PlaceAt(tile)) { Destroy(c.gameObject); continue; }
            Manager.Reserve(tile);
            c.Owner = this;
            c.Lost += OnCivilianLost;
            civilians.Add(c);
        }
        requiredSurvivors = Mathf.Min(requiredSurvivors, civilians.Count);
        if (civilians.Count == 0) Fail("There was nowhere for the civilians to hide on this map.");
    }

    protected override void Cleanup()
    {
        foreach (var c in civilians) if (c != null) { c.RemoveFromGrid(); Destroy(c.gameObject); }
        civilians.Clear();
    }

    // Civilian Shields last until the next player phase, like the heroines'.
    public override void OnRoundStart(int round)
    {
        foreach (var c in civilians) if (c != null) c.shields = 0;
    }

    private void OnCivilianLost(Civilian c)
    {
        Announce($"{c.DisplayName} was taken by the shadows!");
        if (Alive < requiredSurvivors) Fail($"Only {Alive} civilians are left.");
        else Manager.Refresh();
    }

    public override void OnEncounterEnd(bool won)
    {
        if (!won) return;
        if (Alive < requiredSurvivors) { Fail("Not enough civilians survived."); return; }
        moonlightReward = baseMoonlight + Mathf.Max(0, Alive - requiredSurvivors) * moonlightPerExtraSurvivor;
        Complete();
    }
}
