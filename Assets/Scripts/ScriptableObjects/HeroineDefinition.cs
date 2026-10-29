using UnityEngine;

/// <summary>
/// One playable magical girl: what the run stores when the player picks her,
/// and what the combat scene needs to spawn her.
/// </summary>
[CreateAssetMenu(fileName = "Heroine", menuName = "Scriptable Objects/Heroine")]
public class HeroineDefinition : ScriptableObject
{
    public string displayName = "Heroine";
    [Tooltip("e.g. The Cobalt Warden")]
    public string title;
    [Tooltip("Her combat prefab: the model with the Heroine component, Animator, colliders and FX.")]
    public Heroine prefab;
    [Tooltip("Her four dice pools. Overrides the Grimoire on the prefab if set.")]
    public Grimoire grimoire;
    [Tooltip("Shown in the party panel (RawImage). A Texture2D or a RenderTexture both work.")]
    public Texture portrait;

    /// <summary>Copies this definition onto a spawned heroine.</summary>
    public void ApplyTo(Heroine h)
    {
        if (h == null) return;
        if (!string.IsNullOrEmpty(displayName)) h.heroineName = displayName;
        if (grimoire != null) h.grimoire = grimoire;
        if (portrait != null) h.portrait = portrait;
    }
}
