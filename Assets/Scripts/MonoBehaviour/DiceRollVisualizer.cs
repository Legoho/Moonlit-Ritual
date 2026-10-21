using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shows a d8 pool roll (4d8 / 5d8 / 7d8 ...) with the real 3D dice from the Blender file.
///
/// How it works:
///  1. The game logic decides the numbers first (DiceRules.RollPool) so preview and result always agree.
///  2. The dice are thrown in a hidden, separate physics scene and the whole tumble is recorded in one frame.
///  3. Each die's model is turned by a symmetry of the D8 so that the decided number ends up on top
///     (the die still sits exactly as the physics left it, so nothing looks fake).
///  4. The recording is replayed, then Successes (6-7) and Criticals (8) are highlighted.
/// </summary>
public class DiceRollVisualizer : MonoBehaviour
{
    [Header("Dice models (D8_*.fbx exported with Face_N markers)")]
    [SerializeField] private GameObject arcanaDie;
    [SerializeField] private GameObject wardingDie;
    [SerializeField] private GameObject loreDie;
    [SerializeField] private GameObject occultismDie;
    [Tooltip("The FBX die is ~0.09 units wide. 3 makes it ~0.27.")]
    [SerializeField] private float dieScale = 3f;

    [Header("Tray (lives in its own physics scene)")]
    [SerializeField] private Vector3 trayCenter = new Vector3(0f, -50f, 0f);
    [SerializeField] private Vector2 traySize = new Vector2(2.6f, 1.7f);
    [SerializeField] private float trayHeight = 1.5f;
    [Tooltip("Optional visual for the tray (its colliders are disabled; the tray walls are invisible boxes).")]
    [SerializeField] private GameObject trayVisualPrefab;
    [SerializeField] private Color defaultTrayColor = new Color(0.12f, 0.08f, 0.16f);

    [Header("Throw")]
    [SerializeField] private float throwSpeed = 4.5f;
    [SerializeField] private float spinSpeed = 30f;
    [SerializeField] private float spawnHeight = 0.7f;
    [SerializeField] private float gravityScale = 2.5f;
    [SerializeField] private float bounciness = 0.35f;
    [SerializeField] private float friction = 0.45f;
    [SerializeField] private float simulationStep = 1f / 60f;
    [SerializeField] private int maxSimulationSteps = 600;
    [SerializeField] private float playbackSpeed = 1f;

    [Header("Result feedback")]
    [SerializeField] private float revealDelay = 0.12f;
    [Range(0f, 1f)] [SerializeField] private float failDim = 0.35f;
    [SerializeField] private Color successLightColor = new Color(1f, 0.85f, 0.45f);
    [SerializeField] private float successLightIntensity = 1.5f;
    [SerializeField] private Color criticalColor = new Color(0.75f, 0.45f, 1f);
    [SerializeField] private float criticalLightIntensity = 4f;
    [SerializeField] private float criticalEmission = 6f;
    [SerializeField] private float successPunch = 1.15f;
    [SerializeField] private float criticalPunch = 1.35f;
    [Tooltip("0 = dice stay until the next roll.")]
    [SerializeField] private float hideAfterSeconds = 0f;

    [Header("Camera (optional)")]
    [SerializeField] private Camera diceCamera;
    [SerializeField] private bool createCameraIfMissing = true;
    [Tooltip("If set, the dice camera renders here (show it with a UI RawImage). Otherwise it draws in the viewport rect.")]
    [SerializeField] private RenderTexture outputTexture;
    [SerializeField] private Rect viewportRect = new Rect(0.6f, 0.02f, 0.38f, 0.38f);
    [SerializeField] private Color cameraBackground = new Color(0.05f, 0.04f, 0.08f);
    [SerializeField] private float cameraTilt = 25f;

