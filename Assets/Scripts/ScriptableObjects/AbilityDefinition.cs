using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Who or what the player clicks when casting an ability.</summary>
public enum AbilityTarget
{
    None,           // no click: cast at once (e.g. a song at the Anchor)
    Enemy,
    Ally,           // a heroine who is not Downed (see Include Self)
    AllyOrCivilian,
    AnyUnit,        // an enemy or an ally (e.g. a pull)
    Anchor,         // the Ritual Anchor, no click needed (must be in range)
}

/// <summary>Where the heroine must stand to cast it.</summary>
public enum AbilityRequirement
{
    None,
    AdjacentToAnchor,   // orthogonally next to the Ritual Anchor, like Channel
}

public enum AbilityEffectType
{
    Damage,                 // summed per enemy, then checked against its Resolve (reset rule, GDD 5.7)
    Push,                   // away from the caster, stops at the first blocked tile
    Pull,                   // toward the caster, stops at the first blocked tile
    Swap,                   // caster and target swap tiles
    Shield,                 // heroine, civilian or Anchor
    Stability,              // the ritual
    Progress,               // the ritual, limited by Stability
    Insight,                // to the enemy's type (once per type per effect)
    Mark,                   // +1 to the next damage against it
    Delay,                  // the enemy acts last this enemy phase
    RestoreTurnCapacity,    // +1 lost Turn Capacity (team limit per round still applies)
    ExtraMove,              // tiles of Move added to the unit
    Reveal,                 // logs the enemy's full intent (applied before the roll)
}

public enum EffectTrigger
{
    Always,         // whenever the roll has enough Successes
    OnCritical,     // at least one 8
    OnOvercharge,   // two or more 8s
}

public enum EffectScope
{
    Target,                     // the clicked unit (the Anchor for Anchor abilities, the caster if there is no target)
    Caster,
    AlliesAdjacentToCaster,     // other heroines orthogonally next to her
    EnemiesAdjacentToTarget,    // other enemies orthogonally next to the target
    NextEnemyInLine,            // the first enemy behind the target, on the line from the caster (within range)
    AllEnemiesInRange,          // every enemy within the ability's range of the caster
    AllAllies,                  // every heroine who is not Downed
}

/// <summary>
/// One effect of an ability. Amount = flat + perSuccess x Successes (capped by maxAmount).
/// Effects apply in list order, except Damage, which is added up per enemy and applied first,
/// so an enemy that is defeated is not pushed afterwards.
/// </summary>
[Serializable]
public class AbilityEffect
{
    public AbilityEffectType type;
    public EffectTrigger trigger = EffectTrigger.Always;
    public EffectScope scope = EffectScope.Target;
    [Tooltip("Added once if the effect applies.")]
    public int flat;
    [Tooltip("Added for every Success.")]
    public int perSuccess;
    [Tooltip("0 = no cap. E.g. a pull of 1 tile per Success, at most 2.")]
    public int maxAmount;
    [Tooltip("The effect needs at least this many Successes. 1 = any Success (a roll with 0 Successes never does anything).")]
    [Min(1)] public int minSuccesses = 1;

    public int Amount(int successes)
    {
        int a = flat + perSuccess * successes;
        return maxAmount > 0 ? Mathf.Min(a, maxAmount) : a;
    }

    public bool Applies(DiceRollResult roll)
    {
        if (roll.Successes < Mathf.Max(1, minSuccesses)) return false;
        return trigger switch
        {
            EffectTrigger.OnCritical => roll.HasCritical,
            EffectTrigger.OnOvercharge => roll.IsOvercharged,
            _ => true
        };
    }

    public AbilityEffect() { }

    public AbilityEffect(AbilityEffectType type, int flat, int perSuccess = 0, EffectScope scope = EffectScope.Target,
                         EffectTrigger trigger = EffectTrigger.Always, int maxAmount = 0, int minSuccesses = 1)
    {
        this.type = type;
        this.flat = flat;
        this.perSuccess = perSuccess;
        this.scope = scope;
        this.trigger = trigger;
        this.maxAmount = maxAmount;
        this.minSuccesses = minSuccesses;
    }
}

/// <summary>Where an ability comes from (for the Academy screens later).</summary>
public enum AbilitySource
{
    Signature,  // her own, known from the start or taught by the story
    Research,   // learned at the Archive (Moonlight + Nights)
    Technique,  // mission reward
    Story,      // granted by a story mission
}

