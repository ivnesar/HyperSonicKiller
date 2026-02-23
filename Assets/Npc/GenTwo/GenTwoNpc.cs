using UnityEngine;

/// <summary>
/// GenTwo NPC - Melee Interceptor.
/// 
/// Behavior:
/// 1. Idle: Waits and watches the player. Cannot move except by dashing.
/// 2. When the player enters Dashing state AND is within detection range:
///    - Charges up for ~1 second (visual warning for the player)
///    - Calculates an intercept point on the player's dash trajectory
///    - Dashes toward that point at slightly higher speed than the player
/// 3. If GenTwo collides with the player WHILE the player is dashing → 999 damage (lethal)
/// 4. If the player cancels their dash → GenTwo is harmless and flies past
/// 5. GenTwo continues flying until hitting a wall or floor → sticks there
/// 6. Recovery phase → repeat
/// 
/// Counter-play: Player cancels their dash so GenTwo flies past harmlessly.
/// 
/// IMPORTANT: GenTwo does NOT use NavMesh. Movement is purely dash-based.
/// Uses segmented raycasting during dash to prevent tunneling at high speeds.
/// Uses Time.unscaledDeltaTime for dash movement (respects player's time slow).
/// </summary>
public class GenTwoNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Hidden Base Fields
    // ════════════════════════════════════════════════════════════════════════

    public override string[] HiddenBaseFields => new[]
    {
        "behaviorMode",      // GenTwo ist immer stationär (dash-basiert)
        "moveSpeed",         // Kein NavMesh-Movement, nutzt eigenen dashSpeed
        "stoppingDistance",  // Kein NavMesh
        "maxRotationSpeed",  // Nutzt eigene Rotation (FaceDirection / unscaled)
    };

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Detection")]
    [Tooltip("Maximum distance at which GenTwo detects and reacts to player dashes")]
    [SerializeField] private float detectionRange = 30f;

    [Header("Charge")]
    [Tooltip("Time to charge before dashing (gives player a visual warning)")]
    [SerializeField] private float chargeDuration = 1f;

    [Header("Dash")]
    [Tooltip("Base dash speed (before multiplier)")]
    [SerializeField] private float dashSpeed = 25f;

    [Tooltip("Speed multiplier applied ONLY while the player is in Dashing state")]
    [SerializeField] private float dashSpeedMultiplier = 1.3f;

    [Tooltip("Radius for detecting collision with the player during dash")]
    [SerializeField] private float playerHitRadius = 1.2f;

    [Tooltip("Damage dealt to player on collision (while player is dashing)")]
    [SerializeField] private int collisionDamage = 999;

    [Tooltip("Layer mask for surfaces (walls/floors) that stop the dash")]
    [SerializeField] private LayerMask surfaceLayerMask;

    [Tooltip("Number of raycast segments per frame to prevent tunneling")]
    [SerializeField] private int raycastSegments = 4;

    [Header("Recovery")]
    [Tooltip("Time GenTwo stays stuck after a dash before it can act again")]
    [SerializeField] private float recoveryDuration = 1.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip chargeSound;
    [SerializeField] private AudioClip dashSound;
    [SerializeField] private AudioClip impactSound;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<GenTwoNpc> currentState;

    // Player references (cached for fast access)
    private PlayerCore playerCore;
    private PlayerDash playerDash;

    // Dash state
    private Vector3 dashDirection;
    private bool hasHitPlayer;

    // Surface state (wall vs ground)
    private bool isOnWall;

    // Debug: cached intercept point from last calculation
    private Vector3 lastInterceptPoint;
    private bool hasValidIntercept;

    // Unscaled timer — independent of Time.timeScale
    // Needed because the player slows time during their dash
    private float unscaledTimer;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties (accessed by States)
    // ════════════════════════════════════════════════════════════════════════

    public float DetectionRange => detectionRange;
    public float ChargeDuration => chargeDuration;
    public float DashSpeed => dashSpeed;
    public float DashSpeedMultiplier => dashSpeedMultiplier;
    public float PlayerHitRadius => playerHitRadius;
    public int CollisionDamage => collisionDamage;
    public LayerMask SurfaceLayerMask => surfaceLayerMask;
    public int RaycastSegments => raycastSegments;
    public float RecoveryDuration => recoveryDuration;

    public PlayerCore PlayerCore => playerCore;
    public PlayerDash PlayerDash => playerDash;
    public Animator NpcAnimator => animator;

    /// <summary>Current dash direction (set once at dash start, never changes).</summary>
    public Vector3 DashDirection => dashDirection;

    /// <summary>True wenn GenTwo an einer Wand hängt (statt am Boden).</summary>
    public bool IsOnWall => isOnWall;

    /// <summary>True while GenTwo is in the Dashing state.</summary>
    public bool IsDashing => currentState is GenTwoStates.Dashing;

    /// <summary>True if the player is currently in a dash state.</summary>
    public bool IsPlayerDashing => playerCore != null &&
                                    playerCore.CurrentState == PlayerCore.PlayerState.Dashing;

    /// <summary>True if player is within detection range.</summary>
    public bool IsPlayerInRange => DistanceToTarget <= detectionRange;

    /// <summary>Last calculated intercept point (for debug visualization).</summary>
    public Vector3 LastInterceptPoint => lastInterceptPoint;

    /// <summary>True if the last intercept calculation found a valid point.</summary>
    public bool HasValidIntercept => hasValidIntercept;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();

        // GenTwo does NOT use NavMesh - disable it if present
        if (navAgent != null)
        {
            navAgent.enabled = false;
        }
    }

    protected override void Start()
    {
        base.Start();

        // Cache player components for direct access
        if (playerTransform != null)
        {
            playerCore = playerTransform.GetComponent<PlayerCore>();
            playerDash = playerTransform.GetComponent<PlayerDash>();
        }

        if (playerCore == null)
        {
            Debug.LogError($"[GenTwo] {name}: PlayerCore not found! GenTwo will not function.");
        }
    }

    protected override void OnStart()
    {
        ChangeState(new GenTwoStates.Idle());
    }

    protected override void UpdateBehavior()
    {
        if (currentState == null) return;

        var nextState = currentState.Update(this);
        if (nextState != null)
            ChangeState(nextState);
    }

    protected override void OnStunStart()
    {
        ChangeState(new GenTwoStates.Stunned());
    }

    protected override void OnStunEnd()
    {
        if (NpcAnimator != null)
            NpcAnimator.SetTrigger("RecoveryDone");

        ChangeState(new GenTwoStates.Idle());
    }

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.GenTwo;
    public override int GetStateID() => currentState?.StateID ?? 0;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<GenTwoNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Line of Sight
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if GenTwo has a clear line of sight to the player.
    /// Raycasts from GenTwo +1m up to player +1m up to avoid floor clipping.
    /// </summary>
    public bool HasLineOfSightToPlayer()
    {
        if (playerTransform == null) return false;

        Vector3 origin = transform.position + Vector3.up;
        Vector3 target = playerTransform.position + Vector3.up;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, surfaceLayerMask))
        {
            // Hit a surface before reaching the player → no LOS
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if GenTwo has a clear path to a specific world position.
    /// Raycasts from GenTwo +1m up to targetPoint +1m up.
    /// </summary>
    public bool HasClearPathTo(Vector3 targetPoint)
    {
        Vector3 origin = transform.position + Vector3.up;
        Vector3 target = targetPoint + Vector3.up;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, surfaceLayerMask))
        {
            return false;
        }

        return true;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Intercept Calculation
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculates the optimal intercept point on the player's dash trajectory.
    /// Returns the direction GenTwo should dash toward, or Vector3.zero if no valid intercept exists.
    /// 
    /// Uses proper intercept geometry:
    ///   Player moves along: P(t) = playerPos + playerDash.DashSpeed * playerDir * t
    ///   GenTwo must reach P(t) in the same time t:
    ///     |P(t) - genTwoPos| = genTwoSpeed * t
    /// 
    /// The intercept point is clamped to the player's maximum dash distance
    /// to prevent GenTwo from aiming at a point the player will never reach.
    /// 
    /// Additionally checks LOS to the intercept point — returns Vector3.zero
    /// if a wall blocks the path.
    /// </summary>
    public Vector3 CalculateInterceptDirection()
    {
        if (playerDash == null || playerCore == null)
            return GetDirectionToTarget();

        Vector3 playerPos = playerTransform.position;
        // Use the player's locked-in dash direction, NOT camera forward.
        // Camera can rotate freely during dash without affecting flight path.
        Vector3 playerDir = playerDash.DashDirection.normalized;
        Vector3 genTwoPos = transform.position;

        float pSpeed = playerDash != null ? playerDash.DashSpeed : 20f;
        float gSpeed = dashSpeed * dashSpeedMultiplier;

        // Maximum time the player will be dashing (distance / speed)
        float maxDist = playerDash != null ? playerDash.DashMaxDistance : 15f;
        float maxPlayerDashTime = maxDist / pSpeed;

        // Relative position: player relative to GenTwo
        Vector3 relPos = playerPos - genTwoPos;

        // Quadratic: (pSpeed² - gSpeed²)t² + 2·pSpeed·dot(relPos, playerDir)·t + |relPos|² = 0
        float a = (pSpeed * pSpeed) - (gSpeed * gSpeed);
        float b = 2f * Vector3.Dot(relPos, playerDir) * pSpeed;
        float c = relPos.sqrMagnitude;

        float discriminant = b * b - 4f * a * c;
        Vector3 interceptPoint;

        if (discriminant >= 0f && Mathf.Abs(a) > 0.001f)
        {
            float sqrtDisc = Mathf.Sqrt(discriminant);
            float t1 = (-b - sqrtDisc) / (2f * a);
            float t2 = (-b + sqrtDisc) / (2f * a);

            // Pick smallest positive t
            float t = -1f;
            if (t1 > 0.01f && t2 > 0.01f) t = Mathf.Min(t1, t2);
            else if (t1 > 0.01f) t = t1;
            else if (t2 > 0.01f) t = t2;

            if (t > 0f)
            {
                // Clamp to player's max dash time — player stops at the wall
                t = Mathf.Min(t, maxPlayerDashTime);
                interceptPoint = playerPos + playerDir * pSpeed * t;
            }
            else
            {
                interceptPoint = FallbackClosestPoint(genTwoPos, playerPos, playerDir);
            }
        }
        else
        {
            interceptPoint = FallbackClosestPoint(genTwoPos, playerPos, playerDir);
        }

        // Clamp fallback to max dash distance too
        float interceptDistAlongPath = Vector3.Dot(interceptPoint - playerPos, playerDir);
        if (interceptDistAlongPath > maxDist)
        {
            interceptPoint = playerPos + playerDir * maxDist;
        }

        // Check if path to intercept point is clear
        if (!HasClearPathTo(interceptPoint))
        {
            hasValidIntercept = false;
            Debug.Log($"[GenTwo] {name}: Intercept point blocked by wall — aborting dash");
            return Vector3.zero;
        }

        // Cache for debug visualization
        lastInterceptPoint = interceptPoint;
        hasValidIntercept = true;

        Vector3 direction = (interceptPoint - genTwoPos).normalized;

        // Debug: log timing info
        float genTwoTravelDist = Vector3.Distance(genTwoPos, interceptPoint);
        float genTwoTravelTime = genTwoTravelDist / gSpeed;
        float playerTravelTime = interceptDistAlongPath / pSpeed;
        Debug.Log($"[GenTwo] {name}: Intercept calculated — GenTwo arrives in {genTwoTravelTime:F2}s, Player in {playerTravelTime:F2}s");

        return direction;
    }

    /// <summary>
    /// Fallback: closest point on the player's dash line, at least 2m ahead.
    /// Used when the quadratic intercept equation has no valid solution.
    /// </summary>
    private Vector3 FallbackClosestPoint(Vector3 genTwoPos, Vector3 playerPos, Vector3 playerDir)
    {
        Vector3 toGenTwo = genTwoPos - playerPos;
        float t = Vector3.Dot(toGenTwo, playerDir);
        t = Mathf.Max(t, 2f);
        return playerPos + playerDir * t;
    }

    /// <summary>
    /// Sets the dash direction. Called once when entering Dashing state.
    /// </summary>
    public void SetDashDirection(Vector3 direction)
    {
        dashDirection = direction.normalized;
        hasHitPlayer = false;
    }

    /// <summary>
    /// Clears the cached intercept data (called when returning to Idle).
    /// </summary>
    public void ClearInterceptData()
    {
        hasValidIntercept = false;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Movement (called by Dashing state)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs one frame of dash movement with segmented raycasting.
    /// Returns true if the dash should end (hit a surface).
    /// </summary>
    public bool ProcessDashMovement()
    {
        // Calculate speed: multiplier only active while player is dashing
        float currentSpeed = dashSpeed;
        if (IsPlayerDashing)
        {
            currentSpeed *= dashSpeedMultiplier;
        }

        // Total distance to move this frame (unscaled for time-slow compatibility)
        float totalMoveDistance = currentSpeed * Time.unscaledDeltaTime;

        // Segment the movement for anti-tunneling
        float segmentDistance = totalMoveDistance / raycastSegments;
        Vector3 currentPos = transform.position;

        for (int i = 0; i < raycastSegments; i++)
        {
            // 1. Check for surface collision (wall/floor)
            if (Physics.Raycast(currentPos, dashDirection, out RaycastHit surfaceHit,
                segmentDistance + 0.5f, surfaceLayerMask))
            {
                // Hit a surface - stop here
                transform.position = surfaceHit.point + surfaceHit.normal * 0.3f;

                // Determine wall vs ground from the surface normal
                DetermineWallOrGround(surfaceHit.normal);

                PlaySound(impactSound);
                return true; // Dash ends
            }

            // 2. Check for player collision (only damages if player is dashing)
            if (!hasHitPlayer && playerTransform != null)
            {
                float distToPlayer = Vector3.Distance(currentPos, playerTransform.position);

                if (distToPlayer <= playerHitRadius)
                {
                    hasHitPlayer = true;

                    // Trigger DashAttack animation (regardless of damage)
                    if (animator != null)
                        animator.SetTrigger("DashAttack");

                    if (IsPlayerDashing)
                    {
                        // Player is dashing → lethal damage!
                        playerCore.TakeDirectDamage(collisionDamage);
                        Debug.Log($"[GenTwo] {name}: INTERCEPTED player during dash! Dealt {collisionDamage} damage!");
                    }
                    else
                    {
                        // Player is NOT dashing → harmless pass-through
                        Debug.Log($"[GenTwo] {name}: Passed through player (player not dashing - no damage)");
                    }

                    // GenTwo does NOT stop on player hit - continues flying
                }
            }

            // 3. Advance position
            currentPos += dashDirection * segmentDistance;
        }

        // Apply final position
        transform.position = currentPos;

        return false; // Dash continues
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helper Methods (exposed for States)
    // ════════════════════════════════════════════════════════════════════════

    public new void SetStateTimer(float t) => base.SetStateTimer(t);
    public new bool UpdateStateTimer() => base.UpdateStateTimer();
    /// <summary>
    /// Rotates toward target using unscaled time (works during slow-mo).
    /// Blocked while GenTwo is on a wall to prevent clipping.
    /// </summary>
    public new void RotateTowardTargetUnscaled()
    {
        if (isOnWall) return;
        base.RotateTowardTargetUnscaled();
    }

    public new void RotateTowardTarget() => base.RotateTowardTarget();
    public new Vector3 GetDirectionToTarget() => base.GetDirectionToTarget();

    /// <summary>
    /// Set the unscaled timer. Use this instead of SetStateTimer for timing
    /// that must be independent of Time.timeScale (e.g., charge and recovery).
    /// </summary>
    public void SetUnscaledTimer(float duration)
    {
        unscaledTimer = duration;
    }

    /// <summary>
    /// Update the unscaled timer using Time.unscaledDeltaTime.
    /// Returns true when the timer has expired.
    /// </summary>
    public bool UpdateUnscaledTimer()
    {
        unscaledTimer -= Time.unscaledDeltaTime;
        return unscaledTimer <= 0f;
    }

    public void PlayChargeSound() => PlaySound(chargeSound);
    public void PlayDashSound() => PlaySound(dashSound);

    /// <summary>
    /// Rotates GenTwo to face its dash direction instantly.
    /// Called at dash start.
    /// </summary>
    public void FaceDirection(Vector3 direction)
    {
        Vector3 flatDir = new Vector3(direction.x, 0f, direction.z).normalized;
        if (flatDir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(flatDir);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Wall/Ground Detection
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bestimmt anhand der Aufprall-Normale ob GenTwo an einer Wand oder
    /// am Boden gelandet ist. Alles über 45° zur Vertikalen = Wand.
    /// Bei Wand-Landing wird die Rotation auf die Surface-Normal gesetzt,
    /// sodass GenTwo von der Wand wegschaut und korrekt aufliegt.
    /// </summary>
    public void DetermineWallOrGround(Vector3 surfaceNormal)
    {
        float angle = Vector3.Angle(surfaceNormal, Vector3.up);
        isOnWall = angle > 45f;

        if (isOnWall)
        {
            // GenTwo schaut von der Wand weg (entlang der Normale)
            Vector3 flatNormal = new Vector3(surfaceNormal.x, 0f, surfaceNormal.z).normalized;
            if (flatNormal.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(flatNormal);
            }
        }

        if (NpcAnimator != null)
            NpcAnimator.SetBool("IsOnWall", isOnWall);

        Debug.Log($"[GenTwo] {name}: Landed on {(isOnWall ? "WALL" : "GROUND")} (angle: {angle:F1}°)");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Animator Override
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GenTwo nutzt keinen MoveSpeed (kein NavMesh) und steuert Stun über
    /// einen Trigger statt den IsStunned-Bool aus NpcBase.
    /// </summary>
    protected override void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetInteger("StateID", GetStateID());
        animator.SetBool("IsDead", isDead);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Damage Immunity During Dash
    // ════════════════════════════════════════════════════════════════════════

    public override void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (IsDashing) return;
        base.TakeDamage(damage, hitPoint, hitDirection);
    }

    public override void ApplyStun(float duration)
    {
        if (IsDashing) return;
        base.ApplyStun(duration);
    }

    public override void OnMeleeDamage(int damage)
    {
        if (IsDashing) return;
        base.OnMeleeDamage(damage);
    }

    public override void OnThrownSwordHit(int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        if (IsDashing) return;
        base.OnThrownSwordHit(damage, swordDirection, hitPoint);
    }

    public override void OnSwordEmbedded()
    {
        if (IsDashing) return;
        base.OnSwordEmbedded();
    }

    public override void OnBulletDamage(int damage, Vector3 bulletDirection, Vector3 hitPoint)
    {
        if (IsDashing) return;
        base.OnBulletDamage(damage, bulletDirection, hitPoint);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Death Override
    // ════════════════════════════════════════════════════════════════════════

    protected override void Die()
    {
        // NavAgent is already disabled for GenTwo, so skip the navAgent cleanup
        // Just handle ragdoll and destroy
        if (isDead) return;

        isDead = true;
        isStunned = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        if (useRagdollOnDeath && ragdollController != null)
        {
            ragdollController.ActivateRagdollWithAccumulatedImpact();
            if (animator != null) animator.enabled = false;
        }
        else
        {
            if (animator != null)
                animator.SetTrigger("Die");
        }

        if (destroyDelay >= 0)
            Destroy(gameObject, destroyDelay);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Player hit radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, playerHitRadius);

        if (!Application.isPlaying) return;
        if (playerTransform == null) return;

        // Line to player
        Gizmos.color = IsPlayerInRange ? Color.yellow : Color.gray;
        Gizmos.DrawLine(transform.position + Vector3.up, playerTransform.position + Vector3.up);

        // Dash direction
        if (IsDashing)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, dashDirection * 10f);
        }
    }



    #endregion
}
