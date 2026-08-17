using UnityEngine;

public class MonsterHolder : MonoBehaviour
{
    [SerializeField] private RitualManager ritualManager;

    [SerializeField]
    private Monster monsterData;
    public int originalResistance;
    public int solvedResistance;
    public int stabilityDamage;
    public int loreToSolve;
    [SerializeField]
    private GameObject highlight;
    public bool isSolved = false;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        originalResistance = monsterData.resistance;
        stabilityDamage = monsterData.stabilityDamage;
        loreToSolve = monsterData.loreToSolve;
        ritualManager = FindFirstObjectByType<RitualManager>();
        isSolved = monsterData.isSolved;
    }
    public void ResetResistance()
    {
        Debug.Log("Monster resistance reset");
        originalResistance = monsterData.resistance;
        if(isSolved)
        {
            originalResistance = monsterData.solvedResistance;
        }
    }
    public void Die()
    {
        Debug.Log("Monster died");
        Destroy(this.gameObject);
    }
    public Monster GetMonsterData()
    {
        return monsterData;
    }

    private void OnMouseDown()
    {
        Debug.Log("Monster selected: " + this.gameObject.name);
        ritualManager.TrySelectMonster(this);
        
    }


    public void Highlight(bool isDamage=true)
    {
        highlight.SetActive(true);
        highlight.GetComponent<SpriteRenderer>().color = isDamage ? Color.red : Color.greenYellow;
    }
    public void RemoveHighlight() 
    {
        highlight.SetActive(false);
    }
}
