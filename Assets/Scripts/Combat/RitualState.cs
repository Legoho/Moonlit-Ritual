using System;
using UnityEngine;

/// <summary>
/// Ritual numbers for one encounter: Progress (5.3), Turn Capacity (5.4-5.5) and Stability (5.8).
/// Shown in the RitualManager inspector.
///
/// The Settings are the base values. Relics and Omens are added on top through the Effective
/// properties (StartingTurnCapacity, MaxStability, ...), so the base values never change and
/// restarting an encounter can't stack a bonus twice.
/// </summary>
[Serializable]

public class RitualState
{
    [Header("Settings (base values, before Relics and Omens)")]
    public int progressTarget = 48;
    public int startingTurnCapacity = 12;
    public int startingStability = 3;
    public int maxStability = 8;
    public int stabilityDecayPerRound = 1;
    public int stabilityLossPerAnchorHit = 2;
    public int anchorPassiveProgress = 2;
    public int channelCritBonus = 2;
    public int capacityRestoresPerRound = 1;

    [Header("Runtime")]
    public int progress;
    public int turnCapacity;
    public int stability;
    public int progressGainedThisRound;
    public int capacityRestoredThisRound;

    [NonSerialized] private RunEffects fx = RunEffects.None;

    // ---------- Effective values (base + Relics and Omens) ----------

    public int StartingTurnCapacity => Mathf.Max(1, startingTurnCapacity + fx.turnCapacity);
    public int StartingStability => Mathf.Clamp(startingStability + fx.startingStability, 0, MaxStability);
    public int MaxStability => Mathf.Max(1, maxStability + fx.maxStability);
    public int AnchorPassiveProgress => Mathf.Max(0, anchorPassiveProgress + fx.anchorPassiveProgress);
    public int ChannelCritBonus => Mathf.Max(0, channelCritBonus + fx.channelCritBonus);
    public int CapacityRestoresPerRound => Mathf.Max(0, capacityRestoresPerRound + fx.capacityRestoresPerRound);

    public bool IsComplete => progress >= progressTarget;
    public int AbsorbableThisRound => Mathf.Max(0, stability - progressGainedThisRound);

    public string StabilityState =>
        stability <= 4 ? "Unstable" : stability <= 6 ? "Steady" : "Harmonized";

    /// <summary>Starts the encounter. Pass RunManager.Effects to apply Relics and Omens.</summary>
    public void Reset(RunEffects effects = null)
    {
        fx = effects ?? RunEffects.None;
        progress = 0;
        turnCapacity = StartingTurnCapacity;
        stability = StartingStability;
        BeginRound();
    }

    public void BeginRound()
    {
        progressGainedThisRound = 0;
        capacityRestoredThisRound = 0;
    }

    /// <summary>Adds Progress up to what Stability can still absorb this round. Returns the amount kept.</summary>
    public int AddProgress(int amount)
    {
        if (amount <= 0) return 0;
        int gained = Mathf.Min(amount, AbsorbableThisRound);
        progress += gained;
        progressGainedThisRound += gained;
        return gained;
    }

    /// <summary>Returns the Stability actually gained (capped at MaxStability).</summary>
    public int Stabilize(int amount)
    {
        int before = stability;
        stability = Mathf.Min(MaxStability, stability + Mathf.Max(0, amount));
        return stability - before;
    }

    public bool TryRestoreCapacity()
    {
        if (capacityRestoredThisRound >= CapacityRestoresPerRound) return false;
        if (turnCapacity >= StartingTurnCapacity) return false;
        turnCapacity++;
        capacityRestoredThisRound++;
        return true;
    }

    public void LoseCapacity(int amount) => turnCapacity = Mathf.Max(0, turnCapacity - amount);

    /// <summary>Unblocked Anchor hit: can drop below the normal floor, down to 0.</summary>
    public void Disrupt() => stability = Mathf.Max(0, stability - stabilityLossPerAnchorHit);

    /// <summary>End-of-round Strain: -1, but never below the starting value (and never raises it).</summary>
    public void ApplyStrain()
    {
        if (stability > StartingStability)
            stability = Mathf.Max(StartingStability, stability - stabilityDecayPerRound);
    }
}