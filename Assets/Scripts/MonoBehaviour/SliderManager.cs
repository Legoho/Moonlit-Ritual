using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SliderManager : MonoBehaviour
{
    [SerializeField] private Slider turnSlider;
    [SerializeField] private Slider ritualProgressSlider;
    [Tooltip("Optional: Ritual Stability (0 to max).")]
    [SerializeField] private Slider stabilitySlider;
    [Header("Labels (optional)")]
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private TextMeshProUGUI stabilityText;
    [Tooltip("{0} = current value, {1} = maximum.")]
    [SerializeField] private string turnFormat = "Turn {0} / {1}";
    [SerializeField] private string progressFormat = "Progress {0} / {1}";
    [SerializeField] private string stabilityFormat = "Stability {0} / {1}";

    // Awake instead of Start so the sliders exist before RitualManager first updates them.
    // Only fills slots left empty in the Inspector.
    private void Awake()
    {
        var sliders = GetComponentsInChildren<Slider>();
        if (turnSlider == null && sliders.Length > 0) turnSlider = sliders[0];
        if (ritualProgressSlider == null && sliders.Length > 1) ritualProgressSlider = sliders[1];
    }

    public void UpdateTurnSlider(int currentTurn, int currentMaxTurn)
    {
        if (turnSlider == null) return;
        turnSlider.maxValue = currentMaxTurn;
        turnSlider.value = currentTurn;
        SetLabel(turnText, turnFormat, currentTurn, currentMaxTurn);
    }

    public void UpdateRitualProgressSlider(int currentProgress, int maxProgress)
    {
        if (ritualProgressSlider == null) return;
        ritualProgressSlider.maxValue = maxProgress;
        ritualProgressSlider.value = currentProgress;
        SetLabel(progressText, progressFormat, currentProgress, maxProgress);
    }

    public void UpdateStabilitySlider(int currentStability, int maxStability)
    {
        if (stabilitySlider == null) return;
        stabilitySlider.maxValue = maxStability;
        stabilitySlider.value = currentStability;
        SetLabel(stabilityText, stabilityFormat, currentStability, maxStability);
    }
    private static void SetLabel(TextMeshProUGUI label, string format, int current, int max)
    {
        if (label != null) label.text = string.Format(format, current, max);
    }
}
