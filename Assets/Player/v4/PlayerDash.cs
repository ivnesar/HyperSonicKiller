using UnityEngine;
using System;

/// <summary>
/// Handles dash movement and wall-stick mechanics.
/// Communicates with PlayerCore for state changes.
/// 
/// NEW: Added sword dash - dash to stuck sword to retrieve it (invulnerable during dash).
/// FIXED: Floor vs Wall detection - only walls trigger stick, not floors.
/// FIXED: Can now dash to nearby floor positions.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerDash : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action OnDashStarted;
    public event Action<bool, bool> OnDashCompleted;  // (hitSurface, hitWall)
    public event Action OnWallStick;
    public event Action OnUnstick;
    public event Action<int> OnChargesChanged;  // remaining charges
    
    // NEW: Sword dash events
    public event Action OnSwordDashStarted;
    public event Action OnSwordDashCompleted;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Dash Settings")]
    [SerializeField] private int maxDashCharges = 3;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashMaxDistance = 15f;
    [SerializeField] private LayerMask dashSurfaceLayer = -1;
    
    [Header("Surface Detection")]
    [Tooltip("Maximum angle from vertical (Y-up) for a surface to be considered a floor. 0 = only perfectly flat, 45 = gentle slopes")]
    [SerializeField] private float maxFloorAngle = 45f;

    [Header("Time Slow During Dash")]
    [SerializeField] private float dashTimeScale = 0.1f;

    [Header("Dash Cancel Forces")]
    [SerializeField] private float dashCancelUpwardForce = 10f;
    [SerializeField] private float dashCancelDownwardForce = 15f;

    [Header("Wall Stick")]
    [SerializeField] private float wallStickCheckDistance = 1f;
    [SerializeField] private float wallStickOffset = 0.5f;  // Distance from wall surface
    
    [Header("Sword Dash (NEW)")]
    [Tooltip("Speed when dashing to retrieve the thrown sword")]
    [SerializeField] private float swordDashSpeed = 40f;
    
    [Tooltip("How close the player needs to be to 'catch' the sword")]
    [SerializeField] private float swordCatchDistance = 1.5f;
    
    [Tooltip("Damage dealt to enemy when retrieving sword via dash (on top of normal removal damage)")]
    [SerializeField] private int swordDashDamage = 50;
    
    [Tooltip("Time to smoothly rotate towards the sword during dash (in seconds)")]
    [SerializeField] private float swordDashRotationDuration = 0.2f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private PlayerSwordThrow swordThrow;

    // Dash state
    private int currentCharges;
    private Vector3 dashStartPosition;
    private Vector3 dashTargetPosition;
    private Vector3 dashDirection;
    private float dashProgress;
    
    // FIXED: Track if dash target is a wall or floor
    private bool dashTargetIsWall;

    // Wall stick state
    private Vector3 stuckPosition;
    private Vector3 stuckSurfaceNormal;
    private bool isWallStickActive;

    // Flags
    private bool dashDisabled;
    
    // NEW: Sword dash state
    private bool isSwordDashing;
    private Transform swordDashTarget;
    private Quaternion swordDashStartRotation;
    private float swordDashRotationTimer;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public int CurrentCharges => currentCharges;
    public int MaxCharges => maxDashCharges;
    public bool IsDashing => core.CurrentState == PlayerCore.PlayerState.Dashing;
    public bool IsStuck => core.CurrentState == PlayerCore.PlayerState.StuckToSurface;
    public Vector3 StuckSurfaceNormal => stuckSurfaceNormal;
    
    /// <summary>Returns true if currently dashing to sword.</summary>
    public bool IsSwordDashing => isSwordDashing;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        swordThrow = GetComponent<PlayerSwordThrow>();
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
                
            case PlayerCore.PlayerState.DashingToSword:
                ProcessSwordDashMovement();
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
    #region Surface Detection
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines if a surface normal indicates a floor (walkable) or wall (stickable).
    /// Uses the angle between the surface normal and world up (Vector3.up).
    /// </summary>
    /// <param name="surfaceNormal">The normal of the surface to check</param>
    /// <returns>True if the surface is a floor, false if it's a wall</returns>
    private bool IsFloorSurface(Vector3 surfaceNormal)
    {
        // Calculate angle between surface normal and world up
        float angle = Vector3.Angle(surfaceNormal, Vector3.up);
        
        // If angle is less than maxFloorAngle, it's a floor
        // angle = 0 means normal points straight up (flat floor)
        // angle = 90 means normal points horizontally (vertical wall)
        // angle = 180 means normal points straight down (ceiling)
        return angle <= maxFloorAngle;
    }
    
    /// <summary>
    /// Determines if a surface is a wall (vertical enough to stick to).
    /// </summary>
    private bool IsWallSurface(Vector3 surfaceNormal)
    {
        return !IsFloorSurface(surfaceNormal);
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
            // FIXED: Only block dash if targeting the SAME surface we're standing/stuck on
            // Allow dashing to different positions on the same type of surface
            if (!IsSameExactSurface(hit))
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
        
        // FIXED: Determine if target is a wall or floor
        dashTargetIsWall = IsWallSurface(surfaceNormal);
        
        Debug.Log($"[PlayerDash] Dash started - Target is {(dashTargetIsWall ? "WALL" : "FLOOR")} (normal angle: {Vector3.Angle(surfaceNormal, Vector3.up):F1}°)");

        // Slow time during dash
        Time.timeScale = dashTimeScale;

        // Notify core to change state
        core.SetState(PlayerCore.PlayerState.Dashing);
        
        OnDashStarted?.Invoke();
    }

    private void ProcessDashMovement()
    {
        float dashDistance = Vector3.Distance(dashStartPosition, dashTargetPosition);
        
        // FIXED: Handle very short dashes properly
        if (dashDistance < 0.01f)
        {
            // Even with zero distance, complete the dash properly
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
                if (!IsSameExactSurface(hit))
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

        // FIXED: Only activate wall stick if target was a WALL, not a floor
        // Also ensure player is not currently grounded
        if (hitSurface && dashTargetIsWall && !core.Controller.isGrounded)
        {
            ActivateWallStick(transform.position);
            Debug.Log("[PlayerDash] Dash completed - sticking to WALL");
        }
        else
        {
            Debug.Log($"[PlayerDash] Dash completed - landing on {(dashTargetIsWall ? "wall but grounded" : "FLOOR")}");
        }

        // FIXED: Pass both hitSurface AND hitWall to let PlayerCore decide state correctly
        OnDashCompleted?.Invoke(hitSurface, dashTargetIsWall);
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
    #region Sword Dash Logic (NEW)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initiates a dash towards the stuck sword, or recalls if sword is not visible.
    /// Called by PlayerCombat when Attack is pressed while disarmed.
    /// Returns true if sword dash was started successfully.
    /// If sword is blocked by an obstacle, it will be recalled instead (returns false).
    /// </summary>
    public bool TryStartSwordDash()
    {
        // Validate conditions
        if (swordThrow == null || !swordThrow.IsSwordStuck) return false;
        if (swordThrow.ActiveSword == null) return false;
        if (isSwordDashing) return false;
        if (core.IsDead) return false;
        
        // Get the sword transform as target
        swordDashTarget = swordThrow.ActiveSword.transform;
        if (swordDashTarget == null) return false;
        
        // NEW: Check if sword is visible (not blocked by obstacles)
        if (!IsSwordVisible())
        {
            // Sword is blocked - recall instead of dash
            Debug.Log("[PlayerDash] Sword not visible - recalling instead of dashing");
            swordThrow.ForceRecall();
            swordDashTarget = null;
            return false;
        }
        
        StartSwordDash();
        return true;
    }
    
    /// <summary>
    /// Checks if there's a clear line of sight to the sword.
    /// Returns true if sword is visible, false if blocked by obstacles.
    /// </summary>
    private bool IsSwordVisible()
    {
        if (swordDashTarget == null) return false;
        
        // Raycast from player to sword
        Vector3 playerPos = transform.position + Vector3.up * 1f; // Offset up to roughly chest height
        Vector3 swordPos = swordDashTarget.position;
        Vector3 toSword = swordPos - playerPos;
        float distanceToSword = toSword.magnitude;
        
        // Cast ray towards sword
        if (Physics.Raycast(playerPos, toSword.normalized, out RaycastHit hit, distanceToSword, dashSurfaceLayer))
        {
            // Check if we hit the sword itself or something else
            // If hit distance is significantly shorter than sword distance, something is blocking
            float hitDistance = hit.distance;
            
            // Allow small tolerance (sword might be slightly embedded in surface)
            if (hitDistance < distanceToSword - 0.5f)
            {
                // Hit something before reaching the sword - blocked!
                Debug.Log($"[PlayerDash] Sword blocked by {hit.collider.name} at distance {hitDistance:F1} (sword at {distanceToSword:F1})");
                return false;
            }
        }
        
        // No obstruction found - sword is visible
        return true;
    }

    private void StartSwordDash()
    {
        // Deactivate wall stick if active
        DeactivateWallStick();
        
        isSwordDashing = true;
        dashStartPosition = transform.position;
        
        // Store current rotation for smooth interpolation
        swordDashStartRotation = transform.rotation;
        swordDashRotationTimer = 0f;
        
        // Apply same slow-mo as normal dash
        Time.timeScale = dashTimeScale;
        
        OnSwordDashStarted?.Invoke();
        
        Debug.Log("[PlayerDash] Sword dash started!");
    }

    private void ProcessSwordDashMovement()
    {
        // Safety check - if sword was destroyed, cancel dash
        if (swordDashTarget == null || swordThrow == null || swordThrow.ActiveSword == null)
        {
            CompleteSwordDash(caughtSword: false);
            return;
        }
        
        Vector3 targetPos = swordDashTarget.position;
        Vector3 toTarget = targetPos - transform.position;
        float distance = toTarget.magnitude;
        
        // Check if close enough to catch the sword
        if (distance <= swordCatchDistance)
        {
            CompleteSwordDash(caughtSword: true);
            return;
        }
        
        // Move towards sword (always dash in direction of sword, not facing direction)
        Vector3 moveDirection = toTarget.normalized;
        float moveDistance = swordDashSpeed * Time.unscaledDeltaTime;
        
        // Don't overshoot
        moveDistance = Mathf.Min(moveDistance, distance);
        
        core.Controller.Move(moveDirection * moveDistance);
        
        // Smoothly rotate towards the sword over the rotation duration
        swordDashRotationTimer += Time.unscaledDeltaTime;
        float rotationProgress = Mathf.Clamp01(swordDashRotationTimer / swordDashRotationDuration);
        
        Vector3 lookDir = toTarget;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDir.normalized);
            transform.rotation = Quaternion.Slerp(swordDashStartRotation, targetRotation, rotationProgress);
        }
    }

    private void CompleteSwordDash(bool caughtSword)
    {
        Time.timeScale = 1f;
        isSwordDashing = false;
        swordDashTarget = null;
        
        if (caughtSword && swordThrow != null)
        {
            // Force the sword to return with extra dash damage to embedded enemy
            swordThrow.ForceRecallWithDashDamage(swordDashDamage);
            Debug.Log("[PlayerDash] Sword dash completed - sword caught!");
        }
        else
        {
            Debug.Log("[PlayerDash] Sword dash ended without catching sword");
        }
        
        OnSwordDashCompleted?.Invoke();
    }

    /// <summary>
    /// Force cancel the sword dash (e.g., if player dies mid-dash).
    /// </summary>
    public void ForceCancelSwordDash()
    {
        if (!isSwordDashing) return;
        
        Time.timeScale = 1f;
        isSwordDashing = false;
        swordDashTarget = null;
        swordDashRotationTimer = 0f;
        
        OnSwordDashCompleted?.Invoke();
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

    /// <summary>
    /// FIXED: Check if the hit is the EXACT SAME surface we're currently on.
    /// This prevents dashing to the spot directly beneath you, but allows
    /// dashing to a different position on the floor in front of you.
    /// </summary>
    private bool IsSameExactSurface(RaycastHit hit)
    {
        // If stuck to a wall, check if targeting the same wall collider
        if (IsStuck && isWallStickActive)
        {
            if (Physics.Raycast(
                stuckPosition,
                -stuckSurfaceNormal,
                out RaycastHit surfaceHit,
                wallStickCheckDistance + 0.5f,
                dashSurfaceLayer))
            {
                // Same collider AND very close hit point = same surface
                if (surfaceHit.collider == hit.collider)
                {
                    float hitDistance = Vector3.Distance(surfaceHit.point, hit.point);
                    if (hitDistance < 0.5f)
                    {
                        return true;
                    }
                }
            }
        }

        // FIXED: Only block if targeting the EXACT spot directly below
        // Allow dashing to floor positions in front of you
        if (core.Controller.enabled && core.Controller.isGrounded && !IsStuck)
        {
            if (Physics.Raycast(
                transform.position,
                Vector3.down,
                out RaycastHit groundHit,
                core.Controller.height / 2 + 0.2f,
                dashSurfaceLayer))
            {
                // Same collider check
                if (groundHit.collider == hit.collider)
                {
                    // FIXED: Check if the target position is very close to current position
                    // If it's more than 1 meter away horizontally, allow the dash
                    Vector3 currentPosFlat = new Vector3(transform.position.x, 0, transform.position.z);
                    Vector3 targetPosFlat = new Vector3(hit.point.x, 0, hit.point.z);
                    float horizontalDistance = Vector3.Distance(currentPosFlat, targetPosFlat);
                    
                    // Only block if target is basically where we're standing (within 0.5m)
                    if (horizontalDistance < 0.5f)
                    {
                        return true;
                    }
                }
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
        else if (isSwordDashing)
        {
            ForceCancelSwordDash();
            core.SetState(PlayerCore.PlayerState.Airborne);
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
        
        // Reset sword dash state
        isSwordDashing = false;
        swordDashTarget = null;
        swordDashRotationTimer = 0f;
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
            // FIXED: Color indicates floor vs wall target
            Gizmos.color = dashTargetIsWall ? Color.cyan : Color.green;
            Gizmos.DrawLine(dashStartPosition, dashTargetPosition);
            Gizmos.DrawWireSphere(dashTargetPosition, 0.5f);
            
            // Draw surface normal
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(dashTargetPosition, dashTargetPosition + stuckSurfaceNormal);
        }
        
        // NEW: Visualize sword dash
        if (isSwordDashing && swordDashTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, swordDashTarget.position);
            Gizmos.DrawWireSphere(swordDashTarget.position, swordCatchDistance);
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