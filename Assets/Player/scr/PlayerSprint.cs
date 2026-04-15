using UnityEngine;
using System;

/// <summary>
/// Handles the hybrid Sprint system:
///   1. Sprint-Dash: Short, fast dodge in WASD direction (on Shift press, grounded only)
///   2. Sprint: Increased move speed while Shift is held (after dash or during cooldown)
///
/// Sprint-Dash is NOT cancellable and blocks other actions for its duration.
/// Sprint has no cooldown and activates immediately when Shift is held.
///
/// Sits on the Player GameObject alongside other subsystems.
/// Moves the player via CharacterController.Move() during Sprint-Dash.
/// Sets IsSprinting flag for PlayerMovement to read during normal Sprint.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerSprint : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Fired when the sprint-dash begins.</summary>
    public event Action OnSprintDashStarted;

    /// <summary>Fired when the sprint-dash ends.</summary>
    public event Action OnSprintDashCompleted;

    /// <summary>Fired when normal sprint begins (Shift held, not dashing).</summary>
    public event Action OnSprintStarted;

    /// <summary>Fired when normal sprint ends (Shift released or state change).</summary>
    public event Action OnSprintStopped;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Sprint-Dash")]
    [Tooltip("Duration of the sprint-dash in seconds")]
    [SerializeField] private float dashDuration = 0.15f;

    [Tooltip("Speed during the sprint-dash (meters/sec)")]
    [SerializeField] private float dashSpeed = 25f;

    [Tooltip("Cooldown after a sprint-dash before the next one can trigger (seconds)")]
    [SerializeField] private float dashCooldown = 1.5f;

    [Header("Sprint")]
    [Tooltip("Movement speed while sprinting (replaces runSpeed in PlayerMovement)")]
    [SerializeField] private float sprintSpeed = 10f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;

    // Sprint-Dash state
    private bool isDashing;
    private float dashTimer;
    private Vector3 dashDirection;

    // Cooldown
    private bool dashOnCooldown;
    private float cooldownTimer;

    // Sprint state
    private bool isSprinting;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>True while the sprint-dash is active.</summary>
    public bool IsDashing => isDashing;

    /// <summary>True while the player is sprinting (Shift held, not dashing).</summary>
    public bool IsSprinting => isSprinting;

    /// <summary>Sprint movement speed for PlayerMovement to use.</summary>
    public float SprintSpeed => sprintSpeed;

    /// <summary>True if the sprint-dash is on cooldown.</summary>
    public bool IsDashOnCooldown => dashOnCooldown;

    /// <summary>Remaining cooldown time (0 if ready).</summary>
    public float CooldownRemaining => dashOnCooldown ? cooldownTimer : 0f;

    /// <summary>The dash cooldown duration (for UI/debug).</summary>
    public float DashCooldownDuration => dashCooldown;

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
        if (core.IsDead) return;

        UpdateCooldown();

        if (isDashing)
        {
            ProcessDashMovement();
        }
        else
        {
            HandleInput();
            UpdateSprintState();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Input Handling
    // ════════════════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        // Sprint-Dash: Shift press, grounded, not on cooldown, in a valid state
        if (core.Input.GetActionDown("Sprint") && CanStartDash())
        {
            TryStartDash();
        }
    }

    private bool CanStartDash()
    {
        // Must be grounded
        if (!core.Movement.IsGrounded) return false;

        // Must not be on cooldown
        if (dashOnCooldown) return false;

        // Must be in a state that allows sprint-dashing
        if (core.CurrentState != PlayerCore.PlayerState.Normal) return false;

        // Need a movement direction (WASD input)
        Vector2 moveInput = core.Input.GetMoveInput();
        if (moveInput.sqrMagnitude < 0.01f) return false;

        return true;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sprint-Dash Logic
    // ════════════════════════════════════════════════════════════════════════

    private void TryStartDash()
    {
        // Calculate dash direction from WASD input (relative to player facing)
        Vector2 moveInput = core.Input.GetMoveInput();
        Vector3 forward = transform.forward * moveInput.y;
        Vector3 right = transform.right * moveInput.x;
        dashDirection = (forward + right).normalized;

        if (dashDirection.sqrMagnitude < 0.01f) return;

        // Start the dash
        isDashing = true;
        dashTimer = 0f;

        core.SetState(PlayerCore.PlayerState.SprintDashing);
        OnSprintDashStarted?.Invoke();
    }

    private void ProcessDashMovement()
    {
        dashTimer += Time.deltaTime;

        if (dashTimer >= dashDuration)
        {
            // Dash finished
            CompleteDash();
            return;
        }

        // Move the player in the dash direction
        // Gravity is still applied by keeping the Y component
        Vector3 move = dashDirection * dashSpeed * Time.deltaTime;

        // Apply a small downward force to keep grounded
        move.y = -1f * Time.deltaTime;

        core.Controller.Move(move);
    }

    private void CompleteDash()
    {
        isDashing = false;
        dashTimer = 0f;

        // Start cooldown
        dashOnCooldown = true;
        cooldownTimer = dashCooldown;

        // Return to Normal state
        if (core.Controller.isGrounded)
        {
            core.SetState(PlayerCore.PlayerState.Normal);
        }
        else
        {
            core.SetState(PlayerCore.PlayerState.Airborne);
        }

        OnSprintDashCompleted?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sprint Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates the sprint state based on Shift being held.
    /// Sprint is independent of the dash — it works anytime Shift is held
    /// and the player is in a movable state.
    /// </summary>
    private void UpdateSprintState()
    {
        bool shiftHeld = core.Input.GetAction("Sprint");
        bool canSprint = core.CanMove && core.Movement.IsGrounded;

        bool shouldSprint = shiftHeld && canSprint;

        if (shouldSprint && !isSprinting)
        {
            isSprinting = true;
            OnSprintStarted?.Invoke();
        }
        else if (!shouldSprint && isSprinting)
        {
            isSprinting = false;
            OnSprintStopped?.Invoke();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cooldown
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateCooldown()
    {
        if (!dashOnCooldown) return;

        cooldownTimer -= Time.deltaTime;
        if (cooldownTimer <= 0f)
        {
            dashOnCooldown = false;
            cooldownTimer = 0f;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Force-cancel the sprint-dash (e.g. on death).
    /// </summary>
    public void ForceCancelDash()
    {
        if (!isDashing) return;

        isDashing = false;
        dashTimer = 0f;

        // Don't start cooldown on force cancel
        OnSprintDashCompleted?.Invoke();
    }

    /// <summary>
    /// Force-stop sprinting (e.g. when entering a state that doesn't allow it).
    /// </summary>
    public void StopSprint()
    {
        if (!isSprinting) return;

        isSprinting = false;
        OnSprintStopped?.Invoke();
    }

    /// <summary>
    /// Reset the cooldown (e.g. for debug or special abilities).
    /// </summary>
    public void ResetCooldown()
    {
        dashOnCooldown = false;
        cooldownTimer = 0f;
    }

    #endregion
}
