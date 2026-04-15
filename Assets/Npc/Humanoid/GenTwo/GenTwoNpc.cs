using UnityEngine;

/// <summary>
/// GenTwo NPC - Melee Interceptor.
/// 
/// Behavior:
/// 1. Idle: Waits dormant. Reacts ONLY when the player is in Dashing state.
/// 2. When the player dashes AND is within detection range AND line of sight:
///    - Calculates an intercept point on the player's dash trajectory.
///    - The intercept point is placed so the player reaches it in exactly
///      playerArrivalTime seconds (unscaled) from the moment GenTwo activates.
///    - GenTwo must be able to reach that point in (playerArrivalTime - chargeDuration) seconds.
///    - If no valid point is found → stays in Idle.
/// 3. Charging: Visual warning phase. NOT cancellable (except by stun/death).
/// 4. Dashing: Flies toward the intercept point. INVULNERABLE during dash.
///    Continues until hitting a surface (wall/floor). Does NOT self-cancel.
///    If the player is still dashing and within hit radius → lethal damage.
///    If the player cancelled their dash → GenTwo flies past harmlessly.
/// 5. Recovery: Stuck to surface for a duration, then back to Idle.
/// 
/// IMPORTANT: GenTwo does NOT use NavMesh. Movement is purely dash-based.
/// All timing uses unscaled time (TimeManager.Instance.GameDeltaTime) because
/// both the player and GenTwo operate with time distortion.
/// 
/// ANIMANCER:
/// - States call typed methods on AnimManager (e.g. AnimManager.PlayCharge()).
/// - DetermineWallOrGround() uses AnimManager.SetOnWall().
/// - ProcessDashMovement() uses AnimManager.PlayDashAttack().
/// - UpdateAnimator() overridden: GenTwo has no NavMesh movement blending.
/// - playerCore is inherited from NpcBase (not locally declared).
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

    [Header("Intercept Timing")]
    [Tooltip("Time in unscaled seconds from GenTwo activation until the player " +
             "reaches the intercept point. This is the player's total reaction window. " +
             "Must be greater than chargeDuration.")]
    [SerializeField] private float playerArrivalTime = 1f;

    [Header("Charge")]
    [Tooltip("Time GenTwo spends charging before dashing (visual warning for the player). " +
             "Must be less than playerArrivalTime.")]
    [SerializeField] private float chargeDuration = 0.5f;

    [Header("Dash")]
    [Tooltip("GenTwo's dash speed")]
    [SerializeField] private float dashSpeed = 25f;

    [Tooltip("Radius for detecting collision with the player during dash")]
    [SerializeField] private float playerHitRadius = 1.2f;

    [Tooltip("Damage dealt to player on collision (while player is dashing)")]
    [SerializeField] private int collisionDamage = 999;

    [Tooltip("Layer mask for surfaces (walls/floors) that stop the dash")]
    [SerializeField] private LayerMask surfaceLayerMask;

    [Tooltip("Number of raycast segments per frame to prevent tunneling")]
    [SerializeField] private int raycastSegments = 4;

    [Header("Recovery")]
    [Tooltip("Time GenTwo stays stuck after a dash before returning to Idle")]
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

    // Player reference (cached for intercept calculations)
    private PlayerDash playerDash;

    // Intercept data (calculated once at charge start, fixed for entire attack)
    private Vector3 interceptPoint;
    private Vector3 dashDirection;
    private bool hasValidIntercept;

    // Dash runtime
    private bool hasHitPlayer;

    // Surface state (wall vs ground after landing)
    private bool isOnWall;

    // Unscaled timer — independent of Time.timeScale
    private float unscaledTimer;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties (accessed by States)
    // ════════════════════════════════════════════════════════════════════════

    public float DetectionRange => detectionRange;
    public float PlayerArrivalTime => playerArrivalTime;
    public float ChargeDuration => chargeDuration;
    public float DashSpeed => dashSpeed;
    public float PlayerHitRadius => playerHitRadius;
    public int CollisionDamage => collisionDamage;
    public LayerMask SurfaceLayerMask => surfaceLayerMask;
    public int RaycastSegments => raycastSegments;
    public float RecoveryDuration => recoveryDuration;

    public PlayerDash PlayerDash => playerDash;

    /// <summary>
    /// Typed animation manager reference for GenTwoStates.
    /// </summary>
    public GenTwoAnimationManager AnimManager { get; private set; }

    /// <summary>
    /// Referenz auf den LaserPointer_Dash für Intercept-Modus Steuerung.
    /// </summary>
    public LaserPointer_Dash LaserPointer { get; private set; }

    /// <summary>Current dash direction (set once, never changes during dash).</summary>
    public Vector3 DashDirection => dashDirection;

    /// <summary>True wenn GenTwo an einer Wand hängt (statt am Boden).</summary>
    public bool IsOnWall => isOnWall;

    /// <summary>True while GenTwo is in the Dashing state.</summary>
    public bool IsDashing => currentState is GenTwoStates.Dashing;

    /// <summary>True if player is within detection range.</summary>
    public bool IsPlayerInRange => DistanceToTarget <= detectionRange;

    /// <summary>Last calculated intercept point (for debug/laser visualization).</summary>
    public Vector3 InterceptPoint => interceptPoint;

    /// <summary>True if the last intercept calculation found a valid point.</summary>
    public bool HasValidIntercept => hasValidIntercept;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();

        // GenTwo does NOT use NavMesh
        if (navAgent != null)
        {
            navAgent.enabled = false;
        }

        AnimManager = GetComponentInChildren<GenTwoAnimationManager>();

        if (AnimManager == null)
        {
            Debug.LogWarning($"[GenTwoNpc] No GenTwoAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }

        // GenTwo uses LaserPointer_Dash, not the standard NpcLaserPointer
        LaserPointer = GetComponent<LaserPointer_Dash>();
    }

    protected override void Start()
    {
        base.Start();

        if (playerTransform != null)
        {
            playerDash = playerTransform.GetComponent<PlayerDash>();
        }

        if (playerCore == null)
        {
            Debug.LogError($"[GenTwo] {name}: PlayerCore not found! GenTwo will not function.");
        }

        // Validate inspector settings
        if (chargeDuration >= playerArrivalTime)
        {
            Debug.LogError($"[GenTwo] {name}: chargeDuration ({chargeDuration}s) must be less than " +
                           $"playerArrivalTime ({playerArrivalTime}s)! GenTwo would have no flight time.");
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
        ChangeState(new GenTwoStates.Idle());
    }

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.GenTwo;
    public override int GetStateID() => currentState?.StateID ?? 0;

    /// <summary>
    /// GenTwo has no NavMesh movement — UpdateAnimator does nothing.
    /// Animation is fully controlled by states via AnimManager.
    /// </summary>
    protected override void UpdateAnimator()
    {
        // Intentionally empty
    }

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
    /// </summary>
    public bool HasLineOfSightToPlayer()
    {
        if (playerTransform == null) return false;

        Vector3 origin = transform.position + Vector3.up;
        Vector3 target = playerTransform.position + Vector3.up;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        return !Physics.Raycast(origin, direction.normalized, distance, surfaceLayerMask);
    }

    /// <summary>
    /// Checks if GenTwo has a clear path to a specific world position.
    /// </summary>
    public bool HasClearPathTo(Vector3 targetPoint)
    {
        Vector3 origin = transform.position + Vector3.up;
        Vector3 target = targetPoint + Vector3.up;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        return !Physics.Raycast(origin, direction.normalized, distance, surfaceLayerMask);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Intercept Calculation
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Berechnet die Intercept-Position einmalig beim Charge-Start.
    /// 
    /// LOGIK:
    /// Der Spieler soll die Impact-Position in genau playerArrivalTime Sekunden
    /// (unscaled) erreichen, ab JETZT (Moment des Charge-Starts).
    /// 
    ///   interceptPoint = playerPos + playerDir × playerSpeed × playerArrivalTime
    /// 
    /// GenTwo hat chargeDuration Sekunden zum Laden. Danach fliegt er los.
    /// Verfügbare Flugzeit für GenTwo:
    /// 
    ///   genTwoFlightTime = playerArrivalTime - chargeDuration
    /// 
    /// GenTwo muss die Distanz zum interceptPoint in genTwoFlightTime schaffen:
    /// 
    ///   benötigteZeit = distanz / dashSpeed
    ///   → benötigteZeit <= genTwoFlightTime ? → valide
    /// 
    /// Zusätzliche Checks:
    /// - Liegt der Punkt innerhalb der max Dash-Distanz des Spielers?
    /// - Hat GenTwo freie Sicht zum Punkt?
    /// 
    /// Returns true wenn ein valider Intercept-Punkt gefunden wurde.
    /// </summary>
    public bool TryCalculateIntercept()
    {
        hasValidIntercept = false;

        if (playerDash == null || playerCore == null || playerTransform == null)
        {
            return false;
        }

        // ── Spieler-Daten zum Zeitpunkt der Berechnung ──
        Vector3 playerPos = playerTransform.position;
        Vector3 playerDir = playerDash.DashDirection.normalized;
        float playerSpeed = playerDash.DashSpeed;
        float playerMaxDist = playerDash.DashMaxDistance;

        // ── Impact-Position: Wo der Spieler in playerArrivalTime Sekunden sein wird ──
        float playerTravelDist = playerSpeed * playerArrivalTime;
        Vector3 candidatePoint = playerPos + playerDir * playerTravelDist;

        // ── Check 1: Liegt der Punkt innerhalb der Spieler-Dash-Reichweite? ──
        if (playerTravelDist > playerMaxDist)
        {
            Debug.Log($"[GenTwo] {name}: Intercept beyond player dash range " +
                      $"({playerTravelDist:F1}m > {playerMaxDist:F1}m)");
            return false;
        }

        // ── Check 2: Hat GenTwo genug Flugzeit? ──
        float genTwoFlightTime = playerArrivalTime - chargeDuration;

        if (genTwoFlightTime <= 0f)
        {
            Debug.LogError($"[GenTwo] {name}: No flight time available! " +
                           $"chargeDuration ({chargeDuration}s) >= playerArrivalTime ({playerArrivalTime}s)");
            return false;
        }

        float distToPoint = Vector3.Distance(transform.position, candidatePoint);
        float requiredTime = distToPoint / dashSpeed;

        if (requiredTime > genTwoFlightTime)
        {
            Debug.Log($"[GenTwo] {name}: Too far — needs {requiredTime:F2}s " +
                      $"but only {genTwoFlightTime:F2}s flight time available");
            return false;
        }

        // ── Check 3: Freie Sicht zum Punkt? ──
        if (!HasClearPathTo(candidatePoint))
        {
            Debug.Log($"[GenTwo] {name}: Path to intercept point blocked by wall");
            return false;
        }

        // ── Alles valide — Intercept-Daten speichern ──
        interceptPoint = candidatePoint;
        dashDirection = (interceptPoint - transform.position).normalized;
        hasValidIntercept = true;

        Debug.Log($"[GenTwo] {name}: Intercept calculated — " +
                  $"point {playerTravelDist:F1}m ahead of player, " +
                  $"GenTwo flight time: {requiredTime:F2}s / {genTwoFlightTime:F2}s available, " +
                  $"player arrival: {playerArrivalTime:F2}s");

        return true;
    }

    /// <summary>
    /// Clears all intercept data. Called when returning to Idle.
    /// </summary>
    public void ClearInterceptData()
    {
        hasValidIntercept = false;
        interceptPoint = Vector3.zero;
        dashDirection = Vector3.zero;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Movement (called by Dashing state)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prepares the dash. Called by Dashing.Enter().
    /// Resets hit tracking.
    /// </summary>
    public void StartDash()
    {
        hasHitPlayer = false;
    }

    /// <summary>
    /// Performs one frame of dash movement with segmented raycasting.
    /// Returns true if the dash should end (hit a surface).
    /// 
    /// Movement uses TimeManager.Instance.GameDeltaTime (= unscaled delta)
    /// so GenTwo moves at real-world speed regardless of slow-mo.
    /// </summary>
    public bool ProcessDashMovement()
    {
        float totalMoveDistance = dashSpeed * TimeManager.Instance.GameDeltaTime;
        float segmentDistance = totalMoveDistance / raycastSegments;
        Vector3 currentPos = transform.position;

        for (int i = 0; i < raycastSegments; i++)
        {
            // 1. Surface collision check (wall/floor)
            if (Physics.Raycast(currentPos, dashDirection, out RaycastHit surfaceHit,
                segmentDistance + 0.5f, surfaceLayerMask))
            {
                transform.position = surfaceHit.point + surfaceHit.normal * 0.3f;
                DetermineWallOrGround(surfaceHit.normal);
                PlaySound(impactSound);
                return true;
            }

            // 2. Player collision check
            if (!hasHitPlayer && playerTransform != null)
            {
                float distToPlayer = Vector3.Distance(currentPos, playerTransform.position);

                if (distToPlayer <= playerHitRadius)
                {
                    hasHitPlayer = true;

                    AnimManager?.PlayDashAttack();

                    if (IsPlayerDashing)
                    {
                        playerCore.TakeDirectDamage(collisionDamage, gameObject.name);
                        Debug.Log($"[GenTwo] {name}: INTERCEPTED player! Dealt {collisionDamage} damage!");
                    }
                    else
                    {
                        Debug.Log($"[GenTwo] {name}: Passed through player (not dashing — no damage)");
                    }
                }
            }

            // 3. Advance position
            currentPos += dashDirection * segmentDistance;
        }

        transform.position = currentPos;
        return false;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helper Methods (exposed for States)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets the unscaled timer (counts down using GameDeltaTime).
    /// </summary>
    public void SetUnscaledTimer(float duration)
    {
        unscaledTimer = duration;
    }

    /// <summary>
    /// Updates the unscaled timer. Returns true when it reaches zero.
    /// </summary>
    public bool UpdateUnscaledTimer()
    {
        unscaledTimer -= TimeManager.Instance.GameDeltaTime;
        return unscaledTimer <= 0f;
    }

    /// <summary>
    /// Returns timer progress from 0 (just started) to 1 (finished).
    /// </summary>
    public float GetUnscaledTimerProgress(float totalDuration)
    {
        if (totalDuration <= 0f) return 1f;
        float elapsed = totalDuration - unscaledTimer;
        return Mathf.Clamp01(elapsed / totalDuration);
    }

    public void PlayChargeSound() => PlaySound(chargeSound);
    public void PlayDashSound() => PlaySound(dashSound);

    /// <summary>
    /// Rotates toward target using unscaled time.
    /// Blocked while GenTwo is on a wall to prevent clipping.
    /// </summary>
    public new void RotateTowardTargetUnscaled()
    {
        if (isOnWall) return;
        base.RotateTowardTargetUnscaled();
    }

    /// <summary>
    /// Instantly faces a direction (horizontal only).
    /// </summary>
    public void FaceDirection(Vector3 direction)
    {
        Vector3 flatDir = new Vector3(direction.x, 0f, direction.z).normalized;
        if (flatDir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(flatDir);
        }
    }

    /// <summary>
    /// Public wrapper for NpcBase.SetAimProgress (protected).
    /// GenTwo uses this instead of StartAimTracking() because the
    /// unscaledTimer is not synchronized with the base stateTimer.
    /// </summary>
    public void SetAimProgressPublic(float progress)
    {
        SetAimProgress(progress);
    }

    /// <summary>
    /// Public wrapper for NpcBase.ResetAimProgress (protected).
    /// </summary>
    public void ResetAimProgressPublic()
    {
        ResetAimProgress();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Wall/Ground Detection
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether GenTwo landed on a wall or floor based on surface normal.
    /// Anything > 45° from vertical = wall.
    /// </summary>
    public void DetermineWallOrGround(Vector3 surfaceNormal)
    {
        float angle = Vector3.Angle(surfaceNormal, Vector3.up);
        isOnWall = angle > 45f;

        if (isOnWall)
        {
            Vector3 flatNormal = new Vector3(surfaceNormal.x, 0f, surfaceNormal.z).normalized;
            if (flatNormal.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(flatNormal);
            }
        }

        AnimManager?.SetOnWall(isOnWall);

        Debug.Log($"[GenTwo] {name}: Landed on {(isOnWall ? "WALL" : "GROUND")} (angle: {angle:F1}°)");
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
        if (isDead) return;

        isStunned = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        LaserPointer?.ClearInterceptMode();

        base.Die();
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

        // Line to player (yellow if in range)
        Gizmos.color = IsPlayerInRange ? Color.yellow : Color.gray;
        Gizmos.DrawLine(transform.position + Vector3.up, playerTransform.position + Vector3.up);

        // Intercept point
        if (hasValidIntercept)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(interceptPoint, 0.5f);
            Gizmos.DrawLine(transform.position, interceptPoint);
        }

        // Dash direction
        if (IsDashing)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, dashDirection * 10f);
        }
    }

    #endregion
}
