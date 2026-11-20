using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One entry on a spell book's left page: a standard action (Arcane Strike, Guard...) or a learned
/// ability. Shows an icon, title, description and a detail line ("5d8 - range 3", or the reason it
/// can't be used). Hovering shows the AbilityTooltip; clicking starts it.
///
/// Make a prefab: a Button with these as children. Every text and the icon are optional, so the
/// same script works for a wide list row or a small icon-only tile in a Grid Layout Group.
/// </summary>
public class SpellActionEntryUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [Tooltip("Dice pool and range when usable, or the reason it can't be used.")]
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private Button button;
    [Tooltip("Fades the entry when it can't be used. Added automatically if missing.")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private float unavailableAlpha = 0.45f;
    [SerializeField] private Color unavailableTint = new Color(0.45f, 0.45f, 0.45f, 1f);
    [SerializeField] private Color reasonColor = new Color(0.6f, 0.15f, 0.15f);
    [SerializeField] private Color detailColor = new Color(0.25f, 0.2f, 0.15f);
    [Tooltip("Show the tooltip bubble on hover (needs an AbilityTooltip in the scene).")]
    [SerializeField] private bool tooltipOnHover = true;

    /// <summary>Set for standard actions, null for abilities.</summary>
    public SpellActionDefinition Definition { get; private set; }
    /// <summary>Set for abilities, null for standard actions.</summary>
    public AbilityDefinition Ability { get; private set; }

    private string detail = "";
    private bool available = true;
    private bool hovered;

    private string Title => Ability != null ? Ability.displayName : Definition?.title;
    private string Body => Ability != null ? Ability.TooltipText() : Definition?.description;
    private Sprite Icon => Ability != null ? Ability.icon : Definition?.icon;

    public void Setup(SpellActionDefinition def, Action<SpellActionDefinition> onClick)
    {
        Definition = def;
        Ability = null;
        Bind(() => onClick(Definition));
    }

    public void Setup(AbilityDefinition ability, Action<AbilityDefinition> onClick)
    {
        Ability = ability;
        Definition = null;
        Bind(() => onClick(Ability));
    }

    private void Bind(UnityEngine.Events.UnityAction click)
    {
        if (button == null) button = GetComponent<Button>();
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();

        if (titleText != null) titleText.text = Title;
        if (descriptionText != null) descriptionText.text = Body;
        if (iconImage != null)
        {
            iconImage.sprite = Icon;
            iconImage.enabled = Icon != null;
        }
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(click);
        }
    }

    /// <summary>Unusable entries stay clickable: clicking explains why in the log (and can show a tutorial).</summary>
    public void Refresh(bool isAvailable, string detailLine)
    {
        bool changed = available != isAvailable || detail != (detailLine ?? "");
        available = isAvailable;
        detail = detailLine ?? "";
        if (Definition != null && Definition.hideWhenUnavailable && gameObject.activeSelf != available)
            gameObject.SetActive(available);
        if (iconImage != null) iconImage.color = available ? Color.white : unavailableTint;
        if (detailText != null)
        {
            detailText.text = detail;
            detailText.color = available ? detailColor : reasonColor;
        }
        if (hovered && changed) ShowTooltip(); // keep the reason up to date while hovered
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        ShowTooltip();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        if (tooltipOnHover) AbilityTooltip.Hide();
    }

    private void OnDisable()
    {
        if (hovered && tooltipOnHover) AbilityTooltip.Hide();
        hovered = false;
    }

    private void ShowTooltip()
    {
        if (!tooltipOnHover) return;
        string colored = string.IsNullOrEmpty(detail) ? null
            : available ? detail : $"<color=#{ColorUtility.ToHtmlStringRGB(reasonColor)}>{detail}</color>";
        AbilityTooltip.Show(Title, Body, colored, Icon);
    }
}
