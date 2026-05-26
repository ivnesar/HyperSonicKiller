using UnityEngine;
using System;

/// <summary>
/// Handles ground movement: walking, sprinting, jumping, and gravity.
/// Does NOT handle dashing or wall-stick (see PlayerDash).
/// Does NOT handle sprint-dash (see PlayerSprint).
///
/// Sprint speed is controlled by PlayerSprint.IsSprinting — this script
/// simply reads that flag and adjusts movement speed accordingly.
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

        // Always apply gravity (unless stuck to wall or dashing)
        if (core.CurrentState != PlayerCore.PlayerState.StuckToSurface &&
            core.CurrentState != PlayerCore.PlayerState.Dashing &&
            core.CurrentState != PlayerCore.PlayerState.SprintDashing)
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

        // Sprint speed comes from PlayerSprint
        bool isSprinting = core.Sprint != null && core.Sprint.IsSprinting;
        float currentSpeed = isSprinting ? core.Sprint.SprintSpeed : walkSpeed;

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
        core.MovePlayer(moveDirection * Time.deltaTime);
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
