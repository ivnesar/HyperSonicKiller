using UnityEngine;

/// <summary>
/// GenTwo NPC - Melee Interceptor.
/// 
/// Behavior:
/// 1. Idle: Waits dormant. Reacts ONLY when the player is in Dashing state.
/// 2. When the player dashes AND is within detection range AND line of sight:
///    - Calculates a mathematically timed intercept point ONCE.
///    - The intercept solves the time where the player and GenTwo arrive at the same point,
///      after GenTwo has finished its charge windup.
///    - If no valid intercept is found within the player's dash window → stays in Idle.
/// 3. Charging: Visual warning phase. NOT cancellable (except by stun/death).
/// 4. Dashing: Flies toward the intercept point. INVULNERABLE during dash.
///    Surface contacts BEFORE the intercept do not stop the dash; GenTwo keeps sliding.
///    After the intercept has been reached/passed, the next/current surface contact stops him.
///    If the player is still in the main dash when the intercept is reached → lethal damage.
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
    [Tooltip("Maximum unscaled seconds from GenTwo activation in which the mathematical intercept " +
             "is allowed to happen. This is a tuning cap/reaction window, not a fixed impact time. " +
             "Must be greater than chargeDuration.")]
    [SerializeField] private float playerArrivalTime = 1f;

    [Header("Intercept Height Offsets")]
    [Tooltip("Vertikaler Offset (in Metern) für Line-of-Sight/Visualisierung zum Spieler. " +
             "Die Gameplay-Kollision bleibt root-basiert; dieser Wert verschiebt NICHT den echten Abfangpunkt.")]
    [SerializeField] private float playerInterceptHeightOffset = 1.5f;

    [Tooltip("Vertikaler Offset (in Metern) auf GenTwos Root-Position für Laser/Impact-Sphere-Visualisierung. " +
             "Wichtig: Dieser Wert verändert NICHT die mathematische Kollision; GenTwo fängt root-basiert ab.")]
    [SerializeField] private float selfInterceptHeightOffset = 1.0f;

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

    [Tooltip("Minimum number of movement/check segments per frame during the dash. " +
             "Additional segments are added automatically when one frame would move too far.")]
    [SerializeField] private int raycastSegments = 4;

    [Tooltip("Maximale Länge eines einzelnen Dash-Segments. Kleinere Werte reduzieren Tunneling bei sehr hohem Speed.")]
    [SerializeField] private float maxDashSegmentLength = 0.5f;

    [Tooltip("Maximale zusätzliche Flugzeit NACH dem Intercept, falls GenTwo keine Surface mehr berührt. " +
             "Failsafe gegen endloses Fliegen in unerwarteter Level-Geometrie.")]
    [SerializeField] private float maxPostInterceptFlightTime = 1.5f;

    [Tooltip("Abstand (in Metern) entlang der Surface-Normale, mit dem GenTwo beim finalen Stop " +
             "minimal von der Oberfläche weggerückt wird, damit er nicht sichtbar in der Wand steckt.")]
    [SerializeField] private float endPointWallStickOffset = 0.05f;

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

    // CharacterController für kollisionsbewusste Bewegung im Dash.
    // Wird in Awake() gecached. Pflicht-Komponente am GenTwo-Prefab.
    private CharacterController characterController;

    // Intercept data (calculated once at charge start, fixed for entire attack)
    private Vector3 interceptPoint;
    private Vector3 dashDirection;
    private bool hasValidIntercept;

    // Exact intercept timing data. Calculated ONCE at charge start.
    private float interceptArrivalTime;
    private float interceptFlightTime;

    // Dash runtime
    private Vector3 dashStartPosition;
    private float distanceToIntercept;
    private float dashElapsedTime;
    private float postInterceptElapsedTime;
    private bool hasReachedInterceptPoint;
    private bool hasHitPlayer;
    private Vector3 lastSurfaceNormal;

    // Surface state (wall vs ground after landing)
    private bool isOnWall;

    // Unscaled timer — independent of Time.timeScale
    private float unscaledTimer;

    // ── Diagnose-Felder für Surface-/Fallback-Visualisierung ──
    // Bleiben erhalten, damit bestehende Scene-Gizmos weiterhin hilfreich sind.
    private bool diagHasLastCast;
    private Vector3 diagCastFromPos;
    private Vector3 diagCastDirection;
    private float diagCastRadius;
    private bool diagCastSucceeded;
    private string diagCastFailReason = "";
    private Vector3 diagCastHitPoint;        // Wo der Cast getroffen hat (falls Hit)
    private float diagCastHitDistance;       // Wie weit gecastet wurde

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
    public float InterceptArrivalTime => interceptArrivalTime;
    public float InterceptFlightTime => interceptFlightTime;

    /// <summary>
    /// Vertikaler Offset auf GenTwos Root-Position für Laser/Impact-Sphere-Visualisierung.
    /// Beeinflusst NICHT die root-basierte Gameplay-Kollision.
    /// </summary>
    public float SelfInterceptHeightOffset => selfInterceptHeightOffset;

    public PlayerDash PlayerDash => playerDash;

    /// <summary>
    /// Typed animation manager reference for GenTwoStates.
    /// </summary>
    public GenTwoAnimationManager AnimManager { get; private set; }

    /// <summary>
    /// Referenz auf den LaserPointer_Dash für Intercept-Modus Steuerung.
    /// </summary>
    public Gentwo_LaserPointer gentwoLaserPointer { get; private set; }

    /// <summary>Current dash direction (set once, never changes during dash).</summary>
    public Vector3 DashDirection => dashDirection;

    /// <summary>True wenn GenTwo an einer Wand hängt (statt am Boden).</summary>
    public bool IsOnWall => isOnWall;

    /// <summary>True while GenTwo is in the Dashing state.</summary>
    public bool IsDashing => currentState is GenTwoStates.Dashing;

    /// <summary>True if player is within detection range.</summary>
    public bool IsPlayerInRange => DistanceToTarget <= detectionRange;

    /// <summary>Last calculated gameplay intercept point (root-based).</summary>
    public Vector3 InterceptPoint => interceptPoint;

    /// <summary>Visualized intercept point for laser and impact sphere.</summary>
    public Vector3 VisualInterceptPoint => interceptPoint + Vector3.up * selfInterceptHeightOffset;

    /// <summary>True if the last intercept calculation found a valid point.</summary>
    public bool HasValidIntercept => hasValidIntercept;

    /// <summary>
    /// GenTwo only reacts to the main player dash. DashingToSword is intentionally ignored.
    /// </summary>
    public bool IsPlayerInAttackDash =>
        playerCore != null && playerCore.CurrentState == PlayerCore.PlayerState.Dashing;

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

        // CharacterController für kollisionsbewusste Bewegung im Dash.
        // Pflicht-Komponente — ohne diese funktioniert ProcessDashMovement nicht.
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            Debug.LogError($"[GenTwoNpc] {gameObject.name} hat keinen CharacterController! " +
                           "GenTwo benötigt einen CharacterController für die Dash-Bewegung. " +
                           "Bitte am Prefab hinzufügen.");
        }

        AnimManager = GetComponentInChildren<GenTwoAnimationManager>();

        if (AnimManager == null)
        {
            Debug.LogWarning($"[GenTwoNpc] No GenTwoAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }

        // GenTwo uses LaserPointer_Dash, not the standard NpcLaserPointer
        gentwoLaserPointer = GetComponent<Gentwo_LaserPointer>();
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

        ValidateTimingSettings(runtime: true);
    }


    private void OnValidate()
    {
        ValidateTimingSettings(runtime: false);
    }

    private void ValidateTimingSettings(bool runtime)
    {
        playerArrivalTime = Mathf.Max(0.05f, playerArrivalTime);
        dashSpeed = Mathf.Max(0.01f, dashSpeed);
        playerHitRadius = Mathf.Max(0.05f, playerHitRadius);
        raycastSegments = Mathf.Max(1, raycastSegments);
        maxDashSegmentLength = Mathf.Max(0.05f, maxDashSegmentLength);
        maxPostInterceptFlightTime = Mathf.Max(0f, maxPostInterceptFlightTime);
        endPointWallStickOffset = Mathf.Max(0f, endPointWallStickOffset);

        if (chargeDuration >= playerArrivalTime)
        {
            float oldCharge = chargeDuration;
            chargeDuration = Mathf.Max(0.01f, playerArrivalTime * 0.5f);

            if (runtime)
            {
                Debug.LogWarning($"[GenTwo] {name}: chargeDuration ({oldCharge:F2}s) was >= " +
                                 $"playerArrivalTime ({playerArrivalTime:F2}s). Clamped to " +
                                 $"{chargeDuration:F2}s so GenTwo has flight time.");
            }
        }
        else
        {
            chargeDuration = Mathf.Max(0.01f, chargeDuration);
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

        Vector3 origin = transform.position + Vector3.up * selfInterceptHeightOffset;
        Vector3 target = playerTransform.position + Vector3.up * playerInterceptHeightOffset;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        return !Physics.Raycast(origin, direction.normalized, distance, surfaceLayerMask);
    }

    /// <summary>
    /// Utility check for other/debug use. GenTwo's intercept calculation intentionally does NOT
    /// require a clear path to the predicted intercept point.
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
    // Der Intercept wird exakt EINMAL beim Charge-Start berechnet.
    // Danach werden interceptPoint und dashDirection nicht mehr aktualisiert.
    // Wichtig: Es gibt bewusst KEINEN ClearPath-Check zum Intercept-Punkt.
    // Voraussetzung ist nur Line of Sight zum aktuellen Spieler beim Start.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Berechnet einen mathematisch getimten, root-basierten Intercept-Punkt.
    /// Returns true, wenn Spieler und GenTwo denselben Punkt zur selben Zeit erreichen können.
    /// </summary>
    public bool TryCalculateIntercept()
    {
        ClearInterceptData();

        if (playerDash == null || playerCore == null || playerTransform == null)
            return false;

        // DashingToSword ist veraltet und wird für GenTwo bewusst ignoriert.
        if (!IsPlayerInAttackDash)
            return false;

        if (!TrySolveMathematicalIntercept(out Vector3 candidatePoint, out float playerTimeToImpact))
            return false;

        Vector3 candidateDirection = candidatePoint - transform.position;
        if (candidateDirection.sqrMagnitude < 0.0001f)
        {
            Debug.Log($"[GenTwo] {name}: Intercept rejected — target point is too close to GenTwo root.");
            return false;
        }

        interceptPoint = candidatePoint;
        dashDirection = candidateDirection.normalized;
        interceptArrivalTime = playerTimeToImpact;
        interceptFlightTime = playerTimeToImpact - chargeDuration;
        distanceToIntercept = Vector3.Distance(transform.position, interceptPoint);
        hasValidIntercept = true;

        Debug.Log($"[GenTwo] {name}: Intercept valid. " +
                  $"Point={interceptPoint}, playerTime={interceptArrivalTime:F3}s, " +
                  $"charge={chargeDuration:F3}s, flight={interceptFlightTime:F3}s, " +
                  $"distance={distanceToIntercept:F2}m");

        return true;
    }

    /// <summary>
    /// Löst die Intercept-Gleichung:
    /// |playerPosition + playerVelocity * t - genTwoRoot| = dashSpeed * (t - chargeDuration)
    /// mit t > chargeDuration.
    /// </summary>
    private bool TrySolveMathematicalIntercept(out Vector3 solvedPoint, out float solvedPlayerTime)
    {
        solvedPoint = Vector3.zero;
        solvedPlayerTime = 0f;

        Vector3 playerStart = playerTransform.position; // root-basiert: Gameplay-Kollision bleibt am Root.
        Vector3 playerDir = playerDash.DashDirection;
        if (playerDir.sqrMagnitude < 0.0001f)
        {
            Debug.Log($"[GenTwo] {name}: Intercept rejected — player dash direction is zero.");
            return false;
        }

        playerDir.Normalize();

        float playerSpeed = playerDash.DashSpeed;
        if (playerSpeed <= 0.01f || dashSpeed <= 0.01f)
        {
            Debug.Log($"[GenTwo] {name}: Intercept rejected — invalid dash speed. " +
                      $"Player={playerSpeed:F2}, GenTwo={dashSpeed:F2}");
            return false;
        }

        float playerMaxDashTime = playerDash.DashMaxDistance / playerSpeed;
        float maxValidImpactTime = Mathf.Min(playerArrivalTime, playerMaxDashTime);

        if (maxValidImpactTime <= chargeDuration)
        {
            Debug.Log($"[GenTwo] {name}: Intercept rejected — no valid impact window. " +
                      $"maxImpact={maxValidImpactTime:F3}s, charge={chargeDuration:F3}s");
            return false;
        }

        Vector3 playerVelocity = playerDir * playerSpeed;
        Vector3 relativeStart = playerStart - transform.position;
        float genTwoSpeedSquared = dashSpeed * dashSpeed;

        float a = Vector3.Dot(playerVelocity, playerVelocity) - genTwoSpeedSquared;
        float b = 2f * (Vector3.Dot(relativeStart, playerVelocity) + genTwoSpeedSquared * chargeDuration);
        float c = Vector3.Dot(relativeStart, relativeStart) - genTwoSpeedSquared * chargeDuration * chargeDuration;

        float bestTime = float.PositiveInfinity;
        const float epsilon = 0.0001f;

        void ConsiderTime(float t)
        {
            if (t <= chargeDuration + epsilon) return;
            if (t > maxValidImpactTime + epsilon) return;
            if (t < bestTime) bestTime = t;
        }

        if (Mathf.Abs(a) < epsilon)
        {
            if (Mathf.Abs(b) < epsilon)
            {
                Debug.Log($"[GenTwo] {name}: Intercept rejected — degenerate equation.");
                return false;
            }

            ConsiderTime(-c / b);
        }
        else
        {
            float discriminant = b * b - 4f * a * c;
            if (discriminant < -epsilon)
            {
                Debug.Log($"[GenTwo] {name}: Intercept rejected — no real solution. " +
                          $"discriminant={discriminant:F4}");
                return false;
            }

            float sqrt = Mathf.Sqrt(Mathf.Max(0f, discriminant));
            float denominator = 2f * a;
            ConsiderTime((-b - sqrt) / denominator);
            ConsiderTime((-b + sqrt) / denominator);
        }

        if (float.IsPositiveInfinity(bestTime))
        {
            Debug.Log($"[GenTwo] {name}: Intercept rejected — solutions outside valid window. " +
                      $"charge={chargeDuration:F3}s, maxImpact={maxValidImpactTime:F3}s");
            return false;
        }

        solvedPlayerTime = bestTime;
        solvedPoint = playerStart + playerVelocity * bestTime;

        float requiredFlightTime = Vector3.Distance(transform.position, solvedPoint) / dashSpeed;
        float availableFlightTime = solvedPlayerTime - chargeDuration;
        if (Mathf.Abs(requiredFlightTime - availableFlightTime) > 0.02f)
        {
            Debug.LogWarning($"[GenTwo] {name}: Intercept timing drift. " +
                             $"required={requiredFlightTime:F3}s, available={availableFlightTime:F3}s");
        }

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
        interceptArrivalTime = 0f;
        interceptFlightTime = 0f;
        dashStartPosition = transform.position;
        distanceToIntercept = 0f;
        dashElapsedTime = 0f;
        postInterceptElapsedTime = 0f;
        hasReachedInterceptPoint = false;
        hasHitPlayer = false;
        lastSurfaceNormal = Vector3.up;

        diagHasLastCast = false;
        diagCastSucceeded = false;
        diagCastFailReason = "";
        diagCastHitPoint = Vector3.zero;
        diagCastHitDistance = 0f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Movement (called by Dashing state)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prepares the dash. Called by Dashing.Enter().
    /// Resets runtime hit/contact tracking. The intercept itself was already solved once.
    /// </summary>
    public void StartDash()
    {
        hasHitPlayer = false;
        hasReachedInterceptPoint = false;
        dashStartPosition = transform.position;
        distanceToIntercept = Vector3.Distance(dashStartPosition, interceptPoint);
        dashElapsedTime = 0f;
        postInterceptElapsedTime = 0f;
        lastSurfaceNormal = Vector3.up;
    }

    /// <summary>
    /// Performs one frame of dash movement.
    ///
    /// Surface contacts BEFORE the intercept do not end the dash. GenTwo keeps moving/sliding.
    /// Once the intercept point has been reached or passed, the next/current surface contact
    /// ends the dash and sends GenTwo into Recovery.
    ///
    /// Returns true if the dash should end.
    /// </summary>
    public bool ProcessDashMovement()
    {
        if (characterController == null) return true;
        if (!hasValidIntercept || dashDirection.sqrMagnitude < 0.0001f) return true;

        float deltaTime = TimeManager.Instance.GameDeltaTime;
        if (deltaTime <= 0f) return false;

        dashElapsedTime += deltaTime;

        float totalMoveDistance = dashSpeed * deltaTime;
        int dynamicSegments = Mathf.CeilToInt(totalMoveDistance / Mathf.Max(0.05f, maxDashSegmentLength));
        int segmentCount = Mathf.Max(1, Mathf.Max(raycastSegments, dynamicSegments));
        float segmentDistance = totalMoveDistance / segmentCount;

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 beforePos = transform.position;
            float beforeProgress = GetDashProgressDistance(beforePos);

            Vector3 intendedMove = dashDirection * segmentDistance;
            CollisionFlags flags = characterController.Move(intendedMove);
            Vector3 afterPos = transform.position;
            float afterProgress = GetDashProgressDistance(afterPos);

            // Robust gegen Tunneling: prüft das komplette Bewegungssegment gegen die aktuelle Spielerposition.
            TryHitPlayerAlongSegment(beforePos, afterPos);

            bool crossedInterceptThisSegment = beforeProgress < distanceToIntercept && afterProgress >= distanceToIntercept;
            if (crossedInterceptThisSegment || afterProgress >= distanceToIntercept)
            {
                MarkInterceptReached();
            }

            bool touchedSurface = flags != CollisionFlags.None;
            if (touchedSurface && TryResolveSurfaceNormal(beforePos, intendedMove, flags, out Vector3 normal))
            {
                lastSurfaceNormal = normal;
            }

            if (hasReachedInterceptPoint)
            {
                postInterceptElapsedTime += deltaTime / segmentCount;

                // Wenn der Player seinen Dash nicht verlassen hat, wird er beim mathematischen Intercept getroffen.
                TryApplyGuaranteedInterceptHit();

                if (touchedSurface)
                {
                    StopDashOnSurface(lastSurfaceNormal);
                    return true;
                }

                // Failsafe: verhindert endloses Fliegen, falls nach dem Intercept kein Surface mehr kommt.
                if (maxPostInterceptFlightTime > 0f && postInterceptElapsedTime >= maxPostInterceptFlightTime)
                {
                    Debug.LogWarning($"[GenTwo] {name}: Post-intercept surface timeout. " +
                                     $"Stopping dash after {postInterceptElapsedTime:F2}s without surface contact.");
                    StopDashOnSurface(Vector3.up);
                    return true;
                }
            }
        }

        return false;
    }

    private float GetDashProgressDistance(Vector3 worldPosition)
    {
        return Vector3.Dot(worldPosition - dashStartPosition, dashDirection);
    }

    private void MarkInterceptReached()
    {
        if (hasReachedInterceptPoint) return;

        hasReachedInterceptPoint = true;
        postInterceptElapsedTime = 0f;

        Debug.Log($"[GenTwo] {name}: Intercept point reached/passed after " +
                  $"{dashElapsedTime:F3}s dash flight.");
    }

    private void TryHitPlayerAlongSegment(Vector3 segmentStart, Vector3 segmentEnd)
    {
        if (hasHitPlayer || playerTransform == null) return;

        Vector3 playerPos = playerTransform.position;
        Vector3 closestPoint = ClosestPointOnSegment(segmentStart, segmentEnd, playerPos);
        float distance = Vector3.Distance(closestPoint, playerPos);

        if (distance > playerHitRadius)
            return;

        ApplyPlayerDashCollisionDamage("segment sweep");
    }

    private void TryApplyGuaranteedInterceptHit()
    {
        if (hasHitPlayer) return;

        // Der mathematische Intercept bedeutet: Wenn der Spieler noch im Haupt-Dash ist,
        // hat er nicht rechtzeitig reagiert. Dann trifft GenTwo garantiert.
        if (IsPlayerInAttackDash)
        {
            ApplyPlayerDashCollisionDamage("mathematical intercept");
        }
        else
        {
            hasHitPlayer = true;
            Debug.Log($"[GenTwo] {name}: Intercept point reached, but player left dash — no damage.");
        }
    }

    private void ApplyPlayerDashCollisionDamage(string source)
    {
        if (hasHitPlayer) return;

        hasHitPlayer = true;
        AnimManager?.PlayDashAttack();

        if (IsPlayerInAttackDash)
        {
            playerCore.TakeDirectDamage(collisionDamage, gameObject.name);
            Debug.Log($"[GenTwo] {name}: INTERCEPTED player via {source}! " +
                      $"Dealt {collisionDamage} damage.");
        }
        else
        {
            Debug.Log($"[GenTwo] {name}: Passed through player via {source} " +
                      "(player not in main dash — no damage).");
        }
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float abSqr = ab.sqrMagnitude;
        if (abSqr <= 0.0001f) return a;

        float t = Vector3.Dot(point - a, ab) / abSqr;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    private bool TryResolveSurfaceNormal(
        Vector3 beforeMovePosition,
        Vector3 intendedMove,
        CollisionFlags flags,
        out Vector3 normal)
    {
        normal = Vector3.up;

        Vector3 moveDir = intendedMove.sqrMagnitude > 0.0001f
            ? intendedMove.normalized
            : dashDirection;

        float radius = characterController != null
            ? Mathf.Max(0.05f, characterController.radius * 0.95f)
            : 0.25f;

        // Side contacts are the most important for wall-sticking.
        if ((flags & CollisionFlags.Sides) != 0)
        {
            Vector3 sphereOrigin = beforeMovePosition + Vector3.up * Mathf.Max(0.1f, characterController.height * 0.5f);
            float sphereDistance = intendedMove.magnitude + radius + 0.2f;

            if (Physics.SphereCast(
                    sphereOrigin,
                    radius,
                    moveDir,
                    out RaycastHit sideHit,
                    sphereDistance,
                    surfaceLayerMask,
                    QueryTriggerInteraction.Ignore))
            {
                normal = sideHit.normal;
                diagHasLastCast = true;
                diagCastFromPos = sphereOrigin;
                diagCastDirection = moveDir;
                diagCastRadius = radius;
                diagCastSucceeded = true;
                diagCastHitPoint = sideHit.point;
                diagCastHitDistance = sideHit.distance;
                return true;
            }

            normal = -moveDir;
            return true;
        }

        if ((flags & CollisionFlags.Below) != 0)
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 0.2f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHit, 1.0f, surfaceLayerMask, QueryTriggerInteraction.Ignore))
            {
                normal = groundHit.normal;
                return true;
            }

            normal = Vector3.up;
            return true;
        }

        if ((flags & CollisionFlags.Above) != 0)
        {
            normal = Vector3.down;
            return true;
        }

        return false;
    }

    private void StopDashOnSurface(Vector3 surfaceNormal)
    {
        if (surfaceNormal.sqrMagnitude < 0.0001f)
            surfaceNormal = Vector3.up;

        surfaceNormal.Normalize();
        lastSurfaceNormal = surfaceNormal;

        if (endPointWallStickOffset > 0f)
        {
            characterController.Move(surfaceNormal * endPointWallStickOffset);
        }

        DetermineWallOrGround(surfaceNormal);
        PlaySound(impactSound);
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

        gentwoLaserPointer?.ClearInterceptMode();

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
        Gizmos.DrawLine(
            transform.position + Vector3.up * selfInterceptHeightOffset,
            playerTransform.position + Vector3.up * playerInterceptHeightOffset);

        // ── Letzte Surface-Diagnose ──
        if (diagHasLastCast)
        {
            DrawLastCapsuleCastGizmo();
        }

        // Intercept point
        if (hasValidIntercept)
        {
            // Root-basierter Gameplay-Intercept
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(interceptPoint, 0.5f);
            Gizmos.DrawLine(transform.position, interceptPoint);

            // Visualisierter Intercept-Punkt für Laser/Sphere
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(VisualInterceptPoint, 0.35f);
            Gizmos.DrawLine(interceptPoint, VisualInterceptPoint);

            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, dashDirection * Mathf.Max(1f, distanceToIntercept));
        }

        // Dash direction
        if (IsDashing)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, dashDirection * 10f);
        }
    }

    /// <summary>
    /// Visualisiert den letzten CapsuleCast-Versuch.
    /// Funktioniert auch bei Failure — zeigt dann genau wo der Cast gescheitert ist.
    ///
    /// Farbschema:
    ///   - Grün     = Cast erfolgreich
    ///   - Gelb     = Start-Capsule (immer)
    ///   - Rot      = Cast gescheitert (Failure-Capsule am Hit-Punkt oder am Ende)
    ///   - Magenta  = Cast-Richtungslinie
    /// </summary>
    private void DrawLastCapsuleCastGizmo()
    {
        // Start-Capsule (immer gelb-transparent)
        DrawDashCapsuleGizmo(diagCastFromPos, new Color(1f, 1f, 0f, 0.5f));

        // Cast-Richtung als Linie
        float lineLength = diagCastSucceeded || diagCastHitDistance > 0f
            ? diagCastHitDistance
            : 5f;
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(diagCastFromPos, diagCastFromPos + diagCastDirection * lineLength);

        // Bei Failure: Capsule an der Position zeichnen, wo der Cast gestoppt hat
        if (!diagCastSucceeded)
        {
            // Failure-Capsule rot — entweder am Hit-Punkt oder am Ende des Casts
            Vector3 failPos;
            if (diagCastHitDistance > 0f)
            {
                // Cast hat etwas getroffen, aber wurde abgelehnt (z.B. zu früh)
                failPos = diagCastFromPos + diagCastDirection * diagCastHitDistance;

                // Hit-Punkt selbst markieren
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(diagCastHitPoint, 0.15f);
            }
            else
            {
                // Cast hat gar nichts getroffen — zeig Capsule am Ende des Casts
                failPos = diagCastFromPos + diagCastDirection * 5f;
            }

            DrawDashCapsuleGizmo(failPos, Color.red);
        }
    }

    private const float DASH_CAPSULE_RADIUS_FACTOR = 0.9f;

    /// <summary>
    /// Zeichnet eine Wire-Capsule mit denselben Maßen wie GenTwos Dash-Körper.
    /// Die Capsule wird mittig um centerPos platziert.
    ///
    /// Da Unity kein DrawWireCapsule hat, bauen wir sie aus:
    ///   - Wire-Sphere oben (Sphere-Center)
    ///   - Wire-Sphere unten (Sphere-Center)
    ///   - 4 Linien an den Seiten (zylindrischer Mittelteil)
    /// </summary>
    private void DrawDashCapsuleGizmo(Vector3 centerPos, Color color)
    {
        if (characterController == null) return;

        Gizmos.color = color;

        float radius = characterController.radius * DASH_CAPSULE_RADIUS_FACTOR;
        float halfBetweenCenters = Mathf.Max(0f, characterController.height * 0.5f - characterController.radius);

        Vector3 top    = centerPos + Vector3.up   * halfBetweenCenters;
        Vector3 bottom = centerPos + Vector3.down * halfBetweenCenters;

        // Halbkugeln oben und unten
        Gizmos.DrawWireSphere(top,    radius);
        Gizmos.DrawWireSphere(bottom, radius);

        // Vier Seitenlinien (zylindrischer Mittelteil)
        Vector3 right   = Vector3.right   * radius;
        Vector3 forward = Vector3.forward * radius;
        Gizmos.DrawLine(top + right,   bottom + right);
        Gizmos.DrawLine(top - right,   bottom - right);
        Gizmos.DrawLine(top + forward, bottom + forward);
        Gizmos.DrawLine(top - forward, bottom - forward);
    }

    #endregion
}
