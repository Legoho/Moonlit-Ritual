using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Learnable abilities (AbilityDefinition assets): checking, targeting, rolling and applying them.
/// This is the second half of RitualManager (a partial class), so RitualManager.cs stays as it is
/// apart from the word "partial".
/// </summary>
public partial class RitualManager
{
    // =====================================================================
    // Can she cast it?
    // =====================================================================

    /// <summary>True if the active heroine can cast this ability now; otherwise the reason why not.</summary>
    public bool CanCastAbility(AbilityDefinition a, out string reason)
    {
        reason = null;
        if (a == null) { reason = "No ability."; return false; }
        if (Phase != CombatPhase.PlayerPhase) { reason = "Wait for the enemies to finish."; return false; }
        if (IsBusy) { reason = "Wait for the current action."; return false; }
        var h = ActiveHeroine;
        if (h == null) { reason = "Select a heroine first."; return false; }
        if (!h.Knows(a)) { reason = $"{h.DisplayName} doesn't know {a.displayName}."; return false; }
        if (!h.CanAct) { reason = $"{h.DisplayName} has no Actions left."; return false; }
        if (h.HasUsed(a.discipline)) { reason = $"{h.DisplayName} already used {a.discipline} this turn."; return false; }

        if (a.requirement == AbilityRequirement.AdjacentToAnchor && !anchor.IsAdjacent(h.GridPosition))
        { reason = "She must stand next to the Anchor."; return false; }

        switch (a.target)
        {
            case AbilityTarget.None:
                return true;
            case AbilityTarget.Anchor:
                if (!InAbilityRange(h, a, anchor.GridPosition)) { reason = "The Anchor is out of range."; return false; }
                return true;
            default:
                if (!AllUnits().Any(u => IsValidAbilityTarget(h, a, u)))
                { reason = a.target == AbilityTarget.Enemy ? "No enemy within range." : "No valid target within range."; return false; }
                return true;
        }
    }

    private bool InAbilityRange(Heroine h, AbilityDefinition a, Vector2Int tile)
    {
        if (a.straightLineOnly && tile.x != h.GridPosition.x && tile.y != h.GridPosition.y) return false;
        return GridManager.RangeDistance(h.GridPosition, tile) <= a.RangeFor(h) + 0.01f;
    }

    private bool IsValidAbilityTarget(Heroine h, AbilityDefinition a, GridUnit u)
    {
        if (u == null || !u.IsOnGrid) return false;
        bool isSelf = u == h;
        bool isAlly = u is Heroine ally && !ally.IsDowned && (!isSelf || a.includeSelf);
        bool isEnemy = u is MonsterHolder;
        bool isCivilian = u is Civilian civ && !civ.IsLost;

        bool kindOk = a.target switch
        {
            AbilityTarget.Enemy => isEnemy,
            AbilityTarget.Ally => isAlly,
            AbilityTarget.AllyOrCivilian => isAlly || isCivilian,
            AbilityTarget.AnyUnit => isEnemy || (u is Heroine other && !other.IsDowned && !isSelf),
            _ => false
        };
        if (!kindOk) return false;
        return isSelf || InAbilityRange(h, a, u.GridPosition);
    }

    // =====================================================================
    // Casting
    // =====================================================================

    /// <summary>Starts an ability chosen in a spell book. If it can't be used, logs why.</summary>
    public void CastAbility(AbilityDefinition a)
    {
        if (!CanCastAbility(a, out string reason))
        {
            Log(reason);
            return;
        }
        var h = ActiveHeroine;

        if (!a.NeedsClick)
        {
            ClearInputMode();
            GridUnit face = a.target == AbilityTarget.Anchor || a.requirement == AbilityRequirement.AdjacentToAnchor ? anchor : null;
            RollAbility(h, a, a.target == AbilityTarget.Anchor ? anchor : null, face);
            return;
        }

        bool harmful = a.target == AbilityTarget.Enemy;
        bool started = BeginTargeting(a.displayName,
            u => IsValidAbilityTarget(h, a, u),
            u => RollAbility(h, a, u, u),
            harmful ? damageColor : supportColor);
        if (!started) return;

        // Kill chances, like Arcane Strike.
        if (a.HasEffect(AbilityEffectType.Damage))
        {
            int pool = h.GetPool(a.discipline);
            foreach (var m in validTargets.OfType<MonsterHolder>())
            {
                int need = Mathf.Max(1, m.CurrentResolve - (m.isMarked ? 1 : 0));
                Log($"  {m.DisplayName}: Resolve {m.CurrentResolve}{(m.isMarked ? " (marked)" : "")}, " +
                    $"kill chance {dice.ChanceToReach(pool, need, a.CriticalDamageBonus, h.ThresholdOffset(a.discipline)):P0} with {pool}d8.");
            }
        }
    }

