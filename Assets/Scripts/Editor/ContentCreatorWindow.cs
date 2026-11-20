using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Moonlit Ritual > Omen, Relic and Objective Creator
///
/// One window with three tabs for the run content:
///   Omens      - Omen assets (and subclasses with special rules), RunManager Omen Pool toggle,
///                family/scope coverage against the GDD targets, auto rules text.
///   Relics     - Relic assets (and subclasses), RunManager Relic Pool toggle, rarity coverage.
///   Objectives - ObjectiveDefinition assets (texts + rewards), "add to the scene's ObjectiveManager"
///                with any ObjectiveBase type, and the components in the open scene that use it.
/// Each tab can also write a new C# script (an Omen / Relic subclass with hooks, or a new ObjectiveBase
/// type) for rules the data fields can't express.
///
/// New assets are drafts in memory until you press Create. Edits to existing assets apply at once (Undo works).
/// Place this file in an Editor folder (e.g. Assets/Scripts/Editor).
/// </summary>
public class ContentCreatorWindow : EditorWindow
{
    private ContentTab[] tabs;
    private int tabIndex;

    internal RunManager run;
    internal ObjectiveManager objectiveManager;
    internal List<ObjectiveBase> sceneObjectives = new();

    internal GUIStyle richBox, miniGray, listButton, listButtonSelected, richLabel;

    [MenuItem("Tools/Moonlit Ritual/Omen, Relic and Objective Creator")]
    public static void Open()
    {
        var w = GetWindow<ContentCreatorWindow>("Content Creator");
        w.minSize = new Vector2(820, 520);
    }

    private void OnEnable()
    {
        tabs = new ContentTab[] { new OmenTab(this), new RelicTab(this), new ObjectiveTab(this) };
        RefreshScene();
        foreach (var t in tabs) t.Init();
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        if (tabs != null) foreach (var t in tabs) t.DestroyDraft();
    }

    private void OnProjectChange()
    {
        if (tabs == null) return;
        foreach (var t in tabs) t.OnProjectChanged();
        Repaint();
    }

    private void OnHierarchyChange() { RefreshScene(); Repaint(); }
    private void OnFocus() => RefreshScene();

    private void OnUndoRedo()
    {
        if (tabs != null) foreach (var t in tabs) t.so?.Update();
        RefreshScene();
        Repaint();
    }

    internal void RefreshScene()
    {
        run = FindAnyObjectByType<RunManager>(FindObjectsInactive.Include);
        objectiveManager = FindAnyObjectByType<ObjectiveManager>(FindObjectsInactive.Include);
        sceneObjectives = FindObjectsByType<ObjectiveBase>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
    }

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
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int newIndex = GUILayout.Toolbar(tabIndex, tabs.Select(t => t.Title).ToArray(), EditorStyles.toolbarButton, GUILayout.Width(330));
            if (newIndex != tabIndex) { tabIndex = newIndex; GUI.FocusControl(null); }
            GUILayout.FlexibleSpace();
            GUILayout.Label(run != null ? $"RunManager: {run.name}" : "No RunManager in the open scene", EditorStyles.miniLabel);
        }
        tabs[tabIndex].OnGUI();
    }

    // =====================================================================
    // Shared helpers
    // =====================================================================

    internal static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    internal static string ToPascal(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "New";
        s = s.Replace("'", "").Replace("’", "");
        var words = s.Split(new[] { ' ', '-', '_', '.', ',', ':', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            var clean = new string(w.Where(char.IsLetterOrDigit).ToArray());
            if (clean.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(clean[0])).Append(clean.Substring(1));
        }
        return sb.Length > 0 ? sb.ToString() : "New";
    }

    internal static string SanitizeFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Trim().Where(c => !invalid.Contains(c)).ToArray());
    }

    internal static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        if (slash <= 0) return;
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    /// <summary>Draws the children of a serialized class field without a foldout.</summary>
    internal static void DrawChildren(SerializedProperty p)
    {
        var it = p.Copy();
        var end = it.GetEndProperty();
        bool enter = true;
        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
        {
            EditorGUILayout.PropertyField(it, true);
            enter = false;
        }
    }

    /// <summary>Top-level fields not in 'known' (fields a subclass added).</summary>
    internal static bool DrawOtherFields(SerializedObject so, HashSet<string> known)
    {
        bool any = false;
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script" || known.Contains(it.name)) continue;
            EditorGUILayout.PropertyField(it, true);
            any = true;
        }
        return any;
    }

    /// <summary>The RunEffects numbers with which direction helps the player (+1: more is better, -1: less is better).</summary>
    internal static IEnumerable<(string label, float value, int goodSign)> EffectFields(RunEffects e)
    {
        yield return ("Turn Capacity", e.turnCapacity, 1);
        yield return ("Starting Stability", e.startingStability, 1);
        yield return ("Max Stability", e.maxStability, 1);
        yield return ("Anchor passive Progress", e.anchorPassiveProgress, 1);
        yield return ("Channel Critical bonus", e.channelCritBonus, 1);
        yield return ("Capacity restores per round", e.capacityRestoresPerRound, 1);
        yield return ("Max Resolve", e.maxResolve, 1);
        yield return ("Move", e.moveRange, 1);
        yield return ("Ability range", e.abilityRange, 1);
        yield return ("Success threshold", e.successThreshold, -1);
        yield return ("Empty roll rerolls", e.emptyRollRerolls, 1);
        yield return ("Enemy movement", e.enemyMovement, -1);
        yield return ("Extra spawns per round", e.extraSpawnsPerRound, -1);
        yield return ("Reinforcements earlier", e.reinforcementsEarlier, -1);
    }

    internal static bool IsEmpty(RunEffects e) => e == null || EffectFields(e).All(f => f.value == 0);

    internal static bool IsValidClassName(string s) => !string.IsNullOrEmpty(s) && Regex.IsMatch(s, @"^[A-Za-z_][A-Za-z0-9_]*$");

    internal static Type FindType(string className) =>
        TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().FirstOrDefault(t => t.Name == className)
        ?? TypeCache.GetTypesDerivedFrom<ObjectiveBase>().FirstOrDefault(t => t.Name == className);
}

// =========================================================================
// Generic tab: list, drafts, create / rename / delete, preview, checks
// =========================================================================

internal abstract class ContentTab
{
    protected readonly ContentCreatorWindow w;

    public abstract string Title { get; }
    protected abstract Type BaseType { get; }
    protected abstract string DefaultFolder { get; }
    protected abstract string NameProperty { get; }
    protected abstract string ScriptFolder { get; }

    protected List<ScriptableObject> assets = new();
    public ScriptableObject current;
    public SerializedObject so;
    protected bool isDraft, draftDirty, fileNameEdited;
    protected string fileName = "", folder;
    protected string search = "";
    protected int groupFilter;
    private Vector2 listScroll, editScroll;
    private string newScriptName = "";
    private readonly Dictionary<string, bool> folds = new();

    protected ContentTab(ContentCreatorWindow window) { w = window; }

    // ---------- per-type hooks ----------
    protected abstract string NameOf(ScriptableObject a);
    protected abstract string GroupOf(ScriptableObject a);
    protected abstract string[] Groups { get; }
    protected virtual string ListSuffix(ScriptableObject a) => "";
    protected abstract string SuggestFileName(ScriptableObject a);
    protected abstract void InitDraft(ScriptableObject a);
    protected abstract void DrawBody();
    protected abstract void DrawPreview();
    protected abstract void DrawStats();
    protected abstract void Validate(ScriptableObject a, List<(MessageType type, string text)> list);
    protected virtual void DrawSceneLinks() { }
    protected virtual void OnCreated(ScriptableObject a) { }
    protected virtual void OnDeleting(ScriptableObject a) { }
    protected abstract string ScriptTemplate(string className);
    protected virtual string ScriptHelp => "";

    // ---------- lifecycle ----------
    public void Init()
    {
        folder = DefaultFolder;
        RefreshAssets();
        if (current == null) NewDraft(BaseType);
    }

    public void OnProjectChanged()
    {
        RefreshAssets();
        if (!isDraft && current == null) NewDraft(BaseType);
    }

    protected void RefreshAssets()
    {
        assets = AssetDatabase.FindAssets($"t:{BaseType.Name}")
            .Select(g => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null && BaseType.IsInstanceOfType(a))
            .OrderBy(a => Array.IndexOf(Groups, GroupOf(a))).ThenBy(NameOf)
            .ToList();
    }

    protected IEnumerable<T> All<T>() where T : ScriptableObject => assets.OfType<T>();

    public void DestroyDraft()
    {
        if (isDraft && current != null) UnityEngine.Object.DestroyImmediate(current);
    }

    private bool ConfirmLeaveDraft()
    {
        if (!isDraft || !draftDirty) return true;
        return EditorUtility.DisplayDialog("Discard draft?",
            $"\"{NameOf(current)}\" has not been created yet. Discard it?", "Discard", "Keep editing");
    }

    private void Select(ScriptableObject a)
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

    protected void NewDraft(Type type, Action<ScriptableObject> init = null)
    {
        if (!ConfirmLeaveDraft()) return;
        DestroyDraft();
        var a = ScriptableObject.CreateInstance(type);
        a.hideFlags = HideFlags.DontSave;
        InitDraft(a);
        init?.Invoke(a);
        StartDraft(a);
    }

    private void DuplicateCurrent()
    {
        if (current == null) return;
        var copy = UnityEngine.Object.Instantiate(current);
        copy.hideFlags = HideFlags.DontSave;
        var p = new SerializedObject(copy).FindProperty(NameProperty);
        if (p != null) { p.stringValue += " Copy"; p.serializedObject.ApplyModifiedPropertiesWithoutUndo(); }
        DestroyDraft();
        StartDraft(copy);
    }

    private void StartDraft(ScriptableObject a)
    {
        isDraft = true;
        draftDirty = false;
        fileNameEdited = false;
        Bind(a);
        fileName = SuggestFileName(a);
        GUI.FocusControl(null);
    }

    private void Bind(ScriptableObject a)
    {
        current = a;
        so = new SerializedObject(a);
    }

    protected bool IsDraft => isDraft;

    // ---------- GUI ----------
    public void OnGUI()
    {
        if (current == null) NewDraft(BaseType);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawList();
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandHeight(true), GUILayout.Width(1));
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.3f));
            DrawEditor();
        }
    }

    private void DrawList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarDropDown, GUILayout.Width(50)))
                {
                    var menu = new GenericMenu();
                    FillNewMenu(menu);
                    var subTypes = TypeCache.GetTypesDerivedFrom(BaseType).Where(t => !t.IsAbstract).OrderBy(t => t.Name).ToList();
                    if (subTypes.Count > 0)
                    {
                        menu.AddSeparator("");
                        foreach (var t in subTypes)
                            menu.AddItem(new GUIContent($"Custom script/{ObjectNames.NicifyVariableName(t.Name)}"), false, () => NewDraft(t));
                    }
                    menu.ShowAsContext();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), EditorStyles.toolbarButton, GUILayout.Width(28)))
                {
                    RefreshAssets();
                    w.RefreshScene();
                }
            }

            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            groupFilter = EditorGUILayout.Popup(groupFilter, new[] { "All" }.Concat(Groups).ToArray());

            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            if (isDraft) GUILayout.Label($"<b>+ {NameOf(current)}</b>  <i>(draft)</i>", w.listButtonSelected);

            string lastGroup = null;
            foreach (var a in assets)
            {
                string g = GroupOf(a);
                if (groupFilter > 0 && g != Groups[groupFilter - 1]) continue;
                if (!string.IsNullOrEmpty(search)
                    && NameOf(a).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && a.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (g != lastGroup) { lastGroup = g; EditorGUILayout.LabelField(g, EditorStyles.boldLabel); }

                bool selected = !isDraft && a == current;
                if (GUILayout.Button($"{NameOf(a)}{ListSuffix(a)}", selected ? w.listButtonSelected : w.listButton))
                {
                    Select(a);
                    GUIUtility.ExitGUI();
                }
            }
            if (assets.Count == 0)
                EditorGUILayout.HelpBox("None yet. Use New, or Tools > Moonlit Ritual > Create Starter Omens, Relics and Objectives.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"{assets.Count} {Title.ToLower()}", w.miniGray);
        }
    }

    protected virtual void FillNewMenu(GenericMenu menu) => menu.AddItem(new GUIContent("Blank"), false, () => NewDraft(BaseType));

    private void DrawEditor()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            var checks = new List<(MessageType type, string text)>();
            ValidateCommon(current, checks);
            Validate(current, checks);
            DrawToolbar(checks);

            editScroll = EditorGUILayout.BeginScrollView(editScroll);
            so.Update();
            Section("File", true, DrawFile);
            DrawBody();
            if (so.ApplyModifiedProperties())
            {
                if (isDraft) draftDirty = true;
                if (!fileNameEdited) fileName = SuggestFileName(current);
            }

            DrawSceneLinks();
            Section("Preview", true, DrawPreview);
            Section($"Checks ({checks.Count(c => c.type == MessageType.Error)} errors, {checks.Count(c => c.type == MessageType.Warning)} warnings)", true, () =>
            {
                if (checks.Count == 0) EditorGUILayout.HelpBox("No problems found.", MessageType.None);
                foreach (var c in checks.OrderBy(c => c.type == MessageType.Error ? 0 : c.type == MessageType.Warning ? 1 : 2))
                    EditorGUILayout.HelpBox(c.text, c.type);
            });
            Section($"All {Title.ToLower()}: coverage", true, DrawStats);
            Section("New rule script", false, DrawScriptGenerator);
            EditorGUILayout.EndScrollView();
        }
    }

    protected void Section(string title, bool defaultOpen, Action body)
    {
        if (!folds.TryGetValue(title.Split('(')[0], out bool open)) open = defaultOpen;
        EditorGUILayout.Space(4);
        open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
        if (open)
        {
            EditorGUI.indentLevel++;
            body();
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        folds[title.Split('(')[0]] = open;
    }

    private void DrawToolbar(List<(MessageType type, string text)> checks)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int errors = checks.Count(c => c.type == MessageType.Error);
            int warnings = checks.Count(c => c.type == MessageType.Warning);
            string typeName = current.GetType() != BaseType ? $"  [{current.GetType().Name}]" : "";
            GUILayout.Label((isDraft ? "Draft (not saved)" : AssetDatabase.GetAssetPath(current)) + typeName, EditorStyles.miniLabel);
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
                    string picked = EditorUtility.OpenFolderPanel("Folder", folder, "");
                    if (!string.IsNullOrEmpty(picked) && picked.StartsWith(Application.dataPath))
                        folder = "Assets" + picked.Substring(Application.dataPath.Length);
                }
            }
        }
    }

    private void ValidateCommon(ScriptableObject a, List<(MessageType, string)> list)
    {
        string n = NameOf(a);
        if (string.IsNullOrWhiteSpace(n)) list.Add((MessageType.Error, "The name is empty."));
        else if (assets.Any(o => o != a && string.Equals(NameOf(o), n, StringComparison.OrdinalIgnoreCase)))
            list.Add((MessageType.Warning, $"Another one is already called \"{n}\"."));
    }

    // ---------- asset operations ----------
    private void CreateAssetFromDraft(int errors)
    {
        if (errors > 0 && !EditorUtility.DisplayDialog("Create with errors?",
                $"{errors} check(s) report problems. Create anyway?", "Create", "Cancel"))
            return;

        string clean = ContentCreatorWindow.SanitizeFileName(fileName);
        if (string.IsNullOrEmpty(clean)) return;
        ContentCreatorWindow.EnsureFolder(folder);
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
        OnCreated(asset);
        AssetDatabase.SaveAssets();

        isDraft = false;
        draftDirty = false;
        fileNameEdited = true;
        fileName = clean;
        Bind(asset);
        RefreshAssets();
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[Content] Created {path}");
    }

    private void RenameCurrent()
    {
        string clean = ContentCreatorWindow.SanitizeFileName(fileName);
        string path = AssetDatabase.GetAssetPath(current);
        if (string.IsNullOrEmpty(clean) || clean == Path.GetFileNameWithoutExtension(path)) return;
        string error = AssetDatabase.RenameAsset(path, clean);
        if (!string.IsNullOrEmpty(error)) EditorUtility.DisplayDialog("Rename failed", error, "OK");
        else { fileName = clean; RefreshAssets(); }
    }

    private void DeleteCurrent()
    {
        string path = AssetDatabase.GetAssetPath(current);
        if (!EditorUtility.DisplayDialog("Delete?", $"Move {path} to the trash?{DeleteWarning(current)}", "Delete", "Cancel")) return;
        OnDeleting(current);
        AssetDatabase.SaveAssets();
        AssetDatabase.MoveAssetToTrash(path);
        current = null;
        RefreshAssets();
        NewDraft(BaseType);
    }

    protected virtual string DeleteWarning(ScriptableObject a) => "";

    // ---------- script generator ----------
    private void DrawScriptGenerator()
    {
        EditorGUILayout.LabelField(" ", ScriptHelp, w.miniGray);
        using (new EditorGUILayout.HorizontalScope())
        {
            newScriptName = EditorGUILayout.TextField("Class name", newScriptName);
            bool valid = ContentCreatorWindow.IsValidClassName(newScriptName);
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("Write script", GUILayout.Width(100)))
                    WriteScript(newScriptName);
        }
        if (!string.IsNullOrEmpty(newScriptName) && !ContentCreatorWindow.IsValidClassName(newScriptName))
            EditorGUILayout.HelpBox("Use letters, digits and _ only, not starting with a digit.", MessageType.Warning);
        EditorGUILayout.LabelField(" ", $"Saved to {ScriptFolder}/. After Unity recompiles it appears under New > Custom script.", w.miniGray);
    }

    private void WriteScript(string className)
    {
        if (ContentCreatorWindow.FindType(className) != null)
        {
            EditorUtility.DisplayDialog("Name taken", $"A class called {className} already exists.", "OK");
            return;
        }
        ContentCreatorWindow.EnsureFolder(ScriptFolder);
        string assetPath = $"{ScriptFolder}/{className}.cs";
        string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", assetPath);
        if (File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("File exists", $"{assetPath} already exists.", "OK");
            return;
        }
        File.WriteAllText(fullPath, ScriptTemplate(className).Replace("\r\n", "\n"));
        AssetDatabase.ImportAsset(assetPath);
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
        if (script != null) EditorGUIUtility.PingObject(script);
        Debug.Log($"[Content] Wrote {assetPath}. Unity will recompile.");
        newScriptName = "";
    }

    // ---------- small GUI helpers for subclasses ----------
    protected void Prop(string name, string label = null)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        if (label != null) EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip), true);
        else EditorGUILayout.PropertyField(p, true);
    }

    protected void Row(string label, IEnumerable<string> cells, float labelW = 150, float colW = 80)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, w.richLabel, GUILayout.Width(labelW));
            foreach (var c in cells) EditorGUILayout.LabelField(c, w.richLabel, GUILayout.Width(colW));
        }
    }

    protected void PoolToggle<T>(string label, List<T> pool, string poolFieldName, ref bool addOnCreate) where T : ScriptableObject
    {
        if (w.run == null)
        {
            EditorGUILayout.LabelField(" ", $"Open the scene with the RunManager to add it to the {label}.", w.miniGray);
            return;
        }
        if (isDraft)
        {
            addOnCreate = EditorGUILayout.Toggle($"Add to {label} on Create", addOnCreate);
            return;
        }
        var asset = (T)current;
        bool inPool = pool.Contains(asset);
        bool newIn = EditorGUILayout.Toggle($"In RunManager {label}", inPool);
        if (newIn != inPool) SetPool(pool, asset, newIn);
    }

    protected void SetPool<T>(List<T> pool, T asset, bool on) where T : ScriptableObject
    {
        if (w.run == null || asset == null) return;
        Undo.RecordObject(w.run, on ? "Add to pool" : "Remove from pool");
        if (on && !pool.Contains(asset)) pool.Add(asset);
        if (!on) pool.RemoveAll(x => x == asset);
        EditorUtility.SetDirty(w.run);
        if (!EditorUtility.IsPersistent(w.run)) EditorSceneManager.MarkSceneDirty(w.run.gameObject.scene);
    }

    protected static string Signed(float v) => (v > 0 ? "+" : "") + v.ToString("0.#");
}

// =========================================================================
// Omens
// =========================================================================

internal class OmenTab : ContentTab
{
    private bool addToPool = true;
    private static readonly HashSet<string> Known = new() { "displayName", "icon", "description", "flavour", "family", "scope", "inDrawPool", "rules", "teamEffects" };

    public OmenTab(ContentCreatorWindow w) : base(w) { }
    public override string Title => "Omens";
    protected override Type BaseType => typeof(Omen);
    protected override string DefaultFolder => "Assets/ScriptableObjects/Omens";
    protected override string NameProperty => "displayName";
    protected override string ScriptFolder => "Assets/Scripts/Omens";
    protected override string[] Groups => Enum.GetNames(typeof(EnemyCategory));
    protected override string NameOf(ScriptableObject a) => ((Omen)a).displayName;
    protected override string GroupOf(ScriptableObject a) => ((Omen)a).family.ToString();
    protected override string ListSuffix(ScriptableObject a)
    {
        var o = (Omen)a;
        return $"  <color=#888888>{(o.scope == OmenScope.Shared ? "shared" : "")}{(o.inDrawPool ? "" : " (not drawn)")}</color>";
    }
    protected override string SuggestFileName(ScriptableObject a) => $"{((Omen)a).family}_{ContentCreatorWindow.ToPascal(NameOf(a))}";

    protected override void InitDraft(ScriptableObject a) => ((Omen)a).displayName = "New Omen";

    protected override void FillNewMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("Blank"), false, () => NewDraft(BaseType));
        menu.AddSeparator("");
        foreach (EnemyCategory fam in Enum.GetValues(typeof(EnemyCategory)))
        {
            var f = fam;
            menu.AddItem(new GUIContent($"{f}/Personal"), false, () => NewDraft(BaseType, a => { var o = (Omen)a; o.family = f; o.scope = OmenScope.Personal; o.displayName = $"New {f} Omen"; }));
            menu.AddItem(new GUIContent($"{f}/Shared"), false, () => NewDraft(BaseType, a => { var o = (Omen)a; o.family = f; o.scope = OmenScope.Shared; o.displayName = $"New Shared {f} Omen"; }));
        }
    }

    protected override void DrawBody()
    {
        var o = (Omen)current;
        Section("Identity", true, () =>
        {
            Prop("displayName");
            Prop("icon");
            Prop("description");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("Write description from rules"))
                {
                    so.ApplyModifiedProperties();
                    so.FindProperty("description").stringValue = Describe(o);
                    GUI.FocusControl(null);
                }
            }
            Prop("flavour");
        });

        Section("Kind", true, () =>
        {
            Prop("family");
            EditorGUILayout.LabelField(" ", "Assault: body and defence. Sabotage: ritual work. Control: movement and focus.", w.miniGray);
            Prop("scope");
            Prop("inDrawPool", "In draw pool");
            PoolToggle("Omen Pool", w.run != null ? w.run.omenPool : null, "omenPool", ref addToPool);
        });

        bool shared = o.scope == OmenScope.Shared;
        Section(shared ? "Rules on the bearer (optional for Shared)" : "Rules on the bearer", true, () =>
        {
            ContentCreatorWindow.DrawChildren(so.FindProperty("rules"));
        });
        Section(shared ? "Team effects (whole team while she is deployed)" : "Team effects (ignored: only Shared Omens use these)", shared, () =>
        {
            ContentCreatorWindow.DrawChildren(so.FindProperty("teamEffects"));
        });

        if (current.GetType() != typeof(Omen))
            Section($"{current.GetType().Name} settings", true, () =>
            {
                if (!ContentCreatorWindow.DrawOtherFields(so, Known)) EditorGUILayout.LabelField("No extra fields.", w.miniGray);
            });
    }

    protected override void OnCreated(ScriptableObject a)
    {
        if (addToPool && w.run != null) SetPool(w.run.omenPool, (Omen)a, true);
    }

    protected override void OnDeleting(ScriptableObject a)
    {
        if (w.run != null) SetPool(w.run.omenPool, (Omen)a, false);
    }

    protected override void DrawPreview()
    {
        var o = (Omen)current;
        GUILayout.Label($"<b><size=14>{o.displayName}</size></b>\n{o.TooltipText()}", w.richBox);
        if (!string.IsNullOrWhiteSpace(o.description))
        {
            EditorGUILayout.LabelField("Generated from the numbers (compare with your text):", w.miniGray);
            GUILayout.Label(Describe(o), w.miniGray);
        }
    }

    /// <summary>Plain-language rules text from the Rules and Team Effects.</summary>
    internal static string Describe(Omen o)
    {
        var r = o.rules;
        var parts = new List<string>();
        string Less(float v, string what) => v < 0 ? $"{-v:0.#} shorter" : $"{v:0.#} longer";
        if (r.maxResolve != 0) parts.Add(r.maxResolve < 0 ? $"She starts every mission with {-r.maxResolve} less Resolve." : $"She has {r.maxResolve} more Resolve.");
        if (r.moveRange != 0) parts.Add($"Her Move is {Less(r.moveRange, "")}.");
        if (r.arcanaRange != 0) parts.Add($"Her Arcana range is {Less(r.arcanaRange, "")}.");
        if (r.wardingRange != 0) parts.Add($"Her Warding range is {Less(r.wardingRange, "")}.");
        if (r.loreRange != 0) parts.Add($"Her Lore range is {Less(r.loreRange, "")}.");
        if (r.successThreshold != 0)
        {
            string where = r.randomThresholdDiscipline ? "In one discipline (chosen when she receives it), her" : $"Her {r.thresholdDiscipline}";
            parts.Add($"{where} Successes need {6 + r.successThreshold}+ instead of 6+. Shown in every roll.");
        }
        if (r.channelCritBonus != 0) parts.Add($"Her Channel Critical gives +{Mathf.Max(0, 2 + r.channelCritBonus)} Progress instead of +2.");
        if (r.cannotStabilize) parts.Add("She cannot Stabilize. Her Guard on the Anchor only gives Shields.");
        if (r.progressLostWhenDowned > 0) parts.Add($"When she is Downed, the ritual loses {r.progressLostWhenDowned} Progress.");
        if (o.scope == OmenScope.Shared && !ContentCreatorWindow.IsEmpty(o.teamEffects))
        {
            string team = o.teamEffects.Describe();
            if (o.teamEffects.turnCapacity < 0) team += " (never below 10)";
            parts.Add($"Shared: any team she deploys with gets {team}.");
        }
        return parts.Count > 0 ? string.Join(" ", parts) : "(No rules set.)";
    }

    protected override void Validate(ScriptableObject a, List<(MessageType type, string text)> list)
    {
        var o = (Omen)a;
        var r = o.rules;
        bool custom = a.GetType() != typeof(Omen);
        bool rulesEmpty = r.maxResolve == 0 && r.moveRange == 0 && r.arcanaRange == 0 && r.wardingRange == 0 && r.loreRange == 0
                          && r.successThreshold == 0 && r.channelCritBonus == 0 && !r.cannotStabilize && r.progressLostWhenDowned == 0;
        bool teamEmpty = ContentCreatorWindow.IsEmpty(o.teamEffects);

        if (o.scope == OmenScope.Personal)
        {
            if (rulesEmpty && !custom) list.Add((MessageType.Error, "Personal Omen with no rules: it does nothing."));
            if (!teamEmpty) list.Add((MessageType.Error, "Team Effects are set, but RunManager only adds them for Shared Omens. Set Scope to Shared or clear them."));
        }
        else
        {
            if (teamEmpty && !custom) list.Add((MessageType.Error, "Shared Omen with no Team Effects: it does nothing for the team."));
            if (!rulesEmpty) list.Add((MessageType.Info, "This Shared Omen also changes the bearer herself through Rules."));
        }
        if (custom && rulesEmpty && teamEmpty)
            list.Add((MessageType.Info, $"No numbers set: make sure {a.GetType().Name}'s hooks do the work."));

        // Omens are debuffs
        var helps = new List<string>();
        if (r.maxResolve > 0) helps.Add("Resolve");
        if (r.moveRange > 0) helps.Add("Move");
        if (r.arcanaRange > 0 || r.wardingRange > 0 || r.loreRange > 0) helps.Add("range");
        if (r.successThreshold < 0) helps.Add("threshold");
        if (r.channelCritBonus > 0) helps.Add("Channel Critical");
        if (o.scope == OmenScope.Shared)
            helps.AddRange(ContentCreatorWindow.EffectFields(o.teamEffects).Where(f => f.value != 0 && Math.Sign(f.value) == f.goodSign).Select(f => "team " + f.label));
        if (helps.Count > 0) list.Add((MessageType.Warning, $"These numbers help the player: {string.Join(", ", helps)}. An Omen is a lasting debuff."));

        // GDD: change how she is used, never make her unusable
        if (r.maxResolve <= -2) list.Add((MessageType.Warning, "Max Resolve -2 leaves her at 1 Resolve: one hit Downs her. GDD: an Omen changes how you use her, it never makes her unusable."));
        if (r.moveRange <= -2) list.Add((MessageType.Warning, "Move -2 leaves her at Move 1. Too crippling for a heroine who must reach the fight in 2 Moves."));
        if (r.arcanaRange <= -2 || r.wardingRange <= -2 || r.loreRange <= -2) list.Add((MessageType.Warning, "A range of -2 leaves range 1 in that discipline."));
        if (r.successThreshold >= 2) list.Add((MessageType.Warning, $"Successes on {6 + r.successThreshold}+ is harsher than any GDD Omen (Trembling Hand is 7+)."));
        if (r.channelCritBonus <= -2) list.Add((MessageType.Warning, "Her Channel Critical would give no bonus Progress at all."));
        if (r.successThreshold != 0 && !r.randomThresholdDiscipline)
            list.Add((MessageType.Info, $"The threshold always hits {r.thresholdDiscipline}. Random is the GDD default, so it doesn't always land on her weak pool."));

        if (o.scope == OmenScope.Shared)
        {
            if (o.teamEffects.successThreshold > 0)
                list.Add((MessageType.Warning, "A team-wide threshold change hits every girl, and the draw only checks Rules for the one-threshold-per-heroine rule. Consider a Night Condition instead (Dissonance)."));
            if (o.teamEffects.turnCapacity < -1)
                list.Add((MessageType.Warning, "GDD floor: Omens, Night Conditions and difficulty together never take starting Turn Capacity below 10. Two Shared Omens like this already reach it."));
            if (o.teamEffects.extraSpawnsPerRound > 0)
                list.Add((MessageType.Warning, "Extra spawns: Player Trust Rule 5 caps the board at 5 regular enemies. Check the spawners respect the cap."));
        }

        if (o.scope == OmenScope.Personal && string.IsNullOrWhiteSpace(o.description))
            list.Add((MessageType.Error, "No description. Personal rules have no automatic text, so the tooltip would be empty. GDD: Omens are always visible with exact text. Use 'Write description from rules'."));
        if (o.icon == null) list.Add((MessageType.Info, "No icon. GDD: each Omen family has a small readable mark."));

        if (w.run != null && !IsDraft && o.inDrawPool && !w.run.omenPool.Contains(o))
            list.Add((MessageType.Warning, "In Draw Pool is on, but it isn't in the RunManager's Omen Pool, so it can never be drawn."));
        if (w.run != null && !o.inDrawPool && w.run.omenPool.Contains(o))
            list.Add((MessageType.Info, "In the Omen Pool, but In Draw Pool is off: only given by story or test code."));
    }

    protected override void DrawStats()
    {
        var omens = All<Omen>().Where(o => o.inDrawPool).ToList();
        Row("", new[] { "Personal", "Shared", "Threshold" }.Concat(new[] { "Total" }).Select(s => $"<b>{s}</b>"));
        foreach (EnemyCategory f in Enum.GetValues(typeof(EnemyCategory)))
        {
            var fam = omens.Where(o => o.family == f).ToList();
            Row(f.ToString(), new[]
            {
                fam.Count(o => o.scope == OmenScope.Personal).ToString(),
                fam.Count(o => o.scope == OmenScope.Shared).ToString(),
                fam.Count(o => o.rules.ChangesThreshold).ToString(),
                fam.Count.ToString()
            });
        }
        int total = omens.Count, shared = omens.Count(o => o.scope == OmenScope.Shared);
        Row("<b>Total</b>", new[] { (total - shared).ToString(), shared.ToString(), omens.Count(o => o.rules.ChangesThreshold).ToString(), total.ToString() });
        string col(bool ok) => ok ? "#6cbf6c" : "#d9a441";
        EditorGUILayout.LabelField(" ",
            $"<color={col(total >= 12 && total <= 16)}>GDD 1.0 target: 12-16 Omens (now {total}).</color>  " +
            $"<color={col(total == 0 || Mathf.Abs(shared / (float)total - 1f / 3f) < 0.12f)}>About a third Shared (now {(total > 0 ? shared * 100 / total : 0)}%).</color>  " +
            "Each family needs at least 2 non-threshold Omens so a girl who already has a threshold Omen can still draw one.",
            w.miniGray);
    }

    protected override string ScriptHelp => "For a rule the numbers can't express (e.g. Brittle Shield, Hunted). Writes an Omen subclass with OnEncounterStart, OnRoundStart and OnEncounterEnd hooks.";

    protected override string ScriptTemplate(string className) => @"using UnityEngine;

/// <summary>
/// #NAME#: an Omen with a special rule the Omen fields can't express.
/// Its Rules and Team Effects still work as normal; put the extra rule in the hooks below.
/// GDD: an Omen may change what is telegraphed but must never cause an untelegraphed effect,
/// and its rules text must be exact. Announce anything it does.
/// Create one in Tools > Moonlit Ritual > Omen, Relic and Objective Creator (New > Custom script).
/// </summary>
[CreateAssetMenu(fileName = ""#CLASS#"", menuName = ""Scriptable Objects/Omens/#NAME#"")]
public class #CLASS# : Omen
{
    [Header(""Special rule"")]
    [Tooltip(""Example setting for the rule."")]
    public int amount = 1;

    /// <summary>After the ritual and party are set up. 'bearer' is the heroine carrying this Omen.</summary>
    public override void OnEncounterStart(RitualManager ritualManager, Heroine bearer)
    {
        // Example: ritualManager.Announce($""{displayName}: {bearer.DisplayName} ..."");
    }

    /// <summary>At the start of every round, after spawning, before the player phase.</summary>
    public override void OnRoundStart(RitualManager ritualManager, Heroine bearer, int round)
    {
    }

    /// <summary>When the encounter is won or lost.</summary>
    public override void OnEncounterEnd(RitualManager ritualManager, Heroine bearer, bool won)
    {
    }
}
".Replace("#CLASS#", className).Replace("#NAME#", ObjectNames.NicifyVariableName(className));
}

// =========================================================================
// Relics
// =========================================================================

internal class RelicTab : ContentTab
{
    private bool addToPool = true;
    private static readonly HashSet<string> Known = new() { "displayName", "icon", "description", "flavour", "rarity", "inRewardPool", "effects" };

    public RelicTab(ContentCreatorWindow w) : base(w) { }
    public override string Title => "Relics";
    protected override Type BaseType => typeof(Relic);
    protected override string DefaultFolder => "Assets/ScriptableObjects/Relics";
    protected override string NameProperty => "displayName";
    protected override string ScriptFolder => "Assets/Scripts/Relics";
    protected override string[] Groups => Enum.GetNames(typeof(RelicRarity));
    protected override string NameOf(ScriptableObject a) => ((Relic)a).displayName;
    protected override string GroupOf(ScriptableObject a) => ((Relic)a).rarity.ToString();
    protected override string ListSuffix(ScriptableObject a) => ((Relic)a).inRewardPool ? "" : "  <color=#888888>(not offered)</color>";
    protected override string SuggestFileName(ScriptableObject a) => $"Relic_{ContentCreatorWindow.ToPascal(NameOf(a))}";
    protected override void InitDraft(ScriptableObject a) => ((Relic)a).displayName = "New Relic";

    protected override void FillNewMenu(GenericMenu menu)
    {
        menu.AddItem(new GUIContent("Blank"), false, () => NewDraft(BaseType));
        menu.AddSeparator("");
        foreach (RelicRarity rar in Enum.GetValues(typeof(RelicRarity)))
        {
            var r = rar;
            menu.AddItem(new GUIContent(r.ToString()), false, () => NewDraft(BaseType, a => { var x = (Relic)a; x.rarity = r; x.displayName = $"New {r} Relic"; }));
        }
    }

    protected override void DrawBody()
    {
        Section("Identity", true, () =>
        {
            Prop("displayName");
            Prop("icon");
            Prop("description");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("Write description from effects"))
                {
                    so.ApplyModifiedProperties();
                    so.FindProperty("description").stringValue = Describe((Relic)current);
                    GUI.FocusControl(null);
                }
                if (GUILayout.Button("Clear (auto text)", GUILayout.Width(120)))
                {
                    so.FindProperty("description").stringValue = "";
                    GUI.FocusControl(null);
                }
            }
            Prop("flavour");
            Prop("rarity");
            Prop("inRewardPool", "In reward pool");
            PoolToggle("Relic Pool", w.run != null ? w.run.relicPool : null, "relicPool", ref addToPool);
        });
        Section("Effects (team-wide, every encounter while the team has it)", true, () =>
            ContentCreatorWindow.DrawChildren(so.FindProperty("effects")));

        if (current.GetType() != typeof(Relic))
            Section($"{current.GetType().Name} settings", true, () =>
            {
                if (!ContentCreatorWindow.DrawOtherFields(so, Known)) EditorGUILayout.LabelField("No extra fields.", w.miniGray);
            });
    }

    private static string Describe(Relic r)
    {
        string t = r.effects.Describe();
        return string.IsNullOrEmpty(t) ? "" : char.ToUpperInvariant(t[0]) + t.Substring(1) + ".";
    }

    protected override void OnCreated(ScriptableObject a)
    {
        if (addToPool && w.run != null) SetPool(w.run.relicPool, (Relic)a, true);
    }

    protected override void OnDeleting(ScriptableObject a)
    {
        if (w.run != null) SetPool(w.run.relicPool, (Relic)a, false);
    }

    protected override void DrawPreview()
    {
        var r = (Relic)current;
        GUILayout.Label($"<b><size=14>{r.displayName}</size></b>  <color=#999999>{r.rarity} Relic</color>\n{r.TooltipText()}", w.richBox);
    }

    protected override void Validate(ScriptableObject a, List<(MessageType type, string text)> list)
    {
        var r = (Relic)a;
        bool custom = a.GetType() != typeof(Relic);
        var fields = ContentCreatorWindow.EffectFields(r.effects).Where(f => f.value != 0).ToList();

        if (fields.Count == 0)
            list.Add(custom ? (MessageType.Info, $"No numbers set: make sure {a.GetType().Name}'s hooks do the work.")
                            : (MessageType.Error, "No effects: the Relic does nothing."));

        var drawbacks = fields.Where(f => Math.Sign(f.value) != f.goodSign).Select(f => $"{f.label} {Signed(f.value)}").ToList();
        if (drawbacks.Count > 0)
        {
            bool allBad = drawbacks.Count == fields.Count;
            list.Add((allBad ? MessageType.Error : MessageType.Info,
                allBad ? $"Every number hurts the player ({string.Join(", ", drawbacks)}). A Relic is a buff."
                       : $"Has a drawback: {string.Join(", ", drawbacks)}. Fine if it's a trade-off; say so in the description."));
        }

        if (r.effects.successThreshold < 0)
            list.Add((MessageType.Warning, "Lowers the Success threshold for the whole team. GDD: rewards never change dice; the threshold is for Night Conditions, Omens, abilities and Charms."));
        if (r.effects.maxResolve > 0)
            list.Add((MessageType.Warning, "More Resolve for every heroine weakens Omens and the fatigue economy (Resolve carries over between missions)."));
        if (r.effects.maxStability > 1 || r.effects.startingStability > 1 || r.effects.anchorPassiveProgress > 1)
            list.Add((MessageType.Warning, "A big Stability/Progress boost can push completion under the Round 6 floor the GDD balances around. Run it through the Stability simulation."));
        if (r.effects.emptyRollRerolls > 0 || r.effects.moveRange > 0 || r.effects.maxResolve > 0)
            if (r.rarity == RelicRarity.Common)
                list.Add((MessageType.Info, "Move, Resolve and rerolls are strong team-wide effects for a Common Relic."));
        if (r.effects.capacityRestoresPerRound > 0 && r.effects.turnCapacity > 0)
            list.Add((MessageType.Info, "Both more Turn Capacity and more restores: it stacks two sources of time."));

        if (r.icon == null) list.Add((MessageType.Info, "No icon."));
        if (w.run != null && !IsDraft && r.inRewardPool && !w.run.relicPool.Contains(r))
            list.Add((MessageType.Warning, "In Reward Pool is on, but it isn't in the RunManager's Relic Pool, so it is never offered."));
    }

    protected override void DrawStats()
    {
        var relics = All<Relic>().ToList();
        Row("", new[] { "<b>Relics</b>", "<b>In pool</b>" });
        foreach (RelicRarity rar in Enum.GetValues(typeof(RelicRarity)))
        {
            var g = relics.Where(x => x.rarity == rar).ToList();
            Row(rar.ToString(), new[] { g.Count.ToString(), g.Count(x => x.inRewardPool).ToString() });
        }
        // Which numbers the relics touch, to spot gaps and pile-ups
        var counts = new Dictionary<string, int>();
        foreach (var r in relics)
            foreach (var f in ContentCreatorWindow.EffectFields(r.effects).Where(f => f.value != 0 && Math.Sign(f.value) == f.goodSign))
                counts[f.label] = counts.TryGetValue(f.label, out int c) ? c + 1 : 1;
        if (counts.Count > 0)
            EditorGUILayout.LabelField(" ", "Numbers the Relics improve: " + string.Join(", ", counts.OrderByDescending(p => p.Value).Select(p => $"{p.Key} ×{p.Value}")), w.miniGray);
        var unused = ContentCreatorWindow.EffectFields(new RunEffects()).Select(f => f.label)
            .Where(l => !counts.ContainsKey(l) && l != "Success threshold" && !l.StartsWith("Enemy") && !l.StartsWith("Extra") && !l.StartsWith("Reinf")).ToList();
        if (unused.Count > 0)
            EditorGUILayout.LabelField(" ", "No Relic improves yet: " + string.Join(", ", unused), w.miniGray);
    }

    protected override string ScriptHelp => "For a rule numbers can't express (e.g. 'the first Anchor hit each encounter is blocked'). Writes a Relic subclass with encounter and round hooks.";

    protected override string ScriptTemplate(string className) => @"using UnityEngine;

/// <summary>
/// #NAME#: a Relic with a special rule the Effects numbers can't express.
/// Its Effects still apply as normal; put the extra rule in the hooks below.
/// GDD: every number shown is the real number, so describe the rule exactly and announce what it does.
/// Create one in Tools > Moonlit Ritual > Omen, Relic and Objective Creator (New > Custom script).
/// </summary>
[CreateAssetMenu(fileName = ""#CLASS#"", menuName = ""Scriptable Objects/Relics/#NAME#"")]
public class #CLASS# : Relic
{
    [Header(""Special rule"")]
    [Tooltip(""Example setting for the rule."")]
    public int amount = 1;

    /// <summary>After the ritual and party are set up, before round 0.</summary>
    public override void OnEncounterStart(RitualManager ritualManager)
    {
        // Example: ritualManager.Announce($""{displayName}: ..."");
    }

    /// <summary>At the start of every round, after spawning, before the player phase.</summary>
    public override void OnRoundStart(RitualManager ritualManager, int round)
    {
    }

    /// <summary>When the encounter is won or lost.</summary>
    public override void OnEncounterEnd(RitualManager ritualManager, bool won)
    {
    }
}
".Replace("#CLASS#", className).Replace("#NAME#", ObjectNames.NicifyVariableName(className));
}

// =========================================================================
// Objectives
// =========================================================================

internal class ObjectiveTab : ContentTab
{
    private int typeIndex;
    private static readonly HashSet<string> Known = new() { "title", "description", "icon", "disciplines", "moonlightReward", "bonus" };

    public ObjectiveTab(ContentCreatorWindow w) : base(w) { }
    public override string Title => "Objectives";
    protected override Type BaseType => typeof(ObjectiveDefinition);
    protected override string DefaultFolder => "Assets/ScriptableObjects/Objectives";
    protected override string NameProperty => "title";
    protected override string ScriptFolder => "Assets/Scripts/Objectives";
    protected override string[] Groups => new[] { "Used in open scene", "Not used in open scene" };
    protected override string NameOf(ScriptableObject a) => ((ObjectiveDefinition)a).title;
    protected override string GroupOf(ScriptableObject a) => Users(a).Any() ? Groups[0] : Groups[1];
    protected override string ListSuffix(ScriptableObject a)
    {
        var d = (ObjectiveDefinition)a;
        return d.disciplines.Count > 0 ? $"  <color=#888888>{string.Join("/", d.disciplines.Select(x => x.ToString().Substring(0, 3)))}</color>" : "";
    }
    protected override string SuggestFileName(ScriptableObject a) => $"Objective_{ContentCreatorWindow.ToPascal(NameOf(a))}";
    protected override void InitDraft(ScriptableObject a) => ((ObjectiveDefinition)a).title = "New Objective";

    private IEnumerable<ObjectiveBase> Users(ScriptableObject a) => w.sceneObjectives.Where(o => o != null && o.Definition == a);

    private static List<Type> ObjectiveTypes() =>
        TypeCache.GetTypesDerivedFrom<ObjectiveBase>().Where(t => !t.IsAbstract).OrderBy(t => t.Name).ToList();

    protected override void DrawBody()
    {
        Section("Texts", true, () =>
        {
            Prop("title");
            Prop("description");
            Prop("icon");
            Prop("disciplines");
        });
        Section("Rewards", true, () =>
        {
            Prop("moonlightReward", "Moonlight");
            EditorGUILayout.LabelField(" ", "Immediate bonus when completed (all optional):", w.miniGray);
            ContentCreatorWindow.DrawChildren(so.FindProperty("bonus"));
        });
        if (current.GetType() != typeof(ObjectiveDefinition))
            Section($"{current.GetType().Name} settings", true, () => ContentCreatorWindow.DrawOtherFields(so, Known));
    }

    protected override void DrawSceneLinks()
    {
        Section("Behaviour in the open scene", true, () =>
        {
            EditorGUILayout.LabelField(" ", "The definition holds texts and rewards. What happens on the map comes from an objective component " +
                                            "(Rescue Civilians, Defend the Position...) under the ObjectiveManager, with this definition in its Definition field.", w.miniGray);

            if (IsDraft)
            {
                EditorGUILayout.HelpBox("Create the asset first, then add it to the scene.", MessageType.None);
                return;
            }

            var users = Users(current).ToList();
            foreach (var u in users)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{u.gameObject.name}", $"{u.GetType().Name}{(u.isActiveAndEnabled ? "" : "  (disabled)")}");
                    if (GUILayout.Button("Select", GUILayout.Width(60))) { Selection.activeObject = u.gameObject; EditorGUIUtility.PingObject(u.gameObject); }
                }
            }
            if (users.Count == 0) EditorGUILayout.LabelField(" ", "No component in the open scene uses it yet.", w.miniGray);

            if (w.objectiveManager == null)
            {
                EditorGUILayout.HelpBox("No ObjectiveManager in the open scene. Open a combat scene to add it.", MessageType.Info);
                return;
            }
            var types = ObjectiveTypes();
            if (types.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                typeIndex = Mathf.Clamp(typeIndex, 0, types.Count - 1);
                typeIndex = EditorGUILayout.Popup("Objective type", typeIndex, types.Select(t => ObjectNames.NicifyVariableName(t.Name)).ToArray());
                if (GUILayout.Button("Add to ObjectiveManager", GUILayout.Width(170)))
                {
                    AddToScene(types[typeIndex]);
                    GUIUtility.ExitGUI();
                }
            }
        });
    }

    private void AddToScene(Type type)
    {
        var def = (ObjectiveDefinition)current;
        var manager = w.objectiveManager;
        var go = new GameObject(def.title);
        go.transform.SetParent(manager.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Add Objective");
        var comp = (ObjectiveBase)Undo.AddComponent(go, type);

        var cso = new SerializedObject(comp);
        var defProp = cso.FindProperty("definition");
        if (defProp != null) defProp.objectReferenceValue = def;
        cso.ApplyModifiedProperties();

        // The manager only scans its children when its Candidates list is empty.
        var mso = new SerializedObject(manager);
        var cands = mso.FindProperty("candidates");
        if (cands != null && cands.arraySize > 0)
        {
            cands.arraySize++;
            cands.GetArrayElementAtIndex(cands.arraySize - 1).objectReferenceValue = comp;
            mso.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(go.scene);
        w.RefreshScene();
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"[Content] Added {type.Name} '{def.title}' under {manager.name}. Fill in its map settings (prefabs, distances) in the Inspector.");
    }

    protected override string DeleteWarning(ScriptableObject a)
    {
        int n = Users(a).Count();
        return n > 0 ? $"\n\n{n} component(s) in the open scene use it; they fall back to their own Title / Description / Moonlight / Bonus fields." : "";
    }

    protected override void DrawPreview()
    {
        var d = (ObjectiveDefinition)current;
        string reward = d.bonus?.Describe() ?? "";
        string disc = d.disciplines.Count > 0 ? string.Join(", ", d.disciplines.Distinct()) : "no discipline set";
        GUILayout.Label($"<b><size=14>{d.title}</size></b>  <color=#999999>{disc}</color>\n{d.description}\n" +
                        $"<color=#999999>Reward: {d.moonlightReward} Moonlight{(reward.Length > 0 ? ", " + reward : "")}</color>", w.richBox);
        EditorGUILayout.LabelField("Announced at the start of the encounter as:", w.miniGray);
        GUILayout.Label($"Optional objective: {d.title}. {d.description} Reward: {d.moonlightReward} Moonlight{(reward.Length > 0 ? ", " + reward : "")}.", w.miniGray);
    }

    protected override void Validate(ScriptableObject a, List<(MessageType type, string text)> list)
    {
        var d = (ObjectiveDefinition)a;
        var b = d.bonus ?? new ObjectiveBonus();

        if (string.IsNullOrWhiteSpace(d.description)) list.Add((MessageType.Warning, "No description: the player is told the title only."));
        if (d.disciplines.Count == 0) list.Add((MessageType.Warning, "No discipline set. GDD: each objective leans on a different discipline, shown to the player."));
        if (d.disciplines.Count != d.disciplines.Distinct().Count()) list.Add((MessageType.Info, "A discipline is listed twice."));
        if (d.disciplines.Count > 2) list.Add((MessageType.Info, "More than two disciplines: it no longer leans on anything in particular."));

        if (d.moonlightReward <= 0) list.Add((MessageType.Warning, "No Moonlight. GDD: completing an objective always gives Moonlight (the research currency)."));
        else if (d.moonlightReward > 3) list.Add((MessageType.Warning, $"{d.moonlightReward} Moonlight is a lot. GDD: about 2-3 per mission funds 10-14 research projects per campaign."));

        bool noBonus = b.restoreTurnCapacity == 0 && b.stability == 0 && b.healResolve == 0 && b.shieldsForParty == 0 && b.insightAllEnemyTypes == 0 && b.relics == 0;
        if (noBonus) list.Add((MessageType.Info, "No immediate bonus. Fine if the objective already helps in play (like Defend the Position's Anchor Shields)."));
        if (b.restoreTurnCapacity > 1) list.Add((MessageType.Warning, "Restores more than 1 Turn Capacity. Turn Capacity never goes above its starting value anyway."));
        if (b.stability > 2) list.Add((MessageType.Warning, "More than 2 Stability at once can jump the ritual to Harmonized."));
        if (b.relics > 1) list.Add((MessageType.Warning, "More than one Relic from one objective."));
        if (b.healResolve > 1) list.Add((MessageType.Info, "Healing more than 1 Resolve weakens fatigue and Omen pressure."));
        int kinds = new[] { b.restoreTurnCapacity, b.stability, b.healResolve, b.shieldsForParty, b.insightAllEnemyTypes, b.relics }.Count(x => x > 0);
        if (kinds > 2) list.Add((MessageType.Info, "Three or more kinds of bonus: keep rewards simple to read."));

        string text = (d.title + " " + d.description).ToLowerInvariant();
        if (Regex.IsMatch(text, @"\b(before round|within \d+ rounds?|by round|fastest|quickly|in time)\b"))
            list.Add((MessageType.Warning, "Sounds speed-based. GDD: speed objectives are not used, they reward the all-in ritual rush."));
        if (Regex.IsMatch(text, @"\b(must|required|or the mission fails|lose the mission)\b"))
            list.Add((MessageType.Info, "Check the wording: objectives are always optional and never fail the mission (Player Trust Rule 6)."));

        if (d.icon == null) list.Add((MessageType.Info, "No icon for the objective tracker."));
        if (!IsDraft && w.objectiveManager != null && !Users(a).Any())
            list.Add((MessageType.Info, "No objective component in the open scene uses it. Add one below."));
    }

    protected override void DrawStats()
    {
        var defs = All<ObjectiveDefinition>().ToList();
        var cells = Enum.GetValues(typeof(Discipline)).Cast<Discipline>()
            .Select(dis => $"{dis}: {defs.Count(x => x.disciplines.Contains(dis))}");
        EditorGUILayout.LabelField(" ", "Objectives per discipline: " + string.Join(", ", cells), w.miniGray);
        EditorGUILayout.LabelField(" ", $"Moonlight: average {(defs.Count > 0 ? defs.Average(x => x.moonlightReward) : 0):0.0} per objective (GDD: 1-2).", w.miniGray);
        if (w.objectiveManager != null)
        {
            var mso = new SerializedObject(w.objectiveManager);
            int max = mso.FindProperty("maxPerEncounter")?.intValue ?? 2;
            var cands = mso.FindProperty("candidates");
            int count = cands != null && cands.arraySize > 0
                ? cands.arraySize
                : w.objectiveManager.GetComponentsInChildren<ObjectiveBase>(true).Length;
            EditorGUILayout.LabelField(" ", $"Open scene: {count} objective component(s) under {w.objectiveManager.name}, up to {max} per encounter.", w.miniGray);
            var noDef = w.sceneObjectives.Where(o => o != null && o.Definition == null).Select(o => o.gameObject.name).ToList();
            if (noDef.Count > 0)
                EditorGUILayout.LabelField(" ", $"Components without a Definition (using their own fields): {string.Join(", ", noDef)}", w.miniGray);
        }
    }

    protected override string ScriptHelp => "Writes a new objective type (an ObjectiveBase subclass). The template is a working 'Hold the Ground' objective: " +
                                            "a marked tile away from the Anchor that a heroine must stand on for N enemy phases. Change it into your own rule.";

    protected override string ScriptTemplate(string className) => @"using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Objective - #NAME#.
/// Template: a marked tile appears away from the Anchor. Each enemy phase that ends with a standing heroine
/// on it counts once; after Rounds Needed it is completed. Never fails the mission (Player Trust Rule 6).
///
/// To make your own rule: place things in Setup(), react in the hooks, call Complete() or Fail(reason).
/// Add it with Tools > Moonlit Ritual > Omen, Relic and Objective Creator > Objectives > Add to ObjectiveManager.
/// </summary>
public class #CLASS# : ObjectiveBase
{
    [Header(""Marker"")]
    [Tooltip(""Optional prefab shown on the tile (a circle, a banner...)."")]
    [SerializeField] private GameObject markerPrefab;

    [Header(""Rules"")]
    [Tooltip(""Enemy phases a heroine must end on the tile."")]
    [SerializeField] private int roundsNeeded = 2;

    [Header(""Placement (walking distance from the Anchor)"")]
    [SerializeField] private int minDistance = 4;
    [SerializeField] private int maxDistance = 7;
    [SerializeField] private int minDistanceFromSpawners = 2;

    private Vector2Int tile;
    private bool placed;
    private int held;
    private GameObject marker;

    private void Reset()
    {
        title = ""#NAME#"";
        description = ""Hold the marked ground: end the enemy phase with a heroine standing on it."";
        moonlightReward = 2;
        bonus = new ObjectiveBonus();
    }

    public override string StatusLine =>
        State == ObjectiveState.Completed ? ""Ground held""
        : State == ObjectiveState.Failed ? ""Not held""
        : $""Hold the marked tile ({held}/{roundsNeeded})"";

    protected override void Setup()
    {
        held = 0;
        placed = false;
        var tiles = Manager.FindSpreadTiles(1, minDistance, maxDistance, 0, minDistanceFromSpawners);
        if (tiles.Count == 0) { Fail(""There was no place for it on this map.""); return; }

        tile = tiles[0];
        Manager.Reserve(tile);
        placed = true;
        if (markerPrefab != null) marker = Instantiate(markerPrefab, Grid.GridToWorld(tile), markerPrefab.transform.rotation);
    }

    public override void OnEnemyPhaseEnd()
    {
        if (!placed || !IsActive || Manager.Turns == null) return;
        var holder = Manager.Turns.InCombat.FirstOrDefault(h => h != null && !h.IsDowned && h.GridPosition == tile);
        if (holder == null) return;

        held++;
        Announce($""{holder.DisplayName} holds the ground ({held}/{roundsNeeded})."");
        if (held >= roundsNeeded) Complete();
    }

    public override void OnEncounterEnd(bool won)
    {
        if (IsActive) Fail(won ? ""The ritual completed before the ground was held."" : ""The ritual failed."");
    }

    protected override void Cleanup()
    {
        if (marker != null) Destroy(marker);
        marker = null;
        placed = false;
    }
}
".Replace("#CLASS#", className).Replace("#NAME#", ObjectNames.NicifyVariableName(className).Replace(" Objective", ""));
}
