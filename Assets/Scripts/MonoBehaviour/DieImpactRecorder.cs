using System;
using UnityEngine;

/// <summary>
/// Added to each die during the hidden pre-simulation so the visualizer knows
/// when the dice hit something and can play click/clack sounds at the right moment of the replay.
/// </summary>
public class DieImpactRecorder : MonoBehaviour
{
    public Action<float> onImpact;

    private void OnCollisionEnter(Collision collision)
    {
        onImpact?.Invoke(collision.impulse.magnitude);
    }
}
