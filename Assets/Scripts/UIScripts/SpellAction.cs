using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Every action a heroine can pick from a spell book. Add signature abilities here later.</summary>
public enum SpellAction
{
    ArcaneStrike,   // Arcana
    Guard,          // Warding: Shields on herself, an ally or a civilian
    Stabilize,      // Warding: +Stability on the Anchor
    ShieldAnchor,   // Warding: Shields on the Anchor
    Assist,         // no discipline: revive a Downed ally (listed in the Warding book)
    Study,          // Lore
    Channel,        // Occultism: Progress at the Anchor
    SealCircle,     // Occultism: seal a summoning circle (objective)
}

/// <summary>One line on a spell book's left page. Text is editable in the Inspector.</summary>
[Serializable]
public class SpellActionDefinition
{
    public SpellAction action;
    public string title;
    [Tooltip("Optional icon for the book's grid and the tooltip.")]   
    public Sprite icon;
    [TextArea(2, 4)] public string description;
    [Tooltip("Hide the entry completely (instead of greying it out) while it can't be used, e.g. Seal when there is no circle.")]
    public bool hideWhenUnavailable;

    public SpellActionDefinition(SpellAction action, string title, string description, bool hideWhenUnavailable = false)
    {
        this.action = action;
        this.title = title;
        this.description = description;
        this.hideWhenUnavailable = hideWhenUnavailable;
    }
}

public static class SpellActions
{
    /// <summary>The discipline an action uses (and marks as used for the turn). Null = no discipline.</summary>
    public static Discipline? DisciplineOf(SpellAction a) => a switch
    {
        SpellAction.ArcaneStrike => Discipline.Arcana,
        SpellAction.Guard or SpellAction.Stabilize or SpellAction.ShieldAnchor => Discipline.Warding,
        SpellAction.Study => Discipline.Lore,
        SpellAction.Channel or SpellAction.SealCircle => Discipline.Occultism,
        _ => null
    };

    /// <summary>Does the action roll dice? (Assist doesn't.)</summary>
    public static bool Rolls(SpellAction a) => a != SpellAction.Assist;

    /// <summary>The default page for each book. Used when a book's list is left empty.</summary>
    public static List<SpellActionDefinition> Defaults(Discipline book) => book switch
    {
        Discipline.Arcana => new()
        {
            new(SpellAction.ArcaneStrike, "Arcane Strike",
                "Hit an enemy for 1 damage per Success. If it isn't defeated, the damage is lost. Critical: +1 damage or a push."),
        },
        Discipline.Warding => new()
        {
            new(SpellAction.Guard, "Guard",
                "Give herself, an ally or a civilian 1 Shield per Success. Critical: restore 1 Turn Capacity."),
            new(SpellAction.Stabilize, "Stabilize the Anchor",
                "+1 Stability per Success, so the ritual can take more Progress this round."),
            new(SpellAction.ShieldAnchor, "Shield the Anchor",
                "The Anchor gains 1 Shield per Success against the enemies' attacks."),
            new(SpellAction.Assist, "Help Up",
                "Revive a Downed ally next to her with 1 Resolve. Costs an Action, but not her Warding.", hideWhenUnavailable: true),
        },
        Discipline.Lore => new()
        {
            new(SpellAction.Study, "Study",
                "Reveal an enemy's plan and add 1 Insight per Success towards Solving its kind. Also calms restless spirits."),
        },
        Discipline.Occultism => new()
        {
            new(SpellAction.Channel, "Channel",
                "Next to the Anchor: +1 Progress per Success, limited by Stability. Critical: +2 more."),
            new(SpellAction.SealCircle, "Seal the Summoning Circle",
                "Next to a summoning circle: weaken its seal by 1 per Success. A sealed circle stops spawning.", hideWhenUnavailable: true),
        },
        _ => new()
    };
}
