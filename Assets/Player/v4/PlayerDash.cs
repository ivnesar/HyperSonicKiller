using UnityEngine;
using System;

/// <summary>
/// Handles dash movement and wall-stick mechanics.
/// Communicates with PlayerCore for state changes.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerDash : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action OnDashStarted;
    public event Action<bool> OnDashCompleted;  // bool = hit surface
    public event Action OnWallStick;
    public event Action OnUnstick;
    public event Action<int> OnChargesChanged;  // remaining charges

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Dash Settings")]
    [SerializeField] private int maxDashCharges = 3;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashMaxDistance = 15f;
    [SerializeField] private LayerMask dashSurfaceLayer = -1;

    [Header("Time Slow During Dash")]
    [SerializeField] private float dashTimeScale = 0.1f;

    [Header("Dash Cancel Forces")]
    [SerializeField] private float dashCancelUpwardForce = 10f;
    [SerializeField] private float dashCancelDownwardForce = 15f;

    [Header("Wall Stick")]
    [SerializeField] private float wallStickCheckDistance = 1f;
    [SerializeField] private float wallStickOffset = 0.5f;  // Distance from wall surface

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;

    // Dash state
    private int currentCharges;
    private Vector3 dashStartPosition;
    private Vector3 dashTargetPosition;
    private Vector3 dashDirection;
    private float dashProgress;

    // Wall stick state
    private Vector3 stuckPosition;
    private Vector3 stuckSurfaceNormal;
    private bool isWallStickActive;

    // Flags
    private bool dashDisabled;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public int CurrentCharges => currentCharges;
    public int MaxCharges => maxDashCharges;
    public bool IsDashing => core.CurrentState == PlayerCore.PlayerState.Dashing;
    public bool IsStuck => core.CurrentState == PlayerCore.PlayerState.StuckToSurface;
    public Vector3 StuckSurfaceNormal => stuckSurfaceNormal;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        currentCharges = maxDashCharges;
    }

    private void Update()
    {
        if (core.IsDead) return;

        switch (core.CurrentState)
        {
            case PlayerCore.PlayerState.Normal:
            case PlayerCore.PlayerState.Airborne:
                HandleDashInput();
                break;

            case PlayerCore.PlayerState.Dashing:
                ProcessDashMovement();
                CheckDashCancels();
                break;

            case PlayerCore.PlayerState.StuckToSurface:
                MaintainWallStick();
                CheckUnstickInput();
                HandleDashInput();  // Can dash from wall
                break;
        }
    }

    private void LateUpdate()
    {
        // FIXED: Force position in LateUpdate to override any physics
        if (isWallStickActive && core.CurrentState == PlayerCore.PlayerState.StuckToSurface)
        {
            transform.position = stuckPosition;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Logic
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDashInput()
    {
        if (dashDisabled) return;
        if (!core.CanDash) return;
        if (currentCharges <= 0) return;

        if (core.Input.GetActionDown("Dash"))
        {
            TryStartDash();
        }
    }

    private void TryStartDash()
    {
        // Raycast from camera to find dash target
        if (Physics.Raycast(
            core.CameraTransform.position,
            core.CameraTransform.forward,
            out RaycastHit hit,
            dashMaxDistance,
            dashSurfaceLayer))
        {
            // Don't dash to current surface
            if (!IsCurrentSurface(hit))
            {
                StartDash(hit.point, hit.normal);
            }
        }
    }

    private void StartDash(Vector3 targetPoint, Vector3 surfaceNormal)
    {
        // FIXED: Deactivate wall stick when starting new dash
        DeactivateWallStick();

        currentCharges--;
        OnChargesChanged?.Invoke(currentCharges);

        dashStartPosition = transform.position;
        
        // FIXED: Calculate proper target position (offset from wall)
        dashTargetPosition = targetPoint + surfaceNormal * wallStickOffset;
        dashDirection = (dashTargetPosition - dashStartPosition).normalized;
        dashProgress = 0f;
        stuckSurfaceNormal = surfaceNormal;

        // Slow time during dash
        Time.timeScale = dashTimeScale;

        // Notify core to change state
        core.SetState(PlayerCore.PlayerState.Dashing);
        
        OnDashStarted?.Invoke();
    }

    private void ProcessDashMovement()
    {
        float dashDistance = Vector3.Distance(dashStartPosition, dashTargetPosition);
        
        // Prevent division by zero
        if (dashDistance < 0.01f)
        {
            CompleteDash(hitSurface: true);
            return;
        }
        
        float moveDistance = dashSpeed * Time.unscaledDeltaTime;  // Use unscaled for slow-mo

        dashProgress += moveDistance / dashDistance;

        if (dashProgress >= 1f)
        {
            // Reached target
            Vector3 finalMove = dashTargetPosition - transform.position;
            core.Controller.Move(finalMove);
            CompleteDash(hitSurface: true);
        }
        else
        {
            core.Controller.Move(dashDirection * moveDistance);
        }
    }

    private void CheckDashCancels()
    {
        // Redirect dash to new target
        if (core.Input.GetActionDown("Dash") && !dashDisabled && currentCharges > 0)
        {
            if (Physics.Raycast(
                core.CameraTransform.position,
                core.CameraTransform.forward,
                out RaycastHit hit,
                dashMaxDistance,
                dashSurfaceLayer))
            {
                if (!IsCurrentSurface(hit))
                {
                    // Reset time scale before starting new dash
                    Time.timeScale = 1f;
                    StartDash(hit.point, hit.normal);
                    return;
                }
            }
        }

        // Jump cancel (upward)
        if (core.Input.GetActionDown("Jump"))
        {
            CancelDash(dashCancelUpwardForce);
        }
        // Downward cancel
        else if (core.Input.GetActionDown("DashDown"))
        {
            CancelDash(-dashCancelDownwardForce);
        }
    }

    private void CompleteDash(bool hitSurface)
    {
        Time.timeScale = 1f;
        dashProgress = 0f;

        // FIXED: Set up wall stick BEFORE changing state
        if (hitSurface && !core.Controller.isGrounded)
        {
            ActivateWallStick(transform.position);
        }

        OnDashCompleted?.Invoke(hitSurface);
    }

    private void CancelDash(float verticalForce)
    {
        Time.timeScale = 1f;
        dashProgress = 0f;
        
        // FIXED: Make sure wall stick is deactivated
        DeactivateWallStick();

        core.Movement?.ApplyVerticalImpulse(verticalForce);
        core.SetState(PlayerCore.PlayerState.Airborne);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Wall Stick Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Activates wall stick at the specified position.
    /// </summary>
    private void ActivateWallStick(Vector3 position)
    {
        stuckPosition = position;
        isWallStickActive = true;
        
        // Disable CharacterController to prevent physics interference
        if (core.Controller != null)
        {
            core.Controller.enabled = false;
        }
        
        // Force position immediately
        transform.position = stuckPosition;
        
        OnWallStick?.Invoke();
        
        Debug.Log($"[PlayerDash] Wall stick activated at {stuckPosition}");
    }

    /// <summary>
    /// Deactivates wall stick and re-enables physics.
    /// </summary>
    private void DeactivateWallStick()
    {
        if (!isWallStickActive) return;
        
        isWallStickActive = false;
        
        // Re-enable CharacterController
        if (core.Controller != null)
        {
            core.Controller.enabled = true;
        }
        
        Debug.Log("[PlayerDash] Wall stick deactivated");
    }

    private void MaintainWallStick()
    {
        // FIXED: Force position every frame while stuck
        if (isWallStickActive)
        {
            transform.position = stuckPosition;
        }
    }

    private void CheckUnstickInput()
    {
        // Jump off wall
        if (core.Input.GetActionDown("Jump"))
        {
            Unstick(dashCancelUpwardForce);
        }
        // Drop down
        else if (core.Input.GetActionDown("DashDown"))
        {
            Unstick(-dashCancelDownwardForce);
        }
    }

    private void Unstick(float verticalForce)
    {
        // FIXED: Properly deactivate wall stick before applying force
        DeactivateWallStick();
        
        core.Movement?.ApplyVerticalImpulse(verticalForce);
        OnUnstick?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helper Methods
    // ════════════════════════════════════════════════════════════════════════

    private bool IsCurrentSurface(RaycastHit hit)
    {
        // Check if already stuck to this surface
        if (IsStuck && isWallStickActive)
        {
            if (Physics.Raycast(
                stuckPosition,
                -stuckSurfaceNormal,
                out RaycastHit surfaceHit,
                wallStickCheckDistance + 0.5f,
                dashSurfaceLayer))
            {
                if (surfaceHit.collider == hit.collider)
                    return true;
            }
        }

        // Check if standing on this surface
        if (core.Controller.enabled && core.Controller.isGrounded && !IsStuck)
        {
            if (Physics.Raycast(
                transform.position,
                Vector3.down,
                out RaycastHit groundHit,
                core.Controller.height / 2 + 0.2f,
                dashSurfaceLayer))
            {
                if (groundHit.collider == hit.collider)
                    return true;
            }
        }

        return false;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reset dash charges to max (called when landing or sticking to wall).
    /// </summary>
    public void ResetCharges()
    {
        currentCharges = maxDashCharges;
        OnChargesChanged?.Invoke(currentCharges);
    }

    /// <summary>
    /// Enable or disable dashing (e.g., during guard break).
    /// </summary>
    public void SetDashEnabled(bool enabled)
    {
        dashDisabled = !enabled;
    }

    /// <summary>
    /// Force cancel current dash with fall velocity.
    /// </summary>
    public void ForceCancelDash()
    {
        if (IsDashing)
        {
            CancelDash(-5f);
        }
        else if (IsStuck)
        {
            // Also handle being stuck to wall
            Unstick(-5f);
            core.SetState(PlayerCore.PlayerState.Airborne);
        }
    }

    /// <summary>
    /// Check if wall stick is currently active.
    /// </summary>
    public bool IsWallStickActive => isWallStickActive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    private void OnDisable()
    {
        // Make sure to re-enable controller if we get disabled
        if (isWallStickActive && core != null && core.Controller != null)
        {
            core.Controller.enabled = true;
            isWallStickActive = false;
        }
        
        // Reset time scale if we were mid-dash
        if (Time.timeScale != 1f)
        {
            Time.timeScale = 1f;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        if (IsDashing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(dashStartPosition, dashTargetPosition);
            Gizmos.DrawWireSphere(dashTargetPosition, 0.5f);
        }

        if (isWallStickActive)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(stuckPosition, 0.3f);
            Gizmos.DrawLine(stuckPosition, stuckPosition + stuckSurfaceNormal * 2f);
            
            // Show stuck indicator
            Gizmos.color = Color.green;
            Gizmos.DrawCube(stuckPosition + Vector3.up * 2f, Vector3.one * 0.2f);
        }
    }

    #endregion
}