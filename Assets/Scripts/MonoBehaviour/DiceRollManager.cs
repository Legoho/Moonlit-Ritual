using UnityEngine;

public class DiceRollManager : MonoBehaviour
{
    public Grimoire grimoire;
    public int arcanaSuccesses=0;
    public int wardingSuccesses=0;
    public int loreSuccesses=0;
    public int occultismSuccesses=0;
    public int rolledStatAmount = 0;

    public void RollArcanaDice()
    {
        if (rolledStatAmount >= 2)
            return;
        int successCount = arcanaSuccesses;
        for (int i = 0; i < grimoire.arcana; i++)
        {
            int roll = RollDice(6); // Roll a 6-sided die
            if (roll>=5)
            {
                successCount++;
            }
            Debug.Log("Arcana Dice Roll: " + roll);
        }
        rolledStatAmount++;
        arcanaSuccesses = successCount;
    }
    public void RollWardingDice() 
    {
        if (rolledStatAmount >= 2)
            return;
        int successCount = wardingSuccesses;
        for (int i = 0; i < grimoire.warding; i++)
        {
            int roll = RollDice(6); // Roll a 6-sided die
            if (roll >= 5)
            {
                successCount++;
            }
            Debug.Log("Warding Dice Roll: " + roll);
        }
        rolledStatAmount++;
        wardingSuccesses = successCount;
    }
    public void RollLoreDice() 
    {
        if (rolledStatAmount >= 2)
            return;
        int successCount = loreSuccesses;
        for (int i = 0; i < grimoire.lore; i++)
        {
            int roll = RollDice(6); // Roll a 6-sided die
            if (roll >= 5)
            {
                successCount++;
            }
            Debug.Log("Lore Dice Roll: " + roll);
        }
        rolledStatAmount++;
        loreSuccesses = successCount;
    }
    public void RollOccultismDice() 
    {
        if (rolledStatAmount >= 2)
            return;
        int successCount = occultismSuccesses;
        for (int i = 0; i < grimoire.occultism; i++)
        {
            int roll = RollDice(6); // Roll a 6-sided die
            if (roll >= 5)
            {
                successCount++;
            }
            Debug.Log("Occultism Dice Roll: " + roll);
        }
        rolledStatAmount++;
        occultismSuccesses = successCount;
    }
    public void ResetDiceRolls() 
    {
        rolledStatAmount = 0;
        arcanaSuccesses = 0;
        wardingSuccesses = 0;
        loreSuccesses = 0;
        occultismSuccesses = 0;
    }
    private int RollDice(int sides)
    {
        return Random.Range(1, sides + 1);
    }
}
