using System;
using UnityEngine;

/// <summary>
/// One rolled pool. The numbers are decided before the 3D dice are thrown,
/// so the visualizer only has to show them.
/// </summary>
[Serializable]
public class DiceRollResult
{
    public Discipline discipline;
    public int[] values;

    public DiceRollResult(Discipline discipline, int[] values)
    {
        this.discipline = discipline;
        this.values = values ?? Array.Empty<int>();
    }

    public int DiceCount => values.Length;

    public int Successes
    {
        get
        {
            int total = 0;
            foreach (int v in values) total += DiceRules.SuccessesFor(v);
            return total;
        }
    }

    public int Criticals
    {
        get
        {
            int total = 0;
            foreach (int v in values) if (DiceRules.IsCritical(v)) total++;
            return total;
        }
    }

    public bool HasCritical => Criticals > 0;
    public bool IsOvercharged => Criticals >= DiceRules.OverchargeCriticals;

    public override string ToString() =>
        $"{discipline} [{string.Join(",", values)}] = {Successes} Success(es), {Criticals} Critical(s)" +
        (IsOvercharged ? " OVERCHARGE" : "");
}

/// <summary>
/// The d8 pool rules (GDD 6.2) in one place: rolling, scoring and exact odds for the action preview.
/// 1-5 = no Success, 6-7 = 1 Success, 8 = Critical worth 2 Successes.
/// </summary>
public static class DiceRules
{
    public const int Sides = 8;
    public const int BaseSuccessMin = 6;
    /// <summary>Added to the Success threshold, e.g. +1 from an Omen. Set by RitualManager each encounter.</summary>
    public static int ThresholdModifier = 0;
    /// <summary>Lowest face that counts as a Success (6 by default). Never below 2 or above 8 (GDD 6.2).</summary>
    public static int SuccessMin => Mathf.Clamp(BaseSuccessMin + ThresholdModifier, 2, CriticalFace);
    public const int CriticalFace = 8;
    public const int CriticalValue = 2;
    public const int OverchargeCriticals = 2; // GDD 6.7 (PROVISIONAL)

    public static DiceRollResult RollPool(Discipline discipline, int diceCount)
    {
        var values = new int[Mathf.Max(0, diceCount)];
        for (int i = 0; i < values.Length; i++)
            values[i] = UnityEngine.Random.Range(1, Sides + 1);
        return new DiceRollResult(discipline, values);
    }

    public static bool IsCritical(int value) => value >= CriticalFace;

    public static int SuccessesFor(int value) =>
        value >= CriticalFace ? CriticalValue : value >= SuccessMin ? 1 : 0;

    // ---------- Odds (GDD 13.3) ----------

    private static float PFail => (SuccessMin - 1f) / Sides;
    private static float PCrit => (Sides - CriticalFace + 1f) / Sides;
    private static float PHit => Mathf.Max(0f, 1f - PFail - PCrit);

    /// <summary>
    /// Chance that Successes (+ critBonus if at least one Critical is rolled) reach the target.
    /// Example: Arcane Strike kill chance = ChanceToReach(pool, enemyResolve, 1).
    /// </summary>
    public static float ChanceToReach(int pool, int target, int critBonus = 0)
    {
        if (target <= 0) return 1f;
        int max = pool * CriticalValue + 1;
        var noCrit = new float[max];   // probability of s successes, no Critical so far
        var withCrit = new float[max]; // probability of s successes, at least one Critical
        noCrit[0] = 1f;

        for (int d = 0; d < pool; d++)
        {
            var nNo = new float[max];
            var nWith = new float[max];
            for (int s = 0; s < max; s++)
            {
                if (noCrit[s] > 0f)
                {
                    nNo[s] += noCrit[s] * PFail;
                    if (s + 1 < max) nNo[s + 1] += noCrit[s] * PHit;
                    if (s + CriticalValue < max) nWith[s + CriticalValue] += noCrit[s] * PCrit;
                }
                if (withCrit[s] > 0f)
                {
                    nWith[s] += withCrit[s] * PFail;
                    if (s + 1 < max) nWith[s + 1] += withCrit[s] * PHit;
                    if (s + CriticalValue < max) nWith[s + CriticalValue] += withCrit[s] * PCrit;
                }
            }
            noCrit = nNo;
            withCrit = nWith;
        }

        float chance = 0f;
        for (int s = 0; s < max; s++)
        {
            if (s >= target) chance += noCrit[s];
            if (s + critBonus >= target) chance += withCrit[s];
        }
        return chance;
    }

    public static float ChanceOfCritical(int pool) => 1f - Mathf.Pow(1f - PCrit, pool);
    public static float ExpectedSuccesses(int pool) => pool * (PHit + PCrit * CriticalValue);
}
