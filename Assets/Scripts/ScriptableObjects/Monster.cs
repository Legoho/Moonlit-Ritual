using UnityEngine;

[CreateAssetMenu(fileName = "Monster", menuName = "Scriptable Objects/Monster")]
public class Monster : ScriptableObject
{
    public string monsterName;
    public string monsterDescription;
    public int resistance; //hp
    public int solvedResistance; //hp when solved
    public int loreToSolve; //lore needed to solve
    public int stabilityDamage;
    public bool isSolved = false;
}
