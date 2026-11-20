using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Runs the combat phase (GDD 4, 5, 6): rounds, player input, the four standard discipline
/// actions, the enemy phase and win/loss. Button entry points: Move, Arcana, Warding, Stabilize,
/// Lore, Occultism, Assist, FinishHeroine, CancelAction, EndTurn, RestartEncounter.
///
/// Round order:
///   1. Spawners add monsters, every monster plans and telegraphs its intent.
///   2. Player phase: each heroine gets one free Move and two Actions (each discipline once).
///   3. Enemy phase: intents resolve in order.
///   4. Anchor passive Progress, Stability Strain, round + 1, loss check.
/// </summary>
[RequireComponent(typeof(DiceRollManager))]
public partial class RitualManager : MonoBehaviour
{
    [SerializeField] private RitualState ritual = new();

    [Header("References (found automatically if empty)")]
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private GridManager grid;
    [SerializeField] private RitualAnchor anchor;
    [SerializeField] private DiceRollManager dice;
    /// <summary>The DiceRollManager this ritual actually rolls with.</summary>         
    public DiceRollManager Dice => dice != null ? dice : (dice = GetComponent<DiceRollManager>());   
    [SerializeField] private SliderManager sliderManager;
    [SerializeField] private ButtonTextManager ui;
    [SerializeField] private List<MonsterSpawner> spawners = new();

    [Header("Input")]
    [Tooltip("Selecting a heroine who hasn't moved shows her move range immediately (no Move button press needed).")]
    [SerializeField] private bool autoShowMoveOnSelect = true;

    [Header("Enemy phase pacing")]
    [SerializeField] private float stepDuration = 0.12f;
    [SerializeField] private float enemyActionDelay = 0.35f;

    [Header("Highlight colors")]
    [SerializeField] private Color moveColor = new Color(0.3f, 0.6f, 1f, 0.5f);
    [SerializeField] private Color damageColor = new Color(1f, 0.25f, 0.25f, 0.5f);
    [SerializeField] private Color supportColor = new Color(0.5f, 1f, 0.4f, 0.5f);

    [Header("Runtime")]
    public List<MonsterHolder> monsters = new();
    [SerializeField] private List<EnemyIntent> intents = new();

    private readonly HashSet<Monster> solvedTypes = new();
    private readonly Dictionary<Monster, int> insight = new();

    private enum InputMode { Idle, Moving, Targeting }
    private InputMode mode = InputMode.Idle;
    private Action<GridUnit> onTargetChosen;
    private readonly List<GridUnit> validTargets = new();
    private readonly HashSet<Vector2Int> reachableTiles = new();
    private bool busy;
    private bool rolling;
    private Heroine lastSelectedHeroine; // for the "switched heroine" tutorial
    private int encounterId; // stale dice callbacks from a restarted encounter are ignored

    public RitualState Ritual => ritual;
    public bool IsBusy => busy || rolling;

    /// <summary>Raised once when the encounter is won (true) or lost (false), with the reason.</summary>
    public event Action<bool, string> EncounterEnded;
    public CombatPhase Phase => turnManager != null ? turnManager.Phase : CombatPhase.Setup;

    // =====================================================================
    // Setup
    // =====================================================================

    private IEnumerator Start()
    {
        if (turnManager == null) turnManager = FindAnyObjectByType<TurnManager>();
        if (grid == null) grid = FindAnyObjectByType<GridManager>();
        if (anchor == null) anchor = FindAnyObjectByType<RitualAnchor>();
        if (dice == null) dice = GetComponent<DiceRollManager>();
        if (sliderManager == null) sliderManager = FindAnyObjectByType<SliderManager>();
        if (ui == null) ui = FindAnyObjectByType<ButtonTextManager>();

        if (turnManager == null || grid == null || anchor == null)
        {
            Debug.LogError("RitualManager needs a TurnManager, a GridManager and a RitualAnchor in the scene.");
            yield break;
        }

        // Wait one frame: every Start has run by then, so MapManager has generated its map
        // and hand-placed GridUnits have registered their tiles.
        yield return null;

        // Searched only now, because MapManager creates spawners (on Spawn-cell prefabs) in its own Start.
        spawners.RemoveAll(s => s == null);
        if (spawners.Count == 0)
            spawners.AddRange(FindObjectsByType<MonsterSpawner>(FindObjectsSortMode.None));
        Log($"Found {spawners.Count} MonsterSpawner(s).");

        if (grid.UsesMap)
        {
            if (!grid.SyncFromMap())
            {
                Debug.LogError("RitualManager: MapManager has no map (rows/columns are 0).");
                yield break;
            }
            PlaceUnitsOnMap();
        }
        StartEncounter();
    }

