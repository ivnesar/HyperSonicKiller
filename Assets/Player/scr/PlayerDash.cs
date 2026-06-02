using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Handles dash mechanics: Attack Dash and wall-stick.
/// 
/// NEW SYSTEM:
/// - Attack Dash (LMB): Player dashes to a surface, automatically attacking NPCs in the path
/// - Dash auto-pickup: if a stuck sword is close enough, it is instantly picked up before the normal attack dash starts
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
        Attack      // Normal dash with auto-attack on NPCs in path
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

    // Offset between camera and root at the moment a dash starts.
    // Needed because the dash AXIS is calculated from the camera (so trail/marker
    // align with the crosshair), while the player ROOT must move in parallel
    // and end up at (cameraEndPoint - thisOffset).
    private Vector3 rootToCameraOffsetAtDashStart;

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
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public int CurrentCharges => currentCharges;
    public int MaxCharges => maxDashCharges;
    public bool IsDashing => core.CurrentState == PlayerCore.PlayerState.Dashing;
    public bool IsStuck => core.CurrentState == PlayerCore.PlayerState.StuckToSurface;
    public Vector3 StuckSurfaceNormal => stuckSurfaceNormal;
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

    /// <summary>
    /// The world-space point the dash is heading toward, calculated from the
    /// CAMERA position (= where the crosshair was aimed at dash start).
    /// This is intentionally NOT the player root's end position — the root
    /// ends up at (this position - cameraOffset) so it moves parallel to the
    /// camera axis. Use this for visualizations (trail, marker) so they align
    /// with the line of sight.
    /// </summary>
    public Vector3 DashTargetPosition => dashTargetPosition;

    /// <summary>
    /// The world-space point the dash axis STARTS at (= camera position at
    /// the moment the dash was triggered). Pair with DashTargetPosition or
    /// DashDirection for visualizations (trail, debug lines).
    /// </summary>
    public Vector3 DashStartPosition => dashStartPosition;

    /// <summary>True if this dash ends on a real surface (not open air).</summary>
    public bool DashHitSurface => dashHitSurface;
    
    
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

        if (core.Input.GetActionDown("Dash") && currentCharges > 0)
        {
            // If a thrown sword is stuck close to the player, pick it up instantly before
            // the normal attack dash starts. RMB recall still uses visible return flight.
            swordThrow?.TryInstantPickupForDash();
            TryStartAttackDash();
        }
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
            // No surface found — dash full distance into open air.
            // Important: target point is calculated from the CAMERA, not the root,
            // so that the dash axis (camera → target) is straight and the trail
            // visualization stays exactly on the flight path.
            Vector3 openAirTarget = origin + direction * dashMaxDistance;
            StartAttackDash(openAirTarget, -direction, null);
        }
    }

    private void StartAttackDash(Vector3 targetPoint, Vector3 surfaceNormal, Collider hitCollider)
    {
        // Deactivate wall stick if active
        DeactivateWallStick();

        currentCharges--;
        OnChargesChanged?.Invoke(currentCharges);

        // === Dash axis is calculated from the CAMERA, not the root ===
        // The player aimed with the crosshair (= camera), so the flight path
        // must originate at the camera. Otherwise trail and marker would drift
        // off the actual line of sight.
        Vector3 cameraStart = core.CameraTransform.position;
        Vector3 cameraTarget = targetPoint + surfaceNormal * wallStickOffset;

        dashStartPosition = cameraStart;          // semantic: where the dash axis starts (camera)
        dashTargetPosition = cameraTarget;        // semantic: where the dash axis ends (camera-end)
        dashDirection = (dashTargetPosition - dashStartPosition).normalized;

        // The player root must move parallel to the camera axis. We remember
        // the offset between camera and root at dash start so we can compute
        // the correct root end position when CompleteDash() runs.
        rootToCameraOffsetAtDashStart = cameraStart - transform.position;

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
        // dashStartPosition / dashTargetPosition describe the CAMERA axis
        // (start = camera position at dash start, target = where the crosshair pointed).
        // The player ROOT moves parallel to this axis, offset by rootToCameraOffsetAtDashStart.
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
            // Reached target — snap the root to its parallel end position.
            // Root end = camera end - the offset we recorded at dash start.
            Vector3 rootTargetPosition = dashTargetPosition - rootToCameraOffsetAtDashStart;
            Vector3 finalMove = rootTargetPosition - transform.position;
            core.MovePlayer(finalMove);

            // If we pass close enough to a stuck sword during the dash, pick it up
            // instantly before enemy damage is checked. This lets later enemies in
            // the same dash be auto-attacked again after the sword is recovered.
            swordThrow?.TryInstantPickupForDash();

            // Final check for enemies at destination
            CheckAndDamageEnemiesInRadius();

            CompleteDash(hitSurface: dashHitSurface);
        }
        else
        {
            // Move root in the same direction as the camera axis (they're parallel).
            core.MovePlayer(dashDirection * moveDistance);

            // If we pass close enough to a stuck sword during the dash, pick it up
            // instantly before enemy damage is checked. Existing pickup rules still
            // apply (stuck sword, distance threshold, line of sight).
            swordThrow?.TryInstantPickupForDash();

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

        //Check if the surface we landed on is sticky
        bool isStickyLanding = hitSurface && dashTargetCollider != null 
                               && dashTargetCollider.GetComponentInParent<StickySurface>() != null;
        
        // Debug.Log($"[Dash] sticky={isStickyLanding} " +
        //           $"col={(dashTargetCollider ? dashTargetCollider.name : "null")} " +
        //           $"hasSticky={(dashTargetCollider && dashTargetCollider.GetComponentInParent<StickySurface>())}");
        
        //bool isStickyLanding = IsLandingOnStickySurface();
        
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
    
    private bool IsLandingOnStickySurface()
    {
        if (!dashHitSurface) return false;

        // Tasten in Richtung der Fläche (bei Wand entlang -Normal, bei Boden = nach unten).
        Vector3 probeDir = -stuckSurfaceNormal;
        float probeDist = wallStickOffset + 0.5f; // wir enden ~wallStickOffset von der Fläche entfernt

        if (Physics.Raycast(transform.position, probeDir, out RaycastHit hit,
                probeDist, dashSurfaceLayer, QueryTriggerInteraction.Ignore))
        {
            return hit.collider.GetComponentInParent<StickySurface>() != null;
        }
        return false;
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
    
    /// <summary>
    /// Prüft am Ende des Dashes, ob die Fläche, auf der wir gelandet sind,
    /// klebrig ist. Nutzt eine frische Abfrage gegen die echte Landeposition,
    /// statt sich auf den beim Dash-Start gemerkten Collider zu verlassen.
    /// </summary>
    
}