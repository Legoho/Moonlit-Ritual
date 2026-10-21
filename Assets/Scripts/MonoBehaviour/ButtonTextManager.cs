using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class ButtonTextManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI RollText;
    [SerializeField] TextMeshProUGUI ActionsRemaining;
    [SerializeField] TextMeshProUGUI ArcanaSuccess;
    [SerializeField] TextMeshProUGUI WardingSuccess;
    [SerializeField] TextMeshProUGUI LoreSuccess;
    [SerializeField] TextMeshProUGUI OccultismSuccess;

    [Header("New (optional)")]
    [Tooltip("Round, Turn Capacity, Progress, Stability and phase.")]
    [SerializeField] TextMeshProUGUI RitualText;
    [Tooltip("Selected heroine: Resolve, Shields, Move, used disciplines.")]
    [SerializeField] TextMeshProUGUI HeroineText;
    [Tooltip("The last few combat messages.")]
    [SerializeField] TextMeshProUGUI LogText;
    [SerializeField] int logLines = 8;

    private readonly Queue<string> logQueue = new();

    void Start()
    {
        SetText(RollText, "");
        UpdateActionsRemaining(2, 2);
        UpdateArcana(0);
        UpdateWarding(0);
        UpdateLore(0);
        UpdateOccultism(0);
    }

    private static void SetText(TextMeshProUGUI label, string text)
    {
        if (label != null) label.text = text;
    }

    // ---------- Rolls ----------

    /// <summary>Shows each die; Successes (6-7) in green, Criticals (8) in gold (GDD 13.3).</summary>
    public void ShowRoll(DiceRollResult roll)
    {
        var faces = roll.values.Select(f =>
            DiceRules.IsCritical(f) ? $"<color=#FFC83D><b>{f}</b></color>" :
            DiceRules.SuccessesFor(f) > 0 ? $"<color=#7CFF7C>{f}</color>" : f.ToString());
        string extra = roll.IsOvercharged ? " OVERCHARGE!" : roll.HasCritical ? " Critical!" : "";
        SetText(RollText, $"{roll.discipline} Roll: {string.Join(", ", faces)} = {roll.Successes}{extra}");

        switch (roll.discipline)
        {
            case Discipline.Arcana: UpdateArcana(roll.Successes); break;
            case Discipline.Warding: UpdateWarding(roll.Successes); break;
            case Discipline.Lore: UpdateLore(roll.Successes); break;
            case Discipline.Occultism: UpdateOccultism(roll.Successes); break;
        }
    }

    // Kept for compatibility with older calls.
    public void UpdateRollText(string rollType, int[] rollValues)
    {
        SetText(RollText, $"{rollType} Roll: {string.Join(",", rollValues)}");
    }

    // ---------- Status ----------

    public void UpdateRitual(RitualState ritual, int round, CombatPhase phase)
    {
        SetText(RitualText,
            $"{phase}\n" +
            $"Round {round} / {ritual.turnCapacity}\n" +
            $"Progress {ritual.progress} / {ritual.progressTarget}\n" +
            $"Stability {ritual.stability} ({ritual.StabilityState}), absorbable {ritual.AbsorbableThisRound}");
    }

    public void UpdateHeroine(Heroine h)
    {
        if (h == null)
        {
            SetText(HeroineText, "Click a heroine");
            SetText(ActionsRemaining, "");
            return;
        }

        string used = h.UsedDisciplines.Count == 0 ? "none" : string.Join(", ", h.UsedDisciplines);
        SetText(HeroineText,
            $"{h.heroineName}\n" +
            $"Resolve {h.resolve}/{h.maxResolve}  Shields {h.shields}\n" +
            $"Move {(h.hasMoved ? "used" : $"{h.moveRange} tiles")}\n" +
            $"Used: {used}");
        UpdateActionsRemaining(h.actionsRemaining, h.actionsPerTurn);
    }

    public void Log(string message)
    {
        if (LogText == null) return;
        logQueue.Enqueue(message);
        while (logQueue.Count > logLines) logQueue.Dequeue();
        LogText.text = string.Join("\n", logQueue);
    }

    public void UpdateActionsRemaining(int currentActionsRemaining, int maxActions)
    {
        SetText(ActionsRemaining, $"Actions Remaining: {currentActionsRemaining}/{maxActions}");
    }

    public void UpdateArcana(int currentArcanaSuccess) => SetText(ArcanaSuccess, $"Arcana Success: {currentArcanaSuccess}");
    public void UpdateWarding(int currentWardingSuccess) => SetText(WardingSuccess, $"Warding Success: {currentWardingSuccess}");
    public void UpdateLore(int currentLoreSuccess) => SetText(LoreSuccess, $"Lore Success: {currentLoreSuccess}");
    public void UpdateOccultism(int currentOccultismSuccess) => SetText(OccultismSuccess, $"Occultism Success: {currentOccultismSuccess}");
}
