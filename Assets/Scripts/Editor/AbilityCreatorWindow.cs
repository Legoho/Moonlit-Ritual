using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Tools > Moonlit Ritual > Ability Creator
///
/// One window for creating and editing AbilityDefinition assets:
///   - list of every ability in the project (search + discipline filter)
///   - start from a template, duplicate, rename, delete
///   - effect list with quick-add presets and a plain-language line per effect
///   - learnable-by / starting-ability toggles per HeroineDefinition
///   - tooltip preview, exact odds (kill chance) and expected output vs the standard action (GDD balance rule)
///   - checks that catch effects the resolver in RitualManager.Abilities would silently ignore
///
/// New abilities are drafts in memory until you press Create. Edits to existing assets apply at once (Undo works).
/// Place this file in an Editor folder (e.g. Assets/Scripts/Editor).
/// </summary>
public class AbilityCreatorWindow : EditorWindow
{
    private const string DefaultFolder = "Assets/ScriptableObjects/Abilities";
    private static readonly int[] PreviewPools = { 4, 5, 6, 7 };

    private enum Template { Blank, Strike, Ward, Song, Insight }

    // ----- state -----
    private List<AbilityDefinition> assets = new();
    private List<HeroineDefinition> heroines = new();

    private AbilityDefinition current;
    private bool isDraft, draftDirty, fileNameEdited;
    private string fileName = "", folder = DefaultFolder;
    private readonly HashSet<HeroineDefinition> draftStarting = new();

    private SerializedObject so;
    private SerializedProperty effectsProp;
    private ReorderableList effectList;

    private string search = "";
    private int disciplineFilter; // 0 = all, then Discipline + 1
    private Vector2 listScroll, editScroll;
    private bool foldIdentity = true, foldRules = true, foldEffects = true, foldLearning = true,
                 foldPreview = true, foldBalance = true, foldChecks = true;

    private GUIStyle richBox, miniGray, listButton, listButtonSelected;

    // =====================================================================
    // Window lifecycle
    // =====================================================================

    [MenuItem("Tools/Moonlit Ritual/Ability Creator")]
    public static void Open()
    {
        var w = GetWindow<AbilityCreatorWindow>("Ability Creator");
        w.minSize = new Vector2(860, 540);
    }

    private void OnEnable()
    {
        RefreshAssets();
        if (current == null) NewDraft(Template.Blank);
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        DestroyDraft();
    }

    private void OnProjectChange()
    {
        RefreshAssets();
        if (!isDraft && current == null) NewDraft(Template.Blank); // asset deleted outside the window
        Repaint();
    }

    private void OnUndoRedo()
    {
        so?.Update();
        Repaint();
    }

    private void RefreshAssets()
    {
        assets = AssetDatabase.FindAssets("t:AbilityDefinition")
            .Select(g => AssetDatabase.LoadAssetAtPath<AbilityDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null)
            .OrderBy(a => a.discipline).ThenBy(a => a.displayName)
            .ToList();
        heroines = AssetDatabase.FindAssets("t:HeroineDefinition")
            .Select(g => AssetDatabase.LoadAssetAtPath<HeroineDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(h => h != null)
            .OrderBy(h => h.displayName)
            .ToList();
    }

    // =====================================================================
    // Selecting / drafts
    // =====================================================================

    private bool ConfirmLeaveDraft()
    {
        if (!isDraft || !draftDirty) return true;
        return EditorUtility.DisplayDialog("Discard draft?",
            $"The new ability \"{current.displayName}\" has not been created yet. Discard it?", "Discard", "Keep editing");
    }

    private void DestroyDraft()
    {
        if (isDraft && current != null) DestroyImmediate(current);
        draftStarting.Clear();
    }

    private void Select(AbilityDefinition a)
    {
        if (a == current || !ConfirmLeaveDraft()) return;
        DestroyDraft();
        isDraft = false;
        draftDirty = false;
        Bind(a);
        fileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(a));
        fileNameEdited = true;
        GUI.FocusControl(null);
    }

    private void NewDraft(Template t)
    {
        if (!ConfirmLeaveDraft()) return;
        DestroyDraft();
        var a = CreateInstance<AbilityDefinition>();
        a.hideFlags = HideFlags.DontSave;
        ApplyTemplate(a, t);
        StartDraft(a);
    }

    private void DuplicateCurrent()
    {
        if (current == null) return;
        var copy = Instantiate(current);
        copy.hideFlags = HideFlags.DontSave;
        copy.displayName = current.displayName + " Copy";
        copy.learnableBy = new List<HeroineDefinition>(current.learnableBy);
        DestroyDraft(); // duplicating a draft keeps its content in the copy, so no need to ask
        StartDraft(copy);
    }

    private void StartDraft(AbilityDefinition a)
    {
        isDraft = true;
        draftDirty = false;
        fileNameEdited = false;
        Bind(a);
        fileName = SuggestFileName(a);
        GUI.FocusControl(null);
    }

    private void Bind(AbilityDefinition a)
    {
        current = a;
        so = new SerializedObject(a);
        effectsProp = so.FindProperty("effects");
        effectList = new ReorderableList(so, effectsProp, true, true, true, true)
        {
            drawHeaderCallback = r => EditorGUI.LabelField(r, "Effects  (list order; Damage is totalled per enemy and applied first)"),
            elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 3 + 10,
            drawElementCallback = DrawEffect,
            drawElementBackgroundCallback = DrawEffectBackground,
            onAddDropdownCallback = (_, __) => ShowAddEffectMenu(),
        };
    }

    // =====================================================================
    // GUI
    // =====================================================================

