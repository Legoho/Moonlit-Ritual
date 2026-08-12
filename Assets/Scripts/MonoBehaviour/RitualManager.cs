using NUnit.Framework;
using System;
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

    private readonly List<MonsterHolder> validTargets = new();
    private Action<MonsterHolder> onMonsterSelected;

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
            Debug.Log(monster.gameObject.name);
            if (!monster.isSolved)
            {
                Debug.Log("Highlighting monster: " + monster.gameObject.name);
                monster.Highlight();
            }
        }
        GetComponent<DiceRollManager>().RollLoreDice();
        BeginMonsterSelection(
            monster => !monster.isSolved,
            selected =>
            {
                Debug.Log($"Researching {selected.gameObject.name}");

                // Target is chosen before the dice are rolled.
                selected.loreToSolve -= GetComponent<DiceRollManager>().loreSuccesses;
                if (selected.loreToSolve <= 0)
                {
                    selected.isSolved = true;
                    solvedMonsters.Add(selected.GetMonsterData());
                    Debug.Log($"Monster {selected.gameObject.name} is solved!");
                    foreach (var monster in monsters)
                    {
                        if (monster.GetMonsterData() == selected.GetMonsterData())
                        {
                            monster.isSolved = true;
                        }
                    }
                }
            }
        );
    }
    private void BeginMonsterSelection(Predicate<MonsterHolder> isValidTarget,Action<MonsterHolder> selectionResult)
    {
        CancelMonsterSelection();
        Debug.Log("Beginning Monster Selection");
        foreach (MonsterHolder monster in monsters)
        {
            if (monster == null || !isValidTarget(monster))
                continue;

            validTargets.Add(monster);
            monster.Highlight();
        }

        if (validTargets.Count == 0)
        {
            Debug.Log("There are no valid targets.");
            return;
        }

        onMonsterSelected = selectionResult;
    }
    public void TrySelectMonster(MonsterHolder monster)
    {
        if (onMonsterSelected == null)
            return;

        if (!validTargets.Contains(monster))
        {
            Debug.Log($"{monster.gameObject.name} is not a valid target.");
            return;
        }

        // Store the callback because clearing selection sets it to null.
        Action<MonsterHolder> selectionResult = onMonsterSelected;

        ClearMonsterSelection();
        selectionResult.Invoke(monster);
    }

    public void CancelMonsterSelection()
    {
        ClearMonsterSelection();
    }
    private void ClearMonsterSelection()
    {
        foreach (MonsterHolder monster in validTargets)
        {
            if (monster != null)
                monster.RemoveHighlight();
        }

        validTargets.Clear();
        onMonsterSelected = null;
    }
    public MonsterHolder SelectMonster()
    {

        MonsterHolder selected = null;


        return selected;
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