    /// <summary>
    /// Anchor on the ritual centre, heroines on the ritual cells around it (the four sides first,
    /// so they can Channel and Stabilize on round 1), spawners on the map's Spawn cells.
    /// </summary>
    private void PlaceUnitsOnMap()
    {
        Vector2Int center = grid.RitualCenterTile;
        if (!anchor.PlaceAt(center))
            Debug.LogError($"RitualManager: could not place the Ritual Anchor on {center}.");

        var ritualTiles = grid.TilesOfCategory(MapCellType.Ritual)
            .Where(t => t != center)
            .OrderBy(t => GridManager.Distance(t, center)) // the 4 sides (distance 1) before the 4 corners
            .ToList();

        int next = 0;
        foreach (var h in turnManager.party)
        {
            if (h == null) continue;
            while (next < ritualTiles.Count && !grid.IsFree(ritualTiles[next])) next++;

            Vector2Int tile;
            if (next < ritualTiles.Count) tile = ritualTiles[next++];
            else if (!grid.TryFindNearestFree(center, 5, out tile))
            {
                Debug.LogError($"RitualManager: no free tile for {h.DisplayName}.");
                continue;
            }
            h.PlaceAt(tile);
        }

        // Spawners that MapManager built on a Spawn cell are already in place.
        // Spawners placed by hand in the scene are moved onto a Spawn cell nobody uses yet.
        var freeSpawnTiles = grid.TilesOfCategory(MapCellType.Spawn);
        var unplaced = new List<MonsterSpawner>();
        foreach (var s in spawners)
        {
            if (s == null) continue;
            if (!freeSpawnTiles.Remove(s.Tile)) unplaced.Add(s);
        }
        foreach (var s in unplaced)
        {
            if (freeSpawnTiles.Count == 0)
            {
                Debug.LogWarning($"{s.name}: no free Spawn cell left on the map, so it stays where it is.");
                continue;
            }
            s.transform.position = grid.GridToWorld(freeSpawnTiles[0]);
            freeSpawnTiles.RemoveAt(0);
        }
        if (freeSpawnTiles.Count > 0)
            Debug.LogWarning($"{freeSpawnTiles.Count} Spawn cell(s) on the map have no MonsterSpawner; they spawn nothing. " +
                             "Put MonsterSpawner on the Spawn cell prefab, or add more spawners to the scene.");
    }

    private void StartEncounter()
    {
        ritual.Reset(RunManager.Effects);
        DiceRules.ThresholdModifier = RunManager.Effects.successThreshold;
        solvedTypes.Clear();
        insight.Clear();
        foreach (var s in spawners) if (s != null) s.ResetSpawner();
        turnManager.BeginEncounter();
        Log("Encounter begins.");
        TutorialManager.Fire(TutorialTrigger.EncounterStart);
        if (RunManager.Instance != null) RunManager.Instance.OnEncounterStart(this);
        if (ObjectiveManager.Instance != null) ObjectiveManager.Instance.BeginEncounter(this);
        BeginRound();
    }

    public void RestartEncounter()
    {
        StopAllCoroutines();
        encounterId++;
        busy = false;
        rolling = false;
        dice.SkipAnimation();
        ClearInputMode();
        foreach (var m in monsters) if (m != null) m.Die();
        monsters.Clear();
        intents.Clear();
        foreach (var h in turnManager.party) if (h != null) h.ResetForEncounter();
        anchor.shields = 0;
        if (grid.UsesMap) PlaceUnitsOnMap(); // heroines back around the circle
        StartEncounter();
    }

    // =====================================================================
    // Round flow
    // =====================================================================

    private void BeginRound()
    {
        ritual.BeginRound();
        monsters.RemoveAll(m => m == null);

        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            foreach (var m in spawner.SpawnForRound(turnManager.Round))
            {
                monsters.Add(m);
                Log($"{m.DisplayName} appears at {m.GridPosition}.");
            }
        }
        anchor.shields = 0;

        if (RunManager.Instance != null) RunManager.Instance.OnRoundStart(this, turnManager.Round);
        if (ObjectiveManager.Instance != null) ObjectiveManager.Instance.RoundStart(turnManager.Round);
        PlanIntents();
        turnManager.StartPlayerPhase();

