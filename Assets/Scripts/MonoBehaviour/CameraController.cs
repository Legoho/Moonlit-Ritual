using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Simple RTS-style camera controller.
/// WASD        - move forward/left/back/right (relative to where the camera is facing)
/// Q / E       - rotate left / right around the vertical axis
/// Shift       - hold to move faster
/// Mouse wheel - zoom in / out
/// Screen edge - move the mouse to the edge of the screen to pan
///
/// Attach to your Camera (or to an empty "rig" object that has the camera as a child).
/// Works with both the old Input Manager and the new Input System.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Units per second.")]
    public float moveSpeed = 10f;

    [Tooltip("Speed multiplier while holding Shift.")]
    public float fastMultiplier = 2.5f;

    [Tooltip("If true, movement stays on the ground plane (good for top-down/RTS cameras). " +
             "If false, W/S move exactly where the camera is looking (fly-cam style).")]
    public bool keepMovementFlat = true;

    [Header("Rotation")]
    [Tooltip("Degrees per second.")]
    public float rotationSpeed = 90f;

    [Header("Zoom (mouse wheel)")]
    [Tooltip("How far one scroll notch moves the camera (perspective) or changes its size (orthographic).")]
    public float zoomStep = 2f;

    [Tooltip("Perspective: lowest height the camera can zoom down to. Orthographic: smallest size.")]
    public float minZoom = 3f;

    [Tooltip("Perspective: highest height the camera can zoom out to. Orthographic: largest size.")]
    public float maxZoom = 40f;

    [Tooltip("Higher = zoom eases in faster.")]
    public float zoomSmoothness = 10f;

    [Header("Edge Panning")]
    public bool enableEdgePan = true;

    [Tooltip("Distance in pixels from the screen edge that triggers panning.")]
    public float edgeSize = 15f;

    [Tooltip("Turn off to stop edge panning while testing in the Unity editor " +
             "(it can be annoying when your mouse leaves the Game view).")]
    public bool edgePanInEditor = true;

    [Header("Cursor Lock")]
    [Tooltip("Keep the mouse inside the game window. Esc releases it, clicking in the game locks it again.")]
    public bool lockCursorToScreen = true;

    [Tooltip("Pause edge panning while the cursor is released (after pressing Esc).")]
    public bool edgePanOnlyWhenLocked = true;

    [Header("Smoothing")]
    [Tooltip("0 = instant, higher = smoother/slower to respond.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    private Camera _cam;
    private Vector3 _currentVelocity;
    private Vector3 _smoothVelocityRef;
    private float _currentRotation;
    private float _rotationVelocityRef;
    private float _pendingZoom; // zoom distance still to apply (eased out over a few frames)

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = GetComponentInChildren<Camera>();
    }

    void Start()
    {
        if (lockCursorToScreen) SetCursorLocked(true);
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // Re-confine when coming back to the game (e.g. after Alt+Tab), unless the player pressed Esc.
        if (hasFocus && lockCursorToScreen && !_releasedByEsc) SetCursorLocked(true);
    }

    void OnDisable()
    {
        SetCursorLocked(false);
    }

    private bool _releasedByEsc;

    private void SetCursorLocked(bool locked)
    {
        // Confined (not Locked) keeps the cursor visible and free to move, so edge panning still works.
        Cursor.lockState = locked ? CursorLockMode.Confined : CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HandleCursorLock(bool escPressed, bool clicked)
    {
        if (!lockCursorToScreen) return;

        if (escPressed)
        {
            _releasedByEsc = true;
            SetCursorLocked(false);
        }
        else if (clicked && _releasedByEsc)
        {
            _releasedByEsc = false;
            SetCursorLocked(true);
        }
    }

    void Update()
    {
        // ---- Read input ----
        float h = 0f, v = 0f, r = 0f, scroll = 0f;
        bool fast = false;
        Vector2 mousePos = Vector2.zero;
        bool hasMouse = false;
        bool escPressed = false, clicked = false;

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
            escPressed = kb.escapeKey.wasPressedThisFrame;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            clicked = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
            // The new Input System reports raw scroll values (often 120 per notch),
            // so reduce it to -1 / 0 / +1 per frame.
            float raw = mouse.scroll.ReadValue().y;
            if (raw != 0f) scroll = Mathf.Sign(raw);
            mousePos = mouse.position.ReadValue();
            hasMouse = true;
        }
#else
        if (Input.GetKey(KeyCode.W)) v += 1f;
        if (Input.GetKey(KeyCode.S)) v -= 1f;
        if (Input.GetKey(KeyCode.D)) h += 1f;
        if (Input.GetKey(KeyCode.A)) h -= 1f;
        if (Input.GetKey(KeyCode.E)) r += 1f;
        if (Input.GetKey(KeyCode.Q)) r -= 1f;
        fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
 
        escPressed = Input.GetKeyDown(KeyCode.Escape);
        clicked = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
 
        scroll = Input.mouseScrollDelta.y;
        mousePos = Input.mousePosition;
        hasMouse = Input.mousePresent;
#endif

        // ---- Cursor lock (Esc releases, click re-locks) ----
        HandleCursorLock(escPressed, clicked);

        // ---- Edge panning ----
        if (enableEdgePan && hasMouse && CanEdgePan(mousePos))
        {
            if (mousePos.x <= edgeSize) h -= 1f;
            else if (mousePos.x >= Screen.width - edgeSize) h += 1f;

            if (mousePos.y <= edgeSize) v -= 1f;
            else if (mousePos.y >= Screen.height - edgeSize) v += 1f;
        }

        h = Mathf.Clamp(h, -1f, 1f); // keys + edge pan together shouldn't double the speed
        v = Mathf.Clamp(v, -1f, 1f);

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

        // ---- Zoom (mouse wheel) ----
        HandleZoom(scroll);
    }

    private void HandleZoom(float scroll)
    {
        if (scroll != 0f) _pendingZoom += scroll * zoomStep;
        if (Mathf.Abs(_pendingZoom) < 0.0001f) { _pendingZoom = 0f; return; }

        // Apply a portion of the remaining zoom this frame for a smooth ease-out.
        float t = zoomSmoothness > 0f ? Mathf.Clamp01(zoomSmoothness * Time.deltaTime) : 1f;
        float step = _pendingZoom * t;
        _pendingZoom -= step;

        if (_cam != null && _cam.orthographic)
        {
            // Orthographic: scrolling up makes the view smaller (zooms in).
            float newSize = Mathf.Clamp(_cam.orthographicSize - step, minZoom, maxZoom);
            if (Mathf.Approximately(newSize, _cam.orthographicSize)) _pendingZoom = 0f;
            _cam.orthographicSize = newSize;
            return;
        }

        // Perspective: move along the camera's view direction, keeping height within limits.
        Vector3 viewDir = _cam != null ? _cam.transform.forward : transform.forward;

        if (Mathf.Abs(viewDir.y) > 0.0001f)
        {
            float targetY = transform.position.y + viewDir.y * step;
            float clampedY = Mathf.Clamp(targetY, minZoom, maxZoom);
            if (!Mathf.Approximately(targetY, clampedY))
            {
                // Shorten the step so we stop exactly at the limit.
                step = (clampedY - transform.position.y) / viewDir.y;
                _pendingZoom = 0f;
            }
        }

        transform.position += viewDir * step;
    }

    private bool CanEdgePan(Vector2 mousePos)
    {
#if UNITY_EDITOR
        if (!edgePanInEditor) return false;
#endif
        if (!Application.isFocused) return false;
        if (lockCursorToScreen && edgePanOnlyWhenLocked && _releasedByEsc) return false;

        // Ignore the mouse when it's outside the game window.
        return mousePos.x >= 0f && mousePos.y >= 0f &&
               mousePos.x <= Screen.width && mousePos.y <= Screen.height;
    }
}
