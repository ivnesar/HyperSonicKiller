using UnityEngine;
using System;

/// <summary>
/// Handles ground movement: walking, sprinting, jumping, and gravity.
/// Does NOT handle dashing or wall-stick (see PlayerDash).
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerMovement : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action OnJump;
    public event Action OnLanded;
    public event Action OnBecameAirborne;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Walking & Sprint")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;

    [Header("Sprint Burst")]
    [Tooltip("Speed during the burst phase (decays via curve back to runSpeed)")]
    [SerializeField] private float sprintInitialBoost = 12f;
    [Tooltip("Curve shape from 0–1. Y=1 means full boost + full SlowMo, Y=0 means runSpeed + normal time.")]
    [SerializeField] private AnimationCurve sprintDecayCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [Tooltip("How long (in unscaled seconds) the curve takes to play from 0 to 1")]
    [SerializeField] private float burstDuration = 1f;
    [Tooltip("Cooldown (in unscaled seconds) after burst finishes before a new burst can trigger")]
    [SerializeField] private float burstCooldown = 2f;

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float gravity = 20f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private Vector3 moveDirection;
    private float verticalVelocity;

    // Sprint tracking (all timers use unscaledTime so SlowMo doesn't stretch them)
    private bool sprintBurstActive;      // true while the burst curve is playing
    private float burstTimer;            // counts up from 0 to burstDuration (unscaled)
    private bool burstOnCooldown;        // true after burst finishes, until cooldown expires
    private float cooldownTimer;         // counts down from burstCooldown to 0 (unscaled)

    // Ground tracking
    private bool wasGroundedLastFrame;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public float VerticalVelocity
    {
        get => verticalVelocity;
        set => verticalVelocity = value;
    }

    public bool IsGrounded => core.Controller.isGrounded;

    /// <summary>
    /// True while the sprint burst decay curve is active (speed boost + time slow).
    /// Used by PlayerSwordThrow to block throwing during burst.
    /// </summary>
    public bool IsSprintBurstActive => sprintBurstActive;

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
        if (!core.Controller.enabled) return; 

        // Track ground state changes
        CheckGroundStateChange();

        // Only process movement in appropriate states
        if (core.CanMove)
        {
            HandleMovement();
            HandleJump();
        }

        // Always apply gravity (unless stuck to wall)
        if (core.CurrentState != PlayerCore.PlayerState.StuckToSurface &&
            core.CurrentState != PlayerCore.PlayerState.Dashing)
        {
            ApplyGravity();
        }

        ApplyMovement();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Movement Logic
    // ════════════════════════════════════════════════════════════════════════

    private void HandleMovement()
    {
        Vector2 moveInput = core.Input.GetMoveInput();
        Vector3 forward = transform.forward * moveInput.y;
        Vector3 right = transform.right * moveInput.x;
        Vector3 movement = (forward + right).normalized;

        bool isSprinting = core.Input.GetAction("Sprint");
        bool justPressedSprint = core.Input.GetActionDown("Sprint");

        UpdateSprintBurst(justPressedSprint);

        float currentSpeed;
        if (sprintBurstActive)
        {
            currentSpeed = GetBurstSpeed();
        }
        else if (isSprinting)
        {
            currentSpeed = runSpeed;
        }
        else
        {
            currentSpeed = walkSpeed;
        }

        moveDirection = movement * currentSpeed;
    }

    private void HandleJump()
    {
        if (IsGrounded)
        {
            // Small downward force to keep grounded
            verticalVelocity = -1f;

            if (core.Input.GetActionDown("Jump"))
            {
                verticalVelocity = jumpForce;
                OnJump?.Invoke();
            }
        }
    }

    private void ApplyGravity()
    {
        verticalVelocity -= gravity * Time.deltaTime;
    }

    private void ApplyMovement()
    {
        moveDirection.y = verticalVelocity;
        core.Controller.Move(moveDirection * Time.deltaTime);
    }

    private void CheckGroundStateChange()
    {
        bool isGrounded = IsGrounded;

        if (wasGroundedLastFrame && !isGrounded)
        {
            OnBecameAirborne?.Invoke();
        }
        else if (!wasGroundedLastFrame && isGrounded && verticalVelocity <= 0)
        {
            OnLanded?.Invoke();
        }

        wasGroundedLastFrame = isGrounded;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sprint Burst Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Layer name used in TimeManager for sprint burst slow-mo.</summary>
    private const string LAYER_SPRINT_BURST = "SprintBurstSlowMo";

    /// <summary>
    /// Handles the full burst lifecycle: trigger → play curve → cooldown → ready.
    /// Called every frame from HandleMovement.
    /// </summary>
    private void UpdateSprintBurst(bool justPressedSprint)
    {
        // ── Cooldown tick ──
        if (burstOnCooldown)
        {
            cooldownTimer -= Time.unscaledDeltaTime;
            if (cooldownTimer <= 0f)
            {
                burstOnCooldown = false;
            }
        }

        // ── Try to start a new burst ──
        // Requires: fresh press, grounded, not already bursting, not on cooldown
        if (justPressedSprint && IsGrounded && !sprintBurstActive && !burstOnCooldown)
        {
            sprintBurstActive = true;
            burstTimer = 0f;
        }

        // ── Tick the active burst ──
        if (sprintBurstActive)
        {
            burstTimer += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(burstTimer / burstDuration);
            float curveValue = sprintDecayCurve.Evaluate(progress);

            if (progress < 1f)
            {
                // Curve still running — update SlowMo layer
                float dashTimeScale = core.Dash != null ? core.Dash.DashTimeScale : 0.1f;
                float currentTimeScale = Mathf.Lerp(1f, dashTimeScale, curveValue);
                TimeManager.Instance.SetLayer(LAYER_SPRINT_BURST, currentTimeScale, TimeManager.PRIORITY_SLOW_MO, blocksGameTime: false);
            }
            else
            {
                // Curve finished — end burst, start cooldown
                CompleteBurst();
            }
        }
    }

    /// <summary>
    /// Returns the current burst speed based on where the curve is.
    /// </summary>
    private float GetBurstSpeed()
    {
        float progress = Mathf.Clamp01(burstTimer / burstDuration);
        float curveValue = sprintDecayCurve.Evaluate(progress);
        return Mathf.Lerp(runSpeed, sprintInitialBoost, curveValue);
    }

    /// <summary>
    /// Ends the burst cleanly: removes SlowMo layer, starts cooldown.
    /// </summary>
    private void CompleteBurst()
    {
        sprintBurstActive = false;
        burstTimer = 0f;
        TimeManager.Instance.RemoveLayer(LAYER_SPRINT_BURST);

        burstOnCooldown = true;
        cooldownTimer = burstCooldown;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply an instant vertical impulse (used by dash cancel, knockback, etc.)
    /// </summary>
    public void ApplyVerticalImpulse(float force)
    {
        verticalVelocity = force;
    }

    /// <summary>
    /// Stop all horizontal movement immediately.
    /// </summary>
    public void StopHorizontalMovement()
    {
        moveDirection = Vector3.zero;
    }

    /// <summary>
    /// Cancels the sprint burst immediately (removes SlowMo layer, starts cooldown).
    /// Called by PlayerCore when a dash starts.
    /// </summary>
    public void CancelSprintBurst()
    {
        if (!sprintBurstActive) return;

        CompleteBurst();
    }

    #endregion
}
