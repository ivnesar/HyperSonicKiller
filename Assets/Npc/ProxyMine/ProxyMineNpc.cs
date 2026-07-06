using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// PROXY MINE - Statische Mine die durch ein gerichtetes Laser-Segment explodiert
// ════════════════════════════════════════════════════════════════════════════
//
// Auslöser (jeder startet den Fuse-Timer):
//   1. Player-Bewegungssegment kreuzt das Laser-Segment
//   2. Sword-Removal (Residual-Stun wird ignoriert, Fuse startet sofort)
//   3. Explosions-Schaden von einer anderen Mine / Quelle
//
// Besonderheiten:
//   - Kein HP-System: jeder Schaden aktiviert den Zünder
//   - Kann durch Sword-Throw gestunnt werden (normales Embed/Remove)
//   - Stun pausiert den Fuse-Timer
//   - Kein NavMeshAgent / Animator nötig auf dem GameObject
//   - Spielerkennung ist unabhängig von Trigger-/OverlapBox-Timing
//
// Warn-Sphere:
//   - Child-Objekt mit MeshRenderer (transparentes Unlit-Material)
//   - Wird bei Fuse-Start sichtbar, zeigt den Explosionsradius
//   - Farbe wechselt über AnimationCurve von startColor → endColor
//   - Bei Stun unsichtbar, nach Stun-Ende wieder sichtbar
//   - Radius wird automatisch vom ExplosionSphere-Prefab gelesen
//
// ════════════════════════════════════════════════════════════════════════════

