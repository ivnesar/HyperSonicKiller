using UnityEngine;

/// <summary>
/// Handles camera/look rotation for first-person view.
/// Simple and focused - just mouse look.
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

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private float currentVerticalAngle;

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
        if (core.IsDead)
        {
            HandleDeathCamera();
            return;
        }

        HandleLook();
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

        rotationTarget.localEulerAngles = new Vector3(currentVerticalAngle, 0f, 0f); //core.CameraTransform
    }

    private void HandleDeathCamera()
    {
        // Spin camera on death for dramatic effect
        rotationTarget.Rotate(Vector3.forward * deathRotationSpeed * Time.deltaTime); //core.CameraTransform
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

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
    /// Snap look direction to face a world position.
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
        rotationTarget.localEulerAngles = new Vector3(currentVerticalAngle, 0f, 0f); //core.CameraTransform
    }

    #endregion
}
