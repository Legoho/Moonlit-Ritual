using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Team-wide number tweaks from Relics and Shared Omens. Positive = more, negative = less.
/// RunManager adds them all together into one RunEffects (RunManager.Effects), which combat reads.
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
    [Tooltip("Reinforcements start this many rounds earlier (still telegraphed). E.g. the Omen Restless Dead.")]
    public int reinforcementsEarlier;

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
        reinforcementsEarlier += o.reinforcementsEarlier;
    }

    public void CopyFrom(RunEffects o)
    {
        var fresh = new RunEffects();
        fresh.Add(o);
        turnCapacity = fresh.turnCapacity;
        startingStability = fresh.startingStability;
        maxStability = fresh.maxStability;
        anchorPassiveProgress = fresh.anchorPassiveProgress;
        channelCritBonus = fresh.channelCritBonus;
        capacityRestoresPerRound = fresh.capacityRestoresPerRound;
        maxResolve = fresh.maxResolve;
        moveRange = fresh.moveRange;
        abilityRange = fresh.abilityRange;
        successThreshold = fresh.successThreshold;
        emptyRollRerolls = fresh.emptyRollRerolls;
        enemyMovement = fresh.enemyMovement;
        extraSpawnsPerRound = fresh.extraSpawnsPerRound;
        reinforcementsEarlier = fresh.reinforcementsEarlier;
    }

    /// <summary>Plain-language list of the non-zero numbers, for tooltips (e.g. "+1 maximum Stability").</summary>
    public string Describe()
    {
        var parts = new List<string>();
        void Put(float v, string what) { if (v != 0) parts.Add($"{(v > 0 ? "+" : "")}{v:0.#} {what}"); }
        Put(turnCapacity, "starting Turn Capacity");
        Put(startingStability, "starting Stability");
        Put(maxStability, "maximum Stability");
        Put(anchorPassiveProgress, "Progress from the Anchor each round");
        Put(channelCritBonus, "Progress on a Channel Critical");
        Put(capacityRestoresPerRound, "Turn Capacity restore per round");
        Put(maxResolve, "Resolve for every heroine");
        Put(moveRange, "Move for every heroine");
        Put(abilityRange, "Arcana, Warding and Lore range");
        if (successThreshold != 0) parts.Add(successThreshold > 0 ? $"Successes need {6 + successThreshold}+" : $"Successes on {6 + successThreshold}+");
        Put(emptyRollRerolls, "reroll of a roll with 0 Successes per encounter");
        Put(enemyMovement, "enemy movement");
        Put(extraSpawnsPerRound, "enemy per spawner each round");
        if (reinforcementsEarlier > 0) parts.Add($"reinforcements arrive {reinforcementsEarlier} round(s) earlier");
        return string.Join(", ", parts);
    }
}
