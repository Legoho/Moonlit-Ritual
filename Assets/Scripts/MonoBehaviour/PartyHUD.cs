using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Connects the party panels to the heroines in combat: slot 1 = first party member, and so on.
/// Rebinds automatically when the party changes (e.g. when RunManager spawns it).
/// </summary>
public class PartyHUD : MonoBehaviour
{
    [Tooltip("The three panels, top to bottom. Found in children if left empty.")]
    [SerializeField] private List<PartySlotUI> slots = new();

    private TurnManager turnManager;

    private void Start()
    {
        turnManager = FindAnyObjectByType<TurnManager>();
        if (slots.Count == 0) slots.AddRange(GetComponentsInChildren<PartySlotUI>(true));
        if (turnManager == null)
        {
            Debug.LogWarning("[PartyHUD] No TurnManager in the scene.");
            return;
        }

        turnManager.PartyChanged += Rebind;
        Rebind();
    }

    private void OnDestroy()
    {
        if (turnManager != null) turnManager.PartyChanged -= Rebind;
    }

    public void Rebind()
    {
        var party = turnManager.party;
        for (int i = 0; i < slots.Count; i++)
            slots[i].Bind(i < party.Count ? party[i] : null);
    }

    // Three panels, so simply refreshing every frame is cheap and never goes out of sync.
    private void Update()
    {
        if (turnManager == null) return;
        foreach (var slot in slots)
        {
            if (slot == null || slot.Heroine == null) continue;
            slot.Refresh(slot.Heroine == turnManager.ActiveHeroine, turnManager.IsDone(slot.Heroine));
        }
    }
}
