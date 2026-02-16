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

    [Header("Walking")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;

    [Header("Sprint Burst")]
    [Tooltip("Initial burst speed when starting to sprint")]
    [SerializeField] private float sprintInitialBoost = 12f;
    [SerializeField] private AnimationCurve sprintDecayCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private float sprintDecayDuration = 1f;
    [SerializeField] private float sprintResetDelay = 1f;

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

    // Sprint tracking
    private float sprintStartTime;
    private float sprintHoldDuration;
    private bool wasSprintingLastFrame;
    private bool sprintDecayActive;

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

        UpdateSprintState();

        bool isSprinting = core.Input.GetAction("Sprint");
        float currentSpeed = isSprinting ? GetCurrentSprintSpeed() : walkSpeed;

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
    #region Sprint Logic
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateSprintState()
    {
        bool isSprinting = core.Input.GetAction("Sprint");

        if (isSprinting)
        {
            if (!wasSprintingLastFrame)
            {
                // Just started sprinting
                if (!sprintDecayActive)
                {
                    sprintStartTime = Time.time;
                    sprintHoldDuration = 0f;
                }
                else
                {
                    // Resume from where we left off
                    sprintStartTime = Time.time - sprintHoldDuration;
                }
            }
            else
            {
                sprintHoldDuration = Time.time - sprintStartTime;

                // Reset decay if held long enough
                if (sprintHoldDuration >= sprintResetDelay)
                {
                    sprintDecayActive = false;
                }
            }
        }
        else if (wasSprintingLastFrame)
        {
            // Just stopped sprinting
            if (sprintHoldDuration < sprintResetDelay)
            {
                sprintDecayActive = true;
            }
        }

        wasSprintingLastFrame = isSprinting;
    }

    private float GetCurrentSprintSpeed()
    {
        float decayProgress = Mathf.Clamp01(sprintHoldDuration / sprintDecayDuration);
        float curveValue = sprintDecayCurve.Evaluate(decayProgress);
        return Mathf.Lerp(runSpeed, sprintInitialBoost, curveValue);
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

    #endregion
}