/// <summary>
/// A learnable ability (spell) that appears in its discipline's spell book once a heroine knows it.
/// It counts as its discipline: one discipline per turn (GDD 5.2), so she can't Channel and use an
/// Occultism ability in the same turn. Create one with Create > Scriptable Objects > Ability.
/// </summary>
[CreateAssetMenu(fileName = "Ability", menuName = "Scriptable Objects/Ability")]
public class AbilityDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "New Ability";
    [Tooltip("Shown on the spell book's grid and in the tooltip.")]
    public Sprite icon;
    [TextArea(2, 5)] public string description;
    [Tooltip("Optional flavour line for the tooltip.")]
    [TextArea(1, 3)] public string flavour;

    [Header("Rules")]
    public Discipline discipline = Discipline.Arcana;
    public AbilityTarget target = AbilityTarget.Enemy;
    [Tooltip("Ally targets only: she can target herself.")]
    public bool includeSelf = true;
    [Tooltip("-1 = her range for this discipline (3 by default). Straight step 1, diagonal step 1.5.")]
    public float range = -1f;
    [Tooltip("The target must be in the same row or column as her (a line attack).")]
    public bool straightLineOnly;
    public AbilityRequirement requirement = AbilityRequirement.None;

    [Header("Effects (in order)")]
    public List<AbilityEffect> effects = new();

    [Header("Learning")]
    public AbilitySource source = AbilitySource.Research;
    [Tooltip("Heroines who can learn it. Empty = anyone.")]
    public List<HeroineDefinition> learnableBy = new();
    [Tooltip("Research cost in Moonlight (GDD 10.7). Not used yet.")]
    public int moonlightCost = 3;
    [Tooltip("Research time in Nights. Not used yet.")]
    public int researchNights = 2;

    public bool NeedsClick => target != AbilityTarget.None && target != AbilityTarget.Anchor;
    public bool HasEffect(AbilityEffectType t) => effects.Any(e => e.type == t);
    public bool CanBeLearnedBy(HeroineDefinition h) => learnableBy.Count == 0 || learnableBy.Contains(h);

    /// <summary>Range for this heroine: the ability's own, or her discipline range.</summary>
    public float RangeFor(Heroine h) => range >= 0f ? range : (h != null ? h.GetRange(discipline) : 3f);

    /// <summary>Damage the Critical adds (for the kill chance preview).</summary>
    public int CriticalDamageBonus => effects
        .Where(e => e.type == AbilityEffectType.Damage && e.trigger == EffectTrigger.OnCritical && e.scope == EffectScope.Target)
        .Sum(e => e.flat);

    /// <summary>Description for the tooltip. Written from the effects if the Description field is empty.</summary>
    public string TooltipText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrWhiteSpace(description) ? DescribeEffects() : description);
        if (!string.IsNullOrWhiteSpace(flavour)) sb.Append($"\n<i>{flavour}</i>");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Plain-language summary built from the effect list.</summary>
    public string DescribeEffects()
    {
        var lines = new List<string>();
        foreach (var group in effects.GroupBy(e => e.trigger))
        {
            string prefix = group.Key switch
            {
                EffectTrigger.OnCritical => "Critical: ",
                EffectTrigger.OnOvercharge => "Overcharge: ",
                _ => ""
            };
            lines.Add(prefix + string.Join(", ", group.Select(Describe)) + ".");
        }
        return string.Join("\n", lines);
    }

    private static string Describe(AbilityEffect e)
    {
        string amount = e.perSuccess > 0
            ? (e.flat > 0 ? $"{e.flat} + {e.perSuccess} per Success" : e.perSuccess == 1 ? "1 per Success" : $"{e.perSuccess} per Success")
            : e.flat.ToString();
        if (e.maxAmount > 0 && e.perSuccess > 0) amount += $" (max {e.maxAmount})";

        string who = e.scope switch
        {
            EffectScope.Caster => " to herself",
            EffectScope.AlliesAdjacentToCaster => " to each ally next to her",
            EffectScope.EnemiesAdjacentToTarget => " to each enemy next to the target",
            EffectScope.NextEnemyInLine => " to the next enemy in the line",
            EffectScope.AllEnemiesInRange => " to every enemy in range",
            EffectScope.AllAllies => " to every ally",
            _ => ""
        };
        string need = e.minSuccesses > 1 ? $" (needs {e.minSuccesses}+ Successes)" : "";

        string text = e.type switch
        {
            AbilityEffectType.Damage => $"{amount} damage{who}",
            AbilityEffectType.Push => $"push {amount} tile(s){who}",
            AbilityEffectType.Pull => $"pull {amount} tile(s) toward her{who}",
            AbilityEffectType.Swap => "swap places with the target",
            AbilityEffectType.Shield => $"{amount} Shield(s){who}",
            AbilityEffectType.Stability => $"+{amount} Stability",
            AbilityEffectType.Progress => $"+{amount} Ritual Progress",
            AbilityEffectType.Insight => $"+{amount} Insight{who}",
            AbilityEffectType.Mark => $"Mark{who}",
            AbilityEffectType.Delay => $"acts last this enemy phase{who}",
            AbilityEffectType.RestoreTurnCapacity => "restore 1 Turn Capacity",
            AbilityEffectType.ExtraMove => $"+{amount} Move{who}",
            AbilityEffectType.Reveal => $"reveal the full intent{who}",
            _ => e.type.ToString()
        };
        return text + need;
    }
}
