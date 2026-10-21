using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum CombatPhase { Setup, PlayerPhase, EnemyPhase, Victory, Defeat }

/// <summary>
/// Tracks the round, the phase, which heroines are in combat and whose activation it is.
/// GDD 5.2: girls activate in any order, and a girl finishes her activation before the next starts.
/// Here, selecting another girl after the current one has moved or acted ends the current one's activation.
/// </summary>
public class TurnManager : MonoBehaviour
{
    [Tooltip("The deployed heroines (normally three). Leave empty to use every Heroine in the scene.")]
    public List<Heroine> party = new();

    public CombatPhase Phase { get; private set; } = CombatPhase.Setup;
    public int Round { get; private set; }
    public Heroine ActiveHeroine { get; private set; }

    private readonly HashSet<Heroine> finished = new();

    public event Action<Heroine> ActiveHeroineChanged;
    /// <summary>Raised when the party list is replaced (e.g. RunManager spawned the chosen party).</summary>
    public event Action PartyChanged;

    /// <summary>Heroines on the board who are not Downed.</summary>
    public IEnumerable<Heroine> InCombat => party.Where(h => h != null && !h.IsDowned);
    public bool AllDowned => party.Count > 0 && party.All(h => h == null || h.IsDowned);

    private void Awake()
    {
        if (party.Count == 0)
            party.AddRange(FindObjectsByType<Heroine>(FindObjectsSortMode.None));
    }
    /// <summary>Replaces the party. Call before the encounter starts.</summary>
    public void SetParty(IEnumerable<Heroine> heroines)
    {
        party.Clear();
        party.AddRange(heroines.Where(h => h != null));
        PartyChanged?.Invoke();
    }
    public void BeginEncounter()
    {
        Round = 0;
        Phase = CombatPhase.Setup;
        finished.Clear();
        SetActive(null);
    }

    public void StartPlayerPhase()
    {
        Phase = CombatPhase.PlayerPhase;
        finished.Clear();
        foreach (var h in party)
        {
            if (h == null) continue;
            h.BeginTurn();
            h.ClearShields(); // Shields last "until the next player phase" (GDD 6.5)
        }
        SetActive(null);
    }

    public void StartEnemyPhase()
    {
        Phase = CombatPhase.EnemyPhase;
        SetActive(null);
    }

    public void AdvanceRound() => Round++;

    public void SetResult(bool won)
    {
        Phase = won ? CombatPhase.Victory : CombatPhase.Defeat;
        SetActive(null);
    }

    public bool HasStarted(Heroine h) => h.hasMoved || h.actionsRemaining < h.actionsPerTurn;
    public bool IsDone(Heroine h) => h == null || h.IsDowned || finished.Contains(h);

    public bool TrySelect(Heroine h, out string reason)
    {
        reason = null;
        if (Phase != CombatPhase.PlayerPhase) { reason = "It is not the player phase."; return false; }
        if (!party.Contains(h)) { reason = $"{h.DisplayName} is not deployed."; return false; }
        if (h.IsDowned) { reason = $"{h.DisplayName} is Downed."; return false; }
        if (finished.Contains(h)) { reason = $"{h.DisplayName} has already finished this round."; return false; }

        if (ActiveHeroine != null && ActiveHeroine != h && HasStarted(ActiveHeroine))
        {
            finished.Add(ActiveHeroine);
            Debug.Log($"{ActiveHeroine.DisplayName}'s activation ends.");
        }
        SetActive(h);
        return true;
    }

    /// <summary>Ends the active heroine's activation for this round.</summary>
    public void FinishActivation()
    {
        if (ActiveHeroine == null) return;
        finished.Add(ActiveHeroine);
        SetActive(null);
    }

    private void SetActive(Heroine h)
    {
        if (ActiveHeroine != null) ActiveHeroine.SetSelected(false);
        ActiveHeroine = h;
        if (h != null) h.SetSelected(true);
        ActiveHeroineChanged?.Invoke(h);
    }
}
