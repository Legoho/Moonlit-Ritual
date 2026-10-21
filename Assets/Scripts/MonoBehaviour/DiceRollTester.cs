using UnityEngine;

/// <summary>
/// Throwaway test panel: pick a discipline and roll 4d8 / 5d8 / 7d8. Put it on any GameObject.
/// </summary>
public class DiceRollTester : MonoBehaviour
{
    [SerializeField] private DiceRollVisualizer visualizer;

    private Discipline discipline = Discipline.Arcana;
    private string lastResult = "";
    private static readonly string[] Names = { "Arcana", "Warding", "Lore", "Occultism" };

    private void OnGUI()
    {
        if (visualizer == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 330, 140), GUI.skin.box);
        discipline = (Discipline)GUILayout.Toolbar((int)discipline, Names);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("4d8")) Roll(4);
        if (GUILayout.Button("5d8")) Roll(5);
        if (GUILayout.Button("7d8")) Roll(7);
        if (GUILayout.Button("Skip")) visualizer.SkipToResult();
        GUILayout.EndHorizontal();
        GUILayout.Label(visualizer.IsRolling ? "Rolling..." : lastResult);
        GUILayout.EndArea();
    }

    private void Roll(int count)
    {
        DiceRollResult r = visualizer.Roll(discipline, count, done => lastResult = done.ToString());
        Debug.Log("Decided before the throw: " + r);
    }
}
