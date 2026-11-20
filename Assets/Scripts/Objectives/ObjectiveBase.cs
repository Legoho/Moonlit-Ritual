using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ObjectiveState { Inactive, Active, Completed, Failed }

/// <summary>
/// The immediate in-encounter reward for completing an objective (GDD 9.4). All fields are optional.
/// Moonlight (the research currency) is set separately on the objective.
/// </summary>
[Serializable]
public class ObjectiveBonus
{
    [Tooltip("Restore lost Turn Capacity (never above the starting value).")]
    public int restoreTurnCapacity;
    [Tooltip("+Stability (capped at max).")]
    public int stability;
    [Tooltip("Heal every standing heroine by this much Resolve.")]
    public int healResolve;
    [Tooltip("Give every standing heroine this many Shields.")]
    public int shieldsForParty;
    [Tooltip("+Insight towards Solving for every enemy type in this encounter.")]
    public int insightAllEnemyTypes;
    [Tooltip("Random Relics the team finds (e.g. an Elite Guardian's relic cache).")]
    public int relics;

    public ObjectiveBonus Clone() => (ObjectiveBonus)MemberwiseClone();

    public string Describe()
    {
        var parts = new List<string>();
        if (restoreTurnCapacity > 0) parts.Add($"+{restoreTurnCapacity} Turn Capacity");
        if (stability > 0) parts.Add($"+{stability} Stability");
        if (healResolve > 0) parts.Add($"heal {healResolve} Resolve");
        if (shieldsForParty > 0) parts.Add($"+{shieldsForParty} Shield to the team");
        if (insightAllEnemyTypes > 0) parts.Add($"+{insightAllEnemyTypes} Insight on every enemy");
        if (relics > 0) parts.Add(relics == 1 ? "a Relic" : $"{relics} Relics");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// Base class for Optional Objectives (GDD 9.4). Put objective components on child objects of the
/// ObjectiveManager; it picks which ones run each encounter and calls the hooks below.
///
/// To make a new objective: inherit from this, place your things in Setup(), react in the hooks,
/// and call Complete() or Fail().
/// </summary>
public abstract class ObjectiveBase : MonoBehaviour
{
    [Header("Objective")]
    public string title = "Objective";
    [TextArea(2, 4)] public string description;
    [Tooltip("Moonlight (research currency) for completing it.")]
    public int moonlightReward = 2;
    public ObjectiveBonus bonus = new();
    [Tooltip("Optional data asset with the texts and rewards. When set, it replaces the four fields below.")]
    [SerializeField] protected ObjectiveDefinition definition;

    public ObjectiveState State { get; private set; } = ObjectiveState.Inactive;
    public ObjectiveDefinition Definition => definition;
    public Sprite Icon => definition != null ? definition.icon : null;
    public bool IsActive => State == ObjectiveState.Active;

    protected ObjectiveManager Manager { get; private set; }
    protected RitualManager Ritual { get; private set; }
    protected GridManager Grid => GridManager.Instance;

    /// <summary>One short line for the objective tracker, e.g. "2/3 civilians safe".</summary>
    public abstract string StatusLine { get; }

    /// <summary>Units this objective put on the board (civilians, spirits...). Used for targeting and clicks.</summary>
    public virtual IEnumerable<ObjectiveUnit> Units => Enumerable.Empty<ObjectiveUnit>();

    /// <summary>Units enemies may choose to attack.</summary>
    public virtual IEnumerable<GridUnit> EnemyTargets => Enumerable.Empty<GridUnit>();

    /// <summary>Return false if this map can't host the objective (e.g. no spawners).</summary>
    public virtual bool CanRun(ObjectiveManager manager) => true;

    public void Begin(ObjectiveManager manager, RitualManager ritual)
    {
        Manager = manager;
        Ritual = ritual;
        if (definition != null)
        {
            title = definition.title;
            description = definition.description;
            moonlightReward = definition.moonlightReward;
            bonus = definition.bonus != null ? definition.bonus.Clone() : new ObjectiveBonus();
        }
        State = ObjectiveState.Active;
        Setup();
    }

    public void End()
    {
        Cleanup();
        State = ObjectiveState.Inactive;
    }

    /// <summary>Place items, spawn units, mark spawners. Called once when the encounter starts.</summary>
    protected abstract void Setup();

    /// <summary>Remove anything Setup created (encounter restarted in place).</summary>
    protected virtual void Cleanup() { }

    // ---------- Hooks (called by ObjectiveManager) ----------
    public virtual void OnRoundStart(int round) { }
    public virtual void OnHeroineMoved(Heroine heroine, IReadOnlyList<Vector2Int> path) { }
    public virtual void OnEnemyPhaseEnd() { }
    public virtual void OnEncounterEnd(bool won) { }

    // ---------- Results ----------
    protected void Complete()
    {
        if (State != ObjectiveState.Active) return;
        State = ObjectiveState.Completed;
        Manager.ReportCompleted(this);
    }

    protected void Fail(string reason)
    {
        if (State != ObjectiveState.Active) return;
        State = ObjectiveState.Failed;
        Manager.ReportFailed(this, reason);
    }

    protected void Announce(string message) => Ritual.Announce(message);

    // ---------- Small helpers ----------
    /// <summary>Distance counting diagonal neighbours as 1 ("next to, including corners").</summary>
    protected static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
}
