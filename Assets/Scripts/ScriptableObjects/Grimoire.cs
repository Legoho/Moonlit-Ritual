using UnityEngine;

[CreateAssetMenu(fileName = "Grimoire", menuName = "Scriptable Objects/Grimoire")]
public class Grimoire : ScriptableObject
{
    public string grimoireName;
    public string grimoireDescription;

    public int arcana;
    public int warding;
    public int lore;
    public int occultism;
}