        Log($"--- Round {turnManager.Round} / {ritual.turnCapacity}: player phase ---");
        RefreshAll();
    }

    public void EndTurn()
    {
        if (IsBusy || Phase != CombatPhase.PlayerPhase) return;
        ClearInputMode();
        StartCoroutine(EnemyPhase());
    }

    private IEnumerator EnemyPhase()
    {
        busy = true;
        turnManager.StartEnemyPhase();
        Log("--- Enemy phase ---");
        RefreshUI();

        foreach (var intent in OrderedIntents())
        {
            if (intent.monster == null) continue;
            yield return ResolveIntent(intent);
            RefreshUI();
            if (CheckEncounterEnd()) { busy = false; yield break; }
            yield return new WaitForSeconds(enemyActionDelay);
        }

        if (ObjectiveManager.Instance != null) ObjectiveManager.Instance.EnemyPhaseEnd();

        // Passive ritual effects (GDD 5.3): Anchor gives Progress if a girl stands next to it.
        if (turnManager.InCombat.Any(h => anchor.IsAdjacent(h.GridPosition)))
        {
            int gained = ritual.AddProgress(ritual.AnchorPassiveProgress);
            Log($"Anchor passive: +{gained} Progress{LostToStability(ritual.AnchorPassiveProgress, gained)}.");
        }
        if (CheckEncounterEnd()) { busy = false; yield break; }

        ritual.ApplyStrain();
        turnManager.AdvanceRound();
        busy = false;
        if (CheckEncounterEnd()) yield break;

        BeginRound();
    }

    private IEnumerator ResolveIntent(EnemyIntent intent)
    {
        var m = intent.monster;

        // Same stepping rule as EnemyPlanner.Predict: stop before the first blocked tile.
        foreach (var tile in intent.path)
        {
            if (!grid.IsFree(tile)) break;
            yield return m.StepTo(tile, stepDuration);
        }

        if (intent.target == null)
        {
            Log($"{m.DisplayName} has nowhere to go.");
            yield break;
        }
        if (!EnemyPlanner.CanHitFrom(intent, m.GridPosition))
        {
            Log($"{m.DisplayName} cannot reach {intent.target.DisplayName}.");
            yield break;
        }

        if (intent.targetType == IntentTargetType.Anchor) HitAnchor(m);
        else if (intent.target is Civilian civilian) HitCivilian(m, civilian);
        else HitHeroine(m, intent.TargetHeroine);
    }

    private void HitAnchor(MonsterHolder m)
    {
        int damage = m.StabilityDamage;
        int blocked = Mathf.Min(anchor.shields, damage);
        anchor.shields -= blocked;
        damage -= blocked;

        if (damage <= 0)
        {
            Log($"{m.DisplayName} strikes the Anchor, but Shields block it.");
            return;
        }
        ritual.LoseCapacity(damage);
        ritual.Disrupt();
        Log($"{m.DisplayName} hits the Anchor: -{damage} Turn Capacity ({ritual.turnCapacity} left), Stability {ritual.stability}.");
    }

    private void HitCivilian(MonsterHolder m, Civilian c)
    {
        if (c.TakeHit(m.HeroineDamage))
            Log($"{m.DisplayName} attacks {c.DisplayName}{(c.IsLost ? " - they are lost!" : $": Resolve {c.resolve}/{c.maxResolve}.")}");
        else
            Log($"{m.DisplayName} attacks {c.DisplayName}, but Shields block it.");
    }

    private void HitHeroine(MonsterHolder m, Heroine h)
    {
        if (h.TakeHit(m.HeroineDamage))
        {
            Log($"{m.DisplayName} hits {h.DisplayName}: Resolve {h.resolve}/{h.maxResolve}{(h.IsDowned ? " - DOWNED" : "")}.");
            if (h.IsDowned)
            {
                if (m.Data != null) h.downedBy = m.Data.category;
                if (h.downedBy.HasValue)
                    Log($"If the mission ends with {h.DisplayName} Downed, she receives a{(h.downedBy == EnemyCategory.Assault ? "n" : "")} {h.downedBy} Omen.");
                if (h.progressLostWhenDowned > 0)
                {
                    int lost = Mathf.Min(ritual.progress, h.progressLostWhenDowned);
                    ritual.progress -= lost;
                    Log($"Omen: the ritual loses {lost} Progress as {h.DisplayName} falls ({ritual.progress}/{ritual.progressTarget}).");
                }
                TutorialManager.Fire(TutorialTrigger.HeroineDowned, h);
            }
        }
        else
            Log($"{m.DisplayName} hits {h.DisplayName}, but Shields block it.");
    }

    private bool CheckEncounterEnd()
    {
        if (Phase == CombatPhase.Victory || Phase == CombatPhase.Defeat) return true;

        if (ritual.IsComplete) { EndEncounter(true, "The ritual is complete!"); return true; }
        if (turnManager.Round >= ritual.turnCapacity) { EndEncounter(false, "The ritual collapses: out of time."); return true; }
        if (turnManager.AllDowned) { EndEncounter(false, "Every heroine is Downed."); return true; }
        return false;
    }

    private void EndEncounter(bool won, string reason)
    {
        ClearInputMode();
        turnManager.SetResult(won);
        foreach (var m in monsters) if (m != null) m.ShowIntent(null);
        Log(won ? $"VICTORY - {reason}" : $"DEFEAT - {reason}");
        if (ObjectiveManager.Instance != null) ObjectiveManager.Instance.EncounterEnd(won);
        if (RunManager.Instance != null) RunManager.Instance.OnEncounterEnd(this, won);
        RefreshUI();
        EncounterEnded?.Invoke(won, reason);
    }

    // =====================================================================
    // Enemy intents
    // =====================================================================

    private void PlanIntents()
    {
        intents.Clear();
        var reserved = new HashSet<Vector2Int>();
        foreach (var m in monsters)
        {
            var intent = EnemyPlanner.Plan(m, grid, anchor, turnManager.party, reserved, ObjectiveManager.EnemyTargets);
            intents.Add(intent);
            if (intent.path.Count > 0) reserved.Add(intent.path[intent.path.Count - 1]);
        }
        RefreshIntents();
    }

    /// <summary>Re-plans one monster after it was pushed, keeping its place in the order.</summary>
    private void ReplanIntent(MonsterHolder m)
    {
        int index = intents.FindIndex(i => i.monster == m);
        if (index < 0) return;
        bool delayed = intents[index].delayed;

        var reserved = new HashSet<Vector2Int>(intents
            .Where(i => i.monster != m && i.path.Count > 0)
            .Select(i => i.path[i.path.Count - 1]));
        var fresh = EnemyPlanner.Plan(m, grid, anchor, turnManager.party, reserved, ObjectiveManager.EnemyTargets);
        fresh.delayed = delayed;
        intents[index] = fresh;
    }

    private List<EnemyIntent> OrderedIntents() =>
        intents.Where(i => i.monster != null).OrderBy(i => i.delayed ? 1 : 0).ToList();

    /// <summary>Recomputes predictions after any change on the board and redraws them.</summary>
    private void RefreshIntents()
    {
        intents.RemoveAll(i => i.monster == null);
        EnemyPlanner.Predict(OrderedIntents(), grid);
        foreach (var i in intents) i.monster.ShowIntent(i);
    }

    // =====================================================================
    // Input
    // =====================================================================

    public void OnUnitClicked(GridUnit unit)
    {
        if (rolling) { dice.SkipAnimation(); return; }
        if (busy || Phase != CombatPhase.PlayerPhase) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        HandleUnitClick(unit);
    }

    /// <summary>
    /// Called by a party panel. Works like clicking the heroine on the board: selects her,
    /// or picks her as the target if an action (e.g. Warding) is waiting for one.
    /// </summary>
    public void OnHeroinePanelClicked(Heroine heroine)
    {
        if (rolling) { dice.SkipAnimation(); return; }
        if (busy || Phase != CombatPhase.PlayerPhase || heroine == null) return;
        HandleUnitClick(heroine);
    }

    private void HandleUnitClick(GridUnit unit)
    {
        if (mode == InputMode.Targeting)
        {
            if (validTargets.Contains(unit))
            {
                // Each action's callback calls AfterPlayerAction itself (rolls finish later).
                var callback = onTargetChosen;
                ClearInputMode();
                callback?.Invoke(unit);
            }
            else Log($"{unit.DisplayName} is not a valid target. Right-click to cancel.");
            return;
        }

        if (unit is Heroine h) SelectHeroine(h);
        else if (unit is MonsterHolder m) Log(DescribeMonster(m));
        else if (unit is RitualAnchor) Log($"Ritual Anchor: Shields {anchor.shields}, Stability {ritual.stability} ({ritual.StabilityState}).");
        else if (unit is ObjectiveUnit ou) Log(ou.Describe());
    }

    public void OnTileClicked(Vector2Int tile)
    {
        if (rolling) { dice.SkipAnimation(); return; }
        if (busy || Phase != CombatPhase.PlayerPhase) return;
        if (mode == InputMode.Moving) TryMove(tile);
    }

    private void SelectHeroine(Heroine h)
    {
        ClearInputMode();
        if (!turnManager.TrySelect(h, out string reason))
        {
            Log(reason);
            return;
        }
        TutorialManager.Fire(TutorialTrigger.HeroineSelected, h);
        if (lastSelectedHeroine != null && lastSelectedHeroine != h)
            TutorialManager.Fire(TutorialTrigger.HeroineSwitched, h);
        lastSelectedHeroine = h;
        Log($"{h.DisplayName}: {h.actionsRemaining} Action(s), {h.movementRemaining} Move. " +
            $"Pools A{h.GetPool(Discipline.Arcana)} W{h.GetPool(Discipline.Warding)} " +
            $"L{h.GetPool(Discipline.Lore)} O{h.GetPool(Discipline.Occultism)}.");
        RefreshUI();

        // Show her move range straight away; clicking a blue tile moves her, an action button cancels it.
        if (autoShowMoveOnSelect && h.HasMovementLeft) Move();
    }

    public void CancelAction()
    {
        if (mode != InputMode.Idle) Log("Cancelled.");
        ClearInputMode();
    }

    public void FinishHeroine()
    {
        if (IsBusy || Phase != CombatPhase.PlayerPhase) return;
        ClearInputMode();
        turnManager.FinishActivation();
        RefreshUI();
    }

    private void ClearInputMode()
    {
        foreach (var u in validTargets)
            if (u is MonsterHolder m && m != null) m.RemoveHighlight();
        validTargets.Clear();
        reachableTiles.Clear();
        onTargetChosen = null;
        mode = InputMode.Idle;
        if (grid != null) grid.ClearHighlights();
    }

    private Heroine RequireActiveHeroine()
    {
        if (IsBusy || Phase != CombatPhase.PlayerPhase) return null;
        var h = turnManager.ActiveHeroine;
        if (h == null) Log("Select a heroine first.");
        return h;
    }

    /// <summary>
    /// Spends the Action, turns the heroine toward faceTarget, plays the cast, throws her pool
    /// and applies the effect once the dice land. Input is locked throughout; a click skips the dice.
    /// </summary>
    private void RollThen(Heroine h, Discipline discipline, GridUnit faceTarget, Action<DiceRollResult> apply)
    {
        h.SpendAction(discipline);
        rolling = true;
        RefreshUI();
        StartCoroutine(CastRoutine(h, discipline, faceTarget, apply));
    }

    private IEnumerator CastRoutine(Heroine h, Discipline discipline, GridUnit faceTarget, Action<DiceRollResult> apply)
    {
        int id = encounterId;
        yield return h.TurnToFace(faceTarget);
        h.PlayCast(discipline, faceTarget);
        int threshold = h.ThresholdOffset(discipline);
        if (threshold != 0) Log($"Omen: {h.DisplayName}'s {discipline} Successes need {DiceRules.SuccessMin + threshold}+ this roll.");

        dice.Roll(discipline, h.GetPool(discipline), result =>
        {
            if (id != encounterId) return; // encounter was restarted mid-roll
            rolling = false;
            apply(result);
            AfterPlayerAction();
        }, threshold);
    }

    private bool CanUseDiscipline(Heroine h, Discipline d)
    {
        if (!h.CanAct) { Log($"{h.DisplayName} has no Actions left."); return false; }
        if (h.HasUsed(d)) { Log($"{h.DisplayName} already used {d} this turn."); return false; }
        return true;
    }

    private IEnumerable<GridUnit> AllUnits()
    {
        foreach (var m in monsters) if (m != null) yield return m;
        foreach (var h in turnManager.party) if (h != null) yield return h;
        yield return anchor;
        foreach (var u in ObjectiveManager.Units) yield return u;
    }

    private bool BeginTargeting(string label, Predicate<GridUnit> isValid, Action<GridUnit> onChosen, Color color)
    {
        ClearInputMode();
        validTargets.AddRange(AllUnits().Where(u => isValid(u)));
        if (validTargets.Count == 0)
        {
            Log($"{label}: no valid targets in range.");
            return false;
        }

        mode = InputMode.Targeting;
        onTargetChosen = onChosen;
        grid.ShowHighlights(validTargets.Select(u => u.GridPosition), color);
        foreach (var u in validTargets)
            if (u is MonsterHolder m) m.Highlight(color == damageColor);
        Log($"{label}: choose a target (right-click to cancel).");
        return true;
    }

    private void AfterPlayerAction()
    {
        RefreshIntents();
        RefreshUI();
        if (CheckEncounterEnd()) return;

        var h = turnManager.ActiveHeroine;
        if (h != null && !h.HasAnythingLeft)
            turnManager.FinishActivation();                 // nothing left: deselect her
        else if (h != null && autoShowMoveOnSelect && h.HasMovementLeft && mode == InputMode.Idle)
            Move();                                         // show the Move she still has
        RefreshUI();
    }

    // =====================================================================
    // Move (free; up to moveRange tiles per round, split over as many moves as the player likes)
    // =====================================================================

    public void Move()
    {
        var h = RequireActiveHeroine();
        if (h == null) return;
        if (!h.HasMovementLeft) { Log($"{h.DisplayName} has no Move left this round."); return; }

        ClearInputMode();
        foreach (var t in grid.Flood(h.GridPosition, h.movementRemaining, grid.IsFree).Keys)
            if (t != h.GridPosition) reachableTiles.Add(t);

        if (reachableTiles.Count == 0) { Log($"{h.DisplayName} has nowhere to move."); return; }
        mode = InputMode.Moving;
        grid.ShowHighlights(reachableTiles, moveColor);
        Log($"Move {h.DisplayName}: click a highlighted tile.");
    }

    private void TryMove(Vector2Int tile)
    {
        var h = turnManager.ActiveHeroine;
        if (h == null || !reachableTiles.Contains(tile))
        {
            Log($"Tile {tile} is out of reach (she is on {h?.GridPosition}, {h?.movementRemaining} Move left).");
            return;
        }

        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        grid.Flood(h.GridPosition, h.movementRemaining, grid.IsFree, cameFrom);
        var path = GridManager.BuildPath(cameFrom, h.GridPosition, tile);

        h.movementRemaining -= path.Count; // shortest path, so cost = number of tiles walked
        ClearInputMode();
        StartCoroutine(MoveHeroine(h, path));
    }

    private IEnumerator MoveHeroine(Heroine h, List<Vector2Int> path)
    {
        busy = true;
        yield return h.WalkPath(path);
        if (ObjectiveManager.Instance != null) ObjectiveManager.Instance.HeroineMoved(h, path);
        busy = false;
        AfterPlayerAction();
    }

    // =====================================================================
    // Arcane Strike - Arcana (GDD 6.5, Resolve reset 5.7)
    // =====================================================================

    public void Arcana()
    {
        var h = RequireActiveHeroine();
        if (h == null || !CanUseDiscipline(h, Discipline.Arcana)) return;

        bool started = BeginTargeting("Arcane Strike",
            u => u is MonsterHolder m && h.InRange(Discipline.Arcana, m.GridPosition),
            u => ArcaneStrike(h, (MonsterHolder)u),
            damageColor);
        if (!started) return;

        int pool = h.GetPool(Discipline.Arcana);
        foreach (MonsterHolder m in validTargets)
        {
            int need = Mathf.Max(1, m.CurrentResolve - (m.isMarked ? 1 : 0));
            Log($"  {m.DisplayName}: Resolve {m.CurrentResolve}{(m.isMarked ? " (marked)" : "")}, " +
                $"kill chance {dice.ChanceToReach(pool, need, 1, h.ThresholdOffset(Discipline.Arcana)):P0} with {pool}d8.");
        }
    }

    private void ArcaneStrike(Heroine h, MonsterHolder m)
    {
        RollThen(h, Discipline.Arcana, m, roll => ResolveArcaneStrike(h, m, roll));
    }

    private void ResolveArcaneStrike(Heroine h, MonsterHolder m, DiceRollResult roll)
    {
        if (m == null) return;
        int damage = roll.Successes;
        int need = m.CurrentResolve;

        if (damage > 0 && m.isMarked)
        {
            damage++;
            m.isMarked = false;
        }

        // Crit: +1 damage or push 1 tile. Picked automatically: damage if it turns a miss into a kill.
        bool push = false;
        if (roll.IsOvercharged)
        {
            damage++;
            push = true;
        }
        else if (roll.HasCritical)
        {
            if (damage < need && damage + 1 >= need) damage++;
            else push = true;
        }

        if (damage >= need)
        {
            Log($"{h.DisplayName} deals {damage} to {m.DisplayName} (Resolve {need}): defeated!");
            KillMonster(m);
            return;
        }

        Log($"{h.DisplayName} deals {damage} to {m.DisplayName}, short of Resolve {need}: the damage is lost.");
        if (push) Push(m, h.GridPosition, 1);
    }

    private void KillMonster(MonsterHolder m)
    {
        monsters.Remove(m);
        intents.RemoveAll(i => i.monster == m);
        m.Die();
    }

    /// <summary>Forced movement away from a source tile. Stops at the first blocked tile.</summary>
    private void Push(MonsterHolder m, Vector2Int from, int tiles)
    {
        Vector2Int delta = m.GridPosition - from;
        Vector2Int dir = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
            ? new Vector2Int(Math.Sign(delta.x), 0)
            : new Vector2Int(0, Math.Sign(delta.y));
        if (dir == Vector2Int.zero) return;

        int moved = 0;
        for (int i = 0; i < tiles; i++)
        {
            var next = m.GridPosition + dir;
            if (!grid.IsFree(next)) break;
            m.PlaceAt(next);
            moved++;
        }

        Log(moved > 0 ? $"{m.DisplayName} is pushed {moved} tile(s)." : $"{m.DisplayName} cannot be pushed (blocked).");
        if (moved > 0) ReplanIntent(m);
    }

    // =====================================================================
    // Guard / Stabilize - Warding (GDD 5.5, 5.8, 6.5)
    // =====================================================================

    /// <summary>Guard for Shields: self, an ally or the Anchor within Warding range.</summary>
    /// <summary>
    /// One button for all Warding-side actions; the clicked target decides which one happens:
    ///   ally or herself         -> Guard (Shields)
    ///   Ritual Anchor           -> Stabilize (Shift + click: Guard the Anchor with Shields)
    ///   Downed adjacent ally    -> Assist (no discipline, no roll; works even after Warding was used)
    /// </summary>
    public void Warding()
    {
        var h = RequireActiveHeroine();
        if (h == null) return;
        if (!h.CanAct) { Log($"{h.DisplayName} has no Actions left."); return; }

        bool canWard = !h.HasUsed(Discipline.Warding);

        bool started = BeginTargeting("Warding",
            u => (canWard && u is Heroine ally && !ally.IsDowned && h.InRange(Discipline.Warding, ally.GridPosition))
                 || (canWard && u == anchor && h.InRange(Discipline.Warding, anchor.GridPosition))
                 || (canWard && u is Civilian civ && !civ.IsLost && h.InRange(Discipline.Warding, civ.GridPosition))
                 || CanAssist(h, u),
            u =>
            {
                if (u is Heroine ally && ally.IsDowned) DoAssist(h, ally);
                else if (u == anchor) Guard(h, anchor, stabilize: !ShiftHeld() && !h.cannotStabilize);
                else Guard(h, u, false);
            },
            supportColor);

        if (started)
            Log(canWard
                ? "Click an ally (or herself) to Guard, the Anchor to Stabilize (Shift + click: Shields), " +
                  "or a Downed ally next to her to Assist."
                : $"{h.DisplayName} already used Warding this turn; only Assist is available.");
    }

    private static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    private static bool CanAssist(Heroine h, GridUnit u) =>
        u is Heroine ally && ally != h && ally.IsDowned && !ally.hasBeenRevived
        && GridManager.Distance(h.GridPosition, ally.GridPosition) == 1;

    private void DoAssist(Heroine h, Heroine ally)
    {
        h.SpendAction(null);
        ally.Revive();
        Log($"{h.DisplayName} helps {ally.DisplayName} back up (Resolve 1).");
        AfterPlayerAction();
    }

    /// <summary>Guard with Shields on herself, an ally or a civilian (not the Anchor).</summary>
    public void GuardAlly()
    {
        var h = RequireActiveHeroine();
        if (h == null || !CanUseDiscipline(h, Discipline.Warding)) return;

        if (h.cannotStabilize) { Log($"An Omen stops {h.DisplayName} from Stabilizing; she can still Shield the Anchor."); return; }

        BeginTargeting("Guard",
            u => (u is Heroine ally && !ally.IsDowned && h.InRange(Discipline.Warding, ally.GridPosition))
                 || (u is Civilian civ && !civ.IsLost && h.InRange(Discipline.Warding, civ.GridPosition)),
            u => Guard(h, u, false),
            supportColor);
    }

    /// <summary>Guard on the Anchor, choosing Shields.</summary>
    public void ShieldAnchor()
    {
        var h = RequireActiveHeroine();
        if (h == null || !CanUseDiscipline(h, Discipline.Warding)) return;
        if (!h.InRange(Discipline.Warding, anchor.GridPosition)) { Log($"The Anchor is out of {h.DisplayName}'s Warding range."); return; }

        ClearInputMode();
        Guard(h, anchor, false);
    }

    /// <summary>Guard on the Anchor, choosing Stabilize: +1 Stability per Success.</summary>
    public void Stabilize()
    {
        var h = RequireActiveHeroine();
        if (h == null || !CanUseDiscipline(h, Discipline.Warding)) return;
        if (!h.InRange(Discipline.Warding, anchor.GridPosition)) { Log($"The Anchor is out of {h.DisplayName}'s Warding range."); return; }

        ClearInputMode();
        Guard(h, anchor, true);
    }

    private void Guard(Heroine h, GridUnit target, bool stabilize)
    {
        RollThen(h, Discipline.Warding, target, roll => ResolveGuard(h, target, stabilize, roll));
    }

    private void ResolveGuard(Heroine h, GridUnit target, bool stabilize, DiceRollResult roll)
    {
        if (stabilize)
        {
            int gained = ritual.Stabilize(roll.Successes);
            Log($"{h.DisplayName} Stabilizes the ritual: +{gained} (Stability {ritual.stability}, {ritual.StabilityState}).");
        }
        else if (target is Heroine ally)
        {
            ally.shields += roll.Successes;
            Log($"{h.DisplayName} guards {ally.DisplayName}: +{roll.Successes} Shield(s).");
        }
        else if (target is Civilian civilian)
        {
            civilian.shields += roll.Successes;
            Log($"{h.DisplayName} shields {civilian.DisplayName}: +{roll.Successes} Shield(s).");
        }
        else
        {
            anchor.shields += roll.Successes;
            Log($"{h.DisplayName} guards the Anchor: +{roll.Successes} Shield(s).");
        }

        if (roll.HasCritical && ritual.TryRestoreCapacity())
            Log($"Critical: 1 Turn Capacity restored ({ritual.turnCapacity}).");

        if (roll.IsOvercharged)
        {
            foreach (var ally in turnManager.InCombat)
            {
                if (GridManager.Distance(ally.GridPosition, h.GridPosition) != 1) continue;
                ally.shields++;
                Log($"Overcharge: {ally.DisplayName} gains 1 Shield.");
            }
        }
    }

    // =====================================================================
    // Channel - Occultism (GDD 6.5, limited by Stability 5.8)
    // =====================================================================

    /// <summary>Occultism button: seals a summoning circle if she stands next to one, otherwise Channels.</summary>
    public void Occultism()
    {
        var h = RequireActiveHeroine();
        if (h == null) return;
        if (ObjectiveManager.FindSealableCircle(h.GridPosition) != null) SealCircle();
        else ChannelAnchor();
    }

    /// <summary>Channel into the Anchor: +1 Progress per Success, limited by Stability.</summary>
    public void ChannelAnchor()
    {
        var h = RequireActiveHeroine();
        if (h == null || !CanUseDiscipline(h, Discipline.Occultism)) return;
        if (!anchor.IsAdjacent(h.GridPosition))
        {
            Log($"{h.DisplayName} must stand next to the Anchor to Channel.");
            TutorialManager.Fire(TutorialTrigger.ChannelNotAdjacent, h);
            return;
        }

        ClearInputMode();
        RollThen(h, Discipline.Occultism, anchor, roll =>
        {
            int critBonus = Mathf.Max(0, ritual.ChannelCritBonus + h.channelCritModifier);
            int amount = roll.Successes + (roll.HasCritical ? critBonus : 0);
            int gained = ritual.AddProgress(amount);
            if (gained < amount && ritual.stability < ritual.MaxStability)
                TutorialManager.Fire(TutorialTrigger.ChannelLimitedByStability, h);
            Log($"{h.DisplayName} Channels: +{gained} Progress{LostToStability(amount, gained)} ({ritual.progress}/{ritual.progressTarget}).");
            if (roll.IsOvercharged) Log("Overcharge: no Sigils on this map yet, so nothing extra happens.");
        });
    }

    /// <summary>Objective: Occultism next to a summoning circle weakens its seal.</summary>
    public void SealCircle()
    {
        var h = RequireActiveHeroine();
        if (h == null || !CanUseDiscipline(h, Discipline.Occultism)) return;
        var circle = ObjectiveManager.FindSealableCircle(h.GridPosition);
        if (circle == null) { Log($"{h.DisplayName} must stand next to a summoning circle."); return; }

        ClearInputMode();
        RollThen(h, Discipline.Occultism, null, roll =>
            circle.AddSeal(h, roll.Successes + (roll.HasCritical ? ritual.ChannelCritBonus : 0)));
    }

    // =====================================================================
    // Study - Lore (GDD 6.5, Insight and Solving 6.6)
    // =====================================================================

    public void Lore()
    {
        var h = RequireActiveHeroine();
        if (h == null || !CanUseDiscipline(h, Discipline.Lore)) return;

        BeginTargeting("Study",
            u => (u is MonsterHolder m && h.InRange(Discipline.Lore, m.GridPosition))
                 || (u is RestlessSpirit s && h.InRange(Discipline.Lore, s.GridPosition)),
            u =>
            {
                if (u is RestlessSpirit spirit) StudySpirit(h, spirit);
                else Study(h, (MonsterHolder)u);
            },
            supportColor);
    }

    private void StudySpirit(Heroine h, RestlessSpirit spirit)
    {
        RollThen(h, Discipline.Lore, spirit, roll =>
        {
            if (spirit != null && spirit.Owner is RestlessSpiritObjective o)
                o.AddInsight(h, roll.Successes + (roll.HasCritical ? 1 : 0));
        });
    }

    private void Study(Heroine h, MonsterHolder m)
    {
        var intent = intents.FirstOrDefault(i => i.monster == m);
        Log($"Study reveals: {EnemyPlanner.Describe(intent)}");
        RollThen(h, Discipline.Lore, m, roll => ResolveStudy(m, intent, roll));
    }

    private void ResolveStudy(MonsterHolder m, EnemyIntent intent, DiceRollResult roll)
    {
        if (m == null) return;
        if (!m.IsSolved) AddInsight(m.Data, roll.Successes);

        if (roll.HasCritical)
        {
            m.isMarked = true;
            Log($"Critical: {m.DisplayName} is marked (+1 to the next damage against it).");
        }
        if (roll.IsOvercharged && intent != null)
        {
            intent.delayed = true;
            Log($"Overcharge: {m.DisplayName} will act last this enemy phase.");
        }
    }

    private void AddInsight(Monster type, int amount)
    {
        insight.TryGetValue(type, out int current);
        current += amount;
        insight[type] = current;

        if (current >= type.loreToSolve)
        {
            solvedTypes.Add(type);
            Log($"{type.monsterName} is Solved! Its Resolve drops to {type.solvedResistance}.");
        }
        else
        {
            Log($"{type.monsterName}: Insight {current}/{type.loreToSolve}.");
        }
    }

    /// <summary>Gives Insight to every enemy type in this encounter (objective rewards).</summary>
    public void GrantInsightToAllTypes(int amount)
    {
        var types = spawners.Where(s => s != null).SelectMany(s => s.monsterPrefabs)
            .Where(p => p != null).Select(p => p.Data)
            .Concat(monsters.Where(m => m != null).Select(m => m.Data))
            .Where(t => t != null && !IsSolved(t)).Distinct().ToList();
        foreach (var t in types) AddInsight(t, amount);
    }

    /// <summary>Writes a line to the combat log (used by objectives).</summary>
    public void Announce(string message) => Log(message);

    public void AddMonster(MonsterHolder m)
    {
        if (m == null || monsters.Contains(m)) return;
        monsters.Add(m);
        Log($"{m.DisplayName} appears at {m.GridPosition}.");
    }

    /// <summary>Redraws intents, sliders and texts (used by objectives after changing the board).</summary>
    public void RefreshDisplay() => RefreshAll();

    public bool IsSolved(Monster type) => type != null && solvedTypes.Contains(type);
    public int GetInsight(Monster type) => type != null && insight.TryGetValue(type, out int v) ? v : 0;

    // =====================================================================
    // Assist - revive a Downed adjacent ally (GDD 5.6), no discipline
    // =====================================================================

    public void Assist()
    {
        var h = RequireActiveHeroine();
        if (h == null) return;
        if (!h.CanAct) { Log($"{h.DisplayName} has no Actions left."); return; }

        BeginTargeting("Assist", u => CanAssist(h, u), u => DoAssist(h, (Heroine)u), supportColor);
    }

    // =====================================================================
    // Spell books: one entry point per action, and "can she use it right now?"
    // =====================================================================

    public bool IsChoosingTarget => mode == InputMode.Targeting;
    public bool IsRollingDice => rolling;
    public Heroine ActiveHeroine => turnManager != null ? turnManager.ActiveHeroine : null;

    /// <summary>True if the active heroine can start this action now; otherwise the reason why not.</summary>
    public bool CanStartSpell(SpellAction action, out string reason)
    {
        reason = null;
        if (Phase != CombatPhase.PlayerPhase) { reason = "Wait for the enemies to finish."; return false; }
        if (IsBusy) { reason = "Wait for the current action."; return false; }
        var h = ActiveHeroine;
        if (h == null) { reason = "Select a heroine first."; return false; }
        if (!h.CanAct) { reason = $"{h.DisplayName} has no Actions left."; return false; }
        var d = SpellActions.DisciplineOf(action);
        if (d.HasValue && h.HasUsed(d.Value)) { reason = $"{h.DisplayName} already used {d.Value} this turn."; return false; }

        switch (action)
        {
            case SpellAction.ArcaneStrike:
                if (!monsters.Any(m => m != null && h.InRange(Discipline.Arcana, m.GridPosition)))
                { reason = "No enemy within range."; return false; }
                break;
            case SpellAction.Study:
                if (!monsters.Any(m => m != null && h.InRange(Discipline.Lore, m.GridPosition))
                    && !ObjectiveManager.Units.OfType<RestlessSpirit>().Any(s => h.InRange(Discipline.Lore, s.GridPosition)))
                { reason = "Nothing to Study within range."; return false; }
                break;
            case SpellAction.Stabilize:
            case SpellAction.ShieldAnchor:
                if (action == SpellAction.Stabilize && h.cannotStabilize) { reason = "An Omen stops her from Stabilizing."; return false; }
                if (!h.InRange(Discipline.Warding, anchor.GridPosition)) { reason = "The Anchor is out of range."; return false; }
                break;
            case SpellAction.Assist:
                if (!turnManager.party.Any(a => CanAssist(h, a))) { reason = "No Downed ally next to her."; return false; }
                break;
            case SpellAction.Channel:
                if (!anchor.IsAdjacent(h.GridPosition)) { reason = "She must stand next to the Anchor."; return false; }
                break;
            case SpellAction.SealCircle:
                if (ObjectiveManager.FindSealableCircle(h.GridPosition) == null)
                { reason = "She must stand next to a summoning circle."; return false; }
                break;
        }
        return true;
    }

    /// <summary>Starts an action chosen in a spell book. If it can't be used, logs why (and shows tutorials).</summary>
    public void StartSpell(SpellAction action)
    {
        if (!CanStartSpell(action, out string reason))
        {
            Log(reason);
            if (action == SpellAction.Channel && ActiveHeroine != null && Phase == CombatPhase.PlayerPhase)
                TutorialManager.Fire(TutorialTrigger.ChannelNotAdjacent, ActiveHeroine);
            return;
        }

        switch (action)
        {
            case SpellAction.ArcaneStrike: Arcana(); break;
            case SpellAction.Guard: GuardAlly(); break;
            case SpellAction.Stabilize: Stabilize(); break;
            case SpellAction.ShieldAnchor: ShieldAnchor(); break;
            case SpellAction.Assist: Assist(); break;
            case SpellAction.Study: Lore(); break;
            case SpellAction.Channel: ChannelAnchor(); break;
            case SpellAction.SealCircle: SealCircle(); break;
        }
    }

    // =====================================================================
    // UI and logging
    // =====================================================================

    private void RefreshAll()
    {
        RefreshIntents();
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (sliderManager != null)
        {
            sliderManager.UpdateTurnSlider(turnManager.Round, ritual.turnCapacity);
            sliderManager.UpdateRitualProgressSlider(ritual.progress, ritual.progressTarget);
            sliderManager.UpdateStabilitySlider(ritual.stability, ritual.MaxStability);
        }
        if (ui != null)
        {
            ui.UpdateRitual(ritual, turnManager.Round, turnManager.Phase);
            ui.UpdateHeroine(turnManager.ActiveHeroine);
        }
    }

    private string DescribeMonster(MonsterHolder m)
    {
        var intent = intents.FirstOrDefault(i => i.monster == m);
        string solved = m.IsSolved ? "Solved" : $"Insight {GetInsight(m.Data)}/{m.Data.loreToSolve}";
        return $"{m.DisplayName}: Resolve {m.CurrentResolve}, {solved}{(m.isMarked ? ", marked" : "")}. " +
               EnemyPlanner.Describe(intent);
    }

    private static string LostToStability(int attempted, int gained) =>
        gained < attempted ? $" ({attempted - gained} lost: Stability limit)" : "";

    private void Log(string message)
    {
        Debug.Log(message);
        if (ui != null) ui.Log(message);
    }
}
