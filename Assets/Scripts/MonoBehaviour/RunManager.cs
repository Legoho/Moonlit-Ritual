using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives for the whole run (DontDestroyOnLoad) and owns everything that carries between encounters:
///   - the party the player chose (spawned into every combat scene, shown in the portraits),
///   - the Relics the team has found (team-wide buffs),
///   - each heroine's Omens (lasting debuffs from ending a mission Downed) and whether she is Eclipsed,
///   - learned abilities and Moonlight.
///
/// Combat reads the combined team numbers (Relics + Shared Omens of the deployed girls) through
/// RunManager.Effects, which is all zeros when there is no RunManager, so combat scenes still work on their own.
///
/// Testing a combat scene directly: put a RunManager in it and fill Party (and any Relics or Omens).
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

    [Header("Relics")]
    [Tooltip("Every Relic that exists, for rewards and random picks.")]
    public List<Relic> relicPool = new();
    [Tooltip("Relics the team has. Add some here to test them.")]
    [SerializeField] private List<Relic> relics = new();

    [Header("Omens")]
    [Tooltip("Every Omen that exists. Omens are drawn from here by family.")]
    public List<Omen> omenPool = new();
    [Tooltip("Each heroine's Omens. Add entries here to test them.")]
    [SerializeField] private List<HeroineOmens> heroineOmens = new();
    [Tooltip("A heroine can carry this many Omens; one more makes her Eclipsed (GDD: 2).")]
    public int maxOmens = 2;
    [Tooltip("Off: Omens are never given (e.g. while testing combat).")]
    public bool omensEnabled = true;

    [Header("Abilities")]
    [Tooltip("Every learnable ability that exists, for the Archive / research screen.")]
    public List<AbilityDefinition> abilityLibrary = new();
    [Tooltip("Abilities each heroine has learned during this run, on top of her starting abilities. Add some here to test them.")]
    [SerializeField] private List<LearnedAbilities> learnedAbilities = new();

    [Header("Progress (read only)")]
    [SerializeField] private int encountersPlayed;
    [SerializeField] private int encountersWon;
    [Tooltip("Research currency earned from Optional Objectives (GDD 9.4).")]
    [SerializeField] private int moonlight;

    private readonly RunEffects totals = new();
    private int rerollsLeft;

    public IReadOnlyList<HeroineDefinition> Party => party;
    public bool HasParty => party.Any(d => d != null);
    public IReadOnlyList<Relic> Relics => relics;
    public int EncountersPlayed => encountersPlayed;
    public int EncountersWon => encountersWon;
    public int Moonlight => moonlight;

    /// <summary>The heroines spawned for the current combat scene, in party order.</summary>
    public List<Heroine> SpawnedHeroines { get; } = new();

    /// <summary>A Relic was gained or lost (for a Relic bar in the UI).</summary>
    public event Action RelicsChanged;
    /// <summary>A heroine's Omens changed (received, cleansed, Eclipsed).</summary>
    public event Action<HeroineDefinition> OmensChanged;
    public event Action MoonlightChanged;
    /// <summary>A heroine learned an ability (for a "new spell" pop-up later).</summary>
    public event Action<HeroineDefinition, AbilityDefinition> AbilityLearned;

    [Serializable]
    public class LearnedAbilities
    {
        public HeroineDefinition heroine;
        public List<AbilityDefinition> abilities = new();
    }

    private readonly Dictionary<HeroineDefinition, Heroine> spawnedByDefinition = new();

    public void AddMoonlight(int amount)
    {
        if (amount == 0) return;
        moonlight = Mathf.Max(0, moonlight + amount);
        MoonlightChanged?.Invoke();
    }

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
    public void StartNewRun(IEnumerable<HeroineDefinition> chosenParty, IEnumerable<Relic> startingRelics = null)
    {
        SetParty(chosenParty);
        relics.Clear();
        if (startingRelics != null) relics.AddRange(startingRelics.Where(r => r != null).Distinct());
        heroineOmens.Clear();
        encountersPlayed = 0;
        encountersWon = 0;
        moonlight = 0;
        learnedAbilities.Clear();
        RecalculateEffects();
        RelicsChanged?.Invoke();
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
        spawnedByDefinition.Clear();
        var anchor = FindAnyObjectByType<RitualAnchor>();
        var grid = GridManager.Instance;

        // Eclipsed heroines cannot deploy (GDD Omens). Shared Omens count for the girls who do.
        var deploying = new List<HeroineDefinition>();
        foreach (var def in party)
        {
            if (def == null) continue;
            if (IsEclipsed(def)) { Debug.LogWarning($"[Run] {def.displayName} is Eclipsed and cannot deploy until she is cleansed."); continue; }
            deploying.Add(def);
        }
        RecalculateEffects(deploying);

        int i = 0;
        foreach (var def in deploying)
        {
            if (def.prefab == null)
            {
                Debug.LogError($"[Run] {def.name} has no prefab assigned.");
                continue;
            }

            var heroine = Instantiate(def.prefab, SpawnPosition(i, anchor, grid), def.prefab.transform.rotation);
            heroine.name = def.displayName;
            def.ApplyTo(heroine);
            foreach (var a in LearnedBy(def)) heroine.Learn(a);
            ApplyHeroineEffects(heroine);
            foreach (var o in OmensOf(def)) o.omen.ApplyTo(heroine, o);
            SpawnedHeroines.Add(heroine);
            spawnedByDefinition[def] = heroine;
            i++;
        }

        turnManager.SetParty(SpawnedHeroines);
        Debug.Log($"[Run] Spawned party: {string.Join(", ", SpawnedHeroines.Select(h => h.DisplayName))}.");
    }

    // =====================================================================
    // Abilities learned during the run
    // =====================================================================

    private IEnumerable<AbilityDefinition> LearnedBy(HeroineDefinition h) =>
        learnedAbilities.Where(l => l != null && l.heroine == h).SelectMany(l => l.abilities).Where(a => a != null);

    /// <summary>Starting abilities plus everything she learned this run.</summary>
    public IEnumerable<AbilityDefinition> KnownAbilities(HeroineDefinition h) =>
        h == null ? Enumerable.Empty<AbilityDefinition>() : h.startingAbilities.Where(a => a != null).Concat(LearnedBy(h)).Distinct();

    public bool KnowsAbility(HeroineDefinition h, AbilityDefinition a) => a != null && KnownAbilities(h).Contains(a);

    /// <summary>Library abilities she could still learn (not known yet, and allowed for her).</summary>
    public List<AbilityDefinition> LearnableAbilities(HeroineDefinition h) =>
        abilityLibrary.Where(a => a != null && a.CanBeLearnedBy(h) && !KnowsAbility(h, a)).ToList();

    /// <summary>
    /// Teaches a heroine an ability for the rest of the run. If she is in the current combat scene,
    /// it appears in her spell book straight away. Returns false if she already knows it or can't learn it.
    /// </summary>
    public bool LearnAbility(HeroineDefinition h, AbilityDefinition a)
    {
        if (h == null || a == null || KnowsAbility(h, a)) return false;
        if (!a.CanBeLearnedBy(h))
        {
            Debug.LogWarning($"[Run] {h.displayName} can't learn {a.displayName}.");
            return false;
        }

        var entry = learnedAbilities.FirstOrDefault(l => l != null && l.heroine == h);
        if (entry == null) learnedAbilities.Add(entry = new LearnedAbilities { heroine = h });
        entry.abilities.Add(a);

        if (spawnedByDefinition.TryGetValue(h, out var spawned) && spawned != null) spawned.Learn(a);
        Debug.Log($"[Run] {h.displayName} learned {a.displayName}.");
        AbilityLearned?.Invoke(h, a);
        return true;
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
    // Relics
    // =====================================================================

    public bool HasRelic(Relic relic) => relics.Contains(relic);

    /// <summary>Gives the team a Relic. The same one can't be held twice.</summary>
    public bool AddRelic(Relic relic)
    {
        if (relic == null || relics.Contains(relic)) return false;
        relics.Add(relic);
        RecalculateEffects();
        Debug.Log($"[Run] Found Relic: {relic.displayName}.");
        RelicsChanged?.Invoke();
        return true;
    }

    public bool RemoveRelic(Relic relic)
    {
        if (!relics.Remove(relic)) return false;
        RecalculateEffects();
        RelicsChanged?.Invoke();
        return true;
    }

    /// <summary>Random Relics the team doesn't have yet, e.g. three choices for a reward screen.</summary>
    public List<Relic> RollRelicChoices(int count, RelicRarity? rarity = null) =>
        relicPool.Where(r => r != null && r.inRewardPool && !relics.Contains(r) && (!rarity.HasValue || r.rarity == rarity.Value))
                 .OrderBy(_ => UnityEngine.Random.value)
                 .Take(count)
                 .ToList();

    /// <summary>Gives one random Relic the team doesn't have (e.g. an Elite Guardian's cache). Null if none are left.</summary>
    public Relic GrantRandomRelic()
    {
        var relic = RollRelicChoices(1).FirstOrDefault();
        if (relic != null) AddRelic(relic);
        return relic;
    }

    // =====================================================================
    // Omens (GDD: lasting debuffs from ending a mission Downed)
    // =====================================================================

    private HeroineOmens RecordOf(HeroineDefinition h, bool create)
    {
        var rec = heroineOmens.FirstOrDefault(r => r != null && r.heroine == h);
        if (rec == null && create && h != null) heroineOmens.Add(rec = new HeroineOmens { heroine = h });
        return rec;
    }

    public IReadOnlyList<OmenInstance> OmensOf(HeroineDefinition h)
    {
        var rec = RecordOf(h, false);
        return rec != null ? rec.omens.Where(o => o != null && o.omen != null).ToList() : new List<OmenInstance>();
    }

    public bool IsEclipsed(HeroineDefinition h) => RecordOf(h, false)?.eclipsed ?? false;

    /// <summary>"Eclipse risk": one more Omen would Eclipse her.</summary>
    public bool AtEclipseRisk(HeroineDefinition h) => !IsEclipsed(h) && OmensOf(h).Count >= maxOmens;

    /// <summary>
    /// Gives a heroine an Omen of a family (null = any family, e.g. Downed by a boss or hazard),
    /// drawn at random from the pool, excluding ones she has. Respects the GDD rule of at most one
    /// threshold-changing Omen per heroine. If she already carries the maximum, she becomes Eclipsed instead.
    /// Returns the Omen received (null if Eclipsed or none could be drawn).
    /// </summary>
    public Omen GiveOmen(HeroineDefinition h, EnemyCategory? family)
    {
        if (h == null || !omensEnabled) return null;
        var rec = RecordOf(h, true);
        if (rec.eclipsed) return null;

        if (rec.omens.Count(o => o?.omen != null) >= maxOmens)
        {
            rec.eclipsed = true;
            Debug.LogWarning($"[Run] {h.displayName} is ECLIPSED: she cannot deploy until a Cleansing Rite removes all her Omens.");
            OmensChanged?.Invoke(h);
            RecalculateEffects();
            return null;
        }

        bool hasThreshold = rec.omens.Any(o => o?.omen != null && o.omen.rules.ChangesThreshold);
        var candidates = omenPool.Where(o => o != null && o.inDrawPool
                                             && (!family.HasValue || o.family == family.Value)
                                             && !rec.omens.Any(x => x?.omen == o)
                                             && !(hasThreshold && o.rules.ChangesThreshold)).ToList();
        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[Run] No {(family.HasValue ? family.Value + " " : "")}Omen left to give {h.displayName}.");
            return null;
        }
        return AddOmen(h, candidates[UnityEngine.Random.Range(0, candidates.Count)]);
    }

    /// <summary>Gives a specific Omen (story, testing). Returns it, or null if she already has it.</summary>
    public Omen AddOmen(HeroineDefinition h, Omen omen)
    {
        if (h == null || omen == null) return null;
        var rec = RecordOf(h, true);
        if (rec.omens.Any(o => o?.omen == omen)) return null;

        var instance = new OmenInstance
        {
            omen = omen,
            discipline = omen.rules.randomThresholdDiscipline
                ? (Discipline)UnityEngine.Random.Range(0, Enum.GetValues(typeof(Discipline)).Length)
                : omen.rules.thresholdDiscipline
        };
        rec.omens.Add(instance);
        string where = omen.rules.ChangesThreshold ? $" ({instance.discipline})" : "";
        Debug.Log($"[Run] {h.displayName} receives the {omen.family} Omen {omen.displayName}{where}.");
        RecalculateEffects();
        OmensChanged?.Invoke(h);
        return omen;
    }

    /// <summary>Cleansing Rite: removes one Omen of the player's choice.</summary>
    public bool CleanseOmen(HeroineDefinition h, Omen omen)
    {
        var rec = RecordOf(h, false);
        if (rec == null || rec.omens.RemoveAll(o => o?.omen == omen) == 0) return false;
        Debug.Log($"[Run] {h.displayName} is cleansed of {omen.displayName}.");
        RecalculateEffects();
        OmensChanged?.Invoke(h);
        return true;
    }

    /// <summary>The longer Rite for an Eclipsed heroine: removes all her Omens and the Eclipse.</summary>
    public void CleanseAll(HeroineDefinition h)
    {
        var rec = RecordOf(h, false);
        if (rec == null) return;
        rec.omens.Clear();
        rec.eclipsed = false;
        Debug.Log($"[Run] {h.displayName} is cleansed of every Omen.");
        RecalculateEffects();
        OmensChanged?.Invoke(h);
    }

    /// <summary>The family a Downed heroine would receive if the mission ended now (for a marker on her portrait).</summary>
    public static EnemyCategory? PendingOmenFamily(Heroine h) => h != null && h.IsDowned ? h.downedBy : null;

    // =====================================================================
    // Team numbers
    // =====================================================================

    /// <summary>Relics + Shared Omens of the deploying heroines (default: the current party, minus Eclipsed girls).</summary>
    private void RecalculateEffects(IEnumerable<HeroineDefinition> deploying = null)
    {
        deploying ??= spawnedByDefinition.Count > 0
            ? spawnedByDefinition.Keys
            : party.Where(d => d != null && !IsEclipsed(d));

        var fresh = new RunEffects();
        foreach (var r in relics)
            if (r != null) fresh.Add(r.effects);
        foreach (var def in deploying.Distinct())
            foreach (var o in OmensOf(def))
                if (o.omen.scope == OmenScope.Shared) fresh.Add(o.omen.teamEffects);
        totals.CopyFrom(fresh);
    }

    // =====================================================================
    // Called by combat
    // =====================================================================

    /// <summary>RitualManager calls this when an encounter starts (and on restart).</summary>
    public void OnEncounterStart(RitualManager ritualManager)
    {
        rerollsLeft = totals.emptyRollRerolls;
        foreach (var r in relics.ToList())
            if (r != null) r.OnEncounterStart(ritualManager);
        foreach (var (h, o) in DeployedOmens())
            o.OnEncounterStart(ritualManager, h);
    }

    public void OnRoundStart(RitualManager ritualManager, int round)
    {
        foreach (var r in relics.ToList())
            if (r != null) r.OnRoundStart(ritualManager, round);
        foreach (var (h, o) in DeployedOmens())
            o.OnRoundStart(ritualManager, h, round);
    }

    /// <summary>RitualManager calls this on victory or defeat. Heroines still Downed receive an Omen.</summary>
    public void OnEncounterEnd(RitualManager ritualManager, bool won)
    {
        encountersPlayed++;
        if (won) encountersWon++;

        foreach (var r in relics.ToList())
            if (r != null) r.OnEncounterEnd(ritualManager, won);
        foreach (var (h, o) in DeployedOmens())
            o.OnEncounterEnd(ritualManager, h, won);

        // GDD: a girl who is Downed when the mission ends (won or lost) receives one Omen.
        // A girl who was revived and is standing receives none.
        foreach (var pair in spawnedByDefinition.ToList())
        {
            var h = pair.Value;
            if (h == null || !h.IsDowned) continue;
            var omen = GiveOmen(pair.Key, h.downedBy);
            if (omen != null) ritualManager.Announce($"{h.DisplayName} fell and carries a mark back: {omen.displayName}. {omen.description}");
            else if (IsEclipsed(pair.Key)) ritualManager.Announce($"{h.DisplayName} is ECLIPSED and must be cleansed before she can fight again.");
        }
    }

    private IEnumerable<(Heroine, Omen)> DeployedOmens()
    {
        foreach (var pair in spawnedByDefinition.ToList())
            if (pair.Value != null)
                foreach (var o in OmensOf(pair.Key))
                    yield return (pair.Value, o.omen);
    }

    /// <summary>DiceRollManager asks this when a roll has 0 Successes. True = reroll it.</summary>
    public bool TryUseEmptyRollReroll()
    {
        if (rerollsLeft <= 0) return false;
        rerollsLeft--;
        return true;
    }
}
