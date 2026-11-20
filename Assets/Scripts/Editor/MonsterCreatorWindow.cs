using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Moonlit Ritual > Monster Creator
///
/// Creates and edits Monster assets and the prefabs that spawn them:
///   - list of every Monster, grouped by category (Assault / Sabotage / Control)
///   - templates for the six GDD regular enemies and the durability archetypes (Fragile / Standard / Heavy)
///   - prefab maker: a variant of an existing monster prefab, or a new prefab from a model,
///     with MonsterHolder.monsterData filled in
///   - toggles to add the prefab to the MonsterSpawners and Elite Guardian objectives in the open scene
///   - exact odds: Arcane Strike kill chance before / after Solving, Studies needed to Solve,
///     rounds to reach the Anchor, hits to Down a heroine
///   - checks against the GDD enemy rules and the way RitualManager / EnemyPlanner use the fields
///   - roster table comparing every monster
///
/// New monsters are drafts in memory until you press Create. Edits to existing assets apply at once (Undo works).
/// Place this file in an Editor folder (e.g. Assets/Scripts/Editor).
/// </summary>
public class MonsterCreatorWindow : EditorWindow
{
    private const string DefaultFolder = "Assets/ScriptableObjects/Monsters";
    private const string DefaultPrefabFolder = "Assets/Prefabs/Monsters";
    private static readonly int[] Pools = { 4, 5, 7 };
    private const int HeroineResolve = 3;

    private enum PrefabMode { VariantOfExisting, FromModel }

    // ----- state -----
    private List<Monster> assets = new();
    private List<MonsterHolder> prefabs = new();
    private List<MonsterSpawner> spawners = new();
    private List<EliteGuardianObjective> guardians = new();

    private Monster current;
    private SerializedObject so;
    private bool isDraft, draftDirty, fileNameEdited;
    private string fileName = "", folder = DefaultFolder, search = "";
    private int categoryFilter;
    private Vector2 listScroll, editScroll;
    private readonly Dictionary<string, bool> folds = new();

    // prefab maker
    private PrefabMode prefabMode = PrefabMode.VariantOfExisting;
    private MonsterHolder basePrefab;
    private GameObject model;
    private bool keepVariantLink = true;
    private string prefabFolder = DefaultPrefabFolder;
    private string prefabName = "";

    private GUIStyle richBox, miniGray, richLabel, listButton, listButtonSelected;

    // =====================================================================
    // Lifecycle
    // =====================================================================

    [MenuItem("Tools/Moonlit Ritual/Monster Creator")]
    public static void Open()
    {
        var w = GetWindow<MonsterCreatorWindow>("Monster Creator");
        w.minSize = new Vector2(860, 540);
    }

    private void OnEnable()
    {
        RefreshAssets();
        RefreshScene();
        if (current == null) NewDraft(null);
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
        if (!isDraft && current == null) NewDraft(null);
        Repaint();
    }

    private void OnHierarchyChange() { RefreshScene(); Repaint(); }
    private void OnFocus() => RefreshScene();

    private void OnUndoRedo()
    {
        so?.Update();
        RefreshScene();
        Repaint();
    }

    private void RefreshAssets()
    {
        assets = AssetDatabase.FindAssets("t:Monster")
            .Select(g => AssetDatabase.LoadAssetAtPath<Monster>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null)
            .OrderBy(m => m.category).ThenBy(m => m.monsterName)
            .ToList();
        prefabs = AssetDatabase.FindAssets("t:Prefab")
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(go => go != null)
            .Select(go => go.GetComponent<MonsterHolder>())
            .Where(h => h != null)
            .OrderBy(h => h.name)
            .ToList();
    }

    private void RefreshScene()
    {
        spawners = FindObjectsByType<MonsterSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(s => s.name).ToList();
        guardians = FindObjectsByType<EliteGuardianObjective>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
    }

    private List<MonsterHolder> PrefabsOf(Monster m) => prefabs.Where(p => p != null && p.Data == m).ToList();

    // =====================================================================
    // Drafts
    // =====================================================================

    private bool ConfirmLeaveDraft()
    {
        if (!isDraft || !draftDirty) return true;
        return EditorUtility.DisplayDialog("Discard draft?",
            $"\"{current.monsterName}\" has not been created yet. Discard it?", "Discard", "Keep editing");
    }

    private void DestroyDraft()
    {
        if (isDraft && current != null) DestroyImmediate(current);
    }

    private void Select(Monster m)
    {
        if (m == current || !ConfirmLeaveDraft()) return;
        DestroyDraft();
        isDraft = false;
        draftDirty = false;
        Bind(m);
        fileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m));
        fileNameEdited = true;
        prefabName = ToPascal(m.monsterName);
        basePrefab = basePrefab != null ? basePrefab : prefabs.FirstOrDefault(p => p.Data != m);
        GUI.FocusControl(null);
    }

    private void NewDraft(Action<Monster> init)
    {
        if (!ConfirmLeaveDraft()) return;
        DestroyDraft();
        var m = CreateInstance<Monster>();
        m.hideFlags = HideFlags.DontSave;
        m.monsterName = "New Monster";
        m.resistance = 3;
        m.solvedResistance = 1;
        m.loreToSolve = 3;
        m.stabilityDamage = 1;
        m.heroineDamage = 1;
        m.movement = 2;
        m.attackRange = 1;
        init?.Invoke(m);
        StartDraft(m);
    }

    private void DuplicateCurrent()
    {
        if (current == null) return;
        var copy = Instantiate(current);
        copy.hideFlags = HideFlags.DontSave;
        copy.monsterName = current.monsterName + " Copy";
        copy.isSolved = false;
        DestroyDraft();
        StartDraft(copy);
    }

    private void StartDraft(Monster m)
    {
        isDraft = true;
        draftDirty = false;
        fileNameEdited = false;
        Bind(m);
        fileName = SuggestFileName(m);
        prefabName = ToPascal(m.monsterName);
        GUI.FocusControl(null);
    }

    private void Bind(Monster m)
    {
        current = m;
        so = new SerializedObject(m);
    }

    private static string SuggestFileName(Monster m) => ToPascal(m.monsterName);

    // =====================================================================
    // GUI
    // =====================================================================

    private void InitStyles()
    {
        if (richBox != null) return;
        richBox = new GUIStyle(EditorStyles.helpBox) { richText = true, wordWrap = true, fontSize = 12, padding = new RectOffset(8, 8, 6, 6) };
        miniGray = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
        miniGray.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.35f, 0.35f, 0.35f);
        richLabel = new GUIStyle(EditorStyles.label) { richText = true };
        listButton = new GUIStyle(EditorStyles.label) { richText = true, padding = new RectOffset(6, 4, 3, 3) };
        listButtonSelected = new GUIStyle(listButton);
        listButtonSelected.normal.background = MakeTex(EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.55f, 0.72f, 0.92f));
        listButtonSelected.normal.textColor = Color.white;
    }

    private void OnGUI()
    {
        InitStyles();
        if (current == null) NewDraft(null);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawList();
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandHeight(true), GUILayout.Width(1));
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.3f));
            DrawEditor();
        }
    }

    // ---------------- left: list ----------------

    private void DrawList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarDropDown, GUILayout.Width(50))) ShowNewMenu();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                {
                    RefreshAssets();
                    RefreshScene();
                }
            }
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            categoryFilter = EditorGUILayout.Popup(categoryFilter, new[] { "All categories" }.Concat(Enum.GetNames(typeof(EnemyCategory))).ToArray());

            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            if (isDraft) GUILayout.Label($"<b>+ {current.monsterName}</b>  <i>(draft)</i>", listButtonSelected);

            EnemyCategory? last = null;
            foreach (var m in assets)
            {
                if (categoryFilter > 0 && (int)m.category != categoryFilter - 1) continue;
                if (!string.IsNullOrEmpty(search)
                    && (m.monsterName ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && m.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (last != m.category) { last = m.category; EditorGUILayout.LabelField(m.category.ToString(), EditorStyles.boldLabel); }

                string stats = $"  <color=#888888>{m.resistance}({m.solvedResistance}) M{m.movement}{(PrefabsOf(m).Count == 0 ? " · no prefab" : "")}</color>";
                bool selected = !isDraft && m == current;
                if (GUILayout.Button($"{Name(m)}{stats}", selected ? listButtonSelected : listButton))
                {
                    Select(m);
                    GUIUtility.ExitGUI();
                }
            }
            if (assets.Count == 0) EditorGUILayout.HelpBox("No monsters yet. Use New.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"{assets.Count} monsters, {prefabs.Count} monster prefabs, {spawners.Count} spawners in scene", miniGray);
        }
    }

    private void ShowNewMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Blank (Standard, Resolve 3)"), false, () => NewDraft(null));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Archetype/Fragile (Resolve 2, Solved 1)"), false, () => NewDraft(m => { m.monsterName = "New Fragile Monster"; m.resistance = 2; m.solvedResistance = 1; m.loreToSolve = 2; m.movement = 3; }));
        menu.AddItem(new GUIContent("Archetype/Standard (Resolve 3, Solved 1)"), false, () => NewDraft(m => { m.monsterName = "New Standard Monster"; m.resistance = 3; m.solvedResistance = 1; m.loreToSolve = 3; m.movement = 2; }));
        menu.AddItem(new GUIContent("Archetype/Heavy (Resolve 4, Solved 2, meant to be Solved)"), false, () => NewDraft(m => { m.monsterName = "New Heavy Monster"; m.resistance = 4; m.solvedResistance = 2; m.loreToSolve = 4; m.movement = 1; m.stabilityDamage = 2; }));
        menu.AddSeparator("");
        foreach (var t in GddTemplates())
        {
            var tpl = t;
            menu.AddItem(new GUIContent($"GDD roster/{tpl.category}/{tpl.name}"), false, () => NewDraft(m => ApplyGdd(m, tpl)));
        }
        menu.ShowAsContext();
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
            Section("File", true, DrawFile);
            Section("Identity", true, DrawIdentity);
            Section("Durability and Solving", true, DrawDurability);
            Section("Threat", true, DrawThreat);
            Section("Grid behaviour", true, DrawBehaviour);
            if (so.ApplyModifiedProperties())
            {
                if (isDraft) draftDirty = true;
                if (!fileNameEdited) fileName = SuggestFileName(current);
            }

            Section("Prefab and spawning", true, DrawPrefabs);
            Section("Intent preview", true, DrawIntentPreview);
            Section("Odds", true, DrawOdds);
            Section($"Checks ({checks.Count(c => c.type == MessageType.Error)} errors, {checks.Count(c => c.type == MessageType.Warning)} warnings)", true, () =>
            {
                if (checks.Count == 0) EditorGUILayout.HelpBox("No problems found.", MessageType.None);
                foreach (var c in checks.OrderBy(c => c.type == MessageType.Error ? 0 : c.type == MessageType.Warning ? 1 : 2))
                    EditorGUILayout.HelpBox(c.text, c.type);
            });
            Section("Roster", true, DrawRoster);
            EditorGUILayout.EndScrollView();
        }
    }

    private void Section(string title, bool defaultOpen, Action body)
    {
        string key = title.Split('(')[0];
        if (!folds.TryGetValue(key, out bool open)) open = defaultOpen;
        EditorGUILayout.Space(4);
        open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
        if (open)
        {
            EditorGUI.indentLevel++;
            body();
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        folds[key] = open;
    }

    private void DrawToolbar(List<(MessageType type, string text)> checks)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int errors = checks.Count(c => c.type == MessageType.Error);
            int warnings = checks.Count(c => c.type == MessageType.Warning);
            GUILayout.Label(isDraft ? "Draft (not saved)" : AssetDatabase.GetAssetPath(current), EditorStyles.miniLabel);
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

    private void DrawFile()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            fileName = EditorGUILayout.TextField(new GUIContent("File name", "Asset file name, without .asset"), fileName);
            if (EditorGUI.EndChangeCheck()) fileNameEdited = true;
            if (GUILayout.Button("Auto", GUILayout.Width(44)))
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
                    string picked = EditorUtility.OpenFolderPanel("Monster folder", folder, "");
                    if (!string.IsNullOrEmpty(picked) && picked.StartsWith(Application.dataPath))
                        folder = "Assets" + picked.Substring(Application.dataPath.Length);
                }
            }
        }
    }

    private void P(string field, string label, string tooltip)
    {
        var p = so.FindProperty(field);
        if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, string.IsNullOrEmpty(p.tooltip) ? tooltip : p.tooltip), true);
    }

    private void DrawIdentity()
    {
        P("monsterName", "Name", "Shown in the log, intent text and tooltips.");
        P("monsterDescription", "Description", "What it does, in plain words. GDD: each enemy has one memorable trick.");
        P("category", "Category", "Assault (threatens girls and the Anchor), Sabotage (Ritual Progress and tiles), Control (positioning). A heroine it Downs gets an Omen of this family.");
        EditorGUILayout.LabelField(" ", CategoryHint(current.category), miniGray);
        P("monsterSprite", "Sprite", "Portrait / icon.");
        P("monsterHighlightSprite", "Highlight sprite", "Shown when targeted.");
        if (current.isSolved)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox("The old Is Solved flag is set on the asset. Combat ignores it now (Solved lives in RitualManager), but it should be off.", MessageType.Warning);
                if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(38))) so.FindProperty("isSolved").boolValue = false;
            }
        }
    }

    private void DrawDurability()
    {
        P("resistance", "Resolve", "One action must deal this much to defeat it; otherwise it resets (GDD Enemy Resolve reset). Fragile 2, Standard 3, Heavy 4.");
        P("solvedResistance", "Solved Resolve", "Resolve once its type is Solved. Normally 1-2.");
        P("loreToSolve", "Lore to Solve", "Insight needed to Solve the type (shared by all enemies of the type). GDD roster: 2-4; boss 8-10.");
        EditorGUILayout.LabelField(" ", "Fragile 2 (1) · Standard 3 (1) · Heavy 4 (2), meant to be Solved first · Boss Face 4 (3).", miniGray);
    }

    private void DrawThreat()
    {
        P("stabilityDamage", "Anchor damage", "Turn Capacity lost when it hits the Anchor unblocked. Each unblocked hit also Disrupts Stability. Standard 1, Gravehorn/boss 2.");
        P("heroineDamage", "Heroine damage", "Resolve removed from a heroine (or civilian) it hits. GDD: normally 1.");
    }

    private void DrawBehaviour()
    {
        P("movement", "Movement", "Tiles per enemy phase. GDD roster: 1-4. Clear Night and Relics/Omens can add to it.");
        P("attackRange", "Attack range", "Tiles from which it can hit. 1 = must stand next to the target. Range counts orthogonal steps (GridManager.Distance).");
        P("targetPreference", "Target preference", "Ritual: the Anchor first. Heroine: a reachable heroine first. Nearest: whichever is fewest steps away.");
        EditorGUILayout.LabelField(" ", PreferenceHint(current.targetPreference), miniGray);
    }

    // ---------------- prefabs and spawning ----------------

    private void DrawPrefabs()
    {
        if (isDraft)
        {
            EditorGUILayout.HelpBox("Create the asset first, then make its prefab here.", MessageType.None);
            return;
        }

        var mine = PrefabsOf(current);
        EditorGUILayout.LabelField(mine.Count == 0 ? "No prefab uses this monster yet, so nothing can spawn it." : "Prefabs using this monster:", EditorStyles.miniBoldLabel);
        foreach (var p in mine)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(p.name, AssetDatabase.GetAssetPath(p));
                if (GUILayout.Button("Ping", GUILayout.Width(44))) EditorGUIUtility.PingObject(p.gameObject);
                if (GUILayout.Button("Open", GUILayout.Width(44))) AssetDatabase.OpenAsset(p.gameObject);
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Make a prefab", EditorStyles.miniBoldLabel);
        prefabMode = (PrefabMode)EditorGUILayout.EnumPopup("From", prefabMode);
        if (prefabMode == PrefabMode.VariantOfExisting)
        {
            basePrefab = (MonsterHolder)EditorGUILayout.ObjectField(new GUIContent("Base monster prefab", "Its model, highlight, intent line and colliders are reused. Swap the model in the new prefab afterwards."),
                basePrefab, typeof(MonsterHolder), false);
            keepVariantLink = EditorGUILayout.Toggle(new GUIContent("Keep as Prefab Variant", "On: changes to the base (highlight, intent line) carry over. Off: an independent copy."), keepVariantLink);
            if (basePrefab == null && prefabs.Count > 0)
                EditorGUILayout.LabelField(" ", $"Pick one of your {prefabs.Count} monster prefabs, e.g. {prefabs[0].name}.", miniGray);
        }
        else
        {
            model = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Model", "An FBX or prefab. It becomes a child of the new prefab; a MonsterHolder and (if missing) a BoxCollider are added on the root."),
                model, typeof(GameObject), false);
            EditorGUILayout.LabelField(" ", "Highlight and Intent Line are left empty: assign them in the prefab afterwards (copy them from another monster).", miniGray);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            prefabFolder = EditorGUILayout.TextField("Prefab folder", prefabFolder);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string picked = EditorUtility.OpenFolderPanel("Prefab folder", prefabFolder, "");
                if (!string.IsNullOrEmpty(picked) && picked.StartsWith(Application.dataPath))
                    prefabFolder = "Assets" + picked.Substring(Application.dataPath.Length);
            }
        }
        prefabName = EditorGUILayout.TextField("Prefab name", prefabName);
        bool canMake = !string.IsNullOrWhiteSpace(prefabName) && (prefabMode == PrefabMode.VariantOfExisting ? basePrefab != null : model != null);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(EditorGUIUtility.labelWidth);
            using (new EditorGUI.DisabledScope(!canMake))
                if (GUILayout.Button("Create Prefab")) { CreatePrefab(); GUIUtility.ExitGUI(); }
        }

        // ----- open scene -----
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Open scene", EditorStyles.miniBoldLabel);
        if (mine.Count == 0)
        {
            EditorGUILayout.LabelField(" ", "Make a prefab first to add it to spawners.", miniGray);
            return;
        }
        if (spawners.Count == 0 && guardians.Count == 0)
        {
            EditorGUILayout.LabelField(" ", "No MonsterSpawner or Elite Guardian objective in the open scene.", miniGray);
            return;
        }

        var addPrefab = mine[0];
        foreach (var s in spawners)
        {
            if (s == null) continue;
            bool has = s.monsterPrefabs.Any(p => p != null && p.Data == current);
            string info = $"{s.monsterPrefabs.Count(p => p != null)} kinds, {s.monstersPerRound}/round from round {s.firstSpawnRound}";
            bool newHas = EditorGUILayout.ToggleLeft($"Spawner {s.name}  <color=#888888>{info}</color>", has, richLabel);
            if (newHas != has)
            {
                Undo.RecordObject(s, newHas ? "Add monster to spawner" : "Remove monster from spawner");
                if (newHas) s.monsterPrefabs.Add(addPrefab);
                else s.monsterPrefabs.RemoveAll(p => p != null && p.Data == current);
                EditorUtility.SetDirty(s);
                EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
            }
        }
        foreach (var g in guardians)
        {
            if (g == null) continue;
            var gso = new SerializedObject(g);
            var list = gso.FindProperty("guardianPrefabs");
            if (list == null) continue;
            int index = -1;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is MonsterHolder mh && mh.Data == current) { index = i; break; }
            bool has = index >= 0;
            bool newHas = EditorGUILayout.ToggleLeft($"Elite Guardian {g.gameObject.name}  <color=#888888>can be the guardian</color>", has, richLabel);
            if (newHas != has)
            {
                if (newHas)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = addPrefab;
                }
                else
                {
                    list.GetArrayElementAtIndex(index).objectReferenceValue = null;
                    list.DeleteArrayElementAtIndex(index);
                }
                gso.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(g.gameObject.scene);
            }
        }
        if (mine.Count > 1) EditorGUILayout.LabelField(" ", $"Ticking adds {addPrefab.name}.", miniGray);

        int perRound = spawners.Where(s => s != null && s.isActiveAndEnabled).Sum(s => s.monstersPerRound);
        if (perRound > 0)
            EditorGUILayout.LabelField(" ", $"The spawners in this scene add {perRound} monster(s) per round in total. " +
                                            "Nothing in MonsterSpawner caps the board at 5 regular enemies yet (Player Trust Rule 5).", miniGray);
    }

    private void CreatePrefab()
    {
        EnsureFolder(prefabFolder);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{prefabFolder.TrimEnd('/')}/{SanitizeFileName(prefabName)}.prefab");

        GameObject instance;
        if (prefabMode == PrefabMode.VariantOfExisting)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab.gameObject);
            if (!keepVariantLink) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = prefabName;
        }
        else
        {
            instance = new GameObject(prefabName);
            var visual = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (visual == null) visual = Instantiate(model);
            visual.name = model.name;
            visual.transform.SetParent(instance.transform, false);
            instance.AddComponent<MonsterHolder>();
            if (instance.GetComponentInChildren<Collider>() == null)
            {
                var box = instance.AddComponent<BoxCollider>();
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                    box.center = b.center - instance.transform.position;
                    box.size = b.size;
                }
            }
        }

        var holder = instance.GetComponent<MonsterHolder>();
        var hso = new SerializedObject(holder);
        hso.FindProperty("monsterData").objectReferenceValue = current;
        hso.ApplyModifiedPropertiesWithoutUndo();

        var saved = PrefabUtility.SaveAsPrefabAsset(instance, path, out bool ok);
        DestroyImmediate(instance);
        if (!ok || saved == null)
        {
            EditorUtility.DisplayDialog("Prefab failed", $"Could not save {path}.", "OK");
            return;
        }
        RefreshAssets();
        EditorGUIUtility.PingObject(saved);
        Debug.Log($"[Monsters] Created prefab {path} for {current.monsterName}.");
    }

    // ---------------- intent preview ----------------

    private void DrawIntentPreview()
    {
        var m = current;
        var sb = new StringBuilder();
        sb.AppendLine($"<b><size=14>{Name(m)}</size></b>  <color=#999999>{m.category} · Resolve {m.resistance} (Solved {m.solvedResistance}) · Lore to Solve {m.loreToSolve}</color>");
        if (!string.IsNullOrWhiteSpace(m.monsterDescription)) sb.AppendLine(m.monsterDescription);
        sb.AppendLine();
        sb.AppendLine($"<color=#999999>As the combat log will describe its intents:</color>");
        sb.AppendLine($"{Name(m)} -> Anchor: moves {Mathf.Min(m.movement, 3)}/{m.movement} tiles, HITS (-{m.stabilityDamage} Turn Capacity and -Stability).");
        sb.AppendLine($"{Name(m)} -> Kaia: moves {m.movement}/{m.movement} tiles, HITS (-{m.heroineDamage} Resolve).");
        sb.Append($"<color=#999999>Target: {PreferenceHint(m.targetPreference)}</color>");
        GUILayout.Label(sb.ToString(), richBox);
    }

    // ---------------- odds ----------------

    private void DrawOdds()
    {
        var m = current;
        var dists = Pools.Select(SuccessDistribution).ToArray();
        float colW = 80;

        Row("", Pools.Select(p => $"<b>{p}d8</b>"), colW);
        Row($"Arcane Strike kills (Resolve {m.resistance})", dists.Select(d => Pct(AtLeast(d, m.resistance))), colW);
        Row($"... on a Marked target", dists.Select(d => Pct(AtLeast(d, Mathf.Max(1, m.resistance - 1)))), colW);
        Row($"Once Solved (Resolve {m.solvedResistance})", dists.Select(d => Pct(AtLeast(d, Mathf.Max(1, m.solvedResistance)))), colW);
        Row($"Study Solves it in 1 action", dists.Select(d => Pct(AtLeast(d, m.loreToSolve))), colW);
        Row($"Studies to Solve (average)", dists.Select(d => m.loreToSolve <= 0 ? "0" : ExpectedStudies(d, m.loreToSolve).ToString("0.0")), colW);
        EditorGUILayout.LabelField(" ", "Insight is shared by every enemy of the type and Solving lasts the encounter, so two actions (Study, then Strike) " +
                                        "can beat one big Strike. Kill chances include Criticals (an 8 counts as 2 Successes).", miniGray);

        EditorGUILayout.Space(2);
        string Rounds(int d)
        {
            int steps = Mathf.Max(0, d - Mathf.Max(1, m.attackRange));
            if (steps == 0) return "hits in round 1";
            if (m.movement <= 0) return "never (Movement 0)";
            int rounds = Mathf.CeilToInt(steps / (float)m.movement);
            return $"{rounds} enemy phase{(rounds == 1 ? "" : "s")}";
        }
        EditorGUILayout.LabelField("Reaches the Anchor (spawned 6 / 9 tiles away)", $"{Rounds(6)} / {Rounds(9)}", richLabel);
        int hits = m.heroineDamage > 0 ? Mathf.CeilToInt(HeroineResolve / (float)m.heroineDamage) : 0;
        EditorGUILayout.LabelField("Hits to Down a 3-Resolve heroine", hits > 0 ? hits.ToString() : "never", richLabel);
        EditorGUILayout.LabelField("Unblocked Anchor hit", $"-{m.stabilityDamage} Turn Capacity, and Stability Disrupted", richLabel);
        EditorGUILayout.LabelField(" ", "GDD Engagement Ring: enemies spawn 6-9 tiles from the Anchor; first contact should come in round 1 or 2.", miniGray);
    }

    // ---------------- roster ----------------

    private void DrawRoster()
    {
        var cols = new[] { "Cat.", "Res (S)", "Lore", "Move", "Rng", "Anchor", "Hero", "Target", "Prefab" };
        float nameW = 150, colW = 54;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("<b>Monster</b>", richLabel, GUILayout.Width(nameW));
            foreach (var c in cols) EditorGUILayout.LabelField($"<b>{c}</b>", richLabel, GUILayout.Width(colW));
        }
        foreach (var m in assets)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool sel = !isDraft && m == current;
                if (GUILayout.Button(sel ? $"<b>{Name(m)}</b>" : Name(m), listButton, GUILayout.Width(nameW))) { Select(m); GUIUtility.ExitGUI(); }
                var cells = new[]
                {
                    m.category.ToString().Substring(0, 3), $"{m.resistance} ({m.solvedResistance})", m.loreToSolve.ToString(),
                    m.movement.ToString(), m.attackRange.ToString(), m.stabilityDamage.ToString(), m.heroineDamage.ToString(),
                    m.targetPreference.ToString(), PrefabsOf(m).Count.ToString()
                };
                foreach (var c in cells) EditorGUILayout.LabelField(c, richLabel, GUILayout.Width(colW));
            }
        }
        var counts = Enum.GetValues(typeof(EnemyCategory)).Cast<EnemyCategory>().Select(c => $"{c} {assets.Count(m => m.category == c)}");
        EditorGUILayout.LabelField(" ", $"Per category: {string.Join(", ", counts)}. GDD 1.0: six regular enemies, two per category, differing by delivery " +
                                        "(one mobile or immediate, one slower, broader or more durable).", miniGray);
    }

    private void Row(string label, IEnumerable<string> cells, float colW)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, richLabel, GUILayout.Width(230));
            foreach (var c in cells) EditorGUILayout.LabelField(c, richLabel, GUILayout.Width(colW));
        }
    }

    // =====================================================================
    // Asset operations
    // =====================================================================

    private void CreateAssetFromDraft(int errors)
    {
        if (errors > 0 && !EditorUtility.DisplayDialog("Create with errors?", $"{errors} check(s) report problems. Create anyway?", "Create", "Cancel"))
            return;
        string clean = SanitizeFileName(fileName);
        if (string.IsNullOrEmpty(clean)) return;
        EnsureFolder(folder);
        string path = $"{folder.TrimEnd('/')}/{clean}.asset";
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
        {
            EditorUtility.DisplayDialog("File exists", $"{path} already exists. Pick another file name.", "OK");
            return;
        }

        var asset = current;
        asset.hideFlags = HideFlags.None;
        asset.name = clean;
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        isDraft = false;
        draftDirty = false;
        fileNameEdited = true;
        fileName = clean;
        Bind(asset);
        RefreshAssets();
        if (basePrefab == null) basePrefab = prefabs.FirstOrDefault();
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[Monsters] Created {path}. Make its prefab in the 'Prefab and spawning' section.");
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
        var mine = PrefabsOf(current);
        string extra = mine.Count > 0 ? $"\n\n{mine.Count} prefab(s) use it ({string.Join(", ", mine.Select(p => p.name))}). They are kept but will have no Monster Data." : "";
        if (!EditorUtility.DisplayDialog("Delete monster?", $"Move {path} to the trash?{extra}", "Delete", "Cancel")) return;
        AssetDatabase.MoveAssetToTrash(path);
        current = null;
        RefreshAssets();
        NewDraft(null);
    }

    // =====================================================================
    // Checks: GDD enemy rules and how combat reads the fields
    // =====================================================================

    private List<(MessageType type, string text)> Validate(Monster m)
    {
        var list = new List<(MessageType, string)>();
        void Err(string s) => list.Add((MessageType.Error, s));
        void Warn(string s) => list.Add((MessageType.Warning, s));
        void Info(string s) => list.Add((MessageType.Info, s));
        if (m == null) return list;

        if (string.IsNullOrWhiteSpace(m.monsterName)) Err("Name is empty: the log and intents would use the GameObject name.");
        else if (assets.Any(o => o != m && string.Equals(o.monsterName, m.monsterName, StringComparison.OrdinalIgnoreCase)))
            Warn($"Another monster is already called \"{m.monsterName}\". Insight is tracked per Monster asset, so they would Solve separately.");

        // Durability
        if (m.resistance <= 0) Err("Resolve 0: any hit with a Success defeats it.");
        if (m.solvedResistance <= 0) Err("Solved Resolve 0: once Solved, any hit defeats it, even 1 Success. Use at least 1.");
        if (m.solvedResistance > m.resistance) Err("Solved Resolve is higher than Resolve: Solving would make it tougher.");
        else if (m.solvedResistance == m.resistance && m.resistance > 1) Warn("Solving doesn't lower its Resolve, so Lore does nothing against it.");
        if (m.resistance >= 5) Warn($"Resolve {m.resistance}: even a 7d8 specialist kills it only {Pct(AtLeast(SuccessDistribution(7), m.resistance))} of the time. GDD: Heavy is 4; more is boss territory.");
        if (m.resistance >= 4 && m.solvedResistance >= 3) Warn("Heavy but still tough once Solved. GDD: Heavy enemies are meant to be Solved first, Solved Resolve 1-2.");
        if (m.loreToSolve <= 0) Err("Lore to Solve 0: the first Insight Solves it at once.");
        else if (m.loreToSolve > 8) Warn($"Lore to Solve {m.loreToSolve}: GDD gives 8 to the Threefold Revenant and 10 to Zor'Goth. Regular enemies use 2-4.");
        else if (m.loreToSolve > 4) Info($"Lore to Solve {m.loreToSolve} is above the regular roster (2-4).");
        if (m.resistance >= 4 && m.loreToSolve > m.resistance + 1)
            Warn("Hard to kill and slow to Solve: the GDD answer to high Resolve is Solving, so keep Lore to Solve close to its Resolve.");

        // Threat
        if (m.heroineDamage <= 0 && m.targetPreference == TargetPreference.Heroine) Err("Prefers heroines but deals 0 Resolve damage: its hits do nothing.");
        if (m.heroineDamage >= 2) Warn($"Heroine damage {m.heroineDamage}: Downs a 3-Resolve girl in {Mathf.CeilToInt(3f / m.heroineDamage)} hits. GDD: a damaging enemy action normally removes 1.");
        if (m.stabilityDamage <= 0 && m.targetPreference == TargetPreference.Ritual) Warn("Goes for the Anchor but costs 0 Turn Capacity. It still Disrupts Stability on an unblocked hit.");
        if (m.stabilityDamage >= 3) Warn($"Anchor damage {m.stabilityDamage}: GDD gives 2 to the Gravehorn and bosses. Two hits would cost half the 12-round budget.");

        // Behaviour
        if (m.movement <= 0) Warn("Movement 0: it never moves (only Elite Guardians hold position). It only hits what comes into range.");
        if (m.movement >= 5) Warn($"Movement {m.movement}: faster than the Gloam Skulker (4). Hard to telegraph around and to block.");
        if (m.attackRange >= 3) Warn($"Attack range {m.attackRange}: it can hit from {m.attackRange} tiles, so heroines can't body-block it. Path blocking is a core answer in the GDD.");
        else if (m.attackRange == 2) Info("Attack range 2: blocking its path works less well.");
        if (m.attackRange <= 0) Err("Attack range 0: EnemyPlanner needs range 1 or more, so it can never hit.");

        // Category identity
        switch (m.category)
        {
            case EnemyCategory.Sabotage:
                if (m.targetPreference == TargetPreference.Heroine) Warn("Sabotage enemies interfere with the ritual; preferring heroines copies Assault's job.");
                if (m.heroineDamage >= 2) Warn("Sabotage with heavy heroine damage overlaps with Assault.");
                break;
            case EnemyCategory.Control:
                if (m.stabilityDamage >= 2) Warn("Control with heavy Anchor damage overlaps with Assault.");
                if (m.targetPreference == TargetPreference.Ritual) Info("Control enemies manipulate positioning; Heroine or Nearest usually fits better than Ritual.");
                break;
            case EnemyCategory.Assault:
                if (m.heroineDamage <= 0 && m.stabilityDamage <= 0) Err("An Assault enemy that damages nothing.");
                break;
        }
        Info("Special actions (Pounce, Charge, Feed on the Circle, Null Sign, Tether, Fog) and Empowered intents aren't built in combat yet. " +
             "This monster uses the generic intent: walk the shown path, then hit the Anchor or a heroine.");

        // Identity / assets
        if (string.IsNullOrWhiteSpace(m.monsterDescription)) Info("No description. GDD: each enemy has one memorable trick; say what it is.");
        if (m.monsterSprite == null) Info("No sprite.");
        if (m.isSolved) Warn("The old Is Solved flag is on. Combat ignores it, but clear it (button in Identity).");
        if (!isDraft && PrefabsOf(m).Count == 0) Warn("No prefab uses this monster, so spawners can't produce it. Make one in 'Prefab and spawning'.");
        if (!isDraft && PrefabsOf(m).Count > 0 && spawners.Count > 0 && !spawners.Any(s => s.monsterPrefabs.Any(p => p != null && p.Data == m))
            && !guardians.Any())
            Info("No spawner in the open scene produces it.");
        return list;
    }

    private static string CategoryHint(EnemyCategory c) => c switch
    {
        EnemyCategory.Assault => "Assault: threatens the girls and the ritual's endurance. GDD roster: Gloam Skulker (mobile), Gravehorn (slow, heavy). Downs give body and defence Omens.",
        EnemyCategory.Sabotage => "Sabotage: interferes with Ritual Progress and ritual tiles. GDD roster: Mote Leech (mobile), Hex Scribe (ranged, slow). Downs give ritual-work Omens.",
        _ => "Control: manipulates positioning, movement and space. GDD roster: Chain Shade (tether), Fog Weaver (terrain). Downs give movement and focus Omens."
    };

    private static string PreferenceHint(TargetPreference p) => p switch
    {
        TargetPreference.Heroine => "hits a reachable heroine; otherwise the Anchor if it can hit it; otherwise walks toward the nearest heroine.",
        TargetPreference.Nearest => "whichever target is fewest steps away (the Anchor wins ties).",
        _ => "hits the Anchor if it can; otherwise a heroine it can reach; otherwise walks toward the Anchor. Surrounding the Anchor forces it onto the girls."
    };

    // =====================================================================
    // GDD roster templates
    // =====================================================================

    private struct Template
    {
        public string name, description;
        public EnemyCategory category;
        public int resolve, solved, lore, move, range, anchor, hero;
        public TargetPreference pref;
    }

    private static IEnumerable<Template> GddTemplates()
    {
        yield return new Template
        {
            name = "Gloam Skulker", category = EnemyCategory.Assault, resolve = 2, solved = 1, lore = 2, move = 4, range = 1, anchor = 1, hero = 1, pref = TargetPreference.Heroine,
            description = "Pounce: moves up to 4 along a shown path to a target heroine and deals 1 Resolve damage if adjacent. Answered by repositioning, blocking its path, displacing it or Shielding."
        };
        yield return new Template
        {
            name = "Gravehorn", category = EnemyCategory.Assault, resolve = 4, solved = 2, lore = 4, move = 1, range = 1, anchor = 2, hero = 1, pref = TargetPreference.Ritual,
            description = "Ruinous Charge: a straight line at the Anchor; reaching it costs 2 Turn Capacity. (The charge, push and wall damage are not built yet: it walks 1 and hits.) Answered by terrain in its lane, pushing it off line, or Solving then killing it."
        };
        yield return new Template
        {
            name = "Mote Leech", category = EnemyCategory.Sabotage, resolve = 2, solved = 1, lore = 2, move = 2, range = 1, anchor = 1, hero = 1, pref = TargetPreference.Ritual,
            description = "Feed on the Circle: attaches to the nearest Sigil or the Anchor and removes 6 Progress each enemy phase. (Feeding is not built yet: it hits the Anchor.) Answered by displacement and occupying approach tiles."
        };
        yield return new Template
        {
            name = "Hex Scribe", category = EnemyCategory.Sabotage, resolve = 3, solved = 1, lore = 3, move = 1, range = 1, anchor = 1, hero = 1, pref = TargetPreference.Ritual,
            description = "Write the Null Sign: corrupts a Sigil within 4 and its adjacent tiles. (Corruption is not built yet; range left at 1 so it doesn't snipe the Anchor.) Answered by cleansing, using another Sigil, or removing it."
        };
        yield return new Template
        {
            name = "Chain Shade", category = EnemyCategory.Control, resolve = 3, solved = 1, lore = 3, move = 2, range = 1, anchor = 1, hero = 1, pref = TargetPreference.Heroine,
            description = "Spectral Tether: a heroine within 4 has Move 1 and is pulled toward it. (The tether is not built yet: it walks and hits.) Answered by displacing it, swapping the target, or killing it."
        };
        yield return new Template
        {
            name = "Fog Weaver", category = EnemyCategory.Control, resolve = 2, solved = 1, lore = 2, move = 2, range = 1, anchor = 1, hero = 1, pref = TargetPreference.Nearest,
            description = "Shrouded Path: a line of 3 Fog tiles for two rounds (+1 movement cost, blocks ranged targeting). (Fog is not built yet: it walks and hits.) Answered by repositioning early or clearing the Fog."
        };
    }

    private static void ApplyGdd(Monster m, Template t)
    {
        m.monsterName = t.name;
        m.monsterDescription = t.description;
        m.category = t.category;
        m.resistance = t.resolve;
        m.solvedResistance = t.solved;
        m.loreToSolve = t.lore;
        m.movement = t.move;
        m.attackRange = t.range;
        m.stabilityDamage = t.anchor;
        m.heroineDamage = t.hero;
        m.targetPreference = t.pref;
    }

    // =====================================================================
    // Odds (matching DiceRules: 6-7 = 1 Success, 8 = Critical worth 2)
    // =====================================================================

    /// <summary>p[s] = chance of exactly s Successes with this pool.</summary>
    private static double[] SuccessDistribution(int pool)
    {
        int maxS = pool * DiceRules.CriticalValue;
        double pFail = (DiceRules.SuccessMin - 1.0) / DiceRules.Sides;
        double pCrit = (DiceRules.Sides - DiceRules.CriticalFace + 1.0) / DiceRules.Sides;
        double pHit = Math.Max(0.0, 1.0 - pFail - pCrit);
        var p = new double[maxS + 1];
        p[0] = 1.0;
        for (int d = 0; d < pool; d++)
        {
            var n = new double[maxS + 1];
            for (int s = 0; s <= maxS; s++)
            {
                if (p[s] == 0) continue;
                n[s] += p[s] * pFail;
                if (s + 1 <= maxS) n[s + 1] += p[s] * pHit;
                if (s + DiceRules.CriticalValue <= maxS) n[s + DiceRules.CriticalValue] += p[s] * pCrit;
            }
            p = n;
        }
        return p;
    }

    private static double AtLeast(double[] p, int target)
    {
        double t = 0;
        for (int s = Math.Max(0, target); s < p.Length; s++) t += p[s];
        return target <= 0 ? 1.0 : t;
    }

    /// <summary>Expected number of Study actions (1 Insight per Success) to reach the Lore to Solve.</summary>
    private static double ExpectedStudies(double[] p, int lore)
    {
        // notSolved[k] = chance of holding k Insight (k < lore) after the studies so far
        var notSolved = new double[lore];
        notSolved[0] = 1.0;
        double expected = 0;
        for (int step = 0; step < 50; step++)
        {
            double remaining = notSolved.Sum();
            if (remaining < 1e-6) break;
            expected += remaining;
            var next = new double[lore];
            for (int k = 0; k < lore; k++)
            {
                if (notSolved[k] == 0) continue;
                for (int s = 0; s < p.Length && k + s < lore; s++)
                    next[k + s] += notSolved[k] * p[s];
            }
            notSolved = next;
        }
        return expected;
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    private static string Name(Monster m) => string.IsNullOrWhiteSpace(m.monsterName) ? m.name : m.monsterName;
    private static string Pct(double v) => v.ToString("P0");

    private static string ToPascal(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Monster";
        s = s.Replace("'", "").Replace("’", "");
        var words = s.Split(new[] { ' ', '-', '_', '.', ',', ':', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            var clean = new string(w.Where(char.IsLetterOrDigit).ToArray());
            if (clean.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(clean[0])).Append(clean.Substring(1));
        }
        return sb.Length > 0 ? sb.ToString() : "Monster";
    }

    private static string SanitizeFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Trim().Where(c => !invalid.Contains(c)).ToArray());
    }

    private static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        if (slash <= 0) return;
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