    private void InitStyles()
    {
        if (richBox != null) return;
        richBox = new GUIStyle(EditorStyles.helpBox) { richText = true, wordWrap = true, fontSize = 12, padding = new RectOffset(8, 8, 6, 6) };
        miniGray = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
        miniGray.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.35f, 0.35f, 0.35f);
        listButton = new GUIStyle(EditorStyles.label) { richText = true, padding = new RectOffset(6, 4, 3, 3) };
        listButtonSelected = new GUIStyle(listButton);
        listButtonSelected.normal.background = MakeTex(EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.55f, 0.72f, 0.92f));
        listButtonSelected.normal.textColor = Color.white;
    }

    private void OnGUI()
    {
        InitStyles();
        if (current == null) NewDraft(Template.Blank);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawAssetList();
            DrawSeparator();
            DrawEditor();
        }
    }

    private static void DrawSeparator()
    {
        var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandHeight(true), GUILayout.Width(1));
        EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.3f));
    }

    // ---------------- left: ability list ----------------

    private void DrawAssetList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarDropDown, GUILayout.Width(50)))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Blank"), false, () => NewDraft(Template.Blank));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Strike (Arcana, single-target damage)"), false, () => NewDraft(Template.Strike));
                    menu.AddItem(new GUIContent("Ward (Warding, Shields)"), false, () => NewDraft(Template.Ward));
                    menu.AddItem(new GUIContent("Song (Occultism, Progress)"), false, () => NewDraft(Template.Song));
                    menu.AddItem(new GUIContent("Insight (Lore, Insight + Mark)"), false, () => NewDraft(Template.Insight));
                    menu.ShowAsContext();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                    RefreshAssets();
            }

            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            var filterNames = new[] { "All disciplines" }.Concat(Enum.GetNames(typeof(Discipline))).ToArray();
            disciplineFilter = EditorGUILayout.Popup(disciplineFilter, filterNames);

            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            if (isDraft)
                GUILayout.Label($"<b>+ {current.displayName}</b>  <i>(draft)</i>", listButtonSelected);

            Discipline? lastGroup = null;
            foreach (var a in assets)
            {
                if (disciplineFilter > 0 && (int)a.discipline != disciplineFilter - 1) continue;
                if (!string.IsNullOrEmpty(search)
                    && a.displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && a.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (lastGroup != a.discipline)
                {
                    lastGroup = a.discipline;
                    EditorGUILayout.LabelField(a.discipline.ToString(), EditorStyles.boldLabel);
                }
                string owner = a.source == AbilitySource.Signature && a.learnableBy.Count > 0 && a.learnableBy[0] != null
                    ? $"  <color=#888888>{a.learnableBy[0].displayName}</color>"
                    : a.learnableBy.Count == 0 ? "  <color=#888888>library</color>" : "";
                bool selected = !isDraft && a == current;
                if (GUILayout.Button($"{a.displayName}{owner}", selected ? listButtonSelected : listButton))
                {
                    Select(a);
                    GUIUtility.ExitGUI();
                }
            }
            if (assets.Count == 0)
                EditorGUILayout.HelpBox("No abilities yet. Use New, or Tools > Moonlit Ritual > Create Starter Abilities.", MessageType.Info);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField($"{assets.Count} abilities, {heroines.Count} heroines", miniGray);
        }
    }

    // ---------------- right: editor ----------------

    private void DrawEditor()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            var checks = Validate(current);
            DrawToolbar(checks);

            editScroll = EditorGUILayout.BeginScrollView(editScroll);
            so.Update();

            foldIdentity = Section("Identity", foldIdentity, DrawIdentity);
            foldRules = Section("Rules", foldRules, DrawRules);
            foldEffects = Section("Effects", foldEffects, () => effectList.DoLayoutList());
            foldLearning = Section("Learning", foldLearning, DrawLearning);

            if (so.ApplyModifiedProperties())
            {
                if (isDraft) draftDirty = true;
                if (!fileNameEdited) fileName = SuggestFileName(current);
            }

            foldPreview = Section("Tooltip preview", foldPreview, DrawPreview);
            foldBalance = Section("Odds and balance", foldBalance, DrawBalance);
            foldChecks = Section($"Checks ({checks.Count(c => c.type == MessageType.Error)} errors, " +
                                 $"{checks.Count(c => c.type == MessageType.Warning)} warnings)", foldChecks, () => DrawChecks(checks));

            EditorGUILayout.EndScrollView();
        }
    }

    private bool Section(string title, bool open, Action body)
    {
        EditorGUILayout.Space(4);
        open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
        if (open)
        {
            EditorGUI.indentLevel++;
            body();
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        return open;
    }

    private void DrawToolbar(List<(MessageType type, string text)> checks)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int errors = checks.Count(c => c.type == MessageType.Error);
            int warnings = checks.Count(c => c.type == MessageType.Warning);
            string status = isDraft ? "Draft (not saved)" : AssetDatabase.GetAssetPath(current);
            GUILayout.Label(status, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (errors > 0) GUILayout.Label(EditorGUIUtility.TrTextContentWithIcon($" {errors}", MessageType.Error), EditorStyles.miniLabel);
            if (warnings > 0) GUILayout.Label(EditorGUIUtility.TrTextContentWithIcon($" {warnings}", MessageType.Warning), EditorStyles.miniLabel);

            if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton)) { DuplicateCurrent(); GUIUtility.ExitGUI(); }

            if (isDraft)
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(fileName)))
                    if (GUILayout.Button(errors > 0 ? "Create (has errors)" : "Create Asset", EditorStyles.toolbarButton))
                    { CreateAssetFromDraft(errors); GUIUtility.ExitGUI(); }
            }
            else
            {
                if (GUILayout.Button("Ping", EditorStyles.toolbarButton)) EditorGUIUtility.PingObject(current);
                if (GUILayout.Button("Save", EditorStyles.toolbarButton)) AssetDatabase.SaveAssets();
                if (GUILayout.Button("Delete", EditorStyles.toolbarButton)) { DeleteCurrent(); GUIUtility.ExitGUI(); }
            }
        }
    }

    // ---------------- sections ----------------

    private void DrawIdentity()
    {
        EditorGUILayout.PropertyField(so.FindProperty("displayName"));

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            fileName = EditorGUILayout.TextField(new GUIContent("File name", "Asset file name, without .asset"), fileName);
            if (EditorGUI.EndChangeCheck()) fileNameEdited = true;
            if (GUILayout.Button(new GUIContent("Auto", "Suggest a name: Heroine_AbilityName or Library_AbilityName"), GUILayout.Width(44)))
            {
                fileName = SuggestFileName(current);
                fileNameEdited = false;
                GUI.FocusControl(null);
            }
            if (!isDraft && GUILayout.Button("Rename", GUILayout.Width(60))) RenameCurrent();
        }
        if (isDraft)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                folder = EditorGUILayout.TextField("Folder", folder);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Ability folder", folder, "");
                    if (!string.IsNullOrEmpty(picked) && picked.StartsWith(Application.dataPath))
                        folder = "Assets" + picked.Substring(Application.dataPath.Length);
                }
            }
        }

        EditorGUILayout.PropertyField(so.FindProperty("icon"));
        EditorGUILayout.PropertyField(so.FindProperty("description"));
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("Write description from effects"))
            {
                so.ApplyModifiedProperties();
                so.FindProperty("description").stringValue = current.DescribeEffects();
                GUI.FocusControl(null);
            }
            if (GUILayout.Button("Clear (auto text)", GUILayout.Width(120)))
            {
                so.FindProperty("description").stringValue = "";
                GUI.FocusControl(null);
            }
        }
        EditorGUILayout.PropertyField(so.FindProperty("flavour"));
    }

    private void DrawRules()
    {
        EditorGUILayout.PropertyField(so.FindProperty("discipline"));
        var targetProp = so.FindProperty("target");
        EditorGUILayout.PropertyField(targetProp);
        var target = (AbilityTarget)targetProp.enumValueIndex;
        if (target == AbilityTarget.Ally || target == AbilityTarget.AllyOrCivilian)
            EditorGUILayout.PropertyField(so.FindProperty("includeSelf"));

        var rangeProp = so.FindProperty("range");
        using (new EditorGUILayout.HorizontalScope())
        {
            bool useDefault = rangeProp.floatValue < 0f;
            bool newDefault = EditorGUILayout.Toggle(new GUIContent("Use discipline range", "Her range for this discipline (3, Occultism 1)"), useDefault);
            if (newDefault != useDefault) rangeProp.floatValue = newDefault ? -1f : 3f;
            if (!newDefault)
                rangeProp.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(rangeProp.floatValue, GUILayout.Width(60)));
        }
        EditorGUILayout.LabelField(" ", "Straight step 1, diagonal step 1.5. Range 3 = 3 straight, 1 diag + 1 straight, or 2 diag.", miniGray);

        EditorGUILayout.PropertyField(so.FindProperty("straightLineOnly"), new GUIContent("Straight line only"));
        EditorGUILayout.PropertyField(so.FindProperty("requirement"));
    }

    private void DrawLearning()
    {
        EditorGUILayout.PropertyField(so.FindProperty("source"));
        var source = (AbilitySource)so.FindProperty("source").enumValueIndex;
        if (source == AbilitySource.Research)
        {
            EditorGUILayout.PropertyField(so.FindProperty("moonlightCost"));
            EditorGUILayout.PropertyField(so.FindProperty("researchNights"));
        }

        var learnable = so.FindProperty("learnableBy");
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(learnable.arraySize == 0 ? "Learnable by: anyone (no heroine ticked)" : "Learnable by:", EditorStyles.miniBoldLabel);

        if (heroines.Count == 0)
        {
            EditorGUILayout.HelpBox("No HeroineDefinition assets found.", MessageType.Info);
            return;
        }

        foreach (var h in heroines)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool canLearn = IndexOf(learnable, h) >= 0;
                bool newCan = EditorGUILayout.ToggleLeft($"{h.displayName}  <size=10>{PoolSummary(h)}</size>", canLearn,
                    new GUIStyle(EditorStyles.label) { richText = true }, GUILayout.Width(320));
                if (newCan != canLearn) SetInList(learnable, h, newCan);

                bool starts = isDraft ? draftStarting.Contains(h) : h.startingAbilities.Contains(current);
                bool newStarts = EditorGUILayout.ToggleLeft("Starts with it", starts, GUILayout.Width(110));
                if (newStarts != starts)
                {
                    SetStarting(h, newStarts);
                    if (newStarts && IndexOf(learnable, h) < 0) SetInList(learnable, h, true);
                }
            }
        }
    }

    private void DrawPreview()
    {
        var a = current;
        string range = a.range >= 0f ? $"range {a.range:0.#}" : a.discipline == Discipline.Occultism ? "range 1" : "range 3";
        if (a.target == AbilityTarget.None) range = a.effects.Any(e => e.scope == EffectScope.AllEnemiesInRange) ? $"around her, {range}" : "no target";
        if (a.straightLineOnly && a.NeedsClick) range += " (line)";
        string req = a.requirement == AbilityRequirement.AdjacentToAnchor ? " · next to the Anchor" : "";

        var sb = new StringBuilder();
        sb.AppendLine($"<b><size=14>{a.displayName}</size></b>");
        sb.AppendLine($"<color=#999999>{a.discipline} · {TargetLabel(a.target, a.includeSelf)} · {range}{req}</color>");
        sb.AppendLine();
        sb.Append(a.TooltipText());
        GUILayout.Label(sb.ToString(), richBox);

        if (!string.IsNullOrWhiteSpace(a.description))
        {
            EditorGUILayout.LabelField("Generated from effects (compare with your description):", miniGray);
            GUILayout.Label(a.DescribeEffects(), miniGray);
        }
    }

    private void DrawBalance()
    {
        var a = current;
        if (a.effects.Count == 0) { EditorGUILayout.LabelField("Add effects to see odds.", miniGray); return; }

        var dists = PreviewPools.Select(Distribution).ToArray();
        float colW = 70f;

        // Header
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("", GUILayout.Width(230));
            foreach (int p in PreviewPools)
                EditorGUILayout.LabelField(p == 6 ? "6d8*" : $"{p}d8", EditorStyles.boldLabel, GUILayout.Width(colW));
        }

        Row("Any effect at all", dists.Select(d => Pct(ChanceOf(d, (s, c) => a.effects.Any(e => AppliesAt(e, s, c))))), colW);
        Row("Critical (crit effects)", dists.Select(d => Pct(ChanceOf(d, (s, c) => c >= 1))), colW);
        Row("Overcharge", dists.Select(d => Pct(ChanceOf(d, (s, c) => c >= DiceRules.OverchargeCriticals))), colW);
        EditorGUILayout.Space(2);

        // One row per (type, scope) group
        foreach (var g in a.effects.GroupBy(e => (e.type, e.scope)))
        {
            var list = g.ToList();
            string label = $"{g.Key.type}{ScopeSuffix(g.Key.type, g.Key.scope)}";
            if (UsesAmount(g.Key.type) && g.Key.type != AbilityEffectType.RestoreTurnCapacity)
                Row($"Avg {label}", dists.Select(d => Expected(d, (s, c) => list.Where(e => AppliesAt(e, s, c)).Sum(e => e.Amount(s))).ToString("0.00")), colW);
            else
                Row($"Chance: {label}", dists.Select(d => g.Key.type == AbilityEffectType.Reveal && list.Any(e => e.trigger == EffectTrigger.Always)
                    ? "always" : Pct(ChanceOf(d, (s, c) => list.Any(e => AppliesAt(e, s, c))))), colW);
        }

        // Standard action for comparison
        EditorGUILayout.Space(2);
        var (stdName, stdType, stdFunc) = StandardAction(a.discipline);
        Row($"<i>Standard: {stdName}</i>", dists.Select(d => Expected(d, stdFunc).ToString("0.00")), colW, true);

        var main = a.effects.Where(e => e.type == stdType).ToList();
        if (main.Count > 0)
        {
            Row($"<i>vs standard (main stat)</i>", dists.Select(d =>
            {
                double ab = Expected(d, (s, c) => main.Where(e => AppliesAt(e, s, c)).Sum(e => e.Amount(s)));
                double st = Expected(d, stdFunc);
                double ratio = st > 0 ? ab / st : 0;
                string col = ratio > 1.3 ? "#e06c5a" : ratio < 0.7 ? "#d9a441" : "#6cbf6c";
                return $"<color={col}>{ratio:P0}</color>";
            }), colW, true);
            EditorGUILayout.LabelField(" ", $"Per target. Areas multiply this, so an AoE should sit lower. GDD: about the same expected output as {stdName}, in a different shape.", miniGray);
        }

        // Kill chance table (Target-scope damage)
        var targetDamage = a.effects.Where(e => e.type == AbilityEffectType.Damage && e.scope == EffectScope.Target).ToList();
        if (targetDamage.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Kill chance on the target (exact; Arcane Strike in brackets):", EditorStyles.miniBoldLabel);
            for (int resolve = 1; resolve <= 5; resolve++)
            {
                int r = resolve;
                Row($"Resolve {r}", dists.Select(d =>
                {
                    double ab = ChanceOf(d, (s, c) => targetDamage.Where(e => AppliesAt(e, s, c)).Sum(e => e.Amount(s)) >= r);
                    double st = ChanceOf(d, (s, c) => s >= r);
                    return $"{ab:P0} <color=#888888>({st:P0})</color>";
                }), colW + 20, true);
            }
        }

        EditorGUILayout.LabelField(" ", "* 6d8 only for Elin's Equinox stance.", miniGray);

        // Pools of the heroines who can learn it
        var who = (a.learnableBy.Count > 0 ? a.learnableBy.Where(h => h != null) : heroines)
            .Select(h => $"{h.displayName} {PoolOf(h, a.discipline)}d8").ToList();
        if (who.Count > 0)
            EditorGUILayout.LabelField(" ", $"{a.discipline} pools: {string.Join(", ", who)}", miniGray);
    }

    private void DrawChecks(List<(MessageType type, string text)> checks)
    {
        if (checks.Count == 0)
        {
            EditorGUILayout.HelpBox("No problems found.", MessageType.None);
            return;
        }
        foreach (var c in checks.OrderBy(c => c.type == MessageType.Error ? 0 : c.type == MessageType.Warning ? 1 : 2))
            EditorGUILayout.HelpBox(c.text, c.type);
    }

    private void Row(string label, IEnumerable<string> cells, float colW, bool rich = false)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var style = new GUIStyle(EditorStyles.label) { richText = true };
            EditorGUILayout.LabelField(label, style, GUILayout.Width(230));
            foreach (var c in cells) EditorGUILayout.LabelField(c, style, GUILayout.Width(colW));
        }
    }

    // ---------------- effect list ----------------

    private void DrawEffectBackground(Rect r, int index, bool active, bool focused)
    {
        if (index >= 0 && index < effectsProp.arraySize)
        {
            var trig = (EffectTrigger)effectsProp.GetArrayElementAtIndex(index).FindPropertyRelative("trigger").enumValueIndex;
            Color tint = trig switch
            {
                EffectTrigger.OnCritical => new Color(1f, 0.8f, 0.2f, 0.08f),
                EffectTrigger.OnOvercharge => new Color(0.8f, 0.3f, 1f, 0.10f),
                _ => Color.clear
            };
            EditorGUI.DrawRect(r, tint);
        }
        if (active || focused) EditorGUI.DrawRect(r, new Color(0.3f, 0.5f, 0.9f, 0.2f));
    }

    private void DrawEffect(Rect r, int index, bool active, bool focused)
    {
        var e = effectsProp.GetArrayElementAtIndex(index);
        var typeP = e.FindPropertyRelative("type");
        var trigP = e.FindPropertyRelative("trigger");
        var scopeP = e.FindPropertyRelative("scope");
        var flatP = e.FindPropertyRelative("flat");
        var perP = e.FindPropertyRelative("perSuccess");
        var maxP = e.FindPropertyRelative("maxAmount");
        var minP = e.FindPropertyRelative("minSuccesses");
        var type = (AbilityEffectType)typeP.enumValueIndex;

        float lh = EditorGUIUtility.singleLineHeight;
        r.y += 3;
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        float oldLabel = EditorGUIUtility.labelWidth;

        // Line 1: type | trigger | scope
        float w = (r.width - 8) / 3f;
        EditorGUI.PropertyField(new Rect(r.x, r.y, w, lh), typeP, GUIContent.none);
        EditorGUI.PropertyField(new Rect(r.x + w + 4, r.y, w, lh), trigP, GUIContent.none);
        using (new EditorGUI.DisabledScope(!UsesScope(type)))
            EditorGUI.PropertyField(new Rect(r.x + 2 * (w + 4), r.y, w, lh), scopeP, GUIContent.none);

        // Line 2: amounts
        r.y += lh + 2;
        float q = (r.width - 12) / 4f;
        EditorGUIUtility.labelWidth = 62;
        using (new EditorGUI.DisabledScope(!UsesAmount(type) || type == AbilityEffectType.RestoreTurnCapacity))
        {
            EditorGUI.PropertyField(new Rect(r.x, r.y, q, lh), flatP, new GUIContent("Flat", flatP.tooltip));
            EditorGUI.PropertyField(new Rect(r.x + q + 4, r.y, q, lh), perP, new GUIContent("Per Succ.", perP.tooltip));
            EditorGUI.PropertyField(new Rect(r.x + 2 * (q + 4), r.y, q, lh), maxP, new GUIContent("Max", maxP.tooltip));
        }
        EditorGUIUtility.labelWidth = 76;
        EditorGUI.PropertyField(new Rect(r.x + 3 * (q + 4), r.y, q, lh), minP, new GUIContent("Min Succ.", minP.tooltip));
        if (minP.intValue < 1) minP.intValue = 1;

        // Line 3: plain-language line
        r.y += lh + 2;
        var fx = index < current.effects.Count ? current.effects[index] : null;
        if (fx != null) EditorGUI.LabelField(new Rect(r.x, r.y, r.width, lh), Summarize(fx), miniGray);

        EditorGUIUtility.labelWidth = oldLabel;
        EditorGUI.indentLevel = indent;
    }

    private void ShowAddEffectMenu()
    {
        var m = new GenericMenu();
        void Add(string path, AbilityEffect fx) => m.AddItem(new GUIContent(path), false, () => AddEffect(fx));

        Add("Blank", new AbilityEffect(AbilityEffectType.Damage, 0, 1));
        m.AddSeparator("");
        Add("Damage/1 per Success (target)", new AbilityEffect(AbilityEffectType.Damage, 0, 1));
        Add("Damage/Critical: +1 damage", new AbilityEffect(AbilityEffectType.Damage, 1, 0, trigger: EffectTrigger.OnCritical));
        Add("Damage/Overcharge: next enemy in line", new AbilityEffect(AbilityEffectType.Damage, 0, 1, EffectScope.NextEnemyInLine, EffectTrigger.OnOvercharge));
        Add("Damage/1 to every enemy in range", new AbilityEffect(AbilityEffectType.Damage, 1, 0, EffectScope.AllEnemiesInRange));
        Add("Movement/Push 1 (target)", new AbilityEffect(AbilityEffectType.Push, 1));
        Add("Movement/Pull 1 per Success, max 2", new AbilityEffect(AbilityEffectType.Pull, 0, 1, maxAmount: 2));
        Add("Movement/Swap with target", new AbilityEffect(AbilityEffectType.Swap, 0));
        Add("Movement/Overcharge: +1 Move (caster)", new AbilityEffect(AbilityEffectType.ExtraMove, 1, 0, EffectScope.Caster, EffectTrigger.OnOvercharge));
        Add("Protection/Shields 1 per Success (target)", new AbilityEffect(AbilityEffectType.Shield, 0, 1));
        Add("Protection/1 Shield to allies next to her", new AbilityEffect(AbilityEffectType.Shield, 1, 0, EffectScope.AlliesAdjacentToCaster));
        Add("Protection/Critical: restore 1 Turn Capacity", new AbilityEffect(AbilityEffectType.RestoreTurnCapacity, 0, 0, EffectScope.Caster, EffectTrigger.OnCritical));
        Add("Ritual/Critical: +1 Stability (put before Progress)", new AbilityEffect(AbilityEffectType.Stability, 1, 0, trigger: EffectTrigger.OnCritical));
        Add("Ritual/Stability 1 per Success", new AbilityEffect(AbilityEffectType.Stability, 0, 1));
        Add("Ritual/Progress 1 per Success", new AbilityEffect(AbilityEffectType.Progress, 0, 1));
        Add("Ritual/Overcharge: +2 Progress", new AbilityEffect(AbilityEffectType.Progress, 2, 0, trigger: EffectTrigger.OnOvercharge));
        Add("Information/Reveal intent (before the roll)", new AbilityEffect(AbilityEffectType.Reveal, 0));
        Add("Information/+1 Insight (target)", new AbilityEffect(AbilityEffectType.Insight, 1));
        Add("Information/Insight 1 per Success", new AbilityEffect(AbilityEffectType.Insight, 0, 1));
        Add("Information/Mark (target)", new AbilityEffect(AbilityEffectType.Mark, 0));
        Add("Information/Critical: Mark enemies next to target", new AbilityEffect(AbilityEffectType.Mark, 0, 0, EffectScope.EnemiesAdjacentToTarget, EffectTrigger.OnCritical));
        Add("Information/Overcharge: acts last (target)", new AbilityEffect(AbilityEffectType.Delay, 0, 0, trigger: EffectTrigger.OnOvercharge));
        m.ShowAsContext();
    }

    private void AddEffect(AbilityEffect fx)
    {
        Undo.RecordObject(current, "Add Effect");
        current.effects.Add(fx);
        EditorUtility.SetDirty(current);
        if (isDraft) draftDirty = true;
        so.Update();
        Repaint();
    }

    // =====================================================================
    // Asset operations
    // =====================================================================

    private void CreateAssetFromDraft(int errors)
    {
        if (errors > 0 && !EditorUtility.DisplayDialog("Create with errors?",
                $"{errors} check(s) report effects that will do nothing in combat. Create anyway?", "Create", "Cancel"))
            return;

        string cleanName = SanitizeFileName(fileName);
        if (string.IsNullOrEmpty(cleanName)) return;
        EnsureFolder(folder);
        string path = $"{folder}/{cleanName}.asset";
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
        {
            EditorUtility.DisplayDialog("File exists", $"{path} already exists. Pick another file name.", "OK");
            return;
        }

        var asset = current;
        asset.hideFlags = HideFlags.None;
        asset.name = cleanName;
        AssetDatabase.CreateAsset(asset, path);

        foreach (var h in draftStarting)
        {
            if (h == null || h.startingAbilities.Contains(asset)) continue;
            Undo.RecordObject(h, "Add Starting Ability");
            h.startingAbilities.Add(asset);
            EditorUtility.SetDirty(h);
        }
        draftStarting.Clear();

        AssetDatabase.SaveAssets();
        isDraft = false;
        draftDirty = false;
        fileNameEdited = true;
        fileName = cleanName;
        Bind(asset);
        RefreshAssets();
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[Abilities] Created {path}");
    }

    private void RenameCurrent()
    {
        string clean = SanitizeFileName(fileName);
        string path = AssetDatabase.GetAssetPath(current);
        if (string.IsNullOrEmpty(clean) || clean == Path.GetFileNameWithoutExtension(path)) return;
        string error = AssetDatabase.RenameAsset(path, clean);
        if (!string.IsNullOrEmpty(error)) EditorUtility.DisplayDialog("Rename failed", error, "OK");
        else { fileName = clean; RefreshAssets(); }
    }

    private void DeleteCurrent()
    {
        string path = AssetDatabase.GetAssetPath(current);
        var users = heroines.Where(h => h.startingAbilities.Contains(current)).ToList();
        string extra = users.Count > 0 ? $"\n\nIt will be removed from the Starting Abilities of: {string.Join(", ", users.Select(h => h.displayName))}." : "";
        if (!EditorUtility.DisplayDialog("Delete ability?", $"Move {path} to the trash?{extra}", "Delete", "Cancel")) return;

        foreach (var h in users)
        {
            Undo.RecordObject(h, "Remove Starting Ability");
            h.startingAbilities.Remove(current);
            EditorUtility.SetDirty(h);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.MoveAssetToTrash(path);
        current = null;
        RefreshAssets();
        NewDraft(Template.Blank);
    }

    private void SetStarting(HeroineDefinition h, bool on)
    {
        if (isDraft)
        {
            if (on) draftStarting.Add(h); else draftStarting.Remove(h);
            draftDirty = true;
            return;
        }
        Undo.RecordObject(h, on ? "Add Starting Ability" : "Remove Starting Ability");
        if (on && !h.startingAbilities.Contains(current)) h.startingAbilities.Add(current);
        if (!on) h.startingAbilities.Remove(current);
        EditorUtility.SetDirty(h);
    }

    private static int IndexOf(SerializedProperty list, UnityEngine.Object obj)
    {
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == obj) return i;
        return -1;
    }

    private static void SetInList(SerializedProperty list, UnityEngine.Object obj, bool on)
    {
        int i = IndexOf(list, obj);
        if (on && i < 0)
        {
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = obj;
        }
        else if (!on && i >= 0)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = null;
            list.DeleteArrayElementAtIndex(i);
        }
    }

    private static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // =====================================================================
    // Templates and names
    // =====================================================================

    private static void ApplyTemplate(AbilityDefinition a, Template t)
    {
        a.effects = new List<AbilityEffect>();
        switch (t)
        {
            case Template.Blank:
                a.displayName = "New Ability";
                break;
            case Template.Strike:
                a.displayName = "New Strike";
                a.discipline = Discipline.Arcana;
                a.target = AbilityTarget.Enemy;
                a.effects.Add(new AbilityEffect(AbilityEffectType.Damage, 0, 1));
                a.effects.Add(new AbilityEffect(AbilityEffectType.Push, 1, 0, trigger: EffectTrigger.OnOvercharge));
                break;
            case Template.Ward:
                a.displayName = "New Ward";
                a.discipline = Discipline.Warding;
                a.target = AbilityTarget.Ally;
                a.effects.Add(new AbilityEffect(AbilityEffectType.Shield, 0, 1));
                a.effects.Add(new AbilityEffect(AbilityEffectType.RestoreTurnCapacity, 0, 0, EffectScope.Caster, EffectTrigger.OnCritical));
                break;
            case Template.Song:
                a.displayName = "New Song";
                a.discipline = Discipline.Occultism;
                a.target = AbilityTarget.None;
                a.requirement = AbilityRequirement.AdjacentToAnchor;
                a.effects.Add(new AbilityEffect(AbilityEffectType.Stability, 1, 0, trigger: EffectTrigger.OnCritical));
                a.effects.Add(new AbilityEffect(AbilityEffectType.Progress, 0, 1));
                break;
            case Template.Insight:
                a.displayName = "New Reading";
                a.discipline = Discipline.Lore;
                a.target = AbilityTarget.Enemy;
                a.effects.Add(new AbilityEffect(AbilityEffectType.Reveal, 0));
                a.effects.Add(new AbilityEffect(AbilityEffectType.Insight, 0, 1));
                a.effects.Add(new AbilityEffect(AbilityEffectType.Mark, 0, 0, trigger: EffectTrigger.OnCritical));
                break;
        }
    }

    private string SuggestFileName(AbilityDefinition a)
    {
        string pascal = ToPascal(a.displayName);
        var owners = a.learnableBy.Where(h => h != null).ToList();
        if (isDraft && owners.Count == 0 && draftStarting.Count == 1) owners.Add(draftStarting.First());
        string prefix = a.source == AbilitySource.Signature && owners.Count == 1
            ? ToPascal((owners[0].displayName ?? owners[0].name).Split(' ', ',')[0])
            : "Library";
        return $"{prefix}_{pascal}";
    }

    private static string ToPascal(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Ability";
        s = s.Replace("'", "").Replace("’", "");
        var words = s.Split(new[] { ' ', '-', '_', '.', ',', ':', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            var clean = new string(w.Where(char.IsLetterOrDigit).ToArray());
            if (clean.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(clean[0])).Append(clean.Substring(1));
        }
        return sb.Length > 0 ? sb.ToString() : "Ability";
    }

    private static string SanitizeFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Trim().Where(c => !invalid.Contains(c)).ToArray());
    }

    // =====================================================================
    // Validation: mirrors what RitualManager.Abilities actually does
    // =====================================================================

    private List<(MessageType type, string text)> Validate(AbilityDefinition a)
    {
        var list = new List<(MessageType, string)>();
        void Err(string s) => list.Add((MessageType.Error, s));
        void Warn(string s) => list.Add((MessageType.Warning, s));
        void Info(string s) => list.Add((MessageType.Info, s));
        if (a == null) return list;

        if (string.IsNullOrWhiteSpace(a.displayName)) Err("Display Name is empty.");
        if (a.effects.Count == 0) Err("No effects: the ability would do nothing.");

        bool noUnit = a.target == AbilityTarget.None;
        bool anchorTarget = a.target == AbilityTarget.Anchor;
        bool targetCanBeEnemy = a.target == AbilityTarget.Enemy || a.target == AbilityTarget.AnyUnit;
        bool targetCanBeAlly = a.target == AbilityTarget.Ally || a.target == AbilityTarget.AllyOrCivilian || a.target == AbilityTarget.AnyUnit;

        for (int i = 0; i < a.effects.Count; i++)
        {
            var e = a.effects[i];
            if (e == null) continue;
            string n = $"Effect {i + 1} ({e.type})";
            bool enemyOnly = e.type is AbilityEffectType.Damage or AbilityEffectType.Insight or AbilityEffectType.Mark
                                    or AbilityEffectType.Delay or AbilityEffectType.Reveal;

            if (e.type == AbilityEffectType.Reveal && e.trigger != EffectTrigger.Always)
                Err($"{n}: Reveal only happens before the roll, so a {e.trigger} Reveal never resolves.");

            if (UsesScope(e.type))
            {
                if (e.scope == EffectScope.Target)
                {
                    if (noUnit && e.type != AbilityEffectType.Shield && e.type != AbilityEffectType.ExtraMove)
                        Err($"{n}: the ability has no target, so the Target scope hits nothing (only Shield and Extra Move fall back to the caster). Use Caster or an area scope.");
                    else if (anchorTarget && e.type != AbilityEffectType.Shield)
                        Err($"{n}: the Target is the Anchor; only Shields work on it.");
                    else if (enemyOnly && !targetCanBeEnemy)
                        Err($"{n}: {e.type} only affects enemies, but the target is {a.target}.");
                    else if (e.type == AbilityEffectType.ExtraMove && a.target == AbilityTarget.Enemy)
                        Err($"{n}: Extra Move only affects heroines, but the target is an enemy.");
                    else if (enemyOnly && a.target == AbilityTarget.AnyUnit)
                        Info($"{n}: does nothing when the clicked unit is an ally.");
                }
                if (enemyOnly && e.scope is EffectScope.Caster or EffectScope.AllAllies or EffectScope.AlliesAdjacentToCaster)
                    Err($"{n}: {e.type} only affects enemies, but the scope is {e.scope}.");
                if (e.type == AbilityEffectType.ExtraMove && e.scope is EffectScope.EnemiesAdjacentToTarget or EffectScope.NextEnemyInLine or EffectScope.AllEnemiesInRange)
                    Err($"{n}: Extra Move only affects heroines, but the scope picks enemies.");
                if ((noUnit || anchorTarget) && e.scope is EffectScope.EnemiesAdjacentToTarget or EffectScope.NextEnemyInLine)
                    Err($"{n}: {e.scope} needs a clicked unit, but the target is {a.target}.");
                if (e.scope == EffectScope.NextEnemyInLine && !a.straightLineOnly)
                    Warn($"{n}: Next Enemy In Line works best with Straight Line Only; off-line targets use the longer axis as the line.");
                if (e.type == AbilityEffectType.Swap && e.scope != EffectScope.Target)
                    Warn($"{n}: Swap uses only the first unit of its scope; Target is the intended scope.");
            }
            if (e.type == AbilityEffectType.Swap && (noUnit || anchorTarget))
                Err($"{n}: Swap needs a clicked unit.");

            if (UsesAmount(e.type) && e.type != AbilityEffectType.RestoreTurnCapacity && e.flat == 0 && e.perSuccess == 0)
                Err($"{n}: Flat and Per Success are both 0, so it adds nothing.");
            if (e.maxAmount > 0 && e.perSuccess == 0 && e.maxAmount < e.flat)
                Warn($"{n}: Max ({e.maxAmount}) is below Flat ({e.flat}).");
            if (e.type == AbilityEffectType.RestoreTurnCapacity && (e.flat > 1 || e.perSuccess > 0))
                Warn($"{n}: amounts are ignored; it always restores 1 (once per ability, team limit per round).");
            if (e.trigger == EffectTrigger.OnOvercharge && e.minSuccesses <= 4 && e.minSuccesses > 1)
                Info($"{n}: Overcharge already means 4+ Successes, so Min Successes {e.minSuccesses} changes nothing.");
        }

        // Order: Stability before Progress
        int firstProgress = a.effects.FindIndex(e => e?.type == AbilityEffectType.Progress);
        int lastStability = a.effects.FindLastIndex(e => e?.type == AbilityEffectType.Stability);
        if (firstProgress >= 0 && lastStability > firstProgress)
            Warn($"Stability (effect {lastStability + 1}) comes after Progress (effect {firstProgress + 1}), so the new Stability can't absorb that Progress. Move Stability up (GDD: Stabilize before Channel).");

        if (a.effects.Count(e => e?.type == AbilityEffectType.RestoreTurnCapacity) > 1)
            Warn("More than one Restore Turn Capacity: only one can restore per cast.");

        if (a.effects.Count > 0 && a.effects.All(e => e == null || e.trigger != EffectTrigger.Always))
            Warn("No effect on a plain Success: the ability does nothing without a Critical. Usually a base effect should be Always.");

        // In-game kill-chance preview (CastAbility uses ChanceToReach(pool, need, CriticalDamageBonus))
        var dmgTarget = a.effects.Where(e => e != null && e.type == AbilityEffectType.Damage && e.scope == EffectScope.Target).ToList();
        if (dmgTarget.Count > 0)
        {
            var always = dmgTarget.Where(e => e.trigger == EffectTrigger.Always).ToList();
            var crit = dmgTarget.Where(e => e.trigger == EffectTrigger.OnCritical).ToList();
            bool previewExact =
                always.Count == 1 && always[0].flat == 0 && always[0].perSuccess == 1 && always[0].maxAmount == 0 && always[0].minSuccesses <= 1
                && crit.All(e => e.perSuccess == 0 && e.maxAmount == 0 && e.minSuccesses <= 2)
                && dmgTarget.All(e => e.trigger != EffectTrigger.OnOvercharge);
            if (!previewExact)
                Warn("The in-game kill chance preview assumes '1 damage per Success + flat Critical bonus'. This damage setup differs, so the logged kill chance will be wrong (Player Trust Rule 1). Use the table above, or extend RitualManager's preview.");
        }

        // Rules
        if (a.straightLineOnly && !a.NeedsClick)
            Info("Straight Line Only does nothing without a clicked target (area scopes ignore it).");
        if (a.discipline == Discipline.Occultism && a.requirement != AbilityRequirement.AdjacentToAnchor)
            Info("GDD: Occultism normally needs her next to the Anchor. Without the requirement and with discipline range, her range is 1.");
        if (a.HasEffect(AbilityEffectType.Progress) && a.discipline != Discipline.Occultism)
            Info("Progress outside Occultism: the GDD gives the objective state to Occultism. Check it isn't stepping on Channel.");
        if (a.HasEffect(AbilityEffectType.Damage) && a.discipline != Discipline.Arcana)
            Info("Damage outside Arcana: check the discipline identity (GDD: every discipline solves a different problem).");

        // Learning / identity
        if (a.source == AbilitySource.Signature && a.learnableBy.Count == 0)
            Warn("Signature ability with no heroine in Learnable By: anyone could learn it.");
        if (string.IsNullOrWhiteSpace(a.description)) Info("Description is empty: the tooltip is written from the effects.");
        if (a.icon == null) Info("No icon: the spell book grid will show an empty tile.");
        if (assets.Any(o => o != a && string.Equals(o.displayName, a.displayName, StringComparison.OrdinalIgnoreCase)))
            Warn($"Another ability is already called \"{a.displayName}\".");

        // Balance (main stat vs standard action, at 5d8)
        var (stdName, stdType, stdFunc) = StandardAction(a.discipline);
        var main = a.effects.Where(e => e != null && e.type == stdType).ToList();
        if (main.Count > 0)
        {
            var d = Distribution(5);
            double ab = Expected(d, (s, c) => main.Where(e => AppliesAt(e, s, c)).Sum(e => e.Amount(s)));
            double st = Expected(d, stdFunc);
            bool area = main.Any(e => e.scope is EffectScope.AllEnemiesInRange or EffectScope.AllAllies or EffectScope.AlliesAdjacentToCaster or EffectScope.EnemiesAdjacentToTarget);
            if (st > 0 && ab / st > 1.3)
                Warn($"At 5d8 its {stdType} averages {ab:0.00} per target vs {st:0.00} for {stdName} ({ab / st:P0}). GDD: abilities are sidegrades; simulate it before building.");
            else if (st > 0 && area && ab / st > 0.8)
                Warn($"Area {stdType} at {ab / st:P0} of {stdName} per target: hitting several targets makes it stronger than the standard action.");
        }
        return list;
    }

    // =====================================================================
    // Odds: exact distribution of (Successes, Criticals) for a pool, matching DiceRules
    // =====================================================================

    /// <summary>p[s, c] = chance of s Successes and c Criticals (c capped at the Overcharge count).</summary>
    private static double[,] Distribution(int pool)
    {
        int cap = Mathf.Max(1, DiceRules.OverchargeCriticals);
        int maxS = pool * DiceRules.CriticalValue;
        double pFail = (DiceRules.SuccessMin - 1.0) / DiceRules.Sides;
        double pCrit = (DiceRules.Sides - DiceRules.CriticalFace + 1.0) / DiceRules.Sides;
        double pHit = Math.Max(0.0, 1.0 - pFail - pCrit);

        var p = new double[maxS + 1, cap + 1];
        p[0, 0] = 1.0;
        for (int d = 0; d < pool; d++)
        {
            var n = new double[maxS + 1, cap + 1];
            for (int s = 0; s <= maxS; s++)
                for (int c = 0; c <= cap; c++)
                {
                    double v = p[s, c];
                    if (v == 0) continue;
                    n[s, c] += v * pFail;
                    if (s + 1 <= maxS) n[s + 1, c] += v * pHit;
                    if (s + DiceRules.CriticalValue <= maxS) n[s + DiceRules.CriticalValue, Math.Min(c + 1, cap)] += v * pCrit;
                }
            p = n;
        }
        return p;
    }

    private static bool AppliesAt(AbilityEffect e, int s, int c)
    {
        if (e == null || s < Mathf.Max(1, e.minSuccesses)) return false;
        return e.trigger switch
        {
            EffectTrigger.OnCritical => c >= 1,
            EffectTrigger.OnOvercharge => c >= DiceRules.OverchargeCriticals,
            _ => true
        };
    }

    private static double ChanceOf(double[,] p, Func<int, int, bool> pred)
    {
        double t = 0;
        for (int s = 0; s < p.GetLength(0); s++)
            for (int c = 0; c < p.GetLength(1); c++)
                if (p[s, c] > 0 && pred(s, c)) t += p[s, c];
        return t;
    }

    private static double Expected(double[,] p, Func<int, int, double> value)
    {
        double t = 0;
        for (int s = 0; s < p.GetLength(0); s++)
            for (int c = 0; c < p.GetLength(1); c++)
                if (p[s, c] > 0) t += p[s, c] * value(s, c);
        return t;
    }

    private static (string name, AbilityEffectType type, Func<int, int, double> value) StandardAction(Discipline d) => d switch
    {
        Discipline.Arcana => ("Arcane Strike damage", AbilityEffectType.Damage, (s, c) => s),
        Discipline.Warding => ("Guard Shields", AbilityEffectType.Shield, (s, c) => s),
        Discipline.Occultism => ("Channel Progress", AbilityEffectType.Progress, (s, c) => s > 0 ? s + (c >= 1 ? 2 : 0) : 0),
        _ => ("Study Insight", AbilityEffectType.Insight, (s, c) => s),
    };

    // =====================================================================
    // Small helpers
    // =====================================================================

    private static bool UsesAmount(AbilityEffectType t) => t is AbilityEffectType.Damage or AbilityEffectType.Push
        or AbilityEffectType.Pull or AbilityEffectType.Shield or AbilityEffectType.Stability or AbilityEffectType.Progress
        or AbilityEffectType.Insight or AbilityEffectType.ExtraMove or AbilityEffectType.RestoreTurnCapacity;

    private static bool UsesScope(AbilityEffectType t) => t is not (AbilityEffectType.Stability
        or AbilityEffectType.Progress or AbilityEffectType.RestoreTurnCapacity);

    private static string ScopeSuffix(AbilityEffectType t, EffectScope s) =>
        !UsesScope(t) || s == EffectScope.Target ? "" : $" ({ObjectNames.NicifyVariableName(s.ToString()).ToLower()})";

    private static string Pct(double v) => v.ToString("P0");

    private static string TargetLabel(AbilityTarget t, bool includeSelf) => t switch
    {
        AbilityTarget.None => "no click",
        AbilityTarget.Enemy => "an enemy",
        AbilityTarget.Ally => includeSelf ? "an ally or herself" : "another ally",
        AbilityTarget.AllyOrCivilian => "an ally or civilian",
        AbilityTarget.AnyUnit => "an ally or enemy",
        AbilityTarget.Anchor => "the Anchor",
        _ => t.ToString()
    };

    private static string Summarize(AbilityEffect e)
    {
        string when = e.trigger switch
        {
            EffectTrigger.OnCritical => "Critical: ",
            EffectTrigger.OnOvercharge => "Overcharge: ",
            _ => ""
        };
        string amt = e.perSuccess > 0
            ? (e.flat != 0 ? $"{e.flat} + {e.perSuccess}/Success" : $"{e.perSuccess}/Success")
            : e.flat.ToString();
        if (e.maxAmount > 0) amt += $" (max {e.maxAmount})";
        string who = UsesScope(e.type) ? ScopeSuffix(e.type, e.scope) : "";
        string need = e.minSuccesses > 1 ? $", needs {e.minSuccesses}+ Successes" : "";
        string what = e.type switch
        {
            AbilityEffectType.Swap => "swap places with the target",
            AbilityEffectType.Mark => "Mark",
            AbilityEffectType.Delay => "acts last",
            AbilityEffectType.Reveal => "reveal full intent (before the roll)",
            AbilityEffectType.RestoreTurnCapacity => "restore 1 Turn Capacity",
            AbilityEffectType.Push => $"push {amt} tile(s)",
            AbilityEffectType.Pull => $"pull {amt} tile(s)",
            AbilityEffectType.ExtraMove => $"+{amt} Move",
            _ => $"{amt} {e.type}"
        };
        return $"→ {when}{what}{who}{need}";
    }

    private static int PoolOf(HeroineDefinition h, Discipline d)
    {
        var g = h.grimoire != null ? h.grimoire : h.prefab != null ? h.prefab.grimoire : null;
        if (g == null) return 0;
        return d switch
        {
            Discipline.Arcana => g.arcana,
            Discipline.Warding => g.warding,
            Discipline.Lore => g.lore,
            _ => g.occultism
        };
    }

    private static string PoolSummary(HeroineDefinition h) =>
        $"W{PoolOf(h, Discipline.Warding)} O{PoolOf(h, Discipline.Occultism)} A{PoolOf(h, Discipline.Arcana)} L{PoolOf(h, Discipline.Lore)}";

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
