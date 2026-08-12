using UnityEngine;

[CreateAssetMenu(fileName = "Relic", menuName = "Scriptable Objects/Relic")]
public class Relic : ScriptableObject
{
    public string relicName;
    public string relicDescription;

    //??? not sure how to make this work as it is supposed to give bonus effects to the player when aquired
}