    [Header("Optional UI / audio")]
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] impactClips;
    [SerializeField] private AudioClip successClip;
    [SerializeField] private AudioClip criticalClip;

    public bool IsRolling { get; private set; }
    public event Action<DiceRollResult> RollFinished;

    // ---- internals ----
    private const float D8TipSharpness = 1.129f; // sharp-tip distance / chamfered-tip distance of your D8 mesh

    private class DieInstance
    {
        public GameObject root;
        public Rigidbody body;
        public Transform model;
        public DieFaces faces;
        public readonly List<RendererSlot> slots = new List<RendererSlot>();
        public Light light;
    }

    private struct RendererSlot
    {
        public Renderer renderer;
        public int materialIndex;
        public bool isGlow;
        public int colorId;
        public Color color;
        public bool hasEmission;
        public Color emission;
    }

    private struct Impact
    {
        public int step;
        public float strength;
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private Scene simScene;
    private PhysicsScene simPhysics;
    private PhysicsMaterial diceMaterial;
    private readonly List<DieInstance> activeDice = new List<DieInstance>();
    private readonly List<Vector3[]> framePositions = new List<Vector3[]>();
    private readonly List<Quaternion[]> frameRotations = new List<Quaternion[]>();
    private readonly List<Impact> impacts = new List<Impact>();
    private int currentStep;
    private bool skipRequested;
    private Coroutine rollRoutine;
    private DiceRollResult pendingResult;
    private Action<DiceRollResult> pendingCallback;
    private MaterialPropertyBlock block;

    // =====================================================================
    // Public API
    // =====================================================================

    /// <summary>Rolls a pool with DiceRules and shows it. Returns the result immediately (numbers are decided up front).</summary>
    public DiceRollResult Roll(Discipline discipline, int diceCount, Action<DiceRollResult> onComplete = null)
    {
        DiceRollResult result = DiceRules.RollPool(discipline, diceCount);
        Show(result, onComplete);
        return result;
    }

    /// <summary>Shows an already-decided result. onComplete fires after the dice land and are highlighted.</summary>
    public void Show(DiceRollResult result, Action<DiceRollResult> onComplete = null)
    {
        if (IsRolling) FinishInterruptedRoll();
        pendingResult = result;
        pendingCallback = onComplete;
        rollRoutine = StartCoroutine(RollRoutine(result));
    }

    /// <summary>Jump straight to the landed dice (e.g. on click).</summary>
    public void SkipToResult() => skipRequested = true;

    public void Clear()
    {
        foreach (DieInstance d in activeDice)
            if (d.root != null) Destroy(d.root);
        activeDice.Clear();
        if (summaryText != null) summaryText.text = "";
    }

    // =====================================================================
    // Setup
    // =====================================================================

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        if (audioSource == null && (impactClips?.Length > 0 || successClip || criticalClip))
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void EnsureTray()
    {
        if (simScene.IsValid()) return;

        simScene = SceneManager.CreateScene("DiceTray_" + GetInstanceID(),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        simPhysics = simScene.GetPhysicsScene();

        diceMaterial = new PhysicsMaterial("Dice")
        {
            bounciness = bounciness,
            dynamicFriction = friction,
            staticFriction = friction,
            bounceCombine = PhysicsMaterialCombine.Average,
            frictionCombine = PhysicsMaterialCombine.Average
        };

        var tray = new GameObject("DiceTray");
        tray.transform.position = trayCenter;
        float w = traySize.x, d = traySize.y, h = trayHeight, t = 0.5f;
        AddWall(tray, "Floor", new Vector3(0, -t * 0.5f, 0), new Vector3(w + 2 * t, t, d + 2 * t));
        AddWall(tray, "Ceiling", new Vector3(0, h + t * 0.5f, 0), new Vector3(w + 2 * t, t, d + 2 * t));
        AddWall(tray, "WallL", new Vector3(-(w + t) * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
        AddWall(tray, "WallR", new Vector3((w + t) * 0.5f, h * 0.5f, 0), new Vector3(t, h, d));
        AddWall(tray, "WallB", new Vector3(0, h * 0.5f, -(d + t) * 0.5f), new Vector3(w + 2 * t, h, t));
        AddWall(tray, "WallF", new Vector3(0, h * 0.5f, (d + t) * 0.5f), new Vector3(w + 2 * t, h, t));

        GameObject visual;
        if (trayVisualPrefab != null)
        {
            visual = Instantiate(trayVisualPrefab, trayCenter, Quaternion.identity);
            foreach (Collider c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(visual.GetComponent<Collider>());
            visual.name = "TrayFloorVisual";
            visual.transform.SetPositionAndRotation(trayCenter + Vector3.up * 0.001f, Quaternion.Euler(90f, 0f, 0f));
            visual.transform.localScale = new Vector3(w, d, 1f);
            visual.GetComponent<Renderer>().material.color = defaultTrayColor;
        }
        visual.transform.SetParent(tray.transform, true);

        var lightGo = new GameObject("TrayLight");
        lightGo.transform.SetParent(tray.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.6f, -0.6f);
        var trayLight = lightGo.AddComponent<Light>();
        trayLight.type = LightType.Point;
        trayLight.range = 6f;
        trayLight.intensity = 2.5f;

        SceneManager.MoveGameObjectToScene(tray, simScene);
        EnsureCamera();
    }

    private void AddWall(GameObject parent, string wallName, Vector3 localPos, Vector3 size)
    {
        var go = new GameObject(wallName);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        box.sharedMaterial = diceMaterial;
    }

    private void EnsureCamera()
    {
        if (diceCamera == null)
        {
            if (!createCameraIfMissing) return;
            var go = new GameObject("DiceCamera");
            go.transform.SetParent(transform, false);
            diceCamera = go.AddComponent<Camera>();
            diceCamera.clearFlags = CameraClearFlags.SolidColor;
            diceCamera.backgroundColor = cameraBackground;
            diceCamera.fieldOfView = 30f;
            diceCamera.nearClipPlane = 0.05f;
            diceCamera.farClipPlane = 30f;
            diceCamera.depth = 10f;
            if (outputTexture != null) diceCamera.targetTexture = outputTexture;
            else diceCamera.rect = viewportRect;
        }
        else
        {
            return; // a camera you placed yourself is left alone
        }

        float aspect = outputTexture != null
            ? (float)outputTexture.width / outputTexture.height
            : viewportRect.width * Screen.width / Mathf.Max(1f, viewportRect.height * Screen.height);
        float halfV = Mathf.Tan(diceCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float needed = Mathf.Max(traySize.y * 0.5f, traySize.x * 0.5f / Mathf.Max(0.1f, aspect));
        float dist = needed / halfV * 1.1f + 0.3f;
        Vector3 dir = Quaternion.Euler(-cameraTilt, 0f, 0f) * Vector3.up; // tilted toward -Z (viewer side)
        diceCamera.transform.position = trayCenter + dir * dist;
        diceCamera.transform.LookAt(trayCenter, Vector3.forward);
    }

    private GameObject ModelFor(Discipline discipline)
    {
        switch (discipline)
        {
            case Discipline.Arcana: return arcanaDie;
            case Discipline.Warding: return wardingDie;
            case Discipline.Lore: return loreDie;
            default: return occultismDie;
        }
    }

    private DieInstance CreateDie(GameObject modelPrefab, int index)
    {
        var die = new DieInstance();
        die.root = new GameObject("Die_" + index);
        SceneManager.MoveGameObjectToScene(die.root, simScene);

        GameObject model = Instantiate(modelPrefab, die.root.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * dieScale;
        die.model = model.transform;

        die.faces = model.AddComponent<DieFaces>();
        die.faces.Initialize();
        // Put the die's centre exactly on the physics body's origin.
        model.transform.localPosition = -Vector3.Scale(model.transform.localScale, die.faces.localCenter);

        foreach (Collider c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
        {
            var col = mf.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = BuildD8Hull(mf.sharedMesh.bounds);
            col.convex = true;
            col.sharedMaterial = diceMaterial;
        }

        die.body = die.root.AddComponent<Rigidbody>();
        die.body.mass = 1f;
        die.body.useGravity = false; // custom (stronger) gravity is applied per step
        die.body.linearDamping = 0.05f;
        die.body.angularDamping = 0.2f;
        die.body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        die.body.interpolation = RigidbodyInterpolation.None;

        var recorder = die.root.AddComponent<DieImpactRecorder>();
        recorder.onImpact = strength => impacts.Add(new Impact { step = currentStep, strength = strength });

        CacheMaterials(die);
        return die;
    }

    /// <summary>
    /// Convex collider for the chamfered D8: an octahedron with its tips cut where your mesh's tips are.
    /// Built from bounds only, so the FBX does not need Read/Write enabled.
    /// </summary>
    private static Mesh BuildD8Hull(Bounds b)
    {
        Vector3 c = b.center, e = b.extents;
        float sharpTip = (e.x + e.y + e.z) / 3f * D8TipSharpness;
        var verts = new List<Vector3>();
        for (int axis = 0; axis < 3; axis++)
        {
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            for (int s = -1; s <= 1; s += 2)
            {
                float tip = e[axis];
                float cut = Mathf.Max(0.0001f, sharpTip - tip);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 v = Vector3.zero;
                    v[axis] = s * tip;
                    if (k < 2) v[a1] = (k == 0 ? 1 : -1) * cut;
                    else v[a2] = (k == 2 ? 1 : -1) * cut;
                    verts.Add(c + v);
                }
            }
        }
        var tris = new List<int>();
        for (int i = 0; i + 2 < verts.Count; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i + 2); }
        var mesh = new Mesh { name = "D8Hull" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        return mesh;
    }

    private void CacheMaterials(DieInstance die)
    {
        foreach (Renderer r in die.model.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                var slot = new RendererSlot
                {
                    renderer = r,
                    materialIndex = i,
                    isGlow = m.name.ToLowerInvariant().Contains("glow"),
                    colorId = m.HasProperty(BaseColorId) ? BaseColorId : ColorId,
                    hasEmission = m.HasProperty(EmissionId)
                };
                slot.color = m.HasProperty(slot.colorId) ? m.GetColor(slot.colorId) : Color.white;
                slot.emission = slot.hasEmission ? m.GetColor(EmissionId) : Color.black;
                die.slots.Add(slot);
            }
        }
    }

    // =====================================================================
    // Roll
    // =====================================================================

    private IEnumerator RollRoutine(DiceRollResult result)
    {
        IsRolling = true;
        skipRequested = false;
        EnsureTray();
        Clear();
        if (summaryText != null) summaryText.text = "";

        GameObject prefab = ModelFor(result.discipline);
        if (prefab == null)
        {
            Debug.LogError($"[DiceRollVisualizer] No die model assigned for {result.discipline}.", this);
            CompleteRoll();
            yield break;
        }

        for (int i = 0; i < result.DiceCount; i++)
            activeDice.Add(CreateDie(prefab, i));

        // 1-2. Throw in the hidden physics scene until every die lands flat.
        int[] tops = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            ThrowDice();
            if (SimulateAndRecord(out tops)) break;
        }

        // 3. Turn each model so the decided number sits where the landed face is.
        for (int i = 0; i < activeDice.Count; i++)
        {
            DieInstance die = activeDice[i];
            if (!die.faces.TryGetSwapRotation(result.values[i], tops[i], out Quaternion swap))
            {
                Debug.LogWarning($"[DiceRollVisualizer] Could not orient die {i} to {result.values[i]}.", this);
                continue;
            }
            die.model.localRotation = swap;
            die.model.localPosition = -(swap * Vector3.Scale(die.model.localScale, die.faces.localCenter));
        }

        // 4. Replay.
        foreach (DieInstance die in activeDice) die.body.isKinematic = true;
        int lastFrame = framePositions.Count - 1;
        int nextImpact = 0;
        float time = 0f;
        while (true)
        {
            if (skipRequested) time = lastFrame * simulationStep;

            float f = time / simulationStep;
            int a = Mathf.Min((int)f, lastFrame);
            int b = Mathf.Min(a + 1, lastFrame);
            float t = f - a;
            for (int k = 0; k < activeDice.Count; k++)
            {
                activeDice[k].root.transform.SetPositionAndRotation(
                    Vector3.Lerp(framePositions[a][k], framePositions[b][k], t),
                    Quaternion.Slerp(frameRotations[a][k], frameRotations[b][k], t));
            }

            while (nextImpact < impacts.Count && impacts[nextImpact].step <= a)
            {
                if (!skipRequested) PlayImpact(impacts[nextImpact].strength);
                nextImpact++;
            }

            if (a >= lastFrame) break;
            yield return null;
            time += Time.deltaTime * playbackSpeed;
        }

        // 5. Highlight.
        yield return RevealRoutine(result);

        if (summaryText != null) summaryText.text = Summary(result);
        CompleteRoll();

        if (hideAfterSeconds > 0f)
        {
            yield return new WaitForSeconds(hideAfterSeconds);
            Clear();
        }
    }

    private void ThrowDice()
    {
        float size = DieSize();
        float spacing = size * 1.6f;
        int rows = Mathf.CeilToInt(activeDice.Count / 2f);
        float side = UnityEngine.Random.value < 0.5f ? -1f : 1f; // throw from left or right
        Vector3 start = trayCenter + new Vector3(side * (traySize.x * 0.5f - size * 1.5f), spawnHeight, 0f);

        for (int i = 0; i < activeDice.Count; i++)
        {
            int row = i / 2, col = i % 2;
            Vector3 pos = start + new Vector3(
                -side * col * spacing,
                (col * 0.5f) * size,
                (row - (rows - 1) * 0.5f) * spacing);
            pos += new Vector3(UnityEngine.Random.Range(-0.1f, 0.1f), 0f, UnityEngine.Random.Range(-0.1f, 0.1f)) * size;

            Rigidbody body = activeDice[i].body;
            body.isKinematic = false;
            Quaternion rot = UnityEngine.Random.rotation;
            body.transform.SetPositionAndRotation(pos, rot);
            body.position = pos;
            body.rotation = rot;
            body.linearVelocity = new Vector3(
                -side * throwSpeed * UnityEngine.Random.Range(0.8f, 1.2f),
                UnityEngine.Random.Range(-0.5f, 0.8f),
                UnityEngine.Random.Range(-0.3f, 0.3f) * throwSpeed);
            body.angularVelocity = UnityEngine.Random.onUnitSphere * spinSpeed * UnityEngine.Random.Range(0.6f, 1f);
        }
    }

    private bool SimulateAndRecord(out int[] tops)
    {
        framePositions.Clear();
        frameRotations.Clear();
        impacts.Clear();
        currentStep = 0;
        RecordFrame();

        Vector3 gravity = Physics.gravity * gravityScale;
        int quietSteps = 0;
        for (int step = 1; step <= maxSimulationSteps; step++)
        {
            currentStep = step;
            foreach (DieInstance d in activeDice) d.body.AddForce(gravity, ForceMode.Acceleration);
            simPhysics.Simulate(simulationStep);
            RecordFrame();

            bool quiet = true;
            foreach (DieInstance d in activeDice)
            {
                if (d.body.linearVelocity.sqrMagnitude > 0.0025f || d.body.angularVelocity.sqrMagnitude > 0.05f)
                {
                    quiet = false;
                    break;
                }
            }
            quietSteps = quiet ? quietSteps + 1 : 0;
            if (quietSteps >= 20) break;
        }

        tops = new int[activeDice.Count];
        for (int i = 0; i < activeDice.Count; i++)
        {
            tops[i] = activeDice[i].faces.TopFace(out float alignment);
            if (alignment < 0.9f) return false; // cocked die -> rethrow
        }
        return true;
    }

    private void RecordFrame()
    {
        var p = new Vector3[activeDice.Count];
        var r = new Quaternion[activeDice.Count];
        for (int i = 0; i < activeDice.Count; i++)
        {
            p[i] = activeDice[i].root.transform.position;
            r[i] = activeDice[i].root.transform.rotation;
        }
        framePositions.Add(p);
        frameRotations.Add(r);
    }

    private float DieSize()
    {
        if (activeDice.Count == 0) return 0.25f;
        Renderer r = activeDice[0].model.GetComponentInChildren<Renderer>();
        if (r == null) return 0.25f;
        Vector3 s = r.bounds.size;
        return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
    }

    // =====================================================================
    // Feedback
    // =====================================================================

    private IEnumerator RevealRoutine(DiceRollResult result)
    {
        float size = DieSize();
        for (int i = 0; i < activeDice.Count; i++)
        {
            DieInstance die = activeDice[i];
            int value = result.values[i];
            bool crit = DiceRules.IsCritical(value);
            bool success = DiceRules.SuccessesFor(value) > 0;

            if (crit)
            {
                ApplyLook(die, 1f, criticalColor * criticalEmission);
                AddLight(die, criticalColor, criticalLightIntensity, size * 3f);
                StartCoroutine(Punch(die.root.transform, criticalPunch));
                PlayClip(criticalClip);
            }
            else if (success)
            {
                ApplyLook(die, 1f, null);
                AddLight(die, successLightColor, successLightIntensity, size * 2f);
                StartCoroutine(Punch(die.root.transform, successPunch));
                PlayClip(successClip);
            }
            else
            {
                ApplyLook(die, failDim, null);
            }

            if (!skipRequested && revealDelay > 0f) yield return new WaitForSeconds(revealDelay);
        }
    }

    private void ApplyLook(DieInstance die, float brightness, Color? glowEmission)
    {
        foreach (RendererSlot s in die.slots)
        {
            s.renderer.GetPropertyBlock(block, s.materialIndex);
            Color c = s.color * brightness;
            c.a = s.color.a;
            block.SetColor(s.colorId, c);
            if (s.hasEmission)
                block.SetColor(EmissionId, s.isGlow && glowEmission.HasValue ? glowEmission.Value : s.emission * brightness);
            s.renderer.SetPropertyBlock(block, s.materialIndex);
        }
    }

    private void AddLight(DieInstance die, Color color, float intensity, float range)
    {
        var go = new GameObject("ResultLight");
        go.transform.SetParent(die.root.transform, false);
        go.transform.position = die.root.transform.position + Vector3.up * DieSize();
        die.light = go.AddComponent<Light>();
        die.light.type = LightType.Point;
        die.light.color = color;
        die.light.intensity = intensity;
        die.light.range = range;
    }

    private IEnumerator Punch(Transform t, float peak)
    {
        const float duration = 0.25f;
        Vector3 baseScale = t.localScale;
        for (float e = 0f; e < duration; e += Time.deltaTime)
        {
            if (t == null) yield break;
            float k = Mathf.Sin(e / duration * Mathf.PI);
            t.localScale = baseScale * Mathf.Lerp(1f, peak, k);
            yield return null;
        }
        if (t != null) t.localScale = baseScale;
    }

    private void PlayImpact(float strength)
    {
        if (audioSource == null || impactClips == null || impactClips.Length == 0) return;
        if (strength < 0.4f) return;
        AudioClip clip = impactClips[UnityEngine.Random.Range(0, impactClips.Length)];
        audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.15f);
        audioSource.PlayOneShot(clip, Mathf.Clamp01(strength / 6f));
    }

    private void PlayClip(AudioClip clip)
    {
        if (audioSource == null || clip == null || skipRequested) return;
        audioSource.pitch = 1f;
        audioSource.PlayOneShot(clip);
    }

    private static string Summary(DiceRollResult r)
    {
        if (r.Successes == 0) return $"{r.discipline}: no Successes";
        string s = $"{r.discipline}: {r.Successes} Success{(r.Successes == 1 ? "" : "es")}";
        if (r.IsOvercharged) s += $"  ·  OVERCHARGE ({r.Criticals} Crits)";
        else if (r.HasCritical) s += "  ·  Critical!";
        return s;
    }

    private void CompleteRoll()
    {
        IsRolling = false;
        rollRoutine = null;
        DiceRollResult result = pendingResult;
        Action<DiceRollResult> callback = pendingCallback;
        pendingResult = null;
        pendingCallback = null;
        if (result == null) return;
        callback?.Invoke(result);
        RollFinished?.Invoke(result);
    }

    private void FinishInterruptedRoll()
    {
        if (rollRoutine != null) StopCoroutine(rollRoutine);
        CompleteRoll();
    }

    private void OnDestroy()
    {
        if (simScene.IsValid() && simScene.isLoaded)
            SceneManager.UnloadSceneAsync(simScene);
    }
}
