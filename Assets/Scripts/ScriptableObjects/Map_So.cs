using UnityEngine;

[CreateAssetMenu(fileName = "MapCell", menuName = "Scriptable Objects/MapCell")]
public class Map_So : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public MapCellType category;

    [Header("Generation")]
    public GameObject prefab;
    public float generationWeight = 1f;

    [Header("Navigation")]
    public bool isWalkable = true;
    public bool blocksVision;

    [Header("Editor")]
    public Color editorColor = Color.white;

    [Header("Hazard")]
    public bool isHazard;
    public int hazardDamage;
}
