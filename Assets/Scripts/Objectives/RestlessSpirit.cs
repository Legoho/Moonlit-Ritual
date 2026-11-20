using UnityEngine;

/// <summary>
/// The ghost of a lost student, drifting towards the summoning circles.
/// Heroines Study it (Lore) to learn its name and lay it to rest. Enemies ignore it.
/// Put this on the spirit prefab together with a Collider (for clicks).
/// </summary>
public class RestlessSpirit : ObjectiveUnit
{
    [SerializeField] private string spiritName = "Restless Spirit";

    protected override string GetDisplayName() => spiritName;

    public override string Describe() =>
        Owner is RestlessSpiritObjective o
            ? $"{spiritName}: {o.StatusLine}. Study it with Lore to lay it to rest."
            : spiritName;
}
