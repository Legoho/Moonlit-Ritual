using System;
using UnityEngine;

public enum ModifierKind { Relic, Omen }
public enum ModifierDuration { WholeRun, NextEncounter }

/// <summary>
/// A Relic (buff) or Omen (debuff) that changes the rules for the run or for the next encounter.
/// Most modifiers are just numbers in Effects. For a rule numbers can't express,
/// make a subclass and override one of the hooks.
/// </summary>

[CreateAssetMenu(fileName = "RunModifier", menuName = "Scriptable Objects/RunModifier")]
public class RunModifier : ScriptableObject
{
    public string displayName = "Modifier";
    [TextArea(2, 4)] public string description;
    public Sprite icon;
    public ModifierKind kind = ModifierKind.Relic;
    [Tooltip("WholeRun: stays until the run ends. NextEncounter: removed when the next encounter ends.")]
    public ModifierDuration duration = ModifierDuration.WholeRun;

    public RunEffects effects = new();

    // ---------- Hooks for special rules (override in a subclass) ----------

    /// <summary>After the ritual and party are set up, before round 0.</summary>
    public virtual void OnEncounterStart(RitualManager ritualManager) { }

    /// <summary>At the start of every round, after spawning, before the player phase.</summary>
    public virtual void OnRoundStart(RitualManager ritualManager, int round) { }

    /// <summary>When the encounter is won or lost.</summary>
    public virtual void OnEncounterEnd(RitualManager ritualManager, bool won) { }
}

/// <summary>
/// The number tweaks a modifier applies. Positive = more, negative = less.
/// RunManager adds every active modifier together into one RunEffects (RunManager.Effects).
/// </summary>
[Serializable]
public class RunEffects
{
    [Header("Ritual")]
    [Tooltip("Starting Turn Capacity (12 by default).")]
    public int turnCapacity;
    [Tooltip("Starting Stability, also the floor Strain stops at (3 by default).")]
    public int startingStability;
    [Tooltip("Maximum Stability (8 by default).")]
    public int maxStability;
    [Tooltip("Anchor passive Progress per round (2 by default).")]
    public int anchorPassiveProgress;
    [Tooltip("Extra Progress when Channel rolls a Critical (2 by default).")]
    public int channelCritBonus;
    [Tooltip("How many times per round a Warding Critical may restore Turn Capacity (1 by default).")]
    public int capacityRestoresPerRound;

    [Header("Heroines")]
    public int maxResolve;
    public int moveRange;
    [Tooltip("Added to Arcana, Warding and Lore range.")]
    public float abilityRange;

    [Header("Dice")]
    [Tooltip("+1 = Successes need 7+ instead of 6+. Clamped to 2-8. Criticals are always 8s.")]
    public int successThreshold;
    [Tooltip("Per encounter: a roll with 0 Successes is rerolled this many times.")]
    public int emptyRollRerolls;

    [Header("Enemies")]
    public int enemyMovement;
    [Tooltip("Extra monsters each spawner adds per round.")]
    public int extraSpawnsPerRound;

    public static readonly RunEffects None = new();

    public void Add(RunEffects o)
    {
        if (o == null) return;
        turnCapacity += o.turnCapacity;
        startingStability += o.startingStability;
        maxStability += o.maxStability;
        anchorPassiveProgress += o.anchorPassiveProgress;
        channelCritBonus += o.channelCritBonus;
        capacityRestoresPerRound += o.capacityRestoresPerRound;
        maxResolve += o.maxResolve;
        moveRange += o.moveRange;
        abilityRange += o.abilityRange;
        successThreshold += o.successThreshold;
        emptyRollRerolls += o.emptyRollRerolls;
        enemyMovement += o.enemyMovement;
        extraSpawnsPerRound += o.extraSpawnsPerRound;
    }
}
