using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives for the whole run (DontDestroyOnLoad) and owns everything that carries between encounters:
///   - the party the player chose (spawned into every combat scene, shown in the portraits),
///   - the active Relics (buffs) and Omens (debuffs).
///
/// Combat reads the combined modifier numbers through RunManager.Effects, which is all zeros when
/// there is no RunManager, so combat scenes still work on their own.
///
/// Testing a combat scene directly: put a RunManager in it and fill Party (and any Relics/Omens).
/// </summary>
public class RunManager : MonoBehaviour
{
    public static RunManager Instance { get; private set; }

    /// <summary>The combined numbers of every active modifier. Safe to read without a RunManager.</summary>
    public static RunEffects Effects => Instance != null ? Instance.totals : RunEffects.None;

    [Header("Party")]
    [Tooltip("Every heroine the player can choose from.")]
    public List<HeroineDefinition> roster = new();
    [Tooltip("The chosen party, in order (portrait 1, 2, 3). The team-select screen fills this; " +
             "set it here to test a combat scene directly.")]
    [SerializeField] private List<HeroineDefinition> party = new();
    public int partySize = 3;
    [Tooltip("On: heroines placed by hand in a combat scene are removed and replaced by the party.")]
    public bool replaceSceneHeroines = true;

    [Header("Modifiers")]
    [Tooltip("Every Relic and Omen that exists, for reward screens and random picks.")]
    public List<RunModifier> modifierPool = new();
    [Tooltip("Active Relics and Omens. Add some here to test them.")]
    [SerializeField] private List<RunModifier> activeModifiers = new();

    [Header("Progress (read only)")]
    [SerializeField] private int encountersPlayed;
    [SerializeField] private int encountersWon;

    private readonly RunEffects totals = new();
    private int rerollsLeft;

    public IReadOnlyList<HeroineDefinition> Party => party;
    public bool HasParty => party.Any(d => d != null);
    public IReadOnlyList<RunModifier> ActiveModifiers => activeModifiers;
    public IEnumerable<RunModifier> Relics => activeModifiers.Where(m => m != null && m.kind == ModifierKind.Relic);
    public IEnumerable<RunModifier> Omens => activeModifiers.Where(m => m != null && m.kind == ModifierKind.Omen);
    public int EncountersPlayed => encountersPlayed;
    public int EncountersWon => encountersWon;

    /// <summary>The heroines spawned for the current combat scene, in party order.</summary>
    public List<Heroine> SpawnedHeroines { get; } = new();

    /// <summary>Raised when a Relic or Omen is added or removed (for a future modifier bar in the UI).</summary>
    public event Action ModifiersChanged;

    // =====================================================================
    // Lifetime
    // =====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        transform.SetParent(null); // DontDestroyOnLoad only works on root objects
        DontDestroyOnLoad(gameObject);
        RecalculateEffects();
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    private void OnValidate() => RecalculateEffects(); // live updates while tweaking in the Inspector

    /// <summary>Starts a fresh run with a new party. Clears all modifiers and progress.</summary>
    public void StartNewRun(IEnumerable<HeroineDefinition> chosenParty, IEnumerable<RunModifier> startingModifiers = null)
    {
        SetParty(chosenParty);
        activeModifiers.Clear();
        if (startingModifiers != null) activeModifiers.AddRange(startingModifiers.Where(m => m != null));
        encountersPlayed = 0;
        encountersWon = 0;
        RecalculateEffects();
        ModifiersChanged?.Invoke();
    }

    // =====================================================================
    // Party
    // =====================================================================

    /// <summary>Replaces the party. Returns false if the selection isn't valid.</summary>
    public bool SetParty(IEnumerable<HeroineDefinition> chosen)
    {
        var list = chosen.Where(d => d != null).Distinct().ToList();
        if (list.Count == 0 || list.Count > partySize)
        {
            Debug.LogWarning($"[Run] A party needs 1 to {partySize} different heroines (got {list.Count}).");
            return false;
        }
        party = list;
        return true;
    }

    public bool IsInParty(HeroineDefinition d) => party.Contains(d);