    private void RollAbility(Heroine h, AbilityDefinition a, GridUnit target, GridUnit face)
    {
        // Reveal is guaranteed, like Study's: it happens before the dice.
        if (a.effects.Any(e => e.type == AbilityEffectType.Reveal && e.trigger == EffectTrigger.Always))
            foreach (var m in ScopeUnits(AbilityEffectType.Reveal, EffectScope.Target, h, a, target).OfType<MonsterHolder>())
                Log($"{a.displayName} reveals: {EnemyPlanner.Describe(intents.FirstOrDefault(i => i.monster == m))}");

        Log($"{h.DisplayName} casts {a.displayName}{(target != null && target != h ? $" on {target.DisplayName}" : "")}.");
        RollThen(h, a.discipline, face, roll => ResolveAbility(h, a, target, roll));
    }

    // =====================================================================
    // Resolution
    // =====================================================================

    private void ResolveAbility(Heroine h, AbilityDefinition a, GridUnit target, DiceRollResult roll)
    {
        if (h == null) return;
        if (roll.Successes <= 0)
        {
            Log($"{a.displayName}: no Successes, no effect.");
            return;
        }
        int s = roll.Successes;
        var active = a.effects.Where(e => e != null && e.Applies(roll)).ToList();

        // 1. Damage, added up per enemy and checked once against its Resolve (GDD 5.7).
        var damage = new Dictionary<MonsterHolder, int>();
        foreach (var e in active.Where(x => x.type == AbilityEffectType.Damage))
            foreach (var m in ScopeUnits(e.type, e.scope, h, a, target).OfType<MonsterHolder>())
                damage[m] = (damage.TryGetValue(m, out int d) ? d : 0) + e.Amount(s);

        foreach (var pair in damage)
        {
            var m = pair.Key;
            if (m == null) continue;
            int dmg = pair.Value, need = m.CurrentResolve;
            if (dmg > 0 && m.isMarked) { dmg++; m.isMarked = false; }

            if (dmg >= need)
            {
                Log($"{a.displayName} deals {dmg} to {m.DisplayName} (Resolve {need}): defeated!");
                KillMonster(m);
            }
            else Log($"{a.displayName} deals {dmg} to {m.DisplayName}, short of Resolve {need}: the damage is lost.");
        }

        // 2. Everything else, in list order. Defeated enemies are skipped.
        bool capacityRestored = false;
        foreach (var e in active.Where(x => x.type != AbilityEffectType.Damage && x.type != AbilityEffectType.Reveal))
        {
            int amount = e.Amount(s);
            var units = ScopeUnits(e.type, e.scope, h, a, target).Where(x => x != null && x.IsOnGrid).ToList();

            switch (e.type)
            {
                case AbilityEffectType.Push:
                    foreach (var u in units) ForceMove(u, AwayFrom(h.GridPosition, u.GridPosition), amount, "pushed");
                    break;

                case AbilityEffectType.Pull:
                    foreach (var u in units) ForceMove(u, -AwayFrom(h.GridPosition, u.GridPosition), amount, "pulled");
                    break;

                case AbilityEffectType.Swap:
                    foreach (var other in units.Where(x => x != h).Take(1)) SwapUnits(h, other);
                    break;

                case AbilityEffectType.Shield:
                    foreach (var u in units) AddShields(u, amount, a.displayName);
                    break;

                case AbilityEffectType.Stability:
                {
                    int gained = ritual.Stabilize(amount);
                    Log($"{a.displayName}: +{gained} Stability ({ritual.stability}, {ritual.StabilityState}).");
                    break;
                }

                case AbilityEffectType.Progress:
                {
                    int gained = ritual.AddProgress(amount);
                    Log($"{a.displayName}: +{gained} Progress{LostToStability(amount, gained)} ({ritual.progress}/{ritual.progressTarget}).");
                    break;
                }

                case AbilityEffectType.Insight:
                    foreach (var type in units.OfType<MonsterHolder>().Select(m => m.Data).Where(t => t != null && !IsSolved(t)).Distinct())
                        AddInsight(type, amount);
                    break;

                case AbilityEffectType.Mark:
                    foreach (var m in units.OfType<MonsterHolder>())
                    {
                        m.isMarked = true;
                        Log($"{m.DisplayName} is marked (+1 to the next damage against it).");
                    }
                    break;

                case AbilityEffectType.Delay:
                    foreach (var m in units.OfType<MonsterHolder>())
                    {
                        var intent = intents.FirstOrDefault(i => i.monster == m);
                        if (intent == null) continue;
                        intent.delayed = true;
                        Log($"{m.DisplayName} will act last this enemy phase.");
                    }
                    break;

                case AbilityEffectType.RestoreTurnCapacity:
                    if (!capacityRestored && ritual.TryRestoreCapacity())
                    {
                        capacityRestored = true;
                        Log($"{a.displayName}: 1 Turn Capacity restored ({ritual.turnCapacity}).");
                    }
                    break;

                case AbilityEffectType.ExtraMove:
                    foreach (var ally in units.OfType<Heroine>())
                    {
                        ally.movementRemaining += amount;
                        Log($"{ally.DisplayName} gains {amount} Move.");
                    }
                    break;
            }
        }

        if (roll.IsOvercharged && !active.Any(e => e.trigger == EffectTrigger.OnOvercharge))
            Log("Overcharge!");
    }

