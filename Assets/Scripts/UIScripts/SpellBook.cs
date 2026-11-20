using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// One discipline's spell book (Arcana, Warding, Lore, Occultism) in the roll panel.
/// Opening: the cover (BookFront) shrinks on X from openScaleX to 0, then the left page (BookLeft)
/// grows on X from 0 to openScaleX. Closing plays it backwards.
/// Left page: every action of this discipline, built from the Actions list.
/// Right page: the dice camera (RawImage) and the roll result.
///
/// Put this on the book's root object (e.g. "Arcana" under RollPanel). The SpellBookController
/// drives it, so it works even while the object starts disabled.
/// </summary>
public class SpellBook : MonoBehaviour
{
    public Discipline discipline;

    [Header("Book parts")]
    [SerializeField] private RectTransform bookFront;
    [SerializeField] private RectTransform bookLeft;
    [Tooltip("Optional: things shown only once the book is fully open (ribbon, page text...).")]
    [SerializeField] private GameObject[] showWhenOpen;

    [Header("Left page")]
    [Tooltip("Parent the action entries are created under. A Vertical Layout Group on it stacks them.")]
    [SerializeField] private Transform actionList;
    [SerializeField] private SpellActionEntryUI entryPrefab;
    [Tooltip("Optional page heading, e.g. 'Arcana - Mira (7d8)'.")]
    [SerializeField] private TextMeshProUGUI headerText;
    [Header("Learned abilities")]
    [Tooltip("Parent for the selected heroine's abilities, e.g. your panel with the Grid Layout Group. " +
            "Empty = they go into Action List after the standard actions.")]
    [SerializeField] private Transform abilityList;
    [Tooltip("Prefab for ability entries, e.g. an icon tile. Empty = the Entry Prefab.")]
    [SerializeField] private SpellActionEntryUI abilityEntryPrefab;
    [Tooltip("Optional: shown when she knows no abilities of this discipline (e.g. 'No spells learned yet').")]
    [SerializeField] private GameObject noAbilitiesHint;

    [Header("Right page")]
    [Tooltip("The RawImage showing the dice camera. Shown while rolling and afterwards.")]
    [SerializeField] private GameObject diceView;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private string idleText = "Choose a spell on the left page.";

    [Header("Animation")]
    [SerializeField] private float openScaleX = 0.5f;
    [SerializeField] private float coverDuration = 0.2f;
    [SerializeField] private float pageDuration = 0.2f;

    [Header("Docking while a target is chosen")]
    [Tooltip("This book's button at the bottom. While the player picks a target, the book shrinks onto it. " +
             "Empty = the book just becomes invisible instead.")]
    [SerializeField] private RectTransform dockTarget;
    [Tooltip("Size of the docked book, as a fraction of its open size.")]
    [SerializeField, Range(0.05f, 1f)] private float dockedSize = 0.3f;
    [SerializeField] private float dockDuration = 0.25f;

    [Header("Actions on the left page (left empty = defaults for this discipline)")]
    public List<SpellActionDefinition> actions = new();

    private readonly List<SpellActionEntryUI> entries = new();
    private readonly List<SpellActionEntryUI> abilityEntries = new();
    private System.Action<AbilityDefinition> onAbilityClicked;
    private Heroine abilitiesShownFor;
    private int abilitiesShownCount = -1;
    private CanvasGroup group;
    private bool initialized;
    private Vector3 openLocalPosition;
    private Vector3 openLocalScale;
    private Coroutine dockRoutine;
    private Coroutine animRoutine;   // the open or close animation that is running
    private bool rollInProgress;     // dice on the right page: opening the book must not clear them

    public bool IsOpen { get; private set; }
    public bool IsAnimating { get; private set; }
    public bool IsTucked { get; private set; }
    public RectTransform Rect => (RectTransform)transform;

    /// <summary>Called once by the controller (also works while the object is disabled).</summary>
    public void Init(System.Action<SpellActionDefinition> onEntryClicked, System.Action<AbilityDefinition> onAbilityClicked = null)
    {
        if (initialized) return;
        initialized = true;
        this.onAbilityClicked = onAbilityClicked;

        // The size and place the book has in the scene is its open size and place.
        openLocalPosition = transform.localPosition;
        openLocalScale = transform.localScale;

        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        if (actions.Count == 0) actions = SpellActions.Defaults(discipline);

        if (actionList != null && entryPrefab != null)
        {
            foreach (var def in actions)
            {
                var e = Instantiate(entryPrefab, actionList);
                e.Setup(def, onEntryClicked);
                entries.Add(e);
            }
        }

        SetScaleX(bookFront, openScaleX);
        SetScaleX(bookLeft, 0f);
        SetShowWhenOpen(false);
        ShowIdle();
        gameObject.SetActive(false);
    }

