using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GEN ONE NPC - Frühere Version des Spielercharakters
// ════════════════════════════════════════════════════════════════════════════
//
// VERHALTEN:
// 1. Wartet in Idle bis der Spieler einen Dash startet
// 2. Reagiert sofort mit eigenem Dash zum Spieler (Homing-Flugbahn)
// 3. Bei Treffer von vorne → Spieler stirbt
// 4. Bei Treffer von hinten/seitlich → GenOne nimmt Schaden
// 5. Nach Dash: Klebt an Wand oder steht auf Boden
// 6. Kann nur im Idle-State gestunnt werden (Dash-Immunität)
//
// DASH-ENDPUNKT-LOGIK:
// - Raycast von GenOne durch Spielerkopf (pos + Vector3.up) zur Wand dahinter
// - GenOne stoppt erst an dieser Oberfläche, nicht vorher
// - Verhindert frühzeitiges Steckenbleiben an Boden/Wänden vor dem Spieler
// - Sweep-SphereCast über die volle Frame-Distanz verhindert Tunneling
//
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// GenOne NPC - Ein früherer Cyborg-Prototyp der auf Spieler-Dashes reagiert.
/// Dasht zum Spieler wenn dieser dasht, mit aggressivem Homing.
/// Kann nur besiegt werden durch Angriffe von hinten/seitlich.
/// </summary>
public class GenOneNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields - Activation
    // ════════════════════════════════════════════════════════════════════════

    [Header("Activation")]
    [Tooltip("Maximale Distanz in der GenOne auf Spieler-Dash reagiert")]
    [SerializeField] private float activationRange = 25f;

    [Tooltip("Layer die Line-of-Sight blockieren (Wände, Hindernisse)")]
    [SerializeField] private LayerMask losBlockingLayers;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields - Dash
    // ════════════════════════════════════════════════════════════════════════

    [Header("Dash")]
    [Tooltip("Geschwindigkeit des GenOne-Dash (sollte schneller als Spieler sein)")]
    [SerializeField] private float dashSpeed = 60f;

    [Tooltip("Geschwindigkeits-Multiplikator während Spieler-Dash (0.3 = 30% Geschwindigkeit)")]
    [SerializeField] private float slowMotionSpeedMultiplier = 0.3f;

    [Tooltip("Stärke der Homing-Korrektur (höher = aggressiver)")]
    [SerializeField] private float homingStrength = 12f;

    [Tooltip("Radius für Spieler-Treffer-Erkennung")]
    [SerializeField] private float hitRadius = 1.5f;

    [Tooltip("Layer für Kollisionserkennung (Wände, Boden)")]
    [SerializeField] private LayerMask collisionLayers;

    [Tooltip("Layer für Spieler-Erkennung")]
    [SerializeField] private LayerMask playerLayer;

    [Tooltip("Höhen-Offset für das Dash-Ziel am Spieler (1 = Kopfhöhe)")]
    [SerializeField] private float playerHeadOffset = 1f;

    [Tooltip("Maximale Raycast-Distanz für den Endpunkt hinter dem Spieler")]
    [SerializeField] private float maxEndpointRaycastDistance = 200f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields - Cooldown
    // ════════════════════════════════════════════════════════════════════════

    [Header("Cooldown")]
    [Tooltip("Wartezeit nach einem Dash bevor GenOne erneut reagieren kann")]
    [SerializeField] private float postDashCooldown = 0f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields - Combat
    // ════════════════════════════════════════════════════════════════════════

    [Header("Combat")]
    [Tooltip("Schaden den der Spieler bei frontalem Treffer erleidet (999 = Instant-Kill)")]
    [SerializeField] private int frontalDamage = 999;

    [Tooltip("Winkel in Grad der als 'frontal' gilt (z.B. 60 = ±60° = 120° Kegel vorne)")]
    [SerializeField] private float frontalAngleThreshold = 60f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields - Wall Stick
    // ════════════════════════════════════════════════════════════════════════

    [Header("Wall Stick")]
    [Tooltip("Offset von der Wand beim Kleben")]
    [SerializeField] private float wallStickOffset = 0.5f;

    [Tooltip("Maximaler Winkel für Boden-Erkennung (< Wert = Boden)")]
    [SerializeField] private float maxFloorAngle = 45f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields - Feedback
    // ════════════════════════════════════════════════════════════════════════

    [Header("Feedback (VFX/SFX)")]
    [SerializeField] private AudioClip dashStartSound;
    [SerializeField] private AudioClip dashImpactSound;
    [SerializeField] private AudioClip playerHitSound;
    [SerializeField] private ParticleSystem dashTrailEffect;
    [SerializeField] private ParticleSystem impactEffect;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors (für States)
    // ════════════════════════════════════════════════════════════════════════

    public float ActivationRange => activationRange;
    public float DashSpeed => dashSpeed;
    public float SlowMotionSpeedMultiplier => slowMotionSpeedMultiplier;
    public float HomingStrength => homingStrength;
    public float HitRadius => hitRadius;
    public float PostDashCooldown => postDashCooldown;
    public int FrontalDamage => frontalDamage;
    public float FrontalAngleThreshold => frontalAngleThreshold;
    public float WallStickOffset => wallStickOffset;
    public float MaxFloorAngle => maxFloorAngle;
    public LayerMask CollisionLayers => collisionLayers;
    public LayerMask PlayerLayer => playerLayer;
    public LayerMask LosBlockingLayers => losBlockingLayers;
    public Animator NpcAnimator => animator;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<GenOneNpc> currentState;

    // Cached player references
    private PlayerCore playerCore;
    private PlayerDash playerDash;

    // Dash state
    private Vector3 dashDirection;
    private Vector3 stuckPosition;
    private Vector3 stuckSurfaceNormal;
    private bool isStuckToWall;
    private float cooldownEndTime;

    // Dash endpoint (berechnet jeden Frame)
    private Vector3 currentDashEndpoint;
    private bool hasDashEndpoint;

    // Dash immunity flag
    private bool isDashing;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>True wenn GenOne gerade im Dash ist (immun gegen Melee).</summary>
    public bool IsDashing => isDashing;

    /// <summary>True wenn GenOne an einer Wand klebt.</summary>
    public bool IsStuckToWall => isStuckToWall;

    /// <summary>Aktuelle Dash-Richtung (normalisiert).</summary>
    public Vector3 DashDirection => dashDirection;

    /// <summary>Position an der GenOne klebt.</summary>
    public Vector3 StuckPosition => stuckPosition;

    /// <summary>Normale der Oberfläche an der GenOne klebt.</summary>
    public Vector3 StuckSurfaceNormal => stuckSurfaceNormal;

    /// <summary>True wenn Cooldown abgelaufen ist.</summary>
    public bool IsCooldownComplete => Time.unscaledTime >= cooldownEndTime;

    /// <summary>Reference zum PlayerCore.</summary>
    public PlayerCore PlayerCore => playerCore;

    /// <summary>Reference zum PlayerDash.</summary>
    public PlayerDash PlayerDash => playerDash;

    /// <summary>Aktueller berechneter Endpunkt des Dashes.</summary>
    public Vector3 CurrentDashEndpoint => currentDashEndpoint;

    /// <summary>True wenn ein gültiger Endpunkt existiert.</summary>
    public bool HasDashEndpoint => hasDashEndpoint;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();

        // NavMeshAgent deaktivieren - GenOne bewegt sich manuell
        if (navAgent != null)
        {
            navAgent.enabled = false;
        }
    }

    protected override void Start()
    {
        base.Start();

        // Player-Referenzen cachen
        CachePlayerReferences();
    }

    protected override void OnStart()
    {
        ChangeState(new GenOneStates.Idle());
    }

    protected override void Update()
    {
        if (isDead) return;

        // Stun nur prüfen wenn NICHT im Dash (Dash-Immunität)
        if (isStunned && !isDashing)
        {
            HandleStunnedState();
            return;
        }

        // State Machine update
        if (currentState != null)
        {
            var nextState = currentState.Update(this);
            if (nextState != null)
            {
                ChangeState(nextState);
            }
        }

        UpdateAnimator();
    }

    protected override void UpdateBehavior()
    {
        // Wird von State Machine übernommen
    }

    protected override void OnStunStart()
    {
        // Nur stunnen wenn nicht im Dash
        if (!isDashing)
        {
            ChangeState(new GenOneStates.Stunned());
        }
    }

    protected override void OnStunEnd()
    {
        ChangeState(new GenOneStates.Idle());
    }

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.GenOne;
    public override int GetStateID() => currentState?.StateID ?? 0;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<GenOneNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    private void HandleStunnedState()
    {
        // Animator update während Stun
        if (animator != null)
        {
            animator.SetBool("IsStunned", true);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Player Detection
    // ════════════════════════════════════════════════════════════════════════

    private void CachePlayerReferences()
    {
        if (playerTransform != null)
        {
            playerCore = playerTransform.GetComponent<PlayerCore>();
            playerDash = playerTransform.GetComponent<PlayerDash>();
        }
    }

    /// <summary>
    /// Prüft ob der Spieler gerade im Dash-State ist.
    /// </summary>
    public bool IsPlayerDashing()
    {
        if (playerCore == null) return false;

        return playerCore.CurrentState == PlayerCore.PlayerState.Dashing;
    }

    /// <summary>
    /// Prüft ob Slow-Motion aktiv ist (Time.timeScale deutlich unter 1).
    /// Wird für Geschwindigkeitsberechnung verwendet.
    /// </summary>
    private bool IsSlowMotionActive()
    {
        return playerCore.CurrentState == PlayerCore.PlayerState.Dashing;
    }

    /// <summary>
    /// Prüft ob der Spieler in Aktivierungsreichweite ist.
    /// </summary>
    public bool IsPlayerInRange()
    {
        return DistanceToTarget <= activationRange;
    }

    /// <summary>
    /// Prüft ob freie Sichtlinie zum Spieler besteht.
    /// </summary>
    public bool HasLineOfSightToPlayer()
    {
        if (playerTransform == null) return false;

        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 targetPoint = playerTransform.position + Vector3.up * 1f;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        // Raycast gegen Blocker
        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, losBlockingLayers))
        {
            // Nur frei wenn wir den Spieler direkt treffen
            return hit.collider.CompareTag("Player");
        }

        // Nichts blockiert = freie Sicht
        return true;
    }

    /// <summary>
    /// Prüft alle Bedingungen für Dash-Aktivierung.
    /// </summary>
    public bool CanActivateDash()
    {
        return IsPlayerDashing() &&
               IsPlayerInRange() &&
               HasLineOfSightToPlayer() &&
               IsCooldownComplete;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Startet den Dash zum Spieler.
    /// </summary>
    public void StartDash()
    {
        if (playerTransform == null) return;

        isDashing = true;
        hasDashEndpoint = false;

        // Initiale Richtung zum Spielerkopf
        Vector3 playerHead = playerTransform.position + Vector3.up * playerHeadOffset;
        dashDirection = (playerHead - transform.position).normalized;

        // Initalen Endpunkt berechnen
        CalculateDashEndpoint();

        // Feedback
        PlayFeedback(dashStartSound, dashTrailEffect);

        if (animator != null)
        {
            animator.SetBool("IsDashing", true);
        }

        Debug.Log($"[GenOneNpc] Dash started towards player! Endpoint valid: {hasDashEndpoint}");
    }

    /// <summary>
    /// Berechnet den Dash-Endpunkt: Raycast von GenOne durch Spielerkopf zur Wand dahinter.
    /// Wird jeden Frame im Dashing-State aufgerufen (Homing = Endpunkt bewegt sich mit).
    /// </summary>
    public void CalculateDashEndpoint()
    {
        if (playerTransform == null)
        {
            hasDashEndpoint = false;
            return;
        }

        Vector3 playerHead = playerTransform.position + Vector3.up * playerHeadOffset;
        Vector3 directionThroughPlayer = (playerHead - transform.position).normalized;

        // Raycast von GenOne durch Spielerkopf hindurch
        if (Physics.Raycast(
            transform.position,
            directionThroughPlayer,
            out RaycastHit hit,
            maxEndpointRaycastDistance,
            collisionLayers))
        {
            currentDashEndpoint = hit.point;
            hasDashEndpoint = true;
        }
        else
        {
            // Kein Treffer → kein gültiger Endpunkt
            hasDashEndpoint = false;
        }
    }

    /// <summary>
    /// Bewegt GenOne während des Dash mit Homing.
    /// Wird jeden Frame vom Dashing-State aufgerufen.
    /// 
    /// Ablauf pro Frame:
    /// 1. Homing-Richtung durch Spielerkopf berechnen
    /// 2. Endpunkt hinter Spieler neu berechnen
    /// 3. Sweep-SphereCast über volle Frame-Distanz (Anti-Tunneling)
    /// 4. Position updaten
    /// </summary>
    public void UpdateDashMovement()
    {
        if (playerTransform == null) return;

        // ── 1. Geschwindigkeits-Multiplikator ──
        bool slowMoActive = IsSlowMotionActive();
        float speedMultiplier = slowMoActive ? slowMotionSpeedMultiplier : 1f;

        // ── 2. Homing: Richtung durch Spielerkopf ──
        // Nur wenn Spieler noch im Dash, sonst geradeaus weiter
        if (IsPlayerDashing())
        {
            Vector3 playerHead = playerTransform.position + Vector3.up * playerHeadOffset;
            Vector3 toPlayerHead = (playerHead - transform.position).normalized;

            dashDirection = Vector3.Slerp(
                dashDirection,
                toPlayerHead,
                homingStrength * speedMultiplier * Time.unscaledDeltaTime
            );
            dashDirection.Normalize();
        }

        // ── 3. Endpunkt jeden Frame neu berechnen (Homing) ──
        CalculateDashEndpoint();

        // ── 4. Rotation zur Flugrichtung ──
        if (dashDirection.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(dashDirection);
        }

        // ── 5. Bewegung berechnen (noch nicht anwenden!) ──
        float frameDistance = dashSpeed * speedMultiplier * Time.unscaledDeltaTime;
        Vector3 movement = dashDirection * frameDistance;

        // ── 6. Position anwenden ──
        // (Kollisionsprüfung erfolgt separat in CheckSurfaceCollision)
        transform.position += movement;
    }

    /// <summary>
    /// Prüft Kollision mit Oberflächen während des Dash via Sweep-SphereCast.
    /// 
    /// Der SphereCast läuft über die volle Frame-Bewegungsdistanz und verhindert Tunneling.
    /// Die Kollision wird nur akzeptiert, wenn der Treffer in der Nähe des berechneten
    /// Endpunkts liegt (hinter dem Spieler), NICHT wenn er vor dem Spieler liegt.
    /// </summary>
    public bool CheckSurfaceCollision(out RaycastHit hit)
    {
        float speedMultiplier = IsSlowMotionActive() ? slowMotionSpeedMultiplier : 1f;
        float frameDistance = dashSpeed * speedMultiplier * Time.unscaledDeltaTime;

        // Sweep-SphereCast von der Position VOR der Bewegung
        // (wir casten von der aktuellen Position rückwärts um die Frame-Distanz,
        //  plus etwas Puffer, um die gerade zurückgelegte Strecke abzudecken)
        Vector3 sweepOrigin = transform.position - dashDirection * frameDistance;
        float sweepDistance = frameDistance + 0.5f; // kleiner Puffer
        float sphereRadius = hitRadius * 0.5f;

        if (Physics.SphereCast(
            sweepOrigin,
            sphereRadius,
            dashDirection,
            out hit,
            sweepDistance,
            collisionLayers))
        {
            // ── Prüfe: Liegt der Treffer HINTER dem Spieler? ──
            if (IsHitBeyondPlayer(hit.point))
            {
                // Treffer liegt hinter dem Spieler → gültige Kollision
                return true;
            }

            // Treffer liegt VOR dem Spieler → ignorieren, weiterfliegen
        }

        // Kein (gültiger) Treffer
        hit = default;
        return false;
    }

    /// <summary>
    /// Prüft ob ein Punkt "hinter dem Spieler" liegt (aus Sicht des GenOne).
    /// Nutzt Dot-Product: Wenn der Vektor vom Spielerkopf zum Punkt
    /// in gleicher Richtung wie der Dash zeigt, liegt er dahinter.
    /// </summary>
    private bool IsHitBeyondPlayer(Vector3 point)
    {
        if (playerTransform == null) return true; // Safety: im Zweifel akzeptieren

        Vector3 playerHead = playerTransform.position + Vector3.up * playerHeadOffset;
        Vector3 playerHeadToHit = point - playerHead;

        // Positiver Dot = Punkt liegt in Dash-Richtung hinter dem Spieler
        float dot = Vector3.Dot(playerHeadToHit, dashDirection);
        return dot > 0f;
    }

    /// <summary>
    /// Prüft Kollision mit dem Spieler während des Dash.
    /// </summary>
    public bool CheckPlayerCollision(out Collider playerCollider)
    {
        playerCollider = null;

        Collider[] hits = Physics.OverlapSphere(transform.position, hitRadius, playerLayer);
        foreach (var col in hits)
        {
            if (col.CompareTag("Player"))
            {
                playerCollider = col;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Beendet den Dash und aktiviert Wall-Stick oder Grounded-State.
    /// </summary>
    public void EndDash(RaycastHit surfaceHit)
    {
        isDashing = false;
        hasDashEndpoint = false;

        // Position mit Offset von der Oberfläche
        stuckPosition = surfaceHit.point + surfaceHit.normal * wallStickOffset;
        stuckSurfaceNormal = surfaceHit.normal;
        transform.position = stuckPosition;

        // Ist es eine Wand oder Boden?
        float angle = Vector3.Angle(surfaceHit.normal, Vector3.up);
        isStuckToWall = angle > maxFloorAngle;

        // Cooldown starten
        cooldownEndTime = Time.unscaledTime + postDashCooldown;

        // Feedback
        PlayFeedback(dashImpactSound, impactEffect);

        if (animator != null)
        {
            animator.SetBool("IsDashing", false);
            animator.SetBool("IsStuckToWall", isStuckToWall);
        }

        // Trail-Effekt stoppen
        if (dashTrailEffect != null)
        {
            dashTrailEffect.Stop();
        }

        Debug.Log($"[GenOneNpc] Dash ended - {(isStuckToWall ? "WALL STICK" : "GROUNDED")}");
    }

    /// <summary>
    /// Beendet den Dash ohne Oberflächenkontakt (z.B. kein Endpunkt gefunden).
    /// GenOne bleibt an aktueller Position stehen.
    /// </summary>
    public void EndDashInAir()
    {
        isDashing = false;
        isStuckToWall = false;
        hasDashEndpoint = false;

        // Cooldown starten
        cooldownEndTime = Time.unscaledTime + postDashCooldown;

        if (animator != null)
        {
            animator.SetBool("IsDashing", false);
            animator.SetBool("IsStuckToWall", false);
        }

        if (dashTrailEffect != null)
        {
            dashTrailEffect.Stop();
        }

        Debug.Log("[GenOneNpc] Dash ended in air - no endpoint found!");
    }

    /// <summary>
    /// Verlässt den Wall-Stick-State.
    /// </summary>
    public void Unstick()
    {
        isStuckToWall = false;

        if (animator != null)
        {
            animator.SetBool("IsStuckToWall", false);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Combat
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verarbeitet Treffer mit dem Spieler.
    /// Spieler im Airborne-State nimmt keinen Schaden (Ausweich-Mechanik).
    /// Prüft ob frontal (Spieler stirbt) oder von hinten/seitlich (GenOne nimmt Schaden).
    /// </summary>
    public void ProcessPlayerHit()
    {
        if (playerCore == null) return;

        // Spieler im Airborne-State nimmt keinen Schaden - erfolgreiches Ausweichen!
        if (playerCore.CurrentState == PlayerCore.PlayerState.Airborne)
        {
            Debug.Log("[GenOneNpc] Player is airborne - attack passes through!");
            return;
        }

        // Richtung vom Spieler zu GenOne
        Vector3 playerToGenOne = (transform.position - playerTransform.position).normalized;

        // Spieler-Blickrichtung (vereinfacht: transform.forward)
        Vector3 playerForward = playerTransform.forward;

        // Winkel zwischen Spieler-Forward und Richtung zu GenOne
        // Wenn Winkel < threshold → Spieler schaut GenOne an → GenOne kommt von vorne
        float angle = Vector3.Angle(playerForward, playerToGenOne);

        if (angle < frontalAngleThreshold)
        {
            // FRONTAL: GenOne trifft Spieler von vorne → Spieler stirbt
            Debug.Log($"[GenOneNpc] FRONTAL HIT! Angle: {angle:F1}° - Player takes {frontalDamage} damage!");

            PlayFeedback(playerHitSound, null);
            playerCore.TakeDirectDamage(frontalDamage);
        }
        else
        {
            // HINTEN/SEITLICH: Spieler hat ausgewichen → GenOne nimmt Dash-Damage
            Debug.Log($"[GenOneNpc] SIDE/BACK HIT! Angle: {angle:F1}° - GenOne vulnerable!");
        }
    }

    /// <summary>
    /// Override: Melee-Schaden nur wenn nicht im Dash.
    /// </summary>
    public override void OnMeleeDamage(int damage)
    {
        if (isDashing)
        {
            Debug.Log("[GenOneNpc] Melee damage ignored - currently dashing!");
            return;
        }

        base.OnMeleeDamage(damage);
    }

    /// <summary>
    /// Override: Thrown Sword nur wenn nicht im Dash.
    /// </summary>
    public override void OnThrownSwordHit(int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        if (isDashing)
        {
            Debug.Log("[GenOneNpc] Thrown sword ignored - currently dashing!");
            return;
        }

        base.OnThrownSwordHit(damage, swordDirection, hitPoint);
    }

    /// <summary>
    /// Override: Stun nur wenn nicht im Dash.
    /// </summary>
    public override void ApplyStun(float duration)
    {
        if (isDashing)
        {
            Debug.Log("[GenOneNpc] Stun ignored - currently dashing!");
            return;
        }

        base.ApplyStun(duration);
    }

    /// <summary>
    /// Override: Sword Embed nur wenn nicht im Dash.
    /// </summary>
    public override void OnSwordEmbedded()
    {
        if (isDashing)
        {
            Debug.Log("[GenOneNpc] Sword embed ignored - currently dashing!");
            return;
        }

        base.OnSwordEmbedded();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Feedback
    // ════════════════════════════════════════════════════════════════════════

    private void PlayFeedback(AudioClip sound, ParticleSystem particles)
    {
        if (sound != null && audioSource != null)
        {
            audioSource.PlayOneShot(sound);
        }

        if (particles != null)
        {
            particles.Play();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    protected override void Die()
    {
        isDashing = false;
        isStuckToWall = false;
        hasDashEndpoint = false;

        if (dashTrailEffect != null)
        {
            dashTrailEffect.Stop();
        }

        base.Die();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Visualization
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        // Aktivierungsreichweite
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, activationRange);

        // Hit-Radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, hitRadius);

        // Dash-Richtung (wenn im Dash)
        if (Application.isPlaying && isDashing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, dashDirection * 5f);

            // Endpunkt anzeigen
            if (hasDashEndpoint)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(currentDashEndpoint, 0.5f);
                Gizmos.DrawLine(transform.position, currentDashEndpoint);
            }
        }

        // Wall-Stick Position und Normal
        if (Application.isPlaying && isStuckToWall)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(stuckPosition, 0.3f);
            Gizmos.DrawRay(stuckPosition, stuckSurfaceNormal * 2f);
        }

        // Line of Sight zum Spieler
        if (Application.isPlaying && playerTransform != null)
        {
            Vector3 origin = transform.position + Vector3.up;
            Vector3 target = playerTransform.position + Vector3.up;

            Gizmos.color = HasLineOfSightToPlayer() ? Color.green : Color.red;
            Gizmos.DrawLine(origin, target);
        }

        // Frontal-Winkel Visualisierung
        if (Application.isPlaying && playerTransform != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Vector3 forward = playerTransform.forward;
            Vector3 playerPos = playerTransform.position + Vector3.up;

            // Linke und rechte Grenze des Frontal-Kegels
            Vector3 leftBound = Quaternion.Euler(0, -frontalAngleThreshold, 0) * forward;
            Vector3 rightBound = Quaternion.Euler(0, frontalAngleThreshold, 0) * forward;

            Gizmos.DrawRay(playerPos, leftBound * 3f);
            Gizmos.DrawRay(playerPos, rightBound * 3f);
        }
    }

    #endregion
}
