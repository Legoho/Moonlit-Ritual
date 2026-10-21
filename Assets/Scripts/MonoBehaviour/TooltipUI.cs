using TMPro;
using UnityEngine;

/// <summary>
/// The single shared tooltip bubble. Put this on an Image that is a DIRECT child
/// of your root Canvas, with one TextMeshPro text as its child.
/// </summary>
public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance { get; private set; }

    [SerializeField] TMP_Text label;
    [SerializeField] float maxWidth = 320f;          // text wraps beyond this width
    [SerializeField] Vector2 padding = new Vector2(12f, 8f);
    [SerializeField] float gap = 10f;                // space between button and bubble
    [SerializeField] float screenMargin = 8f;        // keep bubble this far from canvas edges

    RectTransform bubble;
    RectTransform canvasRect;
    Canvas rootCanvas;
    TooltipTrigger owner;

    void Awake()
    {
        Instance = this;
        bubble = (RectTransform)transform;
        rootCanvas = GetComponentInParent<Canvas>().rootCanvas;
        canvasRect = (RectTransform)rootCanvas.transform;

        // The bubble must never catch the mouse, or it would flicker on and off.
        if (!TryGetComponent(out CanvasGroup group)) group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        label.raycastTarget = false;

        // Centre anchors so localPosition maps cleanly to canvas space.
        bubble.anchorMin = bubble.anchorMax = new Vector2(0.5f, 0.5f);

        // Stretch the label inside the bubble with padding.
        RectTransform lr = label.rectTransform;
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.offsetMin = padding;
        lr.offsetMax = -padding;

        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Show(TooltipTrigger trigger)
    {
        owner = trigger;

        // Size the bubble to the text, wrapping at maxWidth.
        string text = trigger.Text;
        label.text = text;
        float w = Mathf.Min(label.GetPreferredValues(text).x, maxWidth);
        float h = label.GetPreferredValues(text, w, 0f).y;
        Vector2 size = new Vector2(w + padding.x * 2f, h + padding.y * 2f);
        bubble.sizeDelta = size;

        // Find the button's top and bottom centre in canvas space.
        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        Vector3[] c = new Vector3[4];
        trigger.Rect.GetWorldCorners(c); // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
        Vector2 top = ToCanvas((c[1] + c[2]) * 0.5f, cam);
        Vector2 bottom = ToCanvas((c[0] + c[3]) * 0.5f, cam);

        Rect area = canvasRect.rect;
        Vector2 pos;

        // Prefer above the button; flip below if it would go off the top.
        if (top.y + gap + size.y <= area.yMax - screenMargin)
        {
            bubble.pivot = new Vector2(0.5f, 0f);
            pos = top + Vector2.up * gap;
        }
        else
        {
            bubble.pivot = new Vector2(0.5f, 1f);
            pos = bottom + Vector2.down * gap;
        }

        // Keep it inside the left and right edges.
        float half = size.x * 0.5f;
        pos.x = Mathf.Clamp(pos.x, area.xMin + screenMargin + half, area.xMax - screenMargin - half);

        bubble.localPosition = pos;
        bubble.SetAsLastSibling(); // draw on top of the other UI
        gameObject.SetActive(true);
    }

    public void Hide(TooltipTrigger trigger)
    {
        if (owner != trigger) return; // another button already took over
        owner = null;
        gameObject.SetActive(false);
    }

    public bool IsShowing(TooltipTrigger trigger) => owner == trigger && gameObject.activeSelf;

    Vector2 ToCanvas(Vector3 world, Camera cam)
    {
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out Vector2 local);
        return local;
    }
}