    // ---------- Open / close ----------
    /// <summary>Starts the open animation, stopping a close that may still be running.</summary>
    public Coroutine PlayOpen(bool resetPages = true)
    {
        StopAnimation();
        gameObject.SetActive(true);
        animRoutine = StartCoroutine(OpenRoutine(resetPages));
        return animRoutine;
    }

    /// <summary>Starts the close animation, stopping an open that may still be running.</summary>
    public Coroutine PlayClose()
    {
        StopAnimation();
        if (!gameObject.activeInHierarchy) { CloseInstantly(); return null; }
        animRoutine = StartCoroutine(CloseRoutine());
        return animRoutine;
    }

    private void StopAnimation()
    {
        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = null;
        IsAnimating = false;
    }
    /// <param name="resetPages">False keeps the right page as it is (e.g. dice already rolling).</param>
    public IEnumerator OpenRoutine(bool resetPages = true)
    {
        gameObject.SetActive(true);
        Tuck(false);
        IsAnimating = true;
        if (resetPages && !rollInProgress) ShowIdle();
        SetScaleX(bookFront, openScaleX);
        SetScaleX(bookLeft, 0f);
        SetShowWhenOpen(false);

        yield return AnimateScaleX(bookFront, openScaleX, 0f, coverDuration);
        yield return AnimateScaleX(bookLeft, 0f, openScaleX, pageDuration);

        SetShowWhenOpen(true);
        IsOpen = true;
        IsAnimating = false;
    }

    public IEnumerator CloseRoutine()
    {
        IsAnimating = true;
        IsOpen = false;
        SetShowWhenOpen(false);

        yield return AnimateScaleX(bookLeft, openScaleX, 0f, pageDuration);
        yield return AnimateScaleX(bookFront, 0f, openScaleX, coverDuration);

        IsAnimating = false;
        ResetPose();
        gameObject.SetActive(false);
    }

    /// <summary>Instantly closed and hidden (e.g. when switching books mid-animation).</summary>
    public void CloseInstantly()
    {
        StopAllCoroutines();
        IsOpen = false;
        IsAnimating = false;
        SetScaleX(bookFront, openScaleX);
        SetScaleX(bookLeft, 0f);
        SetShowWhenOpen(false);
        ResetPose();
        gameObject.SetActive(false);
    }

    /// <summary>Hides the book without closing it, so the player can click a target on the board.</summary>
    /// <summary>
    /// Tucked = shrunk onto its button at the bottom (or invisible if it has no Dock Target), so the
    /// board can be clicked. Clicks go through it, so the button underneath still works.
    /// </summary>
    public void Tuck(bool tucked)
    {
        if (IsTucked == tucked && dockRoutine == null) return;
        IsTucked = tucked;
        if (tucked) AbilityTooltip.Hide();
        if (group != null)
        {
            group.blocksRaycasts = !tucked;
            group.interactable = !tucked;
        }

        if (dockTarget == null)
        {
            if (group != null) group.alpha = tucked ? 0f : 1f;
            return;
        }

        if (dockRoutine != null) StopCoroutine(dockRoutine);
        if (!gameObject.activeInHierarchy) { SnapTo(tucked); return; }
        dockRoutine = StartCoroutine(Dock(tucked));
    }

    private IEnumerator Dock(bool tucked)
    {
        Vector3 fromPos = transform.localPosition, fromScale = transform.localScale;
        GetDockPose(tucked, out Vector3 toPos, out Vector3 toScale);

        for (float t = 0f; t < dockDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / dockDuration);
            transform.localPosition = Vector3.Lerp(fromPos, toPos, k);
            transform.localScale = Vector3.Lerp(fromScale, toScale, k);
            yield return null;
        }
        transform.localPosition = toPos;
        transform.localScale = toScale;
        dockRoutine = null;
    }

    private void SnapTo(bool tucked)
    {
        GetDockPose(tucked, out Vector3 pos, out Vector3 scale);
        transform.localPosition = pos;
        transform.localScale = scale;
        dockRoutine = null;
    }

    private void GetDockPose(bool tucked, out Vector3 pos, out Vector3 scale)
    {
        if (!tucked || dockTarget == null)
        {
            pos = openLocalPosition;
            scale = openLocalScale;
            return;
        }
        // The centre of the button, in the book's parent space.
        Vector3 world = dockTarget.TransformPoint(dockTarget.rect.center);
        pos = transform.parent != null ? transform.parent.InverseTransformPoint(world) : world;
        pos.z = openLocalPosition.z;
        scale = openLocalScale * dockedSize;
    }

    /// <summary>Back to the open size and place without animation (used when the book is closed).</summary>
    private void ResetPose()
    {
        if (dockRoutine != null) { StopCoroutine(dockRoutine); dockRoutine = null; }
        IsTucked = false;
        transform.localPosition = openLocalPosition;
        transform.localScale = openLocalScale;
        if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; group.interactable = true; }
    }

    // ---------- Pages ----------

    public void RefreshEntries(RitualManager ritual)
    {
        var h = ritual != null ? ritual.ActiveHeroine : null;
        if (headerText != null)
            headerText.text = h != null ? $"{discipline} - {h.DisplayName} ({h.GetPool(discipline)}d8)" : discipline.ToString();

        RebuildAbilityEntries(h);

        foreach (var e in abilityEntries)
        {
            string reason = "";
            bool ok = ritual != null && ritual.CanCastAbility(e.Ability, out reason);
            string detail = reason ?? "";
            if (ok && h != null)
            {
                var ab = e.Ability;
                string place = ab.requirement == AbilityRequirement.AdjacentToAnchor ? "next to the Anchor"
                             : ab.target == AbilityTarget.None ? "no target"
                             : $"range {ab.RangeFor(h):0.#}{(ab.straightLineOnly ? " (line)" : "")}";
                detail = $"{h.GetPool(ab.discipline)}d8 - {place}";
            }
            e.Refresh(ok, detail);
        }
        foreach (var e in entries)
        {
            var a = e.Definition.action;
            string reason = "";
            bool ok = ritual != null && ritual.CanStartSpell(a, out reason);
            string detail = reason ?? "";
            if (ok && h != null)
            {
                var d = SpellActions.DisciplineOf(a);
                detail = d.HasValue && SpellActions.Rolls(a)
                    ? $"{h.GetPool(d.Value)}d8 - range {h.GetRange(d.Value):0.#}"
                    : "No roll";
            }
            e.Refresh(ok, detail);
        }
    }
    /// <summary>Rebuilds the ability entries when the selected heroine (or what she knows) changes.</summary>
    private void RebuildAbilityEntries(Heroine h)
    {
        int count = h != null ? h.AbilitiesOf(discipline).Count() : 0;
        if (h == abilitiesShownFor && count == abilitiesShownCount) return;
        abilitiesShownFor = h;
        abilitiesShownCount = count;

        foreach (var e in abilityEntries) if (e != null) Destroy(e.gameObject);
        abilityEntries.Clear();

        var parent = abilityList != null ? abilityList : actionList;
        var prefab = abilityEntryPrefab != null ? abilityEntryPrefab : entryPrefab;
        if (h != null && parent != null && prefab != null)
        {
            foreach (var a in h.AbilitiesOf(discipline))
            {
                var e = Instantiate(prefab, parent);
                e.Setup(a, OnAbilityEntryClicked);
                abilityEntries.Add(e);
            }
        }
        if (noAbilitiesHint != null) noAbilitiesHint.SetActive(h != null && count == 0);
    }

    private void OnAbilityEntryClicked(AbilityDefinition a) => onAbilityClicked?.Invoke(a);
    public void ShowIdle()
    {
        if (diceView != null) diceView.SetActive(false);
        if (resultText != null) resultText.text = idleText;
    }

    public void ShowRolling(Heroine h, int dice)
    {
        if (diceView != null) diceView.SetActive(true);
        if (resultText != null)
            resultText.text = h != null ? $"{h.DisplayName} rolls {dice}d8..." : $"Rolling {dice}d8...";
    }

    public void ShowResult(DiceRollResult r)
    {
        if (resultText == null) return;
        var faces = r.values.Select(f =>
            DiceRules.IsCritical(f) ? $"<color=#C8901A><b>{f}</b></color>" :
            r.SuccessesOf(f) > 0 ? $"<color=#2F7D32><b>{f}</b></color>" : $"<color=#7A6A5A>{f}</color>");
        string extra = r.IsOvercharged ? "\n<b>OVERCHARGE!</b>" : r.HasCritical ? "\n<b>Critical!</b>" : "";
        resultText.text = $"{string.Join("  ", faces)}\n<size=120%><b>{r.Successes} Success{(r.Successes == 1 ? "" : "es")}</b></size>{extra}";
    }

    // ---------- Helpers ----------

    private void SetShowWhenOpen(bool on)
    {
        if (showWhenOpen == null) return;
        foreach (var go in showWhenOpen) if (go != null) go.SetActive(on);
    }

    private static void SetScaleX(RectTransform rt, float x)
    {
        if (rt == null) return;
        var s = rt.localScale;
        rt.localScale = new Vector3(x, s.y, s.z);
    }

    private static IEnumerator AnimateScaleX(RectTransform rt, float from, float to, float duration)
    {
        if (rt == null) yield break;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            SetScaleX(rt, Mathf.Lerp(from, to, k));
            yield return null;
        }
        SetScaleX(rt, to);
    }
}
