using System;
using UnityEngine;
using System.Collections;

public class DiceRollManager : MonoBehaviour
{
    [Tooltip("Optional. Leave empty to resolve rolls instantly (useful for testing).")]
    [SerializeField] private DiceRollVisualizer visualizer;
    [SerializeField] private ButtonTextManager buttonTextManager;
    [Tooltip("Off: skip the 3D dice and apply results instantly.")]
    [SerializeField] private bool showDiceAnimation = true;
    [Tooltip("If the dice haven't finished after this many seconds, the result is applied anyway.")]
    [SerializeField] private float maxRollSeconds = 5f;

    public bool IsRolling => visualizer != null && visualizer.IsRolling;

    private void Awake()
    {
        if (visualizer == null) visualizer = FindAnyObjectByType<DiceRollVisualizer>();
        if (buttonTextManager == null) buttonTextManager = FindAnyObjectByType<ButtonTextManager>();
    }

    /// <summary>
    /// Rolls a pool and returns the result straight away (it is decided before the throw).
    /// onResolved is called once the result has been shown.
    /// </summary>
    public DiceRollResult Roll(Discipline discipline, int pool, Action<DiceRollResult> onResolved)
    {
        DiceRollResult result = DiceRules.RollPool(discipline, pool);
        Debug.Log("Rolled: " + result);

        // Relic: reroll a roll with no Successes (limited uses per encounter).
        if (result.Successes == 0 && pool > 0 && RunManager.Instance != null && RunManager.Instance.TryUseEmptyRollReroll())
        {
            result = DiceRules.RollPool(discipline, pool);
            Debug.Log("Empty roll rerolled: " + result);
        }

        bool resolved = false;
        void Resolve(DiceRollResult r)
        {
            if (resolved) return; // the watchdog and a late visualizer must not apply it twice
            resolved = true;
            if (buttonTextManager != null) buttonTextManager.ShowRoll(r);
            onResolved?.Invoke(r);
        }

        if (showDiceAnimation && visualizer != null && visualizer.isActiveAndEnabled && pool > 0)
        {
            try
            {
                visualizer.Show(result, Resolve);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Resolve(result);
                return result;
            }
            StartCoroutine(Watchdog(result, Resolve, () => resolved));
        }
        else
        {
            Resolve(result);
        }

        return result;
    }

    /// <summary>
    /// Safety net: if the dice animation hasn't reported back in time (stuck die, error in the
    /// visualizer, disabled object...), skip it; if it still hasn't finished, apply the result directly.
    /// The numbers were decided before the throw, so the outcome is the same either way.
    /// </summary>
    private IEnumerator Watchdog(DiceRollResult result, Action<DiceRollResult> resolve, Func<bool> isResolved)
    {
        for (float t = 0f; t < maxRollSeconds; t += Time.unscaledDeltaTime)
        {
            if (isResolved()) yield break;
            yield return null;
        }

        Debug.LogWarning($"[Dice] {result.discipline} roll took longer than {maxRollSeconds}s; skipping the animation.");
        if (visualizer != null) visualizer.SkipToResult();

        yield return new WaitForSecondsRealtime(0.5f);
        if (!isResolved())
        {
            Debug.LogWarning("[Dice] The dice never finished; applying the result without them.");
            resolve(result);
        }
    }


    /// <summary>Skips the throw animation and lands the dice now.</summary>
    public void SkipAnimation()
    {
        if (IsRolling) visualizer.SkipToResult();
    }

    // Odds for the action preview, forwarded to DiceRules.
    public float ChanceToReach(int pool, int target, int critBonus = 0) => DiceRules.ChanceToReach(pool, target, critBonus);
    public float ChanceOfCrit(int pool) => DiceRules.ChanceOfCritical(pool);
    public float ExpectedSuccesses(int pool) => DiceRules.ExpectedSuccesses(pool);
}
