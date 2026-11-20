using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Lists the encounter's Optional Objectives with their progress, e.g.
///   ○ Find the Lost Grimoire  (+2 Moonlight)
///      Mira carries the Lost Grimoire - bring it to the Anchor
/// Put it on a TMP text (or point it at one). Hides the root when there are no objectives.
/// </summary>
public class ObjectiveTrackerUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [Tooltip("Optional panel/background, hidden when the encounter has no objectives.")]
    [SerializeField] private GameObject root;
    [Tooltip("Optional: shows the run's total Moonlight.")]
    [SerializeField] private TextMeshProUGUI moonlightText;

    [SerializeField] private Color activeColor = new Color(0.95f, 0.9f, 0.75f);
    [SerializeField] private Color completedColor = new Color(0.55f, 0.95f, 0.55f);
    [SerializeField] private Color failedColor = new Color(0.9f, 0.4f, 0.4f);

    private ObjectiveManager manager;
    private bool dirty = true;

    private void Awake()
    {
        if (text == null) text = GetComponent<TextMeshProUGUI>();
    }

    private void Start()
    {
        manager = ObjectiveManager.Instance;
        if (manager != null) manager.Changed += MarkDirty;
        if (RunManager.Instance != null) RunManager.Instance.MoonlightChanged += MarkDirty;
    }

    private void OnDestroy()
    {
        if (manager != null) manager.Changed -= MarkDirty;
        if (RunManager.Instance != null) RunManager.Instance.MoonlightChanged -= MarkDirty;
    }

    private void MarkDirty() => dirty = true;

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;

        if (moonlightText != null)
            moonlightText.text = RunManager.Instance != null ? $"Moonlight {RunManager.Instance.Moonlight}" : "";

        bool any = manager != null && manager.Active.Count > 0;
        if (root != null) root.SetActive(any);
        if (text == null) return;
        if (!any) { text.text = ""; return; }

        var sb = new StringBuilder();
        foreach (var o in manager.Active)
        {
            Color c = o.State == ObjectiveState.Completed ? completedColor
                    : o.State == ObjectiveState.Failed ? failedColor : activeColor;
            string mark = o.State == ObjectiveState.Completed ? "✓" : o.State == ObjectiveState.Failed ? "✗" : "○";
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{mark} <b>{o.title}</b>");
            if (o.State == ObjectiveState.Active) sb.Append($"  <size=80%>(+{o.moonlightReward} Moonlight)</size>");
            sb.Append($"\n   <size=85%>{o.StatusLine}</size></color>\n");
        }
        text.text = sb.ToString().TrimEnd();
    }
}