public class ProxyMineNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Hidden Base Fields
    // ════════════════════════════════════════════════════════════════════════

    public override string[] HiddenBaseFields => new[]
    {
        "behaviorMode",      // Wird in OnStart() auf Stationary gesetzt
        "moveSpeed",         // Mine bewegt sich nicht
        "stoppingDistance",  // Mine bewegt sich nicht
        "maxRotationSpeed",  // Mine rotiert nicht
        "useRagdollOnDeath", // Mine explodiert, kein Ragdoll
        "snapTarget",        // Mine braucht keinen Kamera-Snap
    };

    /// <summary>
    /// Mine kann nicht vom Dash-Auto-Attack getroffen werden.
    /// Sword-Throw und Explosions-Schaden funktionieren weiterhin.
    /// </summary>
    public override bool CanBeAutoAttacked => false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Mine - Laser Detection")]
    [Tooltip("Startpunkt und Richtung des Laser-Segments. Der Laser läuft entlang der lokalen Y+-Achse dieses Transforms.")]
    [SerializeField] private Transform rayOrigin;

    [Tooltip("Optionaler direkter Verweis auf den PlayerCore. Wenn leer, wird er beim Start automatisch gesucht.")]
    [SerializeField] private PlayerCore playerCore;

    [Tooltip("LayerMask für Wände / Level-Geometrie. Treffer auf dieser Maske kürzen die effektive Laser-Länge, damit der Laser nicht durch Wände geht.")]
    [SerializeField] private LayerMask rayBlockerMask;

    [Tooltip("Trigger-Interaction für Wand-/Blocker-Treffer. Für normale Level-Collider meistens Ignore verwenden.")]
    [SerializeField] private QueryTriggerInteraction blockerRayTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Tooltip("Wenn aktiv, prüft die Mine zusätzlich Line-of-Sight vom Ray Origin zum nächsten Punkt auf dem Player-Bewegungssegment. Verhindert Auslösen hinter Wandkanten.")]
    [SerializeField] private bool requireLineOfSightToMovementSegment = true;

    [Tooltip("Halbe Dicke des Laser-Detektionssegments bei normaler Bewegung. Sollte nah an der visuellen Laserbreite bleiben, damit die Mine nicht unfair früh auslöst.")]
    [Min(0.001f)]
    [SerializeField] private float normalDetectionRadius = 0.04f;

    [Tooltip("Halbe Dicke des Laser-Detektionssegments während Dash / starker Zeitverzerrung. Macht den Laser robust gegen sehr schnelle unscaled Bewegung.")]
    [Min(0.001f)]
    [SerializeField] private float dashDetectionRadius = 0.25f;

    [Tooltip("Wenn aktiv, erzwingt der Player-State Dashing/SprintDashing den Dash Detection Radius.")]
    [SerializeField] private bool useDashStateForDetectionRadius = true;

    [Tooltip("Wenn der Spieler sich innerhalb eines Frames mindestens so weit bewegt, wird ebenfalls der Dash Detection Radius verwendet - unabhängig vom Player-State.")]
    [Min(0f)]
    [SerializeField] private float fastMovementFrameDistance = 2.5f;

    [Tooltip("Wie alt das Player-Bewegungssegment maximal sein darf. 1 unterstützt übliche Script Execution Orders. Ältere Segmente werden zur aktuellen Position kollabiert.")]
    [Min(0)]
    [SerializeField] private int maxPlayerSegmentAgeFrames = 1;

    [Tooltip("Maximale gültige Länge des Player-Bewegungssegments. Verhindert falsche Laser-Auslösung nach Teleport/Respawn. 0 = keine Begrenzung.")]
    [Min(0f)]
    [SerializeField] private float maxValidPlayerSegmentLength = 40f;

    [Tooltip("Optionaler LineRenderer zur Ingame-Visualisierung des Laser-Segments. Wenn leer, wird ein LineRenderer auf diesem GameObject gesucht. Die Breite wird direkt am LineRenderer eingestellt.")]
    [SerializeField] private LineRenderer rayLineRenderer;

    [Header("Mine - Trigger")]
    [Tooltip("Verzögerung zwischen Auslösung und Explosion (Sekunden)")]
    [SerializeField] private float fuseTime = 1f;

    [Header("Mine - Audio")]
    [SerializeField] private AudioClip fuseSound;

    [Header("Mine - Spawns")]
    [Tooltip("Explosions-Prefab (braucht ExplosionSphere-Script)")]
    [SerializeField] private GameObject explosionPrefab;

    [Tooltip("Schaden der Explosion (wird beim Spawn an ExplosionSphere übergeben)")]
    [SerializeField] private float explosionDamage = 50f;

    [Tooltip("Partikel-Prefab (wird parallel gespawnt, zerstört sich selbst)")]
    [SerializeField] private GameObject particlePrefab;

    [Header("Mine - Warn-Sphere")]
    [Tooltip("Child-Objekt mit MeshRenderer für die Warn-Anzeige")]
    [SerializeField] private MeshRenderer warnSphereRenderer;

    [Tooltip("Startfarbe der Warn-Sphere (Fuse-Beginn)")]
    [SerializeField] private Color warnColorStart = new Color(1f, 0.9f, 0f, 0.15f);

    [Tooltip("Endfarbe der Warn-Sphere (kurz vor Explosion)")]
    [SerializeField] private Color warnColorEnd = new Color(1f, 0f, 0f, 0.4f);

    [Tooltip("Farbverlauf über die Fuse-Zeit (X: 0=Start, 1=Explosion / Y: 0=StartColor, 1=EndColor)")]
    [SerializeField] private AnimationCurve warnColorCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private enum MineState
    {
        Idle,       // Wartet auf Spieler
        Triggered,  // Fuse-Timer läuft
        Exploded    // Hat bereits explodiert
    }

    private MineState mineState = MineState.Idle;
    private float fuseTimer;

    // Merkt sich ob Schaden eingetroffen ist während die Mine gestunnt war.
    // Nach Stun-Ende wird dann der Fuse gestartet.
    private bool damagePendingFuse;

    // Material-Instanz für die Warn-Sphere (damit andere Minen nicht beeinflusst werden)
    private Material warnSphereMaterial;

    // Shader-Property-ID für die Hauptfarbe (gecacht für Performance)
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    private RaycastHit blockerRayHit;

    private float currentEffectiveRayLength;
    private float currentDetectionRadius;
    private Vector3 currentLaserStart;
    private Vector3 currentLaserEnd;
    private Vector3 lastClosestPointOnLaser;
    private Vector3 lastClosestPointOnPlayerSegment;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStart()
    {
        behaviorMode = BehaviorMode.Stationary;
        CacheDetectionReferences();
        SetupDetectionRayVisualization();
        SetupWarnSphere();
    }

    protected override void UpdateBehavior()
    {
        switch (mineState)
        {
            case MineState.Idle:
                UpdateLaserSegmentCache();
                CheckPlayerMovementAgainstLaser();
                UpdateDetectionRayVisualization();
                break;

            case MineState.Triggered:
                UpdateDetectionRayVisualization();
                SyncWarnSphereTransform();
                UpdateFuseTimer();
                UpdateWarnSphereColor();
                break;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Detection
    // ════════════════════════════════════════════════════════════════════════

    private void CacheDetectionReferences()
    {
        if (playerCore == null)
        {
            playerCore = FindFirstObjectByType<PlayerCore>();
        }

    }

    private void SetupDetectionRayVisualization()
    {
        if (rayLineRenderer == null)
        {
            rayLineRenderer = GetComponent<LineRenderer>();
        }

        if (rayLineRenderer == null) return;

        rayLineRenderer.useWorldSpace = true;
        rayLineRenderer.positionCount = 2;
        rayLineRenderer.enabled = true;
        UpdateLaserSegmentCache();
        UpdateDetectionRayVisualization();
    }

    private void UpdateLaserSegmentCache()
    {
        Transform originTransform = GetRayOriginTransform();
        Vector3 origin = originTransform.position;
        Vector3 direction = originTransform.up;

        currentEffectiveRayLength = GetEffectiveRayLength(origin, direction);
        currentDetectionRadius = GetCurrentDetectionRadius();
        currentLaserStart = origin;
        currentLaserEnd = origin + direction * currentEffectiveRayLength;
    }

    /// <summary>
    /// Reliable high-speed detection: checks whether the player's movement segment
    /// from PlayerCore.PreviousDetectionPosition to CurrentDetectionPosition crossed
    /// the laser segment this frame. This does not depend on trigger or physics query timing.
    /// </summary>
    private void CheckPlayerMovementAgainstLaser()
    {
        if (mineState != MineState.Idle) return;

        if (playerCore == null)
        {
            playerCore = FindFirstObjectByType<PlayerCore>();
            if (playerCore == null) return;
        }

        if (currentEffectiveRayLength <= 0f || currentDetectionRadius <= 0f) return;

        if (!TryGetValidPlayerMovementSegment(out Vector3 playerPrevious, out Vector3 playerCurrent))
        {
            return;
        }

        float sqrDistance = SegmentSegmentSqrDistance(
            currentLaserStart,
            currentLaserEnd,
            playerPrevious,
            playerCurrent,
            out lastClosestPointOnLaser,
            out lastClosestPointOnPlayerSegment
        );

        float allowedDistance = currentDetectionRadius + playerCore.MovementDetectionRadius;
        if (sqrDistance > allowedDistance * allowedDistance) return;

        if (!HasLineOfSightToMovementSegment()) return;

        StartFuse();
    }

    private bool HasLineOfSightToMovementSegment()
    {
        if (!requireLineOfSightToMovementSegment) return true;
        if (rayBlockerMask.value == 0) return true;

        Vector3 toTarget = lastClosestPointOnPlayerSegment - currentLaserStart;
        float distance = toTarget.magnitude;
        if (distance <= 0.001f) return true;

        Vector3 direction = toTarget / distance;
        return !Physics.Raycast(
            currentLaserStart,
            direction,
            distance,
            rayBlockerMask,
            blockerRayTriggerInteraction
        );
    }

    /// <summary>
    /// Ermittelt die sichtbare / gültige Laser-Länge. Blocker wie Wände schneiden den Laser ab.
    /// </summary>
    private float GetEffectiveRayLength(Vector3 origin, Vector3 direction)
    {
        float maxRayLength = GetExplosionRadius();

        if (maxRayLength <= 0f) return 0f;

        if (rayBlockerMask.value != 0 && Physics.Raycast(
                origin,
                direction,
                out blockerRayHit,
                maxRayLength,
                rayBlockerMask,
                blockerRayTriggerInteraction))
        {
            return Mathf.Max(0f, blockerRayHit.distance);
        }

        return maxRayLength;
    }

    /// <summary>
    /// Gibt den aktuellen halben Radius des Laser-Detektionssegments zurück.
    /// Normal: klein und visuell fair.
    /// Dash: größer, um schnelle unscaled Bewegung zuverlässig zu erkennen.
    /// </summary>
    private float GetCurrentDetectionRadius()
    {
        float radius = Mathf.Max(0.001f, normalDetectionRadius);
        float dashRadius = Mathf.Max(radius, dashDetectionRadius);

        if ((useDashStateForDetectionRadius && IsPlayerInFastMovementState()) || IsPlayerMovingFastThisFrame())
        {
            radius = dashRadius;
        }

        return radius;
    }

    private bool TryGetValidPlayerMovementSegment(out Vector3 previous, out Vector3 current)
    {
        previous = Vector3.zero;
        current = Vector3.zero;

        if (playerCore == null) return false;

        previous = playerCore.PreviousDetectionPosition;
        current = playerCore.CurrentDetectionPosition;

        // Avoid repeatedly evaluating an old high-speed segment forever if the player
        // is no longer moving. Age <= maxPlayerSegmentAgeFrames supports common script
        // execution orders: mine before player movement and mine after player movement.
        int segmentAge = Time.frameCount - playerCore.LastDetectionMoveFrame;
        if (segmentAge > maxPlayerSegmentAgeFrames)
        {
            previous = current;
        }

        // Teleport/Respawn/Scene reset can create a huge artificial segment.
        // Do not let that trigger lasers through the level.
        if (maxValidPlayerSegmentLength > 0f)
        {
            float maxSqrLength = maxValidPlayerSegmentLength * maxValidPlayerSegmentLength;
            if ((current - previous).sqrMagnitude > maxSqrLength)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsPlayerMovingFastThisFrame()
    {
        if (fastMovementFrameDistance <= 0f) return false;

        float frameDistance = GetPlayerMovementFrameDistance();
        return frameDistance >= fastMovementFrameDistance;
    }

    private float GetPlayerMovementFrameDistance()
    {
        if (playerCore == null)
        {
            playerCore = FindFirstObjectByType<PlayerCore>();
        }

        if (playerCore == null) return 0f;

        if (!TryGetValidPlayerMovementSegment(out Vector3 previous, out Vector3 current))
        {
            return 0f;
        }

        return Vector3.Distance(previous, current);
    }

    private bool IsPlayerInFastMovementState()
    {
        if (playerCore == null)
        {
            playerCore = FindFirstObjectByType<PlayerCore>();
        }

        if (playerCore == null) return false;

        return playerCore.CurrentState == PlayerCore.PlayerState.Dashing ||
               playerCore.CurrentState == PlayerCore.PlayerState.SprintDashing;
    }

    private Transform GetRayOriginTransform()
    {
        return rayOrigin != null ? rayOrigin : transform;
    }

    private Vector3 GetRayEndPosition()
    {
        UpdateLaserSegmentCache();
        return currentLaserEnd;
    }

    private void UpdateDetectionRayVisualization()
    {
        if (rayLineRenderer == null) return;

        rayLineRenderer.SetPosition(0, currentLaserStart);
        rayLineRenderer.SetPosition(1, currentLaserEnd);
    }

    /// <summary>
    /// Squared distance between two finite 3D segments.
    /// Based on the standard closest-points formulation and handles degenerate segments.
    /// </summary>
    private static float SegmentSegmentSqrDistance(
        Vector3 p1,
        Vector3 q1,
        Vector3 p2,
        Vector3 q2,
        out Vector3 closestPoint1,
        out Vector3 closestPoint2)
    {
        const float epsilon = 0.000001f;

        Vector3 d1 = q1 - p1; // Direction vector of segment S1
        Vector3 d2 = q2 - p2; // Direction vector of segment S2
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1); // Squared length of S1
        float e = Vector3.Dot(d2, d2); // Squared length of S2
        float f = Vector3.Dot(d2, r);

        float s;
        float t;

        if (a <= epsilon && e <= epsilon)
        {
            closestPoint1 = p1;
            closestPoint2 = p2;
            return (closestPoint1 - closestPoint2).sqrMagnitude;
        }

        if (a <= epsilon)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);

            if (e <= epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;

                if (denom != 0f)
                {
                    s = Mathf.Clamp01((b * f - c * e) / denom);
                }
                else
                {
                    s = 0f;
                }

                t = (b * s + f) / e;

                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        closestPoint1 = p1 + d1 * s;
        closestPoint2 = p2 + d2 * t;
        return (closestPoint1 - closestPoint2).sqrMagnitude;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Warn-Sphere
    // ════════════════════════════════════════════════════════════════════════

    private void SetupWarnSphere()
    {
        if (warnSphereRenderer == null)
        {
            Debug.LogWarning($"[ProxyMineNpc] '{name}': Kein WarnSphere-Renderer zugewiesen!", this);
            return;
        }

        // Material-Instanz erstellen (verhindert, dass alle Minen dasselbe Material teilen)
        warnSphereMaterial = warnSphereRenderer.material;

        // Warn-Sphere exakt auf die spätere Explosion legen und auf dieselbe Weltgröße setzen.
        // Wichtig: localScale alleine wäre abhängig von Parent-Scale der Mine.
        SyncWarnSphereTransform();

        // Zu Beginn unsichtbar
        warnSphereRenderer.enabled = false;
    }

    /// <summary>
    /// Richtet die Warn-Sphere auf dieselbe Position und denselben Welt-Durchmesser
    /// wie die ExplosionSphere aus. Dadurch bleibt sie unabhängig von Parent-Scale
    /// oder lokalen Offsets im Prefab synchron zur tatsächlichen Explosion.
    /// </summary>
    private void SyncWarnSphereTransform()
    {
        if (warnSphereRenderer == null) return;

        Transform warnTransform = warnSphereRenderer.transform;

        float explosionRadius = GetExplosionRadius();
        float diameter = explosionRadius * 2f;

        // Die Explosion wird in Explode() bei transform.position gespawnt.
        warnTransform.position = transform.position;
        warnTransform.rotation = Quaternion.identity;

        SetWorldScale(warnTransform, Vector3.one * diameter);
    }

    /// <summary>
    /// Setzt die gewünschte Welt-Skalierung auch dann korrekt, wenn das Objekt
    /// als Child unter einem skalierten Parent hängt.
    /// </summary>
    private static void SetWorldScale(Transform target, Vector3 worldScale)
    {
        if (target.parent == null)
        {
            target.localScale = worldScale;
            return;
        }

        Vector3 parentScale = target.parent.lossyScale;
        target.localScale = new Vector3(
            DivideSafe(worldScale.x, parentScale.x),
            DivideSafe(worldScale.y, parentScale.y),
            DivideSafe(worldScale.z, parentScale.z)
        );
    }

    private static float DivideSafe(float value, float divisor)
    {
        return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
    }

    /// <summary>
    /// Liest den maxRadius vom ExplosionSphere-Prefab.
    /// Fallback: 5f wenn kein Prefab oder Script vorhanden.
    /// </summary>
    private float GetExplosionRadius()
    {
        if (explosionPrefab == null) return 5f;

        ExplosionSphere explosionSphere = explosionPrefab.GetComponent<ExplosionSphere>();
        if (explosionSphere == null) return 5f;

        return explosionSphere.MaxRadius;
    }

    private void ShowWarnSphere()
    {
        if (warnSphereRenderer == null) return;
        SyncWarnSphereTransform();
        warnSphereRenderer.enabled = true;
    }

    private void HideWarnSphere()
    {
        if (warnSphereRenderer == null) return;
        warnSphereRenderer.enabled = false;
    }

    /// <summary>
    /// Aktualisiert die Farbe der Warn-Sphere basierend auf dem Fuse-Fortschritt.
    /// </summary>
    private void UpdateWarnSphereColor()
    {
        if (warnSphereMaterial == null) return;

        // Fuse-Fortschritt: 0 = gerade gestartet, 1 = kurz vor Explosion
        float fuseProgress = 1f - (fuseTimer / fuseTime);
        fuseProgress = Mathf.Clamp01(fuseProgress);

        // AnimationCurve auswerten → steuert den Blend zwischen den Farben
        float curveValue = warnColorCurve.Evaluate(fuseProgress);

        Color currentColor = Color.Lerp(warnColorStart, warnColorEnd, curveValue);
        warnSphereMaterial.SetColor(BaseColorID, currentColor);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Fuse Timer
    // ════════════════════════════════════════════════════════════════════════

    private void StartFuse()
    {
        if (mineState != MineState.Idle) return;

        mineState = MineState.Triggered;
        fuseTimer = fuseTime;
        PlaySound(fuseSound);
        ShowWarnSphere();
    }

    private void UpdateFuseTimer()
    {
        if (isStunned) return;

        fuseTimer -= Time.deltaTime;

        if (fuseTimer <= 0f)
        {
            Explode();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Explosion
    // ════════════════════════════════════════════════════════════════════════

    private void Explode()
    {
        mineState = MineState.Exploded;

        HideWarnSphere();
        if (rayLineRenderer != null)
        {
            rayLineRenderer.enabled = false;
        }

        Vector3 spawnPos = transform.position;

        if (explosionPrefab != null)
        {
            GameObject explosionGO = Instantiate(explosionPrefab, spawnPos, Quaternion.identity);

            // Schaden an die ExplosionSphere übergeben
            ExplosionSphere explosionSphere = explosionGO.GetComponent<ExplosionSphere>();
            if (explosionSphere != null)
            {
                explosionSphere.SetDamage(explosionDamage);
            }
        }

        if (particlePrefab != null)
        {
            Instantiate(particlePrefab, spawnPos, Quaternion.identity);
        }

        Destroy(gameObject);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Stun
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStunStart()
    {
        // Warn-Sphere verstecken während die Mine gestunnt ist
        HideWarnSphere();
    }

    protected override void OnStunEnd()
    {
        // Nach Stun-Ende: wenn Schaden aufgelaufen ist → Fuse starten
        if (damagePendingFuse)
        {
            damagePendingFuse = false;
            StartFuse();
        }

        // Warn-Sphere wieder anzeigen wenn der Fuse bereits läuft
        if (mineState == MineState.Triggered)
        {
            ShowWarnSphere();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Damage
    // ════════════════════════════════════════════════════════════════════════

    // ── Generischer Schaden (z.B. von ExplosionSphere) ──────────────────

    public override void TakeDamage(float damage)
    {
        if (mineState == MineState.Exploded) return;
        TriggerFuseFromDamage();
    }

    public override void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (mineState == MineState.Exploded) return;
        TriggerFuseFromDamage();
    }

    // ── Melee: ignoriert (Mine reagiert nicht auf Schwerthiebe) ──────────

    public override void OnMeleeDamage(int damage) { }

    // ── Bullet: aktiviert Fuse ──────────────────────────────────────────

    public override void OnBulletDamage(int damage, Vector3 bulletDirection, Vector3 hitPoint)
    {
        if (mineState == MineState.Exploded) return;
        TriggerFuseFromDamage();
    }

    // ── Sword Throw: Embed = Stun, Removal = sofortiger Fuse-Start ──────
    // OnThrownSwordHit, OnSwordEmbedded bleiben von NpcBase.
    // Bei Removal (Recall oder Dash) wird der Residual-Stun ignoriert.

    // ── Hinweis zu Sword-Removal ────────────────────────────────────────
    // Mine ignoriert den Residual-Stun nach Sword-Removal und startet den
    // Fuse sofort. Stun wird aktiv beendet (stunEndTime = 0), damit
    // UpdateFuseTimer() nicht durch isStunned pausiert wird.

    public override void OnSwordRemoved(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;

        // Kein pending sword damage (Mine hat keine HP)
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        // Residual-Stun überspringen und Fuse sofort starten
        stunEndTime = 0f;
        damagePendingFuse = false;
        StartFuse();
    }

    public override void OnSwordDashRemoval(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;

        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        // Residual-Stun überspringen und Fuse sofort starten
        stunEndTime = 0f;
        damagePendingFuse = false;
        StartFuse();
    }

    // ── Gemeinsame Fuse-Aktivierung durch Schaden ───────────────────────

    private void TriggerFuseFromDamage()
    {
        if (isStunned)
        {
            damagePendingFuse = true;
            return;
        }

        StartFuse();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Death (Mine stirbt nicht normal)
    // ════════════════════════════════════════════════════════════════════════

    protected override void Die()
    {
        // Mine stirbt nicht durch HP-Verlust, sie explodiert.
        if (mineState != MineState.Exploded)
        {
            Explode();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    private void OnDestroy()
    {
        // Material-Instanz aufräumen (verhindert Memory Leak)
        if (warnSphereMaterial != null)
        {
            Destroy(warnSphereMaterial);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Abstract Implementations
    // ════════════════════════════════════════════════════════════════════════

    public override string GetCurrentStateName() => mineState.ToString();
    public override NpcType GetNpcType() => NpcType.ProxyMine;
    public override int GetStateID() => (int)mineState;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        // Explosionsradius (zeigt synchronisierten Wert)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, GetExplosionRadius());

        // Laser-Segment: startet am Origin und läuft entlang lokaler Y+-Achse.
        Transform originTransform = GetRayOriginTransform();
        Vector3 rayStart = originTransform.position;
        Vector3 direction = originTransform.up;
        float length = Application.isPlaying && currentEffectiveRayLength > 0f
            ? currentEffectiveRayLength
            : GetExplosionRadius();
        float radius = Application.isPlaying && currentDetectionRadius > 0f
            ? currentDetectionRadius
            : Mathf.Max(0.001f, normalDetectionRadius);
        Vector3 rayEnd = rayStart + direction * length;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(rayStart, 0.1f);
        Gizmos.DrawLine(rayStart, rayEnd);

        // Grobe Visualisierung des Laser-Detektionsradius als Box.
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(
            rayStart + direction * (length * 0.5f),
            originTransform.rotation,
            Vector3.one
        );
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(radius * 2f, length, radius * 2f));
        Gizmos.matrix = oldMatrix;
    }

    #endregion
}

