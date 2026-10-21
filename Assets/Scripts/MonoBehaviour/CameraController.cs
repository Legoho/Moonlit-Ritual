using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Units per second.")]
    public float moveSpeed = 10f;

    [Tooltip("Speed multiplier while holding Shift.")]
    public float fastMultiplier = 2.5f;

    [Tooltip("If true, W/S move along the ground plane (good for top-down/RTS cameras). " +
             "If false, W/S move exactly where the camera is looking (fly-cam style).")]
    public bool keepMovementFlat = true;

    [Header("Rotation")]
    [Tooltip("Degrees per second.")]
    public float rotationSpeed = 90f;

    [Header("Smoothing")]
    [Tooltip("0 = instant, higher = smoother/slower to respond.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    private Vector3 _currentVelocity;
    private Vector3 _smoothVelocityRef;
    private float _currentRotation;
    private float _rotationVelocityRef;

    void Update()
    {
        // ---- Read input ----
        float h = 0f, v = 0f, r = 0f;
        bool fast = false;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed) v += 1f;
            if (kb.sKey.isPressed) v -= 1f;
            if (kb.dKey.isPressed) h += 1f;
            if (kb.aKey.isPressed) h -= 1f;
            if (kb.eKey.isPressed) r += 1f;
            if (kb.qKey.isPressed) r -= 1f;
            fast = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }
#else
        if (Input.GetKey(KeyCode.W)) v += 1f;
        if (Input.GetKey(KeyCode.S)) v -= 1f;
        if (Input.GetKey(KeyCode.D)) h += 1f;
        if (Input.GetKey(KeyCode.A)) h -= 1f;
        if (Input.GetKey(KeyCode.E)) r += 1f;
        if (Input.GetKey(KeyCode.Q)) r -= 1f;
        fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif

        // ---- Movement ----
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        if (keepMovementFlat)
        {
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        Vector3 direction = forward * v + right * h;
        if (direction.sqrMagnitude > 1f) direction.Normalize(); // no faster diagonal movement

        float speed = moveSpeed * (fast ? fastMultiplier : 1f);
        Vector3 targetVelocity = direction * speed;

        _currentVelocity = smoothTime > 0f
            ? Vector3.SmoothDamp(_currentVelocity, targetVelocity, ref _smoothVelocityRef, smoothTime)
            : targetVelocity;

        transform.position += _currentVelocity * Time.deltaTime;

        // ---- Rotation (Q = left, E = right) ----
        float targetRotation = r * rotationSpeed;

        _currentRotation = smoothTime > 0f
            ? Mathf.SmoothDamp(_currentRotation, targetRotation, ref _rotationVelocityRef, smoothTime)
            : targetRotation;

        // Rotate around world up so the camera never rolls/tilts.
        transform.Rotate(Vector3.up, _currentRotation * Time.deltaTime, Space.World);
    }
}