    /// <summary>The units an effect applies to.</summary>
    private IEnumerable<GridUnit> ScopeUnits(AbilityEffectType type, EffectScope scope, Heroine h, AbilityDefinition a, GridUnit target)
    {
        switch (scope)
        {
            case EffectScope.Caster:
                yield return h;
                break;

            case EffectScope.Target:
                if (target != null) yield return target;
                else if (type == AbilityEffectType.Shield || type == AbilityEffectType.ExtraMove) yield return h; // self-cast
                break;

            case EffectScope.AlliesAdjacentToCaster:
                foreach (var ally in turnManager.InCombat)
                    if (ally != h && GridManager.Distance(ally.GridPosition, h.GridPosition) == 1) yield return ally;
                break;

            case EffectScope.EnemiesAdjacentToTarget:
                if (target == null) break;
                foreach (var m in monsters.ToList())
                    if (m != null && m != target && GridManager.Distance(m.GridPosition, target.GridPosition) == 1) yield return m;
                break;

            case EffectScope.NextEnemyInLine:
            {
                if (target == null) break;
                Vector2Int dir = AwayFrom(h.GridPosition, target.GridPosition);
                if (dir == Vector2Int.zero) break;
                for (var t = target.GridPosition + dir; grid.InBounds(t) && InAbilityRange(h, a, t); t += dir)
                {
                    var occupant = grid.GetOccupant(t);
                    if (occupant is MonsterHolder m) { yield return m; break; }
                }
                break;
            }

            case EffectScope.AllEnemiesInRange:
                foreach (var m in monsters.ToList())
                    if (m != null && GridManager.RangeDistance(h.GridPosition, m.GridPosition) <= a.RangeFor(h) + 0.01f) yield return m;
                break;

            case EffectScope.AllAllies:
                foreach (var ally in turnManager.InCombat) yield return ally;
                break;
        }
    }

    // =====================================================================
    // Board helpers
    // =====================================================================

    /// <summary>One orthogonal step pointing from 'from' toward 'to' (along the longer axis).</summary>
    private static Vector2Int AwayFrom(Vector2Int from, Vector2Int to)
    {
        Vector2Int delta = to - from;
        if (delta == Vector2Int.zero) return Vector2Int.zero;
        return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
            ? new Vector2Int(Math.Sign(delta.x), 0)
            : new Vector2Int(0, Math.Sign(delta.y));
    }

    /// <summary>Forced movement of any unit (enemy or heroine). Stops at the first blocked tile.</summary>
    private void ForceMove(GridUnit u, Vector2Int dir, int tiles, string verb)
    {
        if (u == null || u == anchor || dir == Vector2Int.zero || tiles <= 0) return;
        int moved = 0;
        for (int i = 0; i < tiles; i++)
        {
            var next = u.GridPosition + dir;
            if (!grid.IsFree(next)) break;
            u.PlaceAt(next);
            moved++;
        }
        Log(moved > 0 ? $"{u.DisplayName} is {verb} {moved} tile(s)." : $"{u.DisplayName} cannot be {verb} (blocked).");
        if (moved > 0) AfterForcedMove(u);
    }

    private void SwapUnits(GridUnit a, GridUnit b)
    {
        if (a == null || b == null || a == anchor || b == anchor) return;
        Vector2Int pa = a.GridPosition, pb = b.GridPosition;
        a.RemoveFromGrid();
        b.RemoveFromGrid();
        a.PlaceAt(pb);
        b.PlaceAt(pa);
        Log($"{a.DisplayName} and {b.DisplayName} swap places.");
        AfterForcedMove(a);
        AfterForcedMove(b);
    }

    private void AfterForcedMove(GridUnit u)
    {
        if (u is MonsterHolder m) ReplanIntent(m);
        // Heroines keep being the target; RefreshIntents (after the action) shows whether the hit still lands.
    }

    private void AddShields(GridUnit u, int amount, string source)
    {
        if (amount <= 0) return;
        switch (u)
        {
            case Heroine ally: ally.shields += amount; break;
            case Civilian civ: civ.shields += amount; break;
            case RitualAnchor: anchor.shields += amount; break;
            default: return; // enemies don't get Shields
        }
        Log($"{source}: {u.DisplayName} gains {amount} Shield(s).");
    }
}
