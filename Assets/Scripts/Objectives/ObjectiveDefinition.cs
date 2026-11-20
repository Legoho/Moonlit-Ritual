using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The data of an Optional Objective (GDD "Optional Objectives"): its texts, icon and rewards.
/// Drag one into an objective component's Definition field; the component keeps only its map
/// settings (how many civilians, where to place things...). Without a Definition, the component's
/// own Title / Description / Moonlight / Bonus fields are used, as before.
/// Create one with Create > Scriptable Objects > Objective.
/// </summary>
[CreateAssetMenu(fileName = "Objective", menuName = "Scriptable Objects/Objective")]
public class ObjectiveDefinition : ScriptableObject
{
    public string title = "Objective";
    [TextArea(2, 4)] public string description;
    public Sprite icon;
    [Tooltip("The disciplines it leans on (shown to the player; GDD: each objective leans on a different one).")]
    public List<Discipline> disciplines = new();

    [Header("Rewards")]
    [Tooltip("Moonlight (research currency) for completing it.")]
    public int moonlightReward = 2;
    public ObjectiveBonus bonus = new();
}
