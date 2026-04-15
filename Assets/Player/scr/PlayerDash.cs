using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Handles all dash mechanics: Attack Dash, Sword Dash, and wall-stick.
/// 
/// NEW SYSTEM:
/// - Attack Dash (LMB): Player dashes to a surface, automatically attacking NPCs in the path
/// - Sword Dash (LMB while looking at stuck sword): Invulnerable dash to retrieve thrown sword
///   * Requires sword to be within crosshair FOV cone (default 9°)
///   * Requires line of sight to sword (not blocked by walls)
/// - Wall Stick: Cling to walls after dashing
/// 
/// The player acts like a "projectile" - dashing THROUGH enemies to reach surfaces.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerDash : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Enums
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Types of dash the player can perform.
    /// </summary>
    public enum DashType
    {
        Attack,     // Normal dash with auto-attack on NPCs in path
        ToSword     // Invulnerable dash to retrieve sword
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action OnDashStarted;
    public event Action<bool, bool, bool> OnDashCompleted;  // (hitSurface, hitWall, isStickyLanding)
    public event Action OnWallStick;
    public event Action OnUnstick;
    public event Action<int> OnChargesChanged;  // remaining charges
    
    // Sword dash events
    public event Action OnSwordDashStarted;
    public event Action OnSwordDashCompleted;
    
    // NEW: Attack dash events
    public event Action<IEnemy> OnEnemyHitDuringDash;  // Fired for each enemy hit
    
    /// <summary>
    /// Fired when dash is externally blocked or unblocked (e.g. by Anti-Dash Drone).
    /// True = dash is blocked, False = dash is available again.
    /// Does NOT fire for empty charges — only for external SetDashEnabled() calls.
    /// </summary>
    public event Action<bool> OnDashBlockedChanged;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings - General Dash
    // ════════════════════════════════════════════════════════════════════════

    [Header("Dash Settings")]
    [SerializeField] private int maxDashCharges = 3;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashMaxDistance = 15f;
    [SerializeField] private LayerMask dashSurfaceLayer = -1;
    
    [Header("Surface Detection")]
    [Tooltip("Maximum angle from vertical (Y-up) for a surface to be considered a floor")]
    [SerializeField] private float maxFloorAngle = 45f;

    [Header("Time Slow During Dash")]
    [SerializeField] private float dashTimeScale = 0.1f;

    [Header("Dash Cancel Forces")]
    [SerializeField] private float dashCancelUpwardForce = 10f;
    [SerializeField] private float dashCancelDownwardForce = 15f;

    [Header("Wall Stick")]
    [SerializeField] private float wallStickCheckDistance = 1f;
    [SerializeField] private float wallStickOffset = 0.5f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings - Attack Dash (NEW)
    // ════════════════════════════════════════════════════════════════════════

    [Header("Attack Dash (NEW)")]
    [Tooltip("Radius around the dash path to detect and hit enemies")]
    [SerializeField] private float attackDashRadius = 1.5f;
    
    [Tooltip("Damage dealt to each enemy hit during attack dash")]
    [SerializeField] private int attackDashDamage = 50;
    
    [Tooltip("Layer mask for detecting enemies during dash")]
    [SerializeField] private LayerMask enemyLayer;
    
    [Header("Attack Angle Thresholds")]
    [Tooltip("Max angle from dash direction to hit normal enemies (generous)")]
    [SerializeField] private float enemyHitAngle = 60f;

    [Header("Hit Feedback")]
    [Tooltip("Dauer des HitStops in Sekunden (Echtzeit). Typisch: 0.05 - 0.15")]
    [SerializeField] private float hitStopDuration = 0.08f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings - Sword Dash
    // ════════════════════════════════════════════════════════════════════════

    [Header("Sword Dash")]
    [Tooltip("Sword Dash komplett deaktivieren (LMB bei Schwert löst dann immer Attack Dash aus)")]
    [SerializeField] private bool disableSwordDash = false;
    
    [Tooltip("Speed when dashing to retrieve the thrown sword")]
    [SerializeField] private float swordDashSpeed = 40f;
    
    [Tooltip("How close the player needs to be to 'catch' the sword")]
    [SerializeField] private float swordCatchDistance = 1.5f;
    
    [Tooltip("Damage dealt to enemy when retrieving sword via dash")]
    [SerializeField] private int swordDashDamage = 50;
    
    [Tooltip("Time to smoothly rotate towards the sword during dash")]
    [SerializeField] private float swordDashRotationDuration = 0.2f;
    
    [Header("Sword Dash Targeting")]
    [Tooltip("Maximum angle from crosshair to sword for Sword Dash to trigger (in degrees)")]
    [SerializeField] private float swordDashMaxAngle = 9f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State - General
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private PlayerSwordThrow swordThrow;
    private PlayerCombat combat;

    // Dash charges
    private int currentCharges;

    // Current dash state
    private DashType currentDashType;
    private Vector3 dashStartPosition;
    private Vector3 dashTargetPosition;
    private Vector3 dashDirection;
    private float dashProgress;
    private bool dashTargetIsWall;
    private bool dashHitSurface;          // true if dash ends on a surface (not open air)
    private Collider dashTargetCollider;   // collider of the surface we're landing on (null if none)
    private Vector3 stuckSurfaceNormal;

    // Wall stick state
    private Vector3 stuckPosition;
    private bool isWallStickActive;

    // Flags
    private bool dashDisabled;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State - Attack Dash (NEW)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks enemies already hit during current dash to prevent double-hits.
    /// </summary>
    private HashSet<IEnemy> enemiesHitThisDash = new HashSet<IEnemy>();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State - Sword Dash
    // ════════════════════════════════════════════════════════════════════════

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
    public bool IsSwordDashing => isSwordDashing;
    public bool IsWallStickActive => isWallStickActive;
    
    /// <summary>Current dash speed (for external systems like GenTwo intercept calculation).</summary>
    public float DashSpeed => dashSpeed;
    
    /// <summary>Maximum dash distance (for external systems like GenTwo intercept calculation).</summary>
    public float DashMaxDistance => dashMaxDistance;
    
    /// <summary>Current attack dash radius (for external systems).</summary>
    public float AttackDashRadius => attackDashRadius;
    
    /// <summary>
    /// The locked-in dash direction, set once at dash start.
    /// Use this instead of CameraTransform.forward to get the actual flight path.
    /// </summary>
    public Vector3 DashDirection => dashDirection;
    
    /// <summary>The timeScale used during dash slow-mo. Read by PlayerMovement for sprint burst.</summary>
    public float DashTimeScale => dashTimeScale;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        swordThrow = GetComponent<PlayerSwordThrow>();
        combat = GetComponent<PlayerCombat>();
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
                ProcessAttackDashMovement();
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
        // Force position in LateUpdate to override any physics
        if (isWallStickActive && core.CurrentState == PlayerCore.PlayerState.StuckToSurface)
        {
            transform.position = stuckPosition;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Surface Detection
    // ════════════════════════════════════════════════════════════════════════

    private bool IsFloorSurface(Vector3 surfaceNormal)
    {
        float angle = Vector3.Angle(surfaceNormal, Vector3.up);
        return angle <= maxFloorAngle;
    }
    
    private bool IsWallSurface(Vector3 surfaceNormal)
    {
        return !IsFloorSurface(surfaceNormal);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Input Handling
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDashInput()
    {
        if (dashDisabled) return;
        if (!core.CanDash) return;

        if (core.Input.GetActionDown("Dash"))
        {
            // Check if sword is thrown and stuck AND player is looking at it
            if (!disableSwordDash && swordThrow != null && swordThrow.IsSwordStuck && IsSwordInCrosshairFOV())
            {
                TryStartSwordDash();
            }
            else if (currentCharges > 0)
            {
                // Normal attack dash (either no sword out, or not looking at sword)
                TryStartAttackDash();
            }
        }
    }
    
    /// <summary>
    /// Checks if the stuck sword is within the crosshair FOV cone.
    /// Returns true if the angle between camera forward and direction to sword
    /// is less than or equal to swordDashMaxAngle.
    /// </summary>
    private bool IsSwordInCrosshairFOV()
    {
        if (swordThrow == null || swordThrow.ActiveSword == null) return false;
        
        Vector3 cameraPosition = core.CameraTransform.position;
        Vector3 cameraForward = core.CameraTransform.forward;
        Vector3 swordPosition = swordThrow.ActiveSword.transform.position;
        
        // Direction from camera to sword
        Vector3 toSword = (swordPosition - cameraPosition).normalized;
        
        // Angle between look direction and sword direction
        float angleToSword = Vector3.Angle(cameraForward, toSword);
        
        bool isInFOV = angleToSword <= swordDashMaxAngle;
        
        if (!isInFOV)
        {
            Debug.Log($"[PlayerDash] Sword outside FOV cone ({angleToSword:F1}° > {swordDashMaxAngle}°) - using Attack Dash");
        }
        
        return isInFOV;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Attack Dash Logic (NEW)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attempts to start an attack dash. Finds the target surface and NPCs in path.
    /// Always starts a dash — if no surface is found, the player dashes the full
    /// dashMaxDistance and transitions to Airborne.
    /// </summary>
    private void TryStartAttackDash()
    {
        Vector3 origin = core.CameraTransform.position;
        Vector3 direction = core.CameraTransform.forward;

        // Find all hits along the path (surfaces AND enemies)
        RaycastHit[] allHits = Physics.RaycastAll(origin, direction, dashMaxDistance, dashSurfaceLayer | enemyLayer);

        // Sort by distance
        if (allHits.Length > 0)
            System.Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));

        // Find the first SURFACE (not an enemy)
        RaycastHit? surfaceHit = null;
        foreach (var hit in allHits)
        {
            if (!hit.collider.TryGetComponent<IEnemy>(out _))
            {
                if (!IsSameExactSurface(hit))
                {
                    surfaceHit = hit;
                    break;
                }
            }
        }

        if (surfaceHit.HasValue)
        {
            // Surface found — dash to it
            StartAttackDash(surfaceHit.Value.point, surfaceHit.Value.normal, surfaceHit.Value.collider);
        }
        else
        {
            // No surface found — dash full distance into open air
            Vector3 openAirTarget = transform.position + direction * dashMaxDistance;
            StartAttackDash(openAirTarget, -direction, null);
        }
    }

    private void StartAttackDash(Vector3 targetPoint, Vector3 surfaceNormal, Collider hitCollider)
    {
        // Deactivate wall stick if active
        DeactivateWallStick();

        currentCharges--;
        OnChargesChanged?.Invoke(currentCharges);

        dashStartPosition = transform.position;
        dashTargetPosition = targetPoint + surfaceNormal * wallStickOffset;
        dashDirection = (dashTargetPosition - dashStartPosition).normalized;
        dashProgress = 0f;
        stuckSurfaceNormal = surfaceNormal;
        dashTargetIsWall = IsWallSurface(surfaceNormal);
        dashHitSurface = hitCollider != null;
        dashTargetCollider = hitCollider;
        currentDashType = DashType.Attack;
        
        // Clear hit tracking for new dash
        enemiesHitThisDash.Clear();

        // Slow time during dash
        TimeManager.Instance.StartDashSlowMo(dashTimeScale);

        // Notify core to change state
        core.SetState(PlayerCore.PlayerState.Dashing);
        
        OnDashStarted?.Invoke();
        
        //Debug.Log($"[PlayerDash] Attack dash started - Target is {(dashTargetIsWall ? "WALL" : "FLOOR")}");
    }

    private void ProcessAttackDashMovement()
    {
        float dashDistance = Vector3.Distance(dashStartPosition, dashTargetPosition);
        
        if (dashDistance < 0.01f)
        {
            CompleteDash(hitSurface: dashHitSurface);
            return;
        }
        
        // GameDeltaTime: runs at full speed during SlowMo, but stops during Pause/HitStop
        float moveDistance = dashSpeed * TimeManager.Instance.GameDeltaTime;
        dashProgress += moveDistance / dashDistance;

        if (dashProgress >= 1f)
        {
            // Reached target
            Vector3 finalMove = dashTargetPosition - transform.position;
            core.Controller.Move(finalMove);
            
            // Final check for enemies at destination
            CheckAndDamageEnemiesInRadius();
            
            CompleteDash(hitSurface: dashHitSurface);
        }
        else
        {
            core.Controller.Move(dashDirection * moveDistance);
            
            // Check for enemies to hit during movement
            CheckAndDamageEnemiesInRadius();
        }
    }

    /// <summary>
    /// Checks for enemies within the attack radius and processes hits.
    /// If an enemy has a DefenderShield and the player is inside its FOV cone,
    /// the attack is parried (player gets exhausted, no damage dealt).
    /// No damage is dealt when player is Exhausted.
    /// 
    /// Enemies must be within enemyHitAngle of dash direction to be hit (generous).
    /// </summary>
    private void CheckAndDamageEnemiesInRadius()
    {
        // Skip damage dealing if player can't attack (exhausted or disarmed/sword thrown)
        if (combat != null && !combat.CanDealDashDamage)
        {
            return;
        }

        Collider[] enemyHits = Physics.OverlapSphere(transform.position, attackDashRadius, enemyLayer);
        
        foreach (var col in enemyHits)
        {
            if (col.TryGetComponent<IEnemy>(out var enemy))
            {
                // Only hit each enemy once per dash
                if (!enemiesHitThisDash.Contains(enemy))
                {
                    // Skip enemies that can't be auto-attacked (e.g. ProxyMine)
                    if (!enemy.CanBeAutoAttacked)
                        continue;

                    // Check if enemy is within the generous hit angle
                    Vector3 toEnemy = (col.transform.position - transform.position).normalized;
                    float angle = Vector3.Angle(dashDirection, toEnemy);
                    
                    if (angle > enemyHitAngle)
                    {
                        // Enemy is too far off to the side - player is dashing past
                        continue;
                    }
                    
                    // ── Shield Parry Check ──────────────────────────────
                    // If this enemy has a shield and the player is in its FOV,
                    // the attack is parried instead of dealing damage.
                    var shield = col.GetComponent<DefenderShield>();
                    if (shield != null && shield.IsBlockingAttackFrom(transform.position))
                    {
                        enemiesHitThisDash.Add(enemy);
                        shield.ParryMeleeAttack();
                        
                        // Cancel the dash immediately — player should not keep flying
                        ForceCancelDash();
                        
                        Debug.Log($"[PlayerDash] Attack parried by {col.name}'s shield!");
                        return; // Exit entirely, dash is over
                    }
                    
                    enemiesHitThisDash.Add(enemy);
                    
                    // Deal damage via melee interface
                    enemy.OnMeleeDamage(attackDashDamage);
                    
                    OnEnemyHitDuringDash?.Invoke(enemy);

                    // HitStop bei jedem Treffer auslösen
                    if (hitStopDuration > 0f)
                    {
                        TimeManager.Instance.TriggerHitStop(hitStopDuration);
                    }

                    // Kamera-Snap zum getroffenen Gegner
                    if (core.Look != null)
                    {
                        // SnapTarget vom Enemy nutzen, Fallback auf Enemy-Transform
                        Transform snapTarget = enemy.SnapTarget != null 
                            ? enemy.SnapTarget 
                            : enemy.Transform;
                        core.Look.SnapToTarget(snapTarget);
                    }
                    
                   
                }
            }
        }
    }


    private void CheckDashCancels()
    {
        // NOTE: Dash redirect removed - player must complete or cancel dash before starting a new one
        
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
        TimeManager.Instance.StopDashSlowMo();
        dashProgress = 0f;
        enemiesHitThisDash.Clear();

        // Kamera-Snap abbrechen wenn Dash endet
        core.Look?.CancelSnap();

        // Check if the surface we landed on is sticky
        bool isStickyLanding = hitSurface && dashTargetCollider != null 
                               && dashTargetCollider.GetComponentInParent<StickySurface>() != null;

        if (isStickyLanding && dashTargetIsWall && !core.Controller.isGrounded)
        {
            // Sticky wall — activate wall stick and recharge charges
            ActivateWallStick(transform.position);
        }
        else if (isStickyLanding && !dashTargetIsWall)
        {
            // Sticky floor — recharge charges (state transition handled by PlayerCore)
        }
        else if (hitSurface && !isStickyLanding)
        {
            // Non-sticky surface — no wall stick, no charge recharge
          
        }
        else
        {
            // No surface hit (dashed into open air)

        }

        OnDashCompleted?.Invoke(hitSurface, dashTargetIsWall, isStickyLanding);
    }

    private void CancelDash(float verticalForce)
    {
        TimeManager.Instance.StopDashSlowMo();
        dashProgress = 0f;
        enemiesHitThisDash.Clear();
        
        // Kamera-Snap abbrechen wenn Dash gecancelt wird
        core.Look?.CancelSnap();
        
        DeactivateWallStick();

        core.Movement?.ApplyVerticalImpulse(verticalForce);
        core.SetState(PlayerCore.PlayerState.Airborne);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sword Dash Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attempts to start a sword dash. Returns true if successful.
    /// Called only after FOV check has passed (sword is in crosshair cone).
    /// Still checks visibility (line of sight not blocked by walls).
    /// </summary>
    public bool TryStartSwordDash()
    {
        if (swordThrow == null || !swordThrow.IsSwordStuck) return false;
        if (swordThrow.ActiveSword == null) return false;
        if (isSwordDashing) return false;
        if (core.IsDead) return false;
        
        swordDashTarget = swordThrow.ActiveSword.transform;
        if (swordDashTarget == null) return false;
        
        // Check if sword is visible (not blocked)
        if (!IsSwordVisible())
        {
            Debug.Log("[PlayerDash] Sword not visible - recalling instead of dashing");
            swordThrow.ForceRecall();
            swordDashTarget = null;
            return false;
        }
        
        StartSwordDash();
        return true;
    }
    
    private bool IsSwordVisible()
    {
        if (swordDashTarget == null) return false;
        
        Vector3 playerPos = transform.position + Vector3.up * 1f;
        Vector3 swordPos = swordDashTarget.position;
        Vector3 toSword = swordPos - playerPos;
        float distanceToSword = toSword.magnitude;
        
        if (Physics.Raycast(playerPos, toSword.normalized, out RaycastHit hit, distanceToSword, dashSurfaceLayer))
        {
            float hitDistance = hit.distance;
            if (hitDistance < distanceToSword - 0.5f)
            {
                Debug.Log($"[PlayerDash] Sword blocked by {hit.collider.name}");
                return false;
            }
        }
        
        return true;
    }

    private void StartSwordDash()
    {
        DeactivateWallStick();
        
        isSwordDashing = true;
        dashStartPosition = transform.position;
        swordDashStartRotation = transform.rotation;
        swordDashRotationTimer = 0f;
        
        TimeManager.Instance.StartDashSlowMo(dashTimeScale);
        
        OnSwordDashStarted?.Invoke();
        
        Debug.Log("[PlayerDash] Sword dash started!");
    }

    private void ProcessSwordDashMovement()
    {
        if (swordDashTarget == null || swordThrow == null || swordThrow.ActiveSword == null)
        {
            CompleteSwordDash(caughtSword: false);
            return;
        }
        
        Vector3 targetPos = swordDashTarget.position;
        Vector3 toTarget = targetPos - transform.position;
        float distance = toTarget.magnitude;
        
        if (distance <= swordCatchDistance)
        {
            CompleteSwordDash(caughtSword: true);
            return;
        }
        
        Vector3 moveDirection = toTarget.normalized;
        float moveDistance = Mathf.Min(swordDashSpeed * TimeManager.Instance.GameDeltaTime, distance);
        
        core.Controller.Move(moveDirection * moveDistance);
        
        // Smooth rotation towards sword
        swordDashRotationTimer += TimeManager.Instance.GameDeltaTime;
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
        TimeManager.Instance.StopDashSlowMo();
        isSwordDashing = false;
        swordDashTarget = null;
        
        if (caughtSword && swordThrow != null)
        {
            swordThrow.ForceRecallWithDashDamage(swordDashDamage);
            Debug.Log("[PlayerDash] Sword dash completed - sword caught!");
        }
        else
        {
            Debug.Log("[PlayerDash] Sword dash ended without catching sword");
        }
        
        OnSwordDashCompleted?.Invoke();
    }

    public void ForceCancelSwordDash()
    {
        if (!isSwordDashing) return;
        
        TimeManager.Instance.StopDashSlowMo();
        isSwordDashing = false;
        swordDashTarget = null;
        swordDashRotationTimer = 0f;
        
        OnSwordDashCompleted?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Wall Stick Logic
    // ════════════════════════════════════════════════════════════════════════

    private void ActivateWallStick(Vector3 position)
    {
        stuckPosition = position;
        isWallStickActive = true;
        
        if (core.Controller != null)
        {
            core.Controller.enabled = false;
        }
        
        transform.position = stuckPosition;
        
        OnWallStick?.Invoke();
        
    }

    private void DeactivateWallStick()
    {
        if (!isWallStickActive) return;
        
        isWallStickActive = false;
        
        if (core.Controller != null)
        {
            core.Controller.enabled = true;
        }
        
        Debug.Log("[PlayerDash] Wall stick deactivated");
    }

    private void MaintainWallStick()
    {
        if (isWallStickActive)
        {
            transform.position = stuckPosition;
        }
    }

    private void CheckUnstickInput()
    {
        if (core.Input.GetActionDown("Jump"))
        {
            Unstick(dashCancelUpwardForce);
        }
        else if (core.Input.GetActionDown("DashDown"))
        {
            Unstick(-dashCancelDownwardForce);
        }
    }

    private void Unstick(float verticalForce)
    {
        DeactivateWallStick();
        
        core.Movement?.ApplyVerticalImpulse(verticalForce);
        OnUnstick?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helper Methods
    // ════════════════════════════════════════════════════════════════════════

    private bool IsSameExactSurface(RaycastHit hit)
    {
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
                {
                    float hitDistance = Vector3.Distance(surfaceHit.point, hit.point);
                    if (hitDistance < 0.5f)
                    {
                        return true;
                    }
                }
            }
        }

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
                {
                    Vector3 currentPosFlat = new Vector3(transform.position.x, 0, transform.position.z);
                    Vector3 targetPosFlat = new Vector3(hit.point.x, 0, hit.point.z);
                    float horizontalDistance = Vector3.Distance(currentPosFlat, targetPosFlat);
                    
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

    public void ResetCharges()
    {
        currentCharges = maxDashCharges;
        OnChargesChanged?.Invoke(currentCharges);
    }

    public void SetDashEnabled(bool enabled)
    {
        bool wasDisabled = dashDisabled;
        dashDisabled = !enabled;

        // Only fire event if the state actually changed
        if (dashDisabled != wasDisabled)
        {
            OnDashBlockedChanged?.Invoke(dashDisabled);
        }
    }

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
            Unstick(-5f);
            core.SetState(PlayerCore.PlayerState.Airborne);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    private void OnDisable()
    {
        if (isWallStickActive && core != null && core.Controller != null)
        {
            core.Controller.enabled = true;
            isWallStickActive = false;
        }
        
        TimeManager.Instance.StopDashSlowMo();
        
        isSwordDashing = false;
        swordDashTarget = null;
        swordDashRotationTimer = 0f;
        enemiesHitThisDash.Clear();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        if (core == null) return;  
        
        if (IsDashing)
        {
            // Draw dash path
            Gizmos.color = dashTargetIsWall ? Color.cyan : Color.green;
            Gizmos.DrawLine(dashStartPosition, dashTargetPosition);
            Gizmos.DrawWireSphere(dashTargetPosition, 0.5f);
            
            // Draw attack radius along path
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, attackDashRadius);
            
            // Draw surface normal
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(dashTargetPosition, dashTargetPosition + stuckSurfaceNormal);
        }
        
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
            
            Gizmos.color = Color.green;
            Gizmos.DrawCube(stuckPosition + Vector3.up * 2f, Vector3.one * 0.2f);
        }
        
        // Always show attack radius when selected (in editor)
        if (!IsDashing)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, attackDashRadius);
        }
    }

    #endregion
}