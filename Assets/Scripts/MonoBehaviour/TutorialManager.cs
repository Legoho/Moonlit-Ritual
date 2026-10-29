using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Moments in the game that can show a tutorial message.
/// To add a new one: add a value here, and call TutorialManager.Fire(TutorialTrigger.YourValue, heroine)
/// at the place in the code where it happens. Then add a message for it in the Inspector.
/// </summary>
public enum TutorialTrigger
{
    EncounterStart,             // the combat scene has loaded and the encounter begins
    HeroineSelected,            // a heroine is selected
    HeroineSwitched,            // a different heroine than the last one is selected
    ChannelNotAdjacent,         // Occultism pressed while not next to the Anchor
    ChannelLimitedByStability,  // Channel lost Progress because Stability is too low
    HeroineDowned,              // a heroine dropped to 0 Resolve
}

/// <summary>One tutorial line. Placeholders: {narrator} = the girl talking, {heroine} = the girl involved.</summary>
[Serializable]
public class TutorialMessage
{
    [Tooltip("Unique name, used to remember it was shown and to show it by hand with TutorialManager.Show(id).")]
    public string id;
    public TutorialTrigger trigger;
    [TextArea(2, 5)] public string text;
    [Tooltip("Show only the first time the trigger happens.")]
    public bool showOnce = true;

    public TutorialMessage(string id, TutorialTrigger trigger, string text)
    {
        this.id = id;
        this.trigger = trigger;
        this.text = text;
    }
}

