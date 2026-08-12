using UnityEngine;

public class MonsterHolder : MonoBehaviour
{
    [SerializeField]
    private Monster monsterData;
    public int originalResistance;
    public int solvedResistance;
    public int stabilityDamage;
    private GameObject highlight;
    public bool isSolved = false;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        originalResistance = monsterData.resistance;
        stabilityDamage = monsterData.stabilityDamage;
    }
    public void ResetResistance()
    {
        Debug.Log("Monster resistance reset");
        originalResistance = monsterData.resistance;
    }
    public void Die()
    {
        Debug.Log("Monster died");
        Destroy(this.gameObject);
    }
    public void Highlight()
    {
        highlight.SetActive(true);
    }
    public void RemoveHighlight() 
    {
        highlight.SetActive(false);
    }
}
