using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Moonlit Ritual > Create Starter Abilities
/// Creates one signature ability per heroine (converted from the GDD, section 7) and two library
/// abilities anyone can learn, in Assets/ScriptableObjects/Abilities. Existing assets are left alone,
/// so your own tuning is never overwritten. Signature abilities are also added to the Starting
/// Abilities of the HeroineDefinition whose Display Name matches.
/// </summary>
public static class StarterAbilitiesCreator
{
    private const string Folder = "Assets/ScriptableObjects/Abilities";

    private class Spec
    {
        public string file, name, heroine, description, flavour;
        public Discipline discipline;
        public AbilityTarget target;
        public bool includeSelf = true, line;
        public float range = -1f;
        public AbilityRequirement requirement;
        public AbilitySource source = AbilitySource.Signature;
        public List<AbilityEffect> effects;
    }

    [MenuItem("Tools/Moonlit Ritual/Create Starter Abilities")]
    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects"))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Abilities");

        var heroines = AssetDatabase.FindAssets("t:HeroineDefinition")
            .Select(g => AssetDatabase.LoadAssetAtPath<HeroineDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(h => h != null).ToList();

        int created = 0, linked = 0;
        foreach (var spec in Specs())
        {
            string path = $"{Folder}/{spec.file}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AbilityDefinition>();
                asset.displayName = spec.name;
                asset.description = spec.description;
                asset.flavour = spec.flavour;
                asset.discipline = spec.discipline;
                asset.target = spec.target;
                asset.includeSelf = spec.includeSelf;
                asset.range = spec.range;
                asset.straightLineOnly = spec.line;
                asset.requirement = spec.requirement;
                asset.source = spec.source;
                asset.effects = spec.effects;
                AssetDatabase.CreateAsset(asset, path);
                created++;
            }

            if (spec.heroine == null) continue;
            var owner = heroines.FirstOrDefault(h =>
                (h.displayName ?? "").IndexOf(spec.heroine, System.StringComparison.OrdinalIgnoreCase) >= 0
                || h.name.IndexOf(spec.heroine, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (owner == null)
            {
                Debug.Log($"[Abilities] No HeroineDefinition named {spec.heroine}; add {spec.name} to her Starting Abilities by hand.");
                continue;
            }
            if (!asset.learnableBy.Contains(owner)) { asset.learnableBy.Add(owner); EditorUtility.SetDirty(asset); }
            if (!owner.startingAbilities.Contains(asset))
            {
                owner.startingAbilities.Add(asset);
                EditorUtility.SetDirty(owner);
                linked++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Abilities] {created} ability asset(s) created in {Folder}, {linked} added to heroines' Starting Abilities.");
    }

    private static IEnumerable<Spec> Specs()
    {
        // ---- Signature abilities, one per heroine (GDD 7, converted to Successes and Criticals) ----

        yield return new Spec
        {
            file = "Kaia_WhiteFlameLance", name = "White-Flame Lance", heroine = "Kaia",
            discipline = Discipline.Arcana, target = AbilityTarget.Enemy, range = 4f, line = true,
            description = "An enemy in a straight line, up to 4 tiles: 1 damage per Success, and it is pushed 1 tile if it survives.\n" +
                          "Critical: +1 damage.\nOvercharge: the lance goes through and hits the next enemy in the line too.",
            flavour = "White fire does not ask permission.",
            effects = new()
            {
                new(AbilityEffectType.Damage, 0, 1),
                new(AbilityEffectType.Damage, 1, 0, trigger: EffectTrigger.OnCritical),
                new(AbilityEffectType.Damage, 0, 1, EffectScope.NextEnemyInLine, EffectTrigger.OnOvercharge),
                new(AbilityEffectType.Push, 1),
            }
        };

        yield return new Spec
        {
            file = "Mira_ShepherdsPull", name = "Shepherd's Pull", heroine = "Mira",
            discipline = Discipline.Lore, target = AbilityTarget.AnyUnit, range = 3f,
            description = "Pull an ally or an enemy within 3 tiles toward Mira: 1 tile per Success, at most 2.\n" +
                          "Critical: a pulled ally also gains 1 Shield.\nOvercharge: Mira gains 1 Move.",
            flavour = "Come here. Stay close.",
            effects = new()
            {
                new(AbilityEffectType.Pull, 0, 1, maxAmount: 2),
                new(AbilityEffectType.Shield, 1, 0, trigger: EffectTrigger.OnCritical),
                new(AbilityEffectType.ExtraMove, 1, 0, EffectScope.Caster, EffectTrigger.OnOvercharge),
            }
        };

        yield return new Spec
        {
            file = "Rosalia_RefrainOfBinding", name = "Refrain of Binding", heroine = "Rosalia",
            discipline = Discipline.Occultism, target = AbilityTarget.None, requirement = AbilityRequirement.AdjacentToAnchor,
            description = "Next to the Anchor: +1 Progress per Success, +1 more (limited by Stability).\n" +
                          "Critical: the song steadies the circle first: +1 Stability before the Progress is added.\n" +
                          "Overcharge: +2 more Progress.",
            flavour = "Sing it again, and the circle remembers.",
            effects = new()
            {
                // Order matters: Stability first, so the extra point already counts for this Progress.
                new(AbilityEffectType.Stability, 1, 0, trigger: EffectTrigger.OnCritical),
                new(AbilityEffectType.Progress, 1, 1),
                new(AbilityEffectType.Progress, 2, 0, trigger: EffectTrigger.OnOvercharge),
            }
        };

        yield return new Spec
        {
            file = "Iona_ArchiveMark", name = "Archive Mark", heroine = "Iona",
            discipline = Discipline.Lore, target = AbilityTarget.Enemy, range = 4f,
            description = "Reveals an enemy's full intent within 4 tiles, then (with at least 1 Success) Marks it and adds 1 Insight.\n" +
                          "Critical: also Marks every enemy next to it.\nOvercharge: it acts last this enemy phase.",
            flavour = "Catalogued. Cross-referenced. Doomed.",
            effects = new()
            {
                new(AbilityEffectType.Reveal, 0),
                new(AbilityEffectType.Mark, 0),
                new(AbilityEffectType.Insight, 1),
                new(AbilityEffectType.Mark, 0, 0, EffectScope.EnemiesAdjacentToTarget, EffectTrigger.OnCritical),
                new(AbilityEffectType.Delay, 0, 0, trigger: EffectTrigger.OnOvercharge),
            }
        };

        yield return new Spec
        {
            file = "Elin_TwilightExchange", name = "Twilight Exchange", heroine = "Elin",
            discipline = Discipline.Lore, target = AbilityTarget.Ally, includeSelf = false, range = 4f,
            description = "Swap places with an ally within 4 tiles.\n" +
                          "Critical: the ally gains 1 Shield.\nOvercharge: both gain 1 Move.",
            flavour = "Where the sun sets, the moon rises.",
            effects = new()
            {
                new(AbilityEffectType.Swap, 0),
                new(AbilityEffectType.Shield, 1, 0, trigger: EffectTrigger.OnCritical),
                new(AbilityEffectType.ExtraMove, 1, 0, EffectScope.Caster, EffectTrigger.OnOvercharge),
                new(AbilityEffectType.ExtraMove, 1, 0, EffectScope.Target, EffectTrigger.OnOvercharge),
            }
        };

        // ---- Library abilities: anyone can learn them (research) ----

        yield return new Spec
        {
            file = "Library_MoonlitCircle", name = "Moonlit Circle", source = AbilitySource.Research,
            discipline = Discipline.Warding, target = AbilityTarget.None,
            description = "She and every ally next to her gain 1 Shield. With 3 or more Successes, 1 more each.\n" +
                          "Overcharge: restore 1 Turn Capacity.",
            flavour = "Stand together and the light holds.",
            effects = new()
            {
                new(AbilityEffectType.Shield, 1, 0, EffectScope.Caster),
                new(AbilityEffectType.Shield, 1, 0, EffectScope.AlliesAdjacentToCaster),
                new(AbilityEffectType.Shield, 1, 0, EffectScope.Caster, minSuccesses: 3),
                new(AbilityEffectType.Shield, 1, 0, EffectScope.AlliesAdjacentToCaster, minSuccesses: 3),
                new(AbilityEffectType.RestoreTurnCapacity, 0, 0, EffectScope.Caster, EffectTrigger.OnOvercharge),
            }
        };

        yield return new Spec
        {
            file = "Library_StarchartReading", name = "Starchart Reading", source = AbilitySource.Research,
            discipline = Discipline.Lore, target = AbilityTarget.None, range = 3f,
            description = "+1 Insight to every enemy type within 3 tiles.\n" +
                          "Critical: +1 more Insight to each.\nOvercharge: every enemy in range acts last this enemy phase.",
            flavour = "Every shadow follows a pattern.",
            effects = new()
            {
                new(AbilityEffectType.Insight, 1, 0, EffectScope.AllEnemiesInRange),
                new(AbilityEffectType.Insight, 1, 0, EffectScope.AllEnemiesInRange, EffectTrigger.OnCritical),
                new(AbilityEffectType.Delay, 0, 0, EffectScope.AllEnemiesInRange, EffectTrigger.OnOvercharge),
            }
        };
    }
}
