using UnityEngine;

public enum RelicRarity { Common, Rare, Legendary }

/// <summary>
/// A Relic: a team-wide buff the girls find (Elite Guardian caches, Moon Shrines, mission rewards).
/// While the team has it, its Effects are added to RunManager.Effects for every encounter.
/// Most Relics are only numbers. For a rule numbers can't express, make a subclass and override a hook.
/// Create one with Create > Scriptable Objects > Relic.
/// </summary>
[CreateAssetMenu(fileName = "Relic", menuName = "Scriptable Objects/Relic")]
public class Relic : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Relic";
    public Sprite icon;
    [Tooltip("Rules text. Empty = written from the Effects.")]
    [TextArea(2, 4)] public string description;
    [TextArea(1, 3)] public string flavour;
    public RelicRarity rarity = RelicRarity.Common;
    [Tooltip("Off: never offered at random (story Relics, test Relics).")]
    public bool inRewardPool = true;

    [Header("Rules")]
    public RunEffects effects = new();

    /// <summary>Rules text for tooltips.</summary>
    public string TooltipText()
    {
        string rules = string.IsNullOrWhiteSpace(description) ? effects.Describe() : description;
        return string.IsNullOrWhiteSpace(flavour) ? rules : $"{rules}\n<i>{flavour}</i>";
    }

    // ---------- Hooks for special rules (override in a subclass) ----------

    /// <summary>After the ritual and party are set up, before round 0.</summary>
    public virtual void OnEncounterStart(RitualManager ritualManager) { }

    /// <summary>At the start of every round, after spawning, before the player phase.</summary>
    public virtual void OnRoundStart(RitualManager ritualManager, int round) { }

    /// <summary>When the encounter is won or lost.</summary>
    public virtual void OnEncounterEnd(RitualManager ritualManager, bool won) { }
}