    /// <summary>Adds or removes one heroine, e.g. for a click-to-toggle team-select screen.</summary>
    public bool ToggleInParty(HeroineDefinition d)
    {
        if (d == null) return false;
        if (party.Remove(d)) return true;
        if (party.Count(x => x != null) >= partySize) return false;
        party.Add(d);
        return true;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != this) return;
        var turnManager = FindAnyObjectByType<TurnManager>();
        if (turnManager == null) return; // not a combat scene
        SpawnParty(turnManager);
    }

    /// <summary>Spawns the party into the current scene and hands it to the TurnManager (and the portraits).</summary>
    public void SpawnParty(TurnManager turnManager)
    {
        if (!HasParty)
        {
            Debug.Log("[Run] No party chosen; using the heroines placed in the scene.");
            return;
        }

        if (replaceSceneHeroines)
            foreach (var h in FindObjectsByType<Heroine>(FindObjectsSortMode.None))
                Destroy(h.gameObject);

        SpawnedHeroines.Clear();
        var anchor = FindAnyObjectByType<RitualAnchor>();
        var grid = GridManager.Instance;

        int i = 0;
        foreach (var def in party)
        {
            if (def == null) continue;
            if (def.prefab == null)
            {
                Debug.LogError($"[Run] {def.name} has no prefab assigned.");
                continue;
            }

            var heroine = Instantiate(def.prefab, SpawnPosition(i, anchor, grid), def.prefab.transform.rotation);
            heroine.name = def.displayName;
            def.ApplyTo(heroine);
            ApplyHeroineEffects(heroine);
            SpawnedHeroines.Add(heroine);
            i++;
        }

        turnManager.SetParty(SpawnedHeroines);
        Debug.Log($"[Run] Spawned party: {string.Join(", ", SpawnedHeroines.Select(h => h.DisplayName))}.");
    }

    private void ApplyHeroineEffects(Heroine h)
    {
        h.maxResolve = Mathf.Max(1, h.maxResolve + totals.maxResolve);
        h.resolve = h.maxResolve;
        h.moveRange = Mathf.Max(1, h.moveRange + totals.moveRange);
        h.arcanaRange = Mathf.Max(1f, h.arcanaRange + totals.abilityRange);
        h.wardingRange = Mathf.Max(1f, h.wardingRange + totals.abilityRange);
        h.loreRange = Mathf.Max(1f, h.loreRange + totals.abilityRange);
    }

    // On a generated map RitualManager moves them around the Ritual Anchor anyway.
    // On a hand-made board they start on the tiles next to the Anchor.
    private static Vector3 SpawnPosition(int index, RitualAnchor anchor, GridManager grid)
    {
        if (anchor == null) return Vector3.zero;
        if (grid == null || !grid.IsReady) return anchor.transform.position;

        Vector2Int anchorTile = grid.WorldToGrid(anchor.transform.position);
        Vector2Int dir = GridManager.Directions[index % GridManager.Directions.Length];
        return grid.GridToWorld(anchorTile + dir);
    }

    // =====================================================================
    // Relics and Omens
    // =====================================================================

    public bool Has(RunModifier modifier) => activeModifiers.Contains(modifier);

    /// <summary>Adds a Relic or Omen. The same one can't be active twice.</summary>
    public bool AddModifier(RunModifier modifier)
    {
        if (modifier == null || activeModifiers.Contains(modifier)) return false;
        activeModifiers.Add(modifier);
        RecalculateEffects();
        Debug.Log($"[Run] Gained {modifier.kind}: {modifier.displayName}.");
        ModifiersChanged?.Invoke();
        return true;
    }

    public bool RemoveModifier(RunModifier modifier)
    {
        if (!activeModifiers.Remove(modifier)) return false;
        RecalculateEffects();
        ModifiersChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Random modifiers of one kind that aren't active yet, e.g. three Relic choices for a reward screen
    /// or one Omen for the next encounter.
    /// </summary>
    public List<RunModifier> RollChoices(ModifierKind kind, int count)
    {
        return modifierPool
            .Where(m => m != null && m.kind == kind && !activeModifiers.Contains(m))
            .OrderBy(_ => UnityEngine.Random.value)
            .Take(count)
            .ToList();
    }

    private void RecalculateEffects()
    {
        // Reset to zero, then add every active modifier.
        var fresh = new RunEffects();
        foreach (var m in activeModifiers)
            if (m != null) fresh.Add(m.effects);
        CopyInto(fresh, totals);
    }

    private static void CopyInto(RunEffects from, RunEffects to)
    {
        to.turnCapacity = from.turnCapacity;
        to.startingStability = from.startingStability;
        to.maxStability = from.maxStability;
        to.anchorPassiveProgress = from.anchorPassiveProgress;
        to.channelCritBonus = from.channelCritBonus;
        to.capacityRestoresPerRound = from.capacityRestoresPerRound;
        to.maxResolve = from.maxResolve;
        to.moveRange = from.moveRange;
        to.abilityRange = from.abilityRange;
        to.successThreshold = from.successThreshold;
        to.emptyRollRerolls = from.emptyRollRerolls;
        to.enemyMovement = from.enemyMovement;
        to.extraSpawnsPerRound = from.extraSpawnsPerRound;
    }

    // =====================================================================
    // Called by combat
    // =====================================================================

    /// <summary>RitualManager calls this when an encounter starts (and on restart).</summary>
    public void OnEncounterStart(RitualManager ritualManager)
    {
        rerollsLeft = totals.emptyRollRerolls;
        foreach (var m in activeModifiers.ToList())
            if (m != null) m.OnEncounterStart(ritualManager);
    }

    public void OnRoundStart(RitualManager ritualManager, int round)
    {
        foreach (var m in activeModifiers.ToList())
            if (m != null) m.OnRoundStart(ritualManager, round);
    }

    /// <summary>RitualManager calls this on victory or defeat. Encounter-only modifiers expire here.</summary>
    public void OnEncounterEnd(RitualManager ritualManager, bool won)
    {
        encountersPlayed++;
        if (won) encountersWon++;

        foreach (var m in activeModifiers.ToList())
            if (m != null) m.OnEncounterEnd(ritualManager, won);

        int removed = activeModifiers.RemoveAll(m => m != null && m.duration == ModifierDuration.NextEncounter);
        if (removed > 0)
        {
            RecalculateEffects();
            ModifiersChanged?.Invoke();
        }
    }

    /// <summary>DiceRollManager asks this when a roll has 0 Successes. True = reroll it.</summary>
    public bool TryUseEmptyRollReroll()
    {
        if (rerollsLeft <= 0) return false;
        rerollsLeft--;
        return true;
    }
}