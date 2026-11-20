using UnityEngine;

/// <summary>
/// A student, cat, dog... hiding on the board. Enemies may attack it; heroines can Guard it with Warding.
/// Put this on the civilian prefab together with a Collider (for clicks).
/// </summary>
public class Civilian : ObjectiveUnit
{
    [SerializeField] private string civilianName = "Student";
    public int maxResolve = 1;

    [Header("Visuals (optional)")]
    [Tooltip("Shown while it has Shields.")]
    [SerializeField] private GameObject shieldVisual;
    [Tooltip("Hidden when the civilian is taken by the shadows. Empty = the whole object is hidden.")]
    [SerializeField] private GameObject model;

    [Header("Runtime")]
    public int resolve;
    public int shields;

    public bool IsLost => resolve <= 0;
    public System.Action<Civilian> Lost;

    protected override string GetDisplayName() => civilianName;

    protected override void Awake()
    {
        base.Awake();
        resolve = maxResolve;
    }

    /// <summary>Shields absorb first. Returns true if damage got through.</summary>
    public bool TakeHit(int damage)
    {
        int blocked = Mathf.Min(shields, damage);
        shields -= blocked;
        damage -= blocked;
        if (damage <= 0) return false;

        resolve = Mathf.Max(0, resolve - damage);
        if (IsLost)
        {
            RemoveFromGrid();
            if (model != null) model.SetActive(false);
            else gameObject.SetActive(false);
            Lost?.Invoke(this);
        }
        return true;
    }

    private void LateUpdate()
    {
        if (shieldVisual != null && shieldVisual.activeSelf != shields > 0)
            shieldVisual.SetActive(shields > 0);
    }

    public override string Describe() =>
        $"{civilianName}: Resolve {resolve}/{maxResolve}, Shields {shields}. Keep them safe until the ritual is done.";
}