/// <summary>
/// Shows tutorial messages as if a heroine who is NOT on the mission were talking to the team over a link.
/// Messages queue up if several trigger at once. Any click (or Space / Enter) closes the current one.
/// While a message is open, the full-screen overlay blocks clicks on the board.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("On / off")]
    public bool tutorialEnabled = true;
    [Tooltip("On: a message marked 'Show Once' is never shown again, even after restarting the game.")]
    public bool rememberAcrossSessions = false;

    [Header("UI")]
    [Tooltip("Full-screen object holding everything below. Give it a transparent Image with Raycast Target " +
             "ticked so clicks don't reach the board while a message is open. Leave it disabled.")]
    [SerializeField] private GameObject overlayRoot;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TextMeshProUGUI speakerNameText;
    [SerializeField] private RawImage speakerPortrait;
    [Tooltip("Optional, e.g. 'Click to continue'. Shown once the text has finished typing.")]
    [SerializeField] private GameObject continueHint;
    [Tooltip("Letters per second for the typing effect. 0 = show the whole text at once.")]
    [SerializeField] private float charactersPerSecond = 60f;

    [Header("Who is talking")]
    [Tooltip("Optional. If empty, a heroine from the RunManager roster who is not in the party is chosen.")]
    [SerializeField] private HeroineDefinition narratorOverride;
    [Tooltip("Used when no narrator can be found (e.g. no RunManager in the scene).")]
    [SerializeField] private string fallbackNarratorName = "Elin";
    [SerializeField] private Texture fallbackNarratorPortrait;

    [Header("Messages")]
    [SerializeField]
    private List<TutorialMessage> messages = new()
    {
        new("intro", TutorialTrigger.EncounterStart,
            "Hello, can you hear me? It's me, {narrator}! Our team has arrived at the first ritual location! " +
            "To start the turn, choose one of the heroines."),
        new("actions", TutorialTrigger.HeroineSelected,
            "Good! Now you can choose between four actions. Hover over each one to learn more about it, but in short: " +
            "Arcana attacks a ghost, Warding keeps you alive, Lore helps you understand the ghosts, " +
            "and Occultism progresses the ritual."),
        new("objective", TutorialTrigger.HeroineSwitched,
            "Your objective is to finish the ritual! No heroics needed: just stay alive and healthy, and finish the ritual!"),
        new("channel_adjacent", TutorialTrigger.ChannelNotAdjacent,
            "Good thinking, {heroine}, but you can only progress the ritual if you're standing beside the Anchor!"),
        new("channel_stability", TutorialTrigger.ChannelLimitedByStability,
            "Good thinking, {heroine}, but the Anchor's current Stability can't hold this much occult energy. " +
            "You should cast a ward on the Anchor as well."),
        new("downed", TutorialTrigger.HeroineDowned,
            "Watch out, {heroine} is down! Send someone beside her and patch her up with Warding!"),
    };

    private readonly Queue<(TutorialMessage message, string heroineName)> queue = new();
    private readonly HashSet<string> shownThisSession = new();
    private string narratorName;
    private Texture narratorPortrait;
    private bool narratorResolved;

    private bool isOpen;
    private int openedOnFrame;
    private string fullText;
    private float typedCharacters;

    public bool IsShowing => isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        if (overlayRoot != null) overlayRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // =====================================================================
    // Public API
    // =====================================================================

    /// <summary>Tells the tutorial that something happened. Safe to call when there is no TutorialManager.</summary>
    public static void Fire(TutorialTrigger trigger, Heroine heroine = null)
    {
        if (Instance != null) Instance.OnTrigger(trigger, heroine);
    }

    /// <summary>Shows one message by its id, regardless of trigger (e.g. from a UnityEvent or a cutscene).</summary>
    public static void Show(string id, Heroine heroine = null)
    {
        if (Instance == null) return;
        var m = Instance.messages.FirstOrDefault(x => x.id == id);
        if (m != null) Instance.Enqueue(m, heroine);
    }

    /// <summary>Forgets which messages were already shown (for a "replay tutorial" option).</summary>
    public void ResetProgress()
    {
        shownThisSession.Clear();
        foreach (var m in messages) PlayerPrefs.DeleteKey(PrefKey(m));
    }

    private void OnTrigger(TutorialTrigger trigger, Heroine heroine)
    {
        if (!tutorialEnabled) return;
        foreach (var m in messages)
            if (m.trigger == trigger) Enqueue(m, heroine);
    }

    private void Enqueue(TutorialMessage m, Heroine heroine)
    {
        if (m.showOnce && WasShown(m)) return;
        if (m.showOnce) MarkShown(m); // mark now so a trigger firing twice in a row can't queue it twice

        queue.Enqueue((m, heroine != null ? heroine.DisplayName : "you"));
        if (!isOpen) ShowNext();
    }

    // =====================================================================
    // Showing and closing
    // =====================================================================

    private void ShowNext()
    {
        if (queue.Count == 0)
        {
            isOpen = false;
            if (overlayRoot != null) overlayRoot.SetActive(false);
            return;
        }

        ResolveNarrator();
        var (message, heroineName) = queue.Dequeue();
        fullText = message.text.Replace("{narrator}", narratorName).Replace("{heroine}", heroineName);

        if (speakerNameText != null) speakerNameText.text = narratorName;
        if (speakerPortrait != null)
        {
            speakerPortrait.texture = narratorPortrait;
            speakerPortrait.enabled = narratorPortrait != null;
        }
        if (messageText != null)
        {
            messageText.text = fullText;
            messageText.maxVisibleCharacters = charactersPerSecond > 0f ? 0 : int.MaxValue;
        }
        typedCharacters = 0f;
        if (continueHint != null) continueHint.SetActive(charactersPerSecond <= 0f);

        if (overlayRoot != null) overlayRoot.SetActive(true);
        isOpen = true;
        openedOnFrame = Time.frameCount; // the click that opened it must not close it straight away
    }

    private bool IsTyping => messageText != null && charactersPerSecond > 0f && typedCharacters < fullText.Length;

    private void Update()
    {
        if (!isOpen) return;

        if (IsTyping)
        {
            typedCharacters += charactersPerSecond * Time.unscaledDeltaTime;
            messageText.maxVisibleCharacters = Mathf.Min(fullText.Length, (int)typedCharacters);
            if (!IsTyping && continueHint != null) continueHint.SetActive(true);
        }

        if (Time.frameCount == openedOnFrame) return;
        bool pressed = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)
                       || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
        if (!pressed) return;

        if (IsTyping)
        {
            // First click finishes the typing; the next one closes.
            typedCharacters = fullText.Length;
            messageText.maxVisibleCharacters = fullText.Length;
            if (continueHint != null) continueHint.SetActive(true);
            return;
        }
        ShowNext();
    }

    // =====================================================================
    // Narrator and memory
    // =====================================================================

    private void ResolveNarrator()
    {
        if (narratorResolved) return;
        narratorResolved = true;

        HeroineDefinition def = narratorOverride;
        var run = RunManager.Instance;
        if (def == null && run != null)
            def = run.roster.FirstOrDefault(d => d != null && !run.IsInParty(d));

        narratorName = def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : fallbackNarratorName;
        narratorPortrait = def != null && def.portrait != null ? def.portrait : fallbackNarratorPortrait;
    }

    private static string PrefKey(TutorialMessage m) => "Tutorial_" + m.id;

    private bool WasShown(TutorialMessage m) =>
        shownThisSession.Contains(m.id) || (rememberAcrossSessions && PlayerPrefs.GetInt(PrefKey(m), 0) == 1);

    private void MarkShown(TutorialMessage m)
    {
        shownThisSession.Add(m.id);
        if (rememberAcrossSessions) PlayerPrefs.SetInt(PrefKey(m), 1);
    }
}

