using UnityEngine;

/// <summary>
/// Smoothly adjusts camera FOV based on player dash state.
/// Sits on the Player GameObject, reads state from PlayerCore.
/// Three FOV values: normal, attack dash, sword dash.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerDashFOV : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("FOV Values")]
    [SerializeField] private float normalFOV = 70f;
    [SerializeField] private float dashFOV = 90f;
    [SerializeField] private float swordDashFOV = 100f;

    [Header("Transition")]
    [Tooltip("How fast the FOV transitions (lower = faster). Acts as SmoothDamp smoothTime.")]
    [SerializeField] private float transitionSpeed = 0.15f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private Camera playerCamera;
    private float targetFOV;
    private float fovVelocity; // used by SmoothDamp

    // External FOV override (e.g. sword throw aim zoom)
    private bool fovOverrideActive;
    private float fovOverrideValue;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera == null)
        {
            Debug.LogError("[PlayerDashFOV] No Camera found on PlayerCore!");
            enabled = false;
            return;
        }

        targetFOV = normalFOV;
        playerCamera.fieldOfView = normalFOV;
    }

    private void Update()
    {
        UpdateTargetFOV();
        ApplyFOV();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region FOV Logic
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateTargetFOV()
    {
        // External override takes priority (e.g. sword throw aim zoom)
        if (fovOverrideActive)
        {
            targetFOV = fovOverrideValue;
            return;
        }

        targetFOV = core.CurrentState switch
        {
            PlayerCore.PlayerState.Dashing        => dashFOV,
            PlayerCore.PlayerState.DashingToSword  => swordDashFOV,
            _                                      => normalFOV
        };
    }

    private void ApplyFOV()
    {
        // Use unscaledDeltaTime because dash uses Time.timeScale slowdown
        playerCamera.fieldOfView = Mathf.SmoothDamp(
            playerCamera.fieldOfView,
            targetFOV,
            ref fovVelocity,
            transitionSpeed,
            Mathf.Infinity,
            Time.unscaledDeltaTime
        );
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API - FOV Override
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set an external FOV override. While active, dash FOV logic is ignored.
    /// Used by sword throw aim zoom.
    /// </summary>
    public void SetFOVOverride(float fov)
    {
        fovOverrideActive = true;
        fovOverrideValue = fov;
    }

    /// <summary>
    /// Clear the external FOV override, returning to normal dash-based FOV logic.
    /// </summary>
    public void ClearFOVOverride()
    {
        fovOverrideActive = false;
    }

    /// <summary>True if an external system is currently overriding the FOV.</summary>
    public bool IsFOVOverridden => fovOverrideActive;

    /// <summary>The normal (non-dash, non-override) FOV value.</summary>
    public float NormalFOV => normalFOV;

    #endregion
}
