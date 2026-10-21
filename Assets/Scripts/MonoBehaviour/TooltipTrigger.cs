using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Add to any button. Hovering shows the shared tooltip bubble with this text;
/// moving the mouse away hides it.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea(2, 6)]
    [SerializeField] string text = "Describe what this button does.";
    [SerializeField] float showDelay = 0.35f; // seconds of hovering before it appears

    public string Text => text;
    public RectTransform Rect => (RectTransform)transform;

    Coroutine pending;

    public void OnPointerEnter(PointerEventData eventData)
    {
        StopPending();
        pending = StartCoroutine(ShowAfterDelay());
    }

    public void OnPointerExit(PointerEventData eventData) => HideNow();

    // If the button gets disabled or destroyed (e.g. turn changes), don't leave a bubble behind.
    void OnDisable() => HideNow();

    /// <summary>Change the tooltip at runtime, e.g. "Show rolls" / "Hide rolls".</summary>
    public void SetText(string newText)
    {
        text = newText;
        if (TooltipUI.Instance != null && TooltipUI.Instance.IsShowing(this))
            TooltipUI.Instance.Show(this); // redraw with the new text
    }

    IEnumerator ShowAfterDelay()
    {
        if (showDelay > 0f) yield return new WaitForSecondsRealtime(showDelay);
        if (TooltipUI.Instance != null) TooltipUI.Instance.Show(this);
        pending = null;
    }

    void HideNow()
    {
        StopPending();
        if (TooltipUI.Instance != null) TooltipUI.Instance.Hide(this);
    }

    void StopPending()
    {
        if (pending != null) StopCoroutine(pending);
        pending = null;
    }
}
