using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class RitualManager : MonoBehaviour
{
    public int turnCounter = 0;
    public int maxTurns = 10;
    public int remainingMaxTurns = 10;
    public int ritualProgressCounter = 0;
    public int ritualProgressMax = 10;
    public int wardingAmount = 0;
    public List<MonsterHolder> monsters = new List<MonsterHolder>();
    public List<MonsterHolder> monsterPrefabs = new List<MonsterHolder>();
    public List<Monster> solvedMonsters = new List<Monster>();

    public void TakeDamage(int damageAmount)
    {
        damageAmount -= GetComponent<DiceRollManager>().wardingSuccesses;
        remainingMaxTurns -= damageAmount;
        isLost();
    }

    private void ProgressRitual()
    {
        ritualProgressCounter += GetComponent<DiceRollManager>().occultismSuccesses;
        isWon();
    }

    public void DealDamageToMonsters()
    {
        if (monsters.Count <= 0)
            return;
        for (int i = monsters.Count - 1; i >= 0; i--)
        {
            MonsterHolder monster = monsters[i];
            for (int idx = monsters[i].originalResistance; idx > 0; idx--)
            {
                monsters[i].originalResistance--;
                if (GetComponent<DiceRollManager>().arcanaSuccesses == 0)
                {
                    monsters[i].ResetResistance();
                    return;
                }
                GetComponent<DiceRollManager>().arcanaSuccesses--;

            }
            monsters.Remove(monster);
            monster.Die();
        }
    }

    public void ReduceStabilization()
    {
        if (monsters != null && monsters.Count > 0)
        {
            for (int i = monsters.Count - 1; i >= 0; i--)
            {
                TakeDamage(monsters[i].stabilityDamage);
            }
        }
    }

    public void Lore()
    {
        MonsterHolder selected;
        foreach (var monster in monsters)
        {
            if (!monster.isSolved)
            {
             //set child gameobject active if it has the tag highlight
                foreach (var child in monster.GetComponentsInChildren<Transform>())
                {
                    Debug.Log("Checking child: " + child.name + " with tag: " + child.tag);
                    if (child.tag == "Highlight")
                    {
                        Debug.Log("Highlighting monster: " + monster.name);
                        child.gameObject.SetActive(true);
                    }
                }
            }
        }
    }

    public void EndTurn()
    {
        DealDamageToMonsters();
        ReduceStabilization();
        AddMonster();
        AddMonster();
        ProgressRitual();
        GetComponent<DiceRollManager>().ResetDiceRolls();
        turnCounter++;
        isLost();
        isWon();
    }
    private void AddMonster()
    {
        //logic based on which to add monster, but right now as there is only one:
        GameObject monsterPrefab = monsterPrefabs[0].gameObject;
        GameObject newMonster = Instantiate(monsterPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        monsters.Add(newMonster.GetComponent<MonsterHolder>());
    }
    private void GameOver()
    {

        // Reset ritual progress
        ritualProgressCounter = 0;
        // Reset turn counter
        turnCounter = 0;
        // Reset max turns
        remainingMaxTurns = maxTurns;
        foreach (var monster in monsters)
        {
            Destroy(monster.gameObject);
        }
        monsters.Clear();
    }
    private void isLost()
    {
        if (turnCounter >= remainingMaxTurns)
        {
            Debug.Log("Ritual Failed!");
            GameOver();
        }
    }
    private void isWon()
    {
        if (ritualProgressCounter >= remainingMaxTurns)
        {
            Debug.Log("You won!");
            GameOver();
        }

    }
}
