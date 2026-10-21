using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

public class MonsterHolder : GridUnit
{
    [SerializeField] private Monster monsterData;
    [SerializeField] private GameObject highlight;

    [Header("Intent preview (optional)")]
    [Tooltip("Draws the telegraphed path. Use a material that supports vertex colors.")]
    [SerializeField] private LineRenderer intentLine;
    [SerializeField] private Vector3 lineOffset = new Vector3(0f, 0.1f, 0f);
    [SerializeField] private Color anchorHitColor = new Color(1f, 0.2f, 0.2f);
    [SerializeField] private Color heroineHitColor = new Color(1f, 0.6f, 0.1f);
    [SerializeField] private Color noHitColor = new Color(0.6f, 0.6f, 0.6f);

    [Header("Runtime")]
    [Tooltip("Set by a Study Critical: the next damage against it gains +1.")]
    public bool isMarked;

    public EnemyIntent Intent { get; private set; }

    public Monster Data => monsterData;
    public Monster GetMonsterData() => monsterData;

    public bool IsSolved => ritualManager != null && ritualManager.IsSolved(monsterData);
    public int CurrentResolve => IsSolved ? monsterData.solvedResistance : monsterData.resistance;
    public int Movement => monsterData.movement;
    public int AttackRange => monsterData.attackRange;
    public int StabilityDamage => monsterData.stabilityDamage;
    public int HeroineDamage => monsterData.heroineDamage;
    public TargetPreference Preference => monsterData.targetPreference;

    protected override string GetDisplayName() =>
        monsterData != null && !string.IsNullOrEmpty(monsterData.monsterName) ? monsterData.monsterName : name;

    protected override void Awake()
    {
        base.Awake();
        if (highlight != null) highlight.SetActive(false);
    }

    public void Die()
    {
        Debug.Log($"{DisplayName} is defeated.");
        RemoveFromGrid();
        Destroy(gameObject);
    }

    public void Highlight(bool isDamage = true)
    {
        if (highlight == null) return;
        highlight.SetActive(true);
        Color c = isDamage ? Color.red : Color.greenYellow;
        var lightComp = highlight.GetComponent<Light>();
        if (lightComp != null) lightComp.color = c;
        var sprite = highlight.GetComponent<SpriteRenderer>();
        if (sprite != null) sprite.color = c;
    }

    public void RemoveHighlight()
    {
        if (highlight != null) highlight.SetActive(false);
    }

    /// <summary>Stores the intent and draws its predicted path (only the tiles it will really walk).</summary>
    public void ShowIntent(EnemyIntent intent)
    {
        Intent = intent;
        if (intentLine == null) return;

        if (intent == null || Grid == null)
        {
            intentLine.positionCount = 0;
            return;
        }

        var points = new List<Vector3> { Grid.GridToWorld(GridPosition) + lineOffset };
        for (int i = 0; i < intent.predictedSteps; i++)
            points.Add(Grid.GridToWorld(intent.path[i]) + lineOffset);
        if (intent.predictedHit && intent.target != null)
            points.Add(Grid.GridToWorld(intent.target.GridPosition) + lineOffset);

        intentLine.positionCount = points.Count;
        intentLine.SetPositions(points.ToArray());

        Color c = !intent.predictedHit ? noHitColor
            : intent.targetType == IntentTargetType.Anchor ? anchorHitColor : heroineHitColor;
        intentLine.startColor = c;
        intentLine.endColor = c;
    }
}
