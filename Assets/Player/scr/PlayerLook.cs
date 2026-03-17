using UnityEngine;

/// <summary>
/// Handles camera/look rotation for first-person view.
/// Includes smooth camera snap to enemy targets on hit during dash.
/// 
/// SNAP SYSTEM:
///   - Triggered externally via SnapToTarget(Transform)
///   - Three phases: Transition (smooth lerp to target), Hold (camera sticks to target), Release
///   - During Transition + Hold: mouse input is ignored
///   - Uses unscaledDeltaTime so snap runs DURING HitStop (intentional — snap while frozen)
///   - A new snap cancels any active snap
///   - CancelSnap() immediately returns control to the player
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerLook : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Look Settings")] 
    [SerializeField] private Transform rotationTarget;
    [SerializeField] private float sensitivity = 2f;
    [SerializeField] private float maxVerticalAngle = 80f;

    [Header("Death Camera Effect")]
    [SerializeField] private float deathRotationSpeed = 30f;

    [Header("Camera Snap")]
    [Tooltip("Dauer der Transition zum Ziel (in Sekunden, Echtzeit)")]
    [SerializeField] private float snapTransitionDuration = 0.05f;

    [Tooltip("Dauer in der die Kamera am Ziel haftet (in Sekunden, Echtzeit)")]
    [SerializeField] private float snapHoldDuration = 0.1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Snap State
    // ════════════════════════════════════════════════════════════════════════

    private enum SnapPhase
    {
        Inactive,
        Transition,
        Hold
    }

    private SnapPhase snapPhase = SnapPhase.Inactive;
    private Transform snapTarget;
    private float snapTimer;

    // Rotation beim Start der Transition (für Lerp)
    private Quaternion snapStartBodyRotation;
    private float snapStartVerticalAngle;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private float currentVerticalAngle;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>True wenn gerade ein Kamera-Snap aktiv ist (Transition oder Hold).</summary>
    public bool IsSnapping => snapPhase != SnapPhase.Inactive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
    }

    private void Update()
    {
        // Bei Pause kein Umsehen und kein Snap
        if (TimeManager.Instance.IsPaused) return;

        if (core.IsDead)
        {
            CancelSnap();
            HandleDeathCamera();
            return;
        }

        if (snapPhase != SnapPhase.Inactive)
        {
            UpdateSnap();
        }
        else
        {
            HandleLook();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Look Logic
    // ════════════════════════════════════════════════════════════════════════

    private void HandleLook()
    {
        Vector2 lookInput = core.Input.GetLookInput();

        // Horizontal rotation (rotate player body)
        transform.Rotate(Vector3.up * lookInput.x * sensitivity);

        // Vertical rotation (rotate camera only)
        currentVerticalAngle -= lookInput.y * sensitivity;
        currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, -maxVerticalAngle, maxVerticalAngle);

        rotationTarget.localEulerAngles = new Vector3(currentVerticalAngle, 0f, 0f);
    }

    private void HandleDeathCamera()
    {
        // Spin camera on death for dramatic effect
        rotationTarget.Rotate(Vector3.forward * deathRotationSpeed * Time.deltaTime);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Camera Snap
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateSnap()
    {
        // Target zerstört oder null → sofort abbrechen
        if (snapTarget == null)
        {
            CancelSnap();
            return;
        }

        // unscaledDeltaTime: Snap läuft auch während HitStop weiter
        snapTimer += Time.unscaledDeltaTime;

        switch (snapPhase)
        {
            case SnapPhase.Transition:
                UpdateSnapTransition();
                break;

            case SnapPhase.Hold:
                UpdateSnapHold();
                break;
        }
    }

    private void UpdateSnapTransition()
    {
        float t = snapTransitionDuration > 0f
            ? Mathf.Clamp01(snapTimer / snapTransitionDuration)
            : 1f;

        // Smooth-Step für weicheren Übergang
        float smoothT = t * t * (3f - 2f * t);

        ApplySnapRotation(smoothT);

        // Transition abgeschlossen → Hold-Phase starten
        if (t >= 1f)
        {
            snapPhase = SnapPhase.Hold;
            snapTimer = 0f;
        }
    }

    private void UpdateSnapHold()
    {
        // Während Hold: Kamera bleibt am Target (verfolgt es live)
        ApplySnapRotation(1f);

        if (snapTimer >= snapHoldDuration)
        {
            EndSnap();
        }
    }

    /// <summary>
    /// Berechnet die Ziel-Rotation zum Snap-Target und wendet sie an.
    /// t = 0 → Start-Rotation, t = 1 → exakt auf Target gerichtet.
    /// </summary>
    private void ApplySnapRotation(float t)
    {
        Vector3 targetPos = snapTarget.position;
        Vector3 direction = (targetPos - core.CameraTransform.position).normalized;

        // ── Ziel: Horizontal (Body-Rotation) ──
        Vector3 horizontalDir = new Vector3(direction.x, 0f, direction.z).normalized;

        if (horizontalDir.sqrMagnitude < 0.001f)
        {
            // Target ist direkt über/unter uns — keine horizontale Rotation
            return;
        }

        Quaternion targetBodyRotation = Quaternion.LookRotation(horizontalDir);

        // ── Ziel: Vertikal (Camera Pitch) ──
        float targetVerticalAngle = -Mathf.Asin(direction.y) * Mathf.Rad2Deg;
        targetVerticalAngle = Mathf.Clamp(targetVerticalAngle, -maxVerticalAngle, maxVerticalAngle);

        // ── Interpolieren ──
        transform.rotation = Quaternion.Slerp(snapStartBodyRotation, targetBodyRotation, t);
        float interpolatedAngle = Mathf.Lerp(snapStartVerticalAngle, targetVerticalAngle, t);

        currentVerticalAngle = interpolatedAngle;
        rotationTarget.localEulerAngles = new Vector3(currentVerticalAngle, 0f, 0f);
    }

    /// <summary>
    /// Beendet den Snap sauber — currentVerticalAngle ist schon aktuell,
    /// der Spieler kann nahtlos weiter die Maus bewegen.
    /// </summary>
    private void EndSnap()
    {
        snapPhase = SnapPhase.Inactive;
        snapTarget = null;
        snapTimer = 0f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Startet einen Kamera-Snap zum angegebenen Transform.
    /// Wenn bereits ein Snap aktiv ist, wird er abgebrochen und der neue gestartet.
    /// </summary>
    /// <param name="target">Ziel-Transform (z.B. Head-Bone des NPCs)</param>
    public void SnapToTarget(Transform target)
    {
        if (target == null) return;

        // Aktuelle Rotation als Startpunkt speichern
        snapStartBodyRotation = transform.rotation;
        snapStartVerticalAngle = currentVerticalAngle;

        snapTarget = target;
        snapTimer = 0f;
        snapPhase = SnapPhase.Transition;
    }

    /// <summary>
    /// Bricht einen aktiven Snap sofort ab.
    /// Der Spieler kann danach sofort die Kamera wieder frei bewegen.
    /// </summary>
    public void CancelSnap()
    {
        if (snapPhase == SnapPhase.Inactive) return;

        EndSnap();
    }

    /// <summary>
    /// Change sensitivity at runtime (for settings menu).
    /// </summary>
    public void SetSensitivity(float newSensitivity)
    {
        sensitivity = Mathf.Max(0.1f, newSensitivity);
    }

    /// <summary>
    /// Get current sensitivity.
    /// </summary>
    public float GetSensitivity() => sensitivity;

    /// <summary>
    /// Snap look direction to face a world position (instant, no animation).
    /// </summary>
    public void LookAt(Vector3 worldPosition)
    {
        Vector3 direction = (worldPosition - transform.position).normalized;
        
        // Horizontal
        Vector3 horizontalDir = new Vector3(direction.x, 0, direction.z).normalized;
        transform.forward = horizontalDir;

        // Vertical
        currentVerticalAngle = -Mathf.Asin(direction.y) * Mathf.Rad2Deg;
        currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, -maxVerticalAngle, maxVerticalAngle);
        rotationTarget.localEulerAngles = new Vector3(currentVerticalAngle, 0f, 0f);
    }

    #endregion
}
