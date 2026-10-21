using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

/// <summary>
/// A deployed magical girl. Her Grimoire holds her four d8 pools (GDD 6.1).
/// Each round: one free Move and two Actions, each discipline at most once (GDD 5.2).
/// </summary>
public class Heroine : GridUnit
{
    [Header("Identity")]
    public string heroineName = "Heroine";
    [Tooltip("Dice per discipline. Arcana / Warding / Lore / Occultism = number of d8s.")]
    public Grimoire grimoire;
    [Tooltip("Shown in the party panel. Set by her HeroineDefinition when spawned by the RunManager.")]
    public Texture portrait;

    [Header("Stats (GDD 5.2, 5.6)")]
    public int maxResolve = 3;
    public int moveRange = 3;
    public int actionsPerTurn = 2;
    [Header("Ranges (straight step = 1, diagonal step = 1.5)")]
    public float arcanaRange = 3f;
    public float wardingRange = 3f;
    public float loreRange = 3f;

    [Header("Visuals (optional)")]
    [SerializeField] private GameObject selectionHighlight;

    [Header("Animation")]
    [Tooltip("Found in children if left empty.")]
    [SerializeField] private Animator animator;
    [Tooltip("The SpellImpact on her Arcana spell (FX.Spell). Found in children if left empty.")]
    [SerializeField] private SpellImpact spellImpact;

    private static readonly int ArcanaHash = Animator.StringToHash("isArcana");
    private static readonly int WardingHash = Animator.StringToHash("isWarding");
    private static readonly int LoreHash = Animator.StringToHash("isLore");
    private static readonly int OccultismHash = Animator.StringToHash("isOccultism");
    private static readonly int FaintedHash = Animator.StringToHash("isFainted");
    private static readonly int WalkingHash = Animator.StringToHash("isWalking");

    [Header("Walking")]
    [Tooltip("Seconds to walk one tile. Match it to the walk animation so the feet don't slide.")]
    public float walkStepDuration = 0.45f;
    [Tooltip("Turning speed in degrees per second.")]
    public float turnSpeed = 540f;
    [Tooltip("Extra Y rotation if the model doesn't face +Z when its rotation is 0.")]
    public float modelYawOffset = 0f;
    [Tooltip("A turn before casting never takes longer than this; she snaps to the target after it.")]
    public float maxTurnSeconds = 1f;

    [Header("Runtime")]
    public int resolve;
    public int shields;
    public int actionsRemaining;
    public bool hasMoved;
    public bool hasBeenRevived;

    private readonly HashSet<Discipline> usedDisciplines = new();

    public bool IsDowned => resolve <= 0;
    public bool CanAct => !IsDowned && actionsRemaining > 0;
    public IReadOnlyCollection<Discipline> UsedDisciplines => usedDisciplines;

    protected override string GetDisplayName() => heroineName;

    protected override void Awake()
    {
        base.Awake();
        resolve = maxResolve;
        if (selectionHighlight != null) selectionHighlight.SetActive(false);
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spellImpact == null) spellImpact = GetComponentInChildren<SpellImpact>(true);

    }
    protected override void Start()
    {
        base.Start();
        GetComponent<CharacterResolveManager>().UpdateSlider(resolve, maxResolve);

    }

    public int GetPool(Discipline d)
    {
        if (grimoire == null) return 0;
        return d switch
        {
            Discipline.Arcana => grimoire.arcana,
            Discipline.Warding => grimoire.warding,
            Discipline.Lore => grimoire.lore,
            Discipline.Occultism => grimoire.occultism,
            _ => 0
        };
    }

    public bool HasUsed(Discipline d) => usedDisciplines.Contains(d);

    /// <summary>Spends one Action. Pass null for non-discipline actions such as Assist.</summary>
    public void SpendAction(Discipline? discipline)
    {
        actionsRemaining = Mathf.Max(0, actionsRemaining - 1);
        if (discipline.HasValue) usedDisciplines.Add(discipline.Value);
    }
    public float GetRange(Discipline d) => d switch
    {
        Discipline.Arcana => arcanaRange,
        Discipline.Warding => wardingRange,
        Discipline.Lore => loreRange,
        _ => 1f // Occultism: must stand next to the Anchor
    };

    /// <summary>True if the target tile is within this discipline's range.</summary>
    public bool InRange(Discipline d, Vector2Int target) =>
        GridManager.RangeDistance(GridPosition, target) <= GetRange(d) + 0.01f;

    public void BeginTurn()
    {
        actionsRemaining = IsDowned ? 0 : actionsPerTurn;
        hasMoved = IsDowned;
        usedDisciplines.Clear();
    }

    public void ClearShields() => shields = 0;

    /// <summary>Shields absorb first. Returns true if any damage got through.</summary>
    public bool TakeHit(int damage)
    {
        int blocked = Mathf.Min(shields, damage);
        shields -= blocked;
        damage -= blocked;
        if (damage <= 0) return false;

        resolve = Mathf.Max(0, resolve - damage);
        GetComponent<CharacterResolveManager>().UpdateSlider(resolve, maxResolve);
        if (IsDowned)
        {
            actionsRemaining = 0;
            SetFainted(true);
            Debug.Log($"{heroineName} is Downed!");
        }
        return true;
    }

    public void Revive()
    {
        SetFainted(false);
        resolve = 1;
        hasBeenRevived = true;
    }

    public void ResetForEncounter()
    {
        SetFainted(false);
        SetWalking(false); // in case a restart interrupted a walk
        resolve = maxResolve;
        shields = 0;
        hasBeenRevived = false;
        BeginTurn();
    }

    public void SetSelected(bool selected)
    {
        if (selectionHighlight != null) selectionHighlight.SetActive(selected);
    }
    /// <summary>Plays the cast animation for a discipline. Returns to Idle by itself.</summary>
    public void PlayCast(Discipline d, GridUnit target = null)
    {
        if (IsDowned) return;
        if (d == Discipline.Arcana && spellImpact != null) spellImpact.Arm(target);
        if (animator == null) return;
        animator.SetTrigger(d switch
        {
            Discipline.Arcana => ArcanaHash,
            Discipline.Warding => WardingHash,
            Discipline.Lore => LoreHash,
            _ => OccultismHash
        });
    }

    private void SetFainted(bool fainted)
    {
        if (animator != null) animator.SetBool(FaintedHash, fainted);
    }
    private void SetWalking(bool walking)
    {
        if (animator != null) animator.SetBool(WalkingHash, walking);
    }
    /// <summary>Turns on the spot (in Idle) until she faces the target. Does nothing if she targets herself.</summary>
    public IEnumerator TurnToFace(GridUnit target)
    {
        if (target == null || target == this) yield break;

        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) yield break;

        Quaternion facing = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, modelYawOffset, 0f);
        if (Quaternion.Angle(transform.rotation, facing) <= 1f) yield break;

        SetWalking(false);
        // Time limit, in case something else (e.g. the Animator) keeps overriding her rotation.
        for (float t = 0f; t < maxTurnSeconds && Quaternion.Angle(transform.rotation, facing) > 0.5f; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * Time.deltaTime);
            yield return null;
        }
        transform.rotation = facing;
    }

    /// <summary>
    /// Walks a path tile by tile: if the next tile is in a new direction she stops (Idle),
    /// turns on the spot, then walks (isWalking) to it. Straight runs are walked without stopping.
    /// </summary>
    public IEnumerator WalkPath(List<Vector2Int> path)
    {
        foreach (var tile in path)
        {
            Vector3 dir = Grid.GridToWorld(tile) - Grid.GridToWorld(GridPosition);
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion facing = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, modelYawOffset, 0f);
                if (Quaternion.Angle(transform.rotation, facing) > 1f)
                {
                    SetWalking(false); // back to Idle while turning
                    while (Quaternion.Angle(transform.rotation, facing) > 0.5f)
                    {
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * Time.deltaTime);
                        yield return null;
                    }
                    transform.rotation = facing;
                }
            }

            SetWalking(true);
            yield return StepTo(tile, walkStepDuration);
        }
        SetWalking(false);
    }
}
