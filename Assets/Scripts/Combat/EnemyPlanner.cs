using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum IntentTargetType { None, Anchor, Heroine }

/// <summary>
/// A telegraphed enemy action (GDD 2.1): who it targets and the path it will walk this round.
/// The target and path are fixed when planned. The predicted* fields are recomputed after every
/// player change, using the same stepping rule the enemy phase uses, so preview and result match.
/// </summary>
[Serializable]
public class EnemyIntent
{
    public MonsterHolder monster;
    public IntentTargetType targetType;
    public GridUnit target;
    public Heroine TargetHeroine => target as Heroine;

    [Tooltip("Tiles to walk this round, already cut to the monster's movement.")]
    public List<Vector2Int> path = new();
    [Tooltip("True if the target is in range by the end of the planned path.")]
    public bool plannedHit;
    [Tooltip("Study Overcharge: acts last this enemy phase.")]
    public bool delayed;

    [Header("Prediction")]
    public int predictedSteps;
    public bool predictedHit;
}

/// <summary>Plans, predicts and describes enemy intents. Pure logic, no scene state of its own.</summary>
public static class EnemyPlanner
{
    private class Option
    {
        public GridUnit target;
        public IntentTargetType type;
        public bool reachable;
        public int steps;          // steps needed to get into range
        public Vector2Int tile;    // tile it will attack from
        public int movement;
        public bool CanHitThisRound => reachable && steps <= movement;
    }

    /// <summary>
    /// Decides whether the monster goes for the Anchor or a heroine.
    /// A target counts as hittable this round if some tile within the monster's movement
    /// (walking only through free tiles) is within its attack range of the target.
    /// Heroines standing in the way block paths, so surrounding the Anchor forces enemies onto the girls.
    /// </summary>
    public static EnemyIntent Plan(MonsterHolder monster, GridManager grid, RitualAnchor anchor,
        IEnumerable<Heroine> heroines, ISet<Vector2Int> reservedTiles)
    {
        var intent = new EnemyIntent { monster = monster, targetType = IntentTargetType.None };
        if (monster == null || grid == null) return intent;

        Vector2Int start = monster.GridPosition;
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var dist = grid.Flood(start, -1,
            t => grid.IsFree(t) && (reservedTiles == null || !reservedTiles.Contains(t)), cameFrom);

        Option Evaluate(GridUnit target, IntentTargetType type)
        {
            var o = new Option { target = target, type = type, movement = monster.Movement, steps = int.MaxValue };
            foreach (var kv in dist)
            {
                int range = GridManager.Distance(kv.Key, target.GridPosition);
                if (range >= 1 && range <= monster.AttackRange && kv.Value < o.steps)
                {
                    o.steps = kv.Value;
                    o.tile = kv.Key;
                    o.reachable = true;
                }
            }
            return o;
        }

        Option anchorOpt = anchor != null ? Evaluate(anchor, IntentTargetType.Anchor) : null;
        var heroOpts = heroines
            .Where(h => h != null && h.IsOnGrid && !h.IsDowned)
            .Select(h => Evaluate(h, IntentTargetType.Heroine))
            .Where(o => o.reachable)
            .ToList();

        Option bestHeroHit = heroOpts.Where(o => o.CanHitThisRound)
            .OrderBy(o => o.steps).ThenBy(o => ((Heroine)o.target).resolve).FirstOrDefault();
        Option nearestHero = heroOpts.OrderBy(o => o.steps).FirstOrDefault();
        bool anchorHit = anchorOpt != null && anchorOpt.CanHitThisRound;
        bool anchorReachable = anchorOpt != null && anchorOpt.reachable;

        Option chosen;
        switch (monster.Preference)
        {
            case TargetPreference.Heroine:
                chosen = bestHeroHit ?? (anchorHit ? anchorOpt : nearestHero ?? (anchorReachable ? anchorOpt : null));
                break;
            case TargetPreference.Nearest:
                chosen = new[] { anchorReachable ? anchorOpt : null, nearestHero }
                    .Where(o => o != null).OrderBy(o => o.steps).FirstOrDefault();
                break;
            default: // Ritual
                chosen = anchorHit ? anchorOpt : bestHeroHit ?? (anchorReachable ? anchorOpt : nearestHero);
                break;
        }

        if (chosen == null) return intent; // boxed in: stays put

        intent.targetType = chosen.type;
        intent.target = chosen.target;
        var fullPath = GridManager.BuildPath(cameFrom, start, chosen.tile);
        intent.path = fullPath.Take(monster.Movement).ToList();
        intent.plannedHit = chosen.CanHitThisRound;
        return intent;
    }

    /// <summary>
    /// Simulates the enemy phase in order on a virtual board and fills predictedSteps / predictedHit.
    /// Rule (same as resolution): walk the planned path tile by tile, stop before the first blocked tile,
    /// then hit if the target is within attack range of where it stopped.
    /// </summary>
    public static void Predict(IList<EnemyIntent> ordered, GridManager grid)
    {
        var vacated = new HashSet<Vector2Int>();
        var filled = new HashSet<Vector2Int>();
        bool Blocked(Vector2Int t) => (!grid.IsFree(t) && !vacated.Contains(t)) || filled.Contains(t);

        foreach (var intent in ordered)
        {
            if (intent == null || intent.monster == null) continue;

            Vector2Int start = intent.monster.GridPosition;
            vacated.Add(start);

            int steps = 0;
            foreach (var tile in intent.path)
            {
                if (Blocked(tile)) break;
                steps++;
            }

            Vector2Int end = steps > 0 ? intent.path[steps - 1] : start;
            filled.Add(end);

            intent.predictedSteps = steps;
            intent.predictedHit = CanHitFrom(intent, end);
        }
    }

    public static bool CanHitFrom(EnemyIntent intent, Vector2Int from)
    {
        if (intent.target == null) return false;
        if (intent.TargetHeroine != null && intent.TargetHeroine.IsDowned) return false;
        int range = GridManager.Distance(from, intent.target.GridPosition);
        return range >= 1 && range <= intent.monster.AttackRange;
    }

    public static string Describe(EnemyIntent intent)
    {
        if (intent == null || intent.monster == null) return "(no intent)";
        var m = intent.monster;
        if (intent.target == null) return $"{m.DisplayName}: blocked in, stays put.";

        string effect = intent.targetType == IntentTargetType.Anchor
            ? $"-{m.StabilityDamage} Turn Capacity and -Stability"
            : $"-{m.HeroineDamage} Resolve";
        string outcome = intent.predictedHit ? $"HITS ({effect})" : "falls short";
        string delayed = intent.delayed ? " [acts last]" : "";
        return $"{m.DisplayName} -> {intent.target.DisplayName}: moves {intent.predictedSteps}/{intent.path.Count} tiles, {outcome}{delayed}.";
    }
}
