using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One heroine panel on the left of the screen. Every field is optional.
/// Clicking the panel (Button) selects that heroine, or picks her as the target
/// when an action such as Warding is waiting for one.
/// </summary>
public class PartySlotUI : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("The white area: her portrait.")]
    [SerializeField] private RawImage portrait;
    [Tooltip("The red bar. Set its Image Type to Filled, Fill Method Vertical, Origin Bottom.")]
    [SerializeField] private Image resolveBar;
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("e.g. \"2 Actions, Move\", \"Done\", \"Downed\".")]
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("Shown while this heroine is selected (e.g. a glowing frame).")]
    [SerializeField] private GameObject selectedHighlight;
    [Tooltip("Shown while she has Shields.")]
    [SerializeField] private GameObject shieldIcon;
    [SerializeField] private Button button;

    [Header("Look")]
    [SerializeField] private Color normalTint = Color.white;
    [SerializeField] private Color doneTint = new Color(0.7f, 0.7f, 0.7f, 1f);
    [SerializeField] private Color downedTint = new Color(0.3f, 0.3f, 0.3f, 1f);

    public Heroine Heroine { get; private set; }
    private RitualManager ritualManager;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(OnClick);
        ritualManager = FindAnyObjectByType<RitualManager>();
        if (selectedHighlight != null) selectedHighlight.SetActive(false);
    }

    /// <summary>Connects this panel to a heroine; null hides the panel.</summary>
    public void Bind(Heroine h)
    {
        Heroine = h;
        gameObject.SetActive(h != null);
        if (h == null) return;

        if (portrait != null)
        {
            portrait.texture = h.portrait;
            portrait.enabled = h.portrait != null;
        }
        if (nameText != null) nameText.text = h.DisplayName;
    }

    public void Refresh(bool isSelected, bool isDone)
    {
        if (Heroine == null) return;

        if (resolveBar != null)
            resolveBar.fillAmount = Heroine.maxResolve > 0 ? (float)Heroine.resolve / Heroine.maxResolve : 0f;

        if (portrait != null)
            portrait.color = Heroine.IsDowned ? downedTint : isDone ? doneTint : normalTint;

        if (selectedHighlight != null && selectedHighlight.activeSelf != isSelected)
            selectedHighlight.SetActive(isSelected);

        if (shieldIcon != null && shieldIcon.activeSelf != Heroine.shields > 0)
            shieldIcon.SetActive(Heroine.shields > 0);

        if (statusText != null)
        {
            statusText.text =
                Heroine.IsDowned ? "Downed" :
                isDone ? "Done" :
                $"{Heroine.actionsRemaining} Action{(Heroine.actionsRemaining == 1 ? "" : "s")}" +
                (Heroine.hasMoved ? "" : ", Move");
        }
    }

    private void OnClick()
    {
        if (Heroine != null && ritualManager != null)
            ritualManager.OnHeroinePanelClicked(Heroine);
    }
}
