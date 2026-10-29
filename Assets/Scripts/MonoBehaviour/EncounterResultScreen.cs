using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Victory / defeat screen. Appears when the encounter ends and offers one button:
///   - won:  goes to the Run Scene (the run map between encounters),
///   - lost: goes to the Main Menu Scene.
/// While a scene is left empty (or isn't in the Build Settings yet), that case restarts the encounter instead.
/// </summary>
public class EncounterResultScreen : MonoBehaviour
{
    public enum RestartMode
    {
        ReloadScene,     // full reload: fresh map, fresh spawn of the party (RunManager survives)
        RestartInPlace   // same map, monsters cleared, heroines reset (RitualManager.RestartEncounter)
    }

    [Header("UI")]
    [Tooltip("The whole screen (dim background + panel). Leave it disabled in the scene.")]
    [SerializeField] private GameObject root;
    [SerializeField] private TextMeshProUGUI titleText;
    [Tooltip("Optional: why the encounter ended, e.g. 'The ritual collapses: out of time.'")]
    [SerializeField] private TextMeshProUGUI reasonText;
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI buttonText;
    [Tooltip("Optional: shown only on victory / only on defeat (art, glow, particles...).")]
    [SerializeField] private GameObject victoryDecoration;
    [SerializeField] private GameObject defeatDecoration;

    [Header("Texts")]
    [SerializeField] private string victoryTitle = "Ritual Complete!";
    [SerializeField] private string defeatTitle = "The Ritual Has Failed";
    [SerializeField] private string restartLabel = "Try Again";
    [SerializeField] private string continueLabel = "Continue";
    [SerializeField] private string menuLabel = "Main Menu";

    [Header("Behaviour")]
    [Tooltip("Seconds to wait after the last action before the screen appears.")]
    [SerializeField] private float delay = 1.0f;
    [Tooltip("Scene loaded after a VICTORY (the run map). Leave empty until it exists; the button restarts until then. " +
             "The scene must also be added to File > Build Profiles / Build Settings.")]
    [SerializeField] private string runScene = "";
    [Tooltip("Scene loaded after a DEFEAT. Leave empty until it exists; the button restarts until then. " +
             "The scene must also be added to File > Build Profiles / Build Settings.")]
    [SerializeField] private string mainMenuScene = "";
    [SerializeField] private RestartMode restartMode = RestartMode.ReloadScene;

    private RitualManager ritualManager;
    private bool lastWon;

    /// <summary>The scene the button leads to for this result, or null to restart.</summary>
    private string Destination
    {
        get
        {
            string scene = lastWon ? runScene : mainMenuScene;
            return !string.IsNullOrEmpty(scene) && Application.CanStreamedLevelBeLoaded(scene) ? scene : null;
        }
    }

    private void Awake()
    {
        if (root != null) root.SetActive(false);
        if (button != null) button.onClick.AddListener(OnButton);
    }

    private void OnEnable()
    {
        ritualManager = FindAnyObjectByType<RitualManager>();
        if (ritualManager != null) ritualManager.EncounterEnded += OnEncounterEnded;
    }

    private void OnDisable()
    {
        if (ritualManager != null) ritualManager.EncounterEnded -= OnEncounterEnded;
    }

    private void OnEncounterEnded(bool won, string reason)
    {
        StopAllCoroutines();
        StartCoroutine(ShowAfterDelay(won, reason));
    }

    private IEnumerator ShowAfterDelay(bool won, string reason)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        lastWon = won;

        if (titleText != null) titleText.text = won ? victoryTitle : defeatTitle;
        if (reasonText != null) reasonText.text = reason;
        if (buttonText != null)
            buttonText.text = Destination == null ? restartLabel : won ? continueLabel : menuLabel;
        if (victoryDecoration != null) victoryDecoration.SetActive(won);
        if (defeatDecoration != null) defeatDecoration.SetActive(!won);
        if (root != null) root.SetActive(true);
    }

    private void OnButton()
    {
        string destination = Destination;
        if (destination != null)
        {
            SceneManager.LoadScene(destination);
            return;
        }

        // A scene that isn't in the Build Settings can't be reloaded, so fall back to restarting in place.
        bool canReload = SceneManager.GetActiveScene().buildIndex >= 0;
        if ((restartMode == RestartMode.RestartInPlace || !canReload) && ritualManager != null)
        {
            if (root != null) root.SetActive(false);
            ritualManager.RestartEncounter();
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}