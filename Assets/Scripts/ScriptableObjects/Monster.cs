using UnityEngine;
public enum TargetPreference
{
    Ritual,  // hits the Anchor if it can; otherwise a heroine it can reach; otherwise walks toward the Anchor
    Heroine, // hits a reachable heroine; otherwise the Anchor if reachable; otherwise walks toward the nearest heroine
    Nearest  // whichever target is fewest steps away (Anchor wins ties)
}
[CreateAssetMenu(fileName = "Monster", menuName = "Scriptable Objects/Monster")]
public class Monster : ScriptableObject
{
    public string monsterName;
    public string monsterDescription;
    public int resistance;        // Resolve (GDD 5.7): one action must deal this much to defeat it
    public int solvedResistance;  // Resolve once its type is Solved
    public int loreToSolve;       // Insight needed to Solve the type
    public int stabilityDamage;   // Turn Capacity lost when it hits the Anchor unblocked

    [Header("Grid behaviour")]
    public int movement = 2;
    [Tooltip("Tiles from which it can hit. 1 = must stand adjacent.")]
    public int attackRange = 1;
    [Tooltip("Resolve removed from a heroine it hits (GDD 5.6).")]
    public int heroineDamage = 1;
    public TargetPreference targetPreference = TargetPreference.Ritual;

    [Tooltip("Not used at runtime anymore: Solved state lives in RitualManager so the asset is never modified in Play mode.")]
    public bool isSolved = false;

    public Sprite monsterSprite;
    public Sprite monsterHighlightSprite;
}
