using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Personal Omens weaken only their bearer; Shared Omens weaken any team she deploys with.</summary>
public enum OmenScope { Personal, Shared }

/// <summary>What a Personal Omen changes about its bearer. All zero = nothing.</summary>
[Serializable]
public class OmenRules
{
    [Header("Her numbers")]
    [Tooltip("E.g. Fractured Ward: -1 (she starts missions at 2 Resolve).")]
    public int maxResolve;
    [Tooltip("E.g. Heavy Step: -1 (Move 2).")]
    public int moveRange;
    public float arcanaRange;
    public float wardingRange;
    [Tooltip("E.g. Clouded Sight: -1 (Lore range 2).")]
    public float loreRange;

    [Header("Dice")]
    [Tooltip("E.g. Trembling Hand: +1 (Successes need 7+) in one discipline. Never more than one per heroine (GDD).")]
    public int successThreshold;
    [Tooltip("Which discipline the threshold change applies to.")]
    public Discipline thresholdDiscipline = Discipline.Arcana;
    [Tooltip("On: the discipline is picked at random when she receives the Omen, then stays the same.")]
    public bool randomThresholdDiscipline = true;

    [Header("Ritual work")]
    [Tooltip("E.g. Hollow Voice: -1 (her Channel Critical gives +1 instead of +2).")]
    public int channelCritBonus;
    [Tooltip("Dim Circle: she cannot Stabilize (her Guard on the Anchor only gives Shields).")]
    public bool cannotStabilize;
    [Tooltip("Bleeding Sigil: the ritual loses this much Progress when she is Downed.")]
    public int progressLostWhenDowned;

    public bool ChangesThreshold => successThreshold != 0;
}

/// <summary>
/// An Omen (GDD "Omens"): a lasting debuff a heroine receives when she ends a mission Downed.
/// Its family is the category of the enemy that Downed her. It stays until cleansed at the Chapel.
/// A Personal Omen changes her (Rules); a Shared Omen changes the whole team while she is deployed (Team Effects).
/// For a rule these fields can't express, make a subclass and override a hook.
/// Create one with Create > Scriptable Objects > Omen.
/// </summary>
[CreateAssetMenu(fileName = "Omen", menuName = "Scriptable Objects/Omen")]
public class Omen : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Omen";
    public Sprite icon;
    [Tooltip("Exact rules text, always shown on her portrait and the deployment screen (GDD).")]
    [TextArea(2, 4)] public string description;
    [TextArea(1, 3)] public string flavour;

    [Header("Kind")]
    [Tooltip("Assault (body and defence), Sabotage (ritual work), Control (movement and focus).")]
    public EnemyCategory family = EnemyCategory.Assault;
    public OmenScope scope = OmenScope.Personal;
    [Tooltip("Off: never drawn at random (story Omens, test Omens).")]
    public bool inDrawPool = true;

    [Header("Personal: what it changes about her")]
    public OmenRules rules = new();

    [Header("Shared: what it changes for the whole team while she is deployed")]
    public RunEffects teamEffects = new();

    public string TooltipText()
    {
        string head = $"{family} Omen, {scope}";
        string rulesText = string.IsNullOrWhiteSpace(description) ? teamEffects.Describe() : description;
        return string.IsNullOrWhiteSpace(flavour) ? $"{head}\n{rulesText}" : $"{head}\n{rulesText}\n<i>{flavour}</i>";
    }

    /// <summary>Applies the Personal rules to a spawned heroine.</summary>
    public virtual void ApplyTo(Heroine h, OmenInstance instance)
    {
        h.omens.Add(this);
        h.maxResolve = Mathf.Max(1, h.maxResolve + rules.maxResolve);
        h.resolve = h.maxResolve;
        h.moveRange = Mathf.Max(1, h.moveRange + rules.moveRange);
        h.arcanaRange = Mathf.Max(1f, h.arcanaRange + rules.arcanaRange);
        h.wardingRange = Mathf.Max(1f, h.wardingRange + rules.wardingRange);
        h.loreRange = Mathf.Max(1f, h.loreRange + rules.loreRange);
        if (rules.ChangesThreshold) h.AddThresholdOffset(instance.discipline, rules.successThreshold);
        h.channelCritModifier += rules.channelCritBonus;
        h.cannotStabilize |= rules.cannotStabilize;
        h.progressLostWhenDowned += rules.progressLostWhenDowned;
    }

    // ---------- Hooks for special rules (override in a subclass) ----------

    /// <summary>After the ritual and party are set up. 'bearer' is the heroine carrying it.</summary>
    public virtual void OnEncounterStart(RitualManager ritualManager, Heroine bearer) { }

    /// <summary>At the start of every round, after spawning, before the player phase.</summary>
    public virtual void OnRoundStart(RitualManager ritualManager, Heroine bearer, int round) { }

    /// <summary>When the encounter is won or lost.</summary>
    public virtual void OnEncounterEnd(RitualManager ritualManager, Heroine bearer, bool won) { }
}

/// <summary>One Omen a heroine carries, with the choice made when she received it.</summary>
[Serializable]
public class OmenInstance
{
    public Omen omen;
    [Tooltip("For Omens that change one discipline's threshold: which one.")]
    public Discipline discipline;
}

/// <summary>Campaign state for one heroine's Omens (lives in RunManager, never in the asset).</summary>
[Serializable]
public class HeroineOmens
{
    public HeroineDefinition heroine;
    public List<OmenInstance> omens = new();
    [Tooltip("Eclipsed: she would have received a third Omen. She cannot deploy until a Cleansing Rite removes all of them.")]
    public bool eclipsed;
}
