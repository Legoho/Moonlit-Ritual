using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens and closes the four spell books. Hook the bottom book buttons to OpenArcana / OpenWarding /
/// OpenLore / OpenOccultism (clicking the same button again closes the book).
///
/// - Choosing an action that needs a target tucks the book away so the board can be clicked.
///   Right-click to cancel brings it back.
/// - When the dice are thrown, the book of that discipline is shown with the dice on the right page,
///   then the result.
/// - Clicking the board (not the UI) closes the open book, unless dice are rolling.
/// Put this on an object that stays active (e.g. RollPanel).
/// </summary>
public class SpellBookController : MonoBehaviour
{
    [Tooltip("The four books. Leave empty to find every SpellBook among this object's children.")]
    [SerializeField] private List<SpellBook> books = new();
    [Tooltip("Close the open book when the player clicks the board.")]
    [SerializeField] private bool closeOnBoardClick = true;
    [Tooltip("Seconds after a roll before the book closes by itself. 0 = stays open until clicked away.")]
    [SerializeField] private float autoCloseAfterResult = 0f;

    private RitualManager ritual;
    private DiceRollManager dice;
    private SpellBook current;
    private Coroutine transition;
    private Coroutine autoClose;

    private void Awake()
    {
        if (books.Count == 0) books.AddRange(GetComponentsInChildren<SpellBook>(true));
        foreach (var b in books) b.Init(OnEntryClicked, OnAbilityClicked);
    }

    private void Start()
    {
        ritual = FindAnyObjectByType<RitualManager>();
        // The ritual's own DiceRollManager: if the scene has more than one, a plain Find picks one at random.
        dice = ritual != null ? ritual.Dice : FindAnyObjectByType<DiceRollManager>();
        if (FindObjectsByType<DiceRollManager>(FindObjectsSortMode.None).Length > 1)
            Debug.LogWarning("[SpellBooks] There is more than one DiceRollManager in the scene. The RitualManager " +
                             "uses the one on its own object; the others can be removed.");
        if (dice != null)
        {
            dice.RollStarted += OnRollStarted;
            dice.RollFinished += OnRollFinished;
        }
    }

    private void OnDestroy()
    {
        if (dice != null)
        {
            dice.RollStarted -= OnRollStarted;
            dice.RollFinished -= OnRollFinished;
        }
    }

    // ---------- Button entry points ----------

    public void OpenArcana() => Toggle(Discipline.Arcana);
    public void OpenWarding() => Toggle(Discipline.Warding);
    public void OpenLore() => Toggle(Discipline.Lore);
    public void OpenOccultism() => Toggle(Discipline.Occultism);

    public void Toggle(Discipline d)
    {
        if (ritual != null && ritual.IsRollingDice) return; // don't swap books while dice are in the air
        if (current != null && current.discipline == d && (current.IsOpen || current.IsAnimating) && !current.IsTucked)
        {
            Close();
            return;
        }
        if (ritual != null && ritual.IsChoosingTarget) ritual.CancelAction();
        Open(d);
    }

    public void Open(Discipline d) => Open(d, true);

    private void Open(Discipline d, bool resetPages)
    {
        var book = books.FirstOrDefault(b => b.discipline == d);
        if (book == null) { Debug.LogWarning($"[SpellBooks] No book for {d}."); return; }
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(SwitchTo(book, resetPages));
    }

    public void Close()
    {
        AbilityTooltip.Hide();
        if (current == null) return;
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(CloseCurrent());
    }

    // ---------- Transitions ----------

    private IEnumerator SwitchTo(SpellBook book, bool resetPages)
    {
        if (current != null && current != book)
        {
            if (current.IsOpen || current.IsAnimating) yield return current.PlayClose();
            else current.CloseInstantly();
        }
        current = book;
        if (book.IsOpen) { book.Tuck(false); yield break; }

        book.gameObject.SetActive(true);
        book.RefreshEntries(ritual);
        yield return book.PlayOpen(resetPages);
        transition = null;
    }

    private IEnumerator CloseCurrent()
    {
        var book = current;
        current = null;
        if (book.IsOpen || book.IsAnimating) yield return book.PlayClose();
        else book.CloseInstantly();
        transition = null;
    }

    // ---------- Actions and rolls ----------

    private void OnEntryClicked(SpellActionDefinition def)
    {
        if (ritual == null || current == null || current.IsAnimating) return;
        if (!ritual.CanStartSpell(def.action, out _))
        {
            ritual.StartSpell(def.action); // logs the reason (and may show a tutorial tip)
            return;
        }

        ritual.StartSpell(def.action);
        if (ritual.IsChoosingTarget) current.Tuck(true); // let the player click a target on the board
        else current.RefreshEntries(ritual);
    }
    private void OnAbilityClicked(AbilityDefinition a)
    {
        if (ritual == null || current == null || current.IsAnimating) return;
        AbilityTooltip.Hide();
        bool ok = ritual.CanCastAbility(a, out _);
        ritual.CastAbility(a); // logs the reason if it can't be cast
        if (!ok) return;
        if (ritual.IsChoosingTarget) current.Tuck(true); // let the player click a target on the board
        else current.RefreshEntries(ritual);
    }
    private void OnRollStarted(Discipline d, int pool)
    {
        if (autoClose != null) { StopCoroutine(autoClose); autoClose = null; }
        var book = books.FirstOrDefault(b => b.discipline == d);
        if (book == null) return;

        book.ShowRolling(ritual != null ? ritual.ActiveHeroine : null, pool);
        if (current != book || !book.IsOpen) Open(d, resetPages: false);
        book.Tuck(false);
    }

    private void OnRollFinished(DiceRollResult r)
    {
        var book = books.FirstOrDefault(b => b.discipline == r.discipline);
        if (book == null) return;
        book.ShowResult(r);
        book.RefreshEntries(ritual);
        if (autoCloseAfterResult > 0f) autoClose = StartCoroutine(AutoClose(book));
    }

    private IEnumerator AutoClose(SpellBook book)
    {
        yield return new WaitForSecondsRealtime(autoCloseAfterResult);
        if (current == book) Close();
        autoClose = null;
    }

    private void Update()
    {
        if (current == null || ritual == null) return;

        // A tucked book comes back when targeting ends without a roll (cancelled, no target, Assist).
        if (current.IsTucked && !ritual.IsChoosingTarget && !ritual.IsRollingDice)
        {
            current.Tuck(false);
            current.RefreshEntries(ritual);
        }

        if (current.IsOpen && !current.IsTucked && !current.IsAnimating)
        {
            current.RefreshEntries(ritual); // availability changes as she moves, acts or is reselected

            bool boardClick = closeOnBoardClick && Input.GetMouseButtonDown(0)
                              && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
            if (boardClick && !ritual.IsRollingDice) Close();
        }
    }
}
