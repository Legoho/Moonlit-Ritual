using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hover bubble for spell book entries. Put it on your ToolTipBubble object (a panel with a
/// title text, a body text and optionally a detail text and an icon). It follows the mouse while shown.
/// The bubble never blocks clicks, so it can't flicker by covering the entry under the mouse.
/// Only one is used; the entries find it on their own.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class AbilityTooltip : MonoBehaviour
{
    public static AbilityTooltip Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [Tooltip("Optional: dice pool and range, or why it can't be used.")]
    [SerializeField] private TextMeshProUGUI detailText;
    [Tooltip("Optional.")]
    [SerializeField] private Image iconImage;
    [Tooltip("Distance from the mouse, in canvas pixels.")]
    [SerializeField] private Vector2 offset = new Vector2(18f, -18f);

    private RectTransform rect;
    private RectTransform canvasRect;
    private Canvas canvas;

    private bool initialized;

    private void Awake()
    {
        // If Show() already initialised it (it started disabled), this Awake is from Show itself: stay visible.
        if (initialized) return;
        Init();
        gameObject.SetActive(false);
    }

    private void Init()
    {
        if (initialized) return;
        initialized = true;
        Instance = this;
        rect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null) canvas = canvas.rootCanvas;
        canvasRect = canvas != null ? (RectTransform)canvas.transform : null;

        var group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // The bubble pivots on its top-left corner so it opens down and to the right of the mouse.
        rect.pivot = new Vector2(0f, 1f);
    }

    // Also works if the bubble starts disabled in the scene (its Awake hasn't run then).
    private static AbilityTooltip Find()
    {
        if (Instance == null)
        {
            var found = FindAnyObjectByType<AbilityTooltip>(FindObjectsInactive.Include);
            if (found != null) found.Init();
        }
        return Instance;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static void Show(string title, string body, string detail = null, Sprite icon = null)
    {
        var t = Find();
        if (t != null) t.ShowInternal(title, body, detail, icon);
    }

    public static void Hide()
    {
        if (Instance != null) Instance.gameObject.SetActive(false);
    }

    private void ShowInternal(string title, string body, string detail, Sprite icon)
    {
        if (titleText != null) titleText.text = title;
        if (bodyText != null) bodyText.text = body;
        if (detailText != null)
        {
            detailText.text = detail ?? "";
            detailText.gameObject.SetActive(!string.IsNullOrEmpty(detail));
        }
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }
        transform.SetAsLastSibling(); // drawn above the books
        gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect); // correct size for the edge check this frame
        FollowMouse();
    }

    private void LateUpdate() => FollowMouse();

    private void FollowMouse()
    {
        if (canvasRect == null) return;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, cam, out Vector2 local))
            return;

        Vector2 pos = local + offset;
        Vector2 size = rect.rect.size;
        Rect bounds = canvasRect.rect;

        // Flip to the other side of the mouse instead of leaving the screen.
        if (pos.x + size.x > bounds.xMax) pos.x = local.x - offset.x - size.x;
        if (pos.y - size.y < bounds.yMin) pos.y = local.y - offset.y + size.y;
        pos.x = Mathf.Clamp(pos.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x));
        pos.y = Mathf.Clamp(pos.y, Mathf.Min(bounds.yMax, bounds.yMin + size.y), bounds.yMax);

        // Position in the canvas space, then converted to the bubble's parent space.
        Vector3 world = canvasRect.TransformPoint(pos);
        rect.position = world;
    }
}
