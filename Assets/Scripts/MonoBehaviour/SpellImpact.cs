using UnityEngine;

/// <summary>
/// Put on the animated spell object (e.g. FX.Spell in the rig) together with a Collider.
/// When the spell touches a monster it hides its renderers, so it doesn't fly on through.
/// The animation keeps running; only the visuals are switched off until the next cast.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SpellImpact : MonoBehaviour
{
    [Tooltip("Renderers to hide on impact. Leave empty to use every renderer under this object.")]
    [SerializeField] private Renderer[] renderers;
    [Tooltip("Optional: an effect spawned where the spell hits (sparks, flash...).")]
    [SerializeField] private GameObject impactEffectPrefab;
    [SerializeField] private float impactEffectLifetime = 2f;
    [Tooltip("On: only the monster being cast at stops the spell; others are flown past. " +
             "Off: the first monster touched stops it.")]
    [SerializeField] private bool onlyStopAtTarget = true;

    private GridUnit target;
    private bool hasHit;

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        // An animated collider needs a kinematic Rigidbody to report trigger hits reliably.
        GetComponent<Collider>().isTrigger = true;
        var body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    /// <summary>Called when a cast starts: shows the spell again and remembers who it is aimed at.</summary>
    public void Arm(GridUnit castTarget)
    {
        target = castTarget;
        hasHit = false;
        SetVisible(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasHit) return;

        var monster = other.GetComponentInParent<MonsterHolder>();
        if (monster == null) return;
        if (onlyStopAtTarget && target != null && monster != target) return;

        hasHit = true;
        SetVisible(false);

        if (impactEffectPrefab != null)
        {
            var fx = Instantiate(impactEffectPrefab, transform.position, Quaternion.identity);
            Destroy(fx, impactEffectLifetime);
        }
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in renderers)
            if (r != null) r.enabled = visible;
    }
}
