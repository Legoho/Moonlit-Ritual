using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Moonlit Ritual > Create Starter Omens, Relics and Objectives
/// Creates the GDD Omens, a first set of Relics, and an ObjectiveDefinition for every objective,
/// in Assets/ScriptableObjects/Omens, /Relics and /Objectives. Existing assets are left alone,
/// so your own tuning is never overwritten. If a RunManager is in the open scene, the Omens and
/// Relics are added to its pools.
/// </summary>
public static class StarterOmensRelicsCreator
{
    private const string Root = "Assets/ScriptableObjects";

    [MenuItem("Tools/Moonlit Ritual/Create Starter Omens, Relics and Objectives")]
    public static void Create()
    {
        var omens = Omens().Select(o => Save($"{Root}/Omens", o.name, o)).ToList();
        var relics = Relics().Select(r => Save($"{Root}/Relics", r.name, r)).ToList();
        var objectives = Objectives().Select(d => Save($"{Root}/Objectives", d.name, d)).ToList();

        var run = UnityEngine.Object.FindAnyObjectByType<RunManager>(FindObjectsInactive.Include);
        if (run != null)
        {
            Undo.RecordObject(run, "Fill Omen and Relic pools");
            foreach (var o in omens) if (!run.omenPool.Contains(o)) run.omenPool.Add(o);
            foreach (var r in relics) if (!run.relicPool.Contains(r)) run.relicPool.Add(r);
            EditorUtility.SetDirty(run);
            EditorSceneManager.MarkSceneDirty(run.gameObject.scene);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Starter] {omens.Count} Omens, {relics.Count} Relics, {objectives.Count} Objective definitions ready in {Root}." +
                  (run != null ? " Omen and Relic pools filled on the RunManager in this scene." : " No RunManager in the open scene: fill its Omen Pool and Relic Pool by hand."));
    }

    /// <summary>Saves the asset if no asset exists at that path yet; returns the asset at that path.</summary>
    private static T Save<T>(string folder, string file, T asset) where T : ScriptableObject
    {
        EnsureFolder(folder);
        string path = $"{folder}/{file}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) { UnityEngine.Object.DestroyImmediate(asset); return existing; }
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        string parent = folder.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
    }

    // =====================================================================
    // Omens (GDD "Omens" table). Brittle Shield and Hunted need combat rules that aren't built yet.
    // =====================================================================

    private static Omen NewOmen(string file, string name, EnemyCategory family, OmenScope scope, string text, string flavour,
                                Action<OmenRules> rules = null, Action<RunEffects> team = null)
    {
        var o = ScriptableObject.CreateInstance<Omen>();
        o.name = file;
        o.displayName = name;
        o.family = family;
        o.scope = scope;
        o.description = text;
        o.flavour = flavour;
        rules?.Invoke(o.rules);
        team?.Invoke(o.teamEffects);
        return o;
    }

    private static IEnumerable<Omen> Omens()
    {
        // Assault: body and defence
        yield return NewOmen("Assault_FracturedWard", "Fractured Ward", EnemyCategory.Assault, OmenScope.Personal,
            "She starts every mission with 1 less Resolve.", "Something in her guard did not heal.",
            r => r.maxResolve = -1);
        yield return NewOmen("Assault_CrackedHourglass", "Cracked Hourglass", EnemyCategory.Assault, OmenScope.Shared,
            "Shared: any team she deploys with starts with 1 less Turn Capacity (never below 10).", "The circle remembers her fall.",
            team: t => t.turnCapacity = -1);

        // Sabotage: ritual work
        yield return NewOmen("Sabotage_HollowVoice", "Hollow Voice", EnemyCategory.Sabotage, OmenScope.Personal,
            "Her Channel Critical gives +1 Progress instead of +2.", "Her voice carries, but not as far.",
            r => r.channelCritBonus = -1);
        yield return NewOmen("Sabotage_DimCircle", "Dim Circle", EnemyCategory.Sabotage, OmenScope.Personal,
            "She cannot Stabilize. Her Guard on the Anchor only gives Shields.", "The circle will not answer her.",
            r => r.cannotStabilize = true);
        yield return NewOmen("Sabotage_BleedingSigil", "Bleeding Sigil", EnemyCategory.Sabotage, OmenScope.Personal,
            "When she is Downed, the ritual loses 3 Progress.", "The mark opens when she falls.",
            r => r.progressLostWhenDowned = 3);
        yield return NewOmen("Sabotage_ThinCircle", "Thin Circle", EnemyCategory.Sabotage, OmenScope.Shared,
            "Shared: any team she deploys with starts with 1 less Stability.", "The chalk lines fade around her.",
            team: t => t.startingStability = -1);

        // Control: movement and focus
        yield return NewOmen("Control_HeavyStep", "Heavy Step", EnemyCategory.Control, OmenScope.Personal,
            "Her Move is 1 shorter.", "The chains are gone. The weight is not.",
            r => r.moveRange = -1);
        yield return NewOmen("Control_CloudedSight", "Clouded Sight", EnemyCategory.Control, OmenScope.Personal,
            "Her Lore range is 1 shorter.", "Fog lingers at the edge of her vision.",
            r => r.loreRange = -1);
        yield return NewOmen("Control_TremblingHand", "Trembling Hand", EnemyCategory.Control, OmenScope.Personal,
            "In one discipline (chosen when she receives it), her Successes need 7+ instead of 6+. Shown in every roll.",
            "Her hand shakes when she reaches for that magic.",
            r => { r.successThreshold = 1; r.randomThresholdDiscipline = true; });
        yield return NewOmen("Control_RestlessDead", "Restless Dead", EnemyCategory.Control, OmenScope.Shared,
            "Shared: on any team she deploys with, the first reinforcements arrive 1 round earlier (still telegraphed).",
            "The dead follow her home.",
            team: t => t.reinforcementsEarlier = 1);
    }

    // =====================================================================
    // Relics: found team buffs (first set, PROVISIONAL values)
    // =====================================================================

    private static Relic NewRelic(string file, string name, RelicRarity rarity, string text, string flavour, Action<RunEffects> fx)
    {
        var r = ScriptableObject.CreateInstance<Relic>();
        r.name = file;
        r.displayName = name;
        r.rarity = rarity;
        r.description = text;
        r.flavour = flavour;
        fx(r.effects);
        return r;
    }

    private static IEnumerable<Relic> Relics()
    {
        yield return NewRelic("Relic_SteadyChalk", "Steady Chalk", RelicRarity.Common,
            "The ritual starts with +1 Stability.", "Drawn once, it never smudges.", e => e.startingStability = 1);
        yield return NewRelic("Relic_SilverHourglass", "Silver Hourglass", RelicRarity.Common,
            "+1 starting Turn Capacity.", "Its sand falls a little slower at night.", e => e.turnCapacity = 1);
        yield return NewRelic("Relic_ChoirBell", "Choir Bell", RelicRarity.Common,
            "Channel Criticals give +1 more Progress.", "It rings on its own when the circle sings.", e => e.channelCritBonus = 1);
        yield return NewRelic("Relic_MoonstoneLantern", "Moonstone Lantern", RelicRarity.Rare,
            "Maximum Stability +1.", "A little of the goddess's light, kept in glass.", e => e.maxStability = 1);
        yield return NewRelic("Relic_WardensThread", "Warden's Thread", RelicRarity.Rare,
            "Warding Criticals can restore Turn Capacity one more time each round.", "Mended twice, never broken.", e => e.capacityRestoresPerRound = 1);
        yield return NewRelic("Relic_SecondCandle", "Second Candle", RelicRarity.Rare,
            "Once per encounter, a roll with 0 Successes is rerolled.", "When the first flame dies, light the second.", e => e.emptyRollRerolls = 1);
        yield return NewRelic("Relic_WayfarersSigil", "Wayfarer's Sigil", RelicRarity.Legendary,
            "Every heroine gets +1 Move.", "The paths of the academy remember its bearers.", e => e.moveRange = 1);
    }

    // =====================================================================
    // Objective definitions (texts and rewards for every objective component)
    // =====================================================================

    private static ObjectiveDefinition NewObjective(string file, string title, string text, int moonlight, ObjectiveBonus bonus, params Discipline[] disciplines)
    {
        var d = ScriptableObject.CreateInstance<ObjectiveDefinition>();
        d.name = file;
        d.title = title;
        d.description = text;
        d.moonlightReward = moonlight;
        d.bonus = bonus;
        d.disciplines = disciplines.ToList();
        return d;
    }

    private static IEnumerable<ObjectiveDefinition> Objectives()
    {
        yield return NewObjective("Objective_LostGrimoire", "Find the Lost Grimoire",
            "A grimoire was lost behind the enemy lines. Retrieve it and bring it back to the Anchor.",
            2, new ObjectiveBonus { insightAllEnemyTypes = 2 }, Discipline.Lore);
        yield return NewObjective("Objective_SaveCivilians", "Save the Civilians",
            "Students and their pets are hiding from the shadows. Keep them alive until the ritual is complete.",
            2, new ObjectiveBonus { restoreTurnCapacity = 1 }, Discipline.Warding);
        yield return NewObjective("Objective_StopSummoning", "Stop the Summoning",
            "The shadows pour out of summoning circles. Seal them with Occultism while standing next to them.",
            2, new ObjectiveBonus { stability = 2 }, Discipline.Occultism);
        yield return NewObjective("Objective_RestlessSpirit", "The Restless Spirit",
            "A lost student's spirit drifts towards the summoning circles. Study it with Lore to lay it to rest before it is consumed.",
            2, new ObjectiveBonus { healResolve = 1, shieldsForParty = 1 }, Discipline.Lore);
        yield return NewObjective("Objective_EliteGuardian", "Elite Guardian",
            "An empowered shadow guards a relic cache. Defeat it: Solve it first, or land a big Arcana roll.",
            2, new ObjectiveBonus { relics = 1 }, Discipline.Arcana, Discipline.Lore);
        yield return NewObjective("Objective_DefendPosition", "Defend the Position",
            "A ward-stone protects the circle. Keep it standing until the ritual is complete; while it stands, the Anchor gains 1 Shield each round.",
            1, new ObjectiveBonus(), Discipline.Warding);
    }
}
