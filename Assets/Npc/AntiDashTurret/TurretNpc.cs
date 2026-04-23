using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// TURRET NPC - Stationäres Geschütz mit Laserstrahl
// ════════════════════════════════════════════════════════════════════════════
//
// KONZEPT:
//   - Stationär: Kein NavMeshAgent, keine Bewegung.
//   - Feuert nur während der Spieler im Dash-State ist.
//   - Zwingt den Spieler, den Dash abzubrechen, damit andere NPCs angreifen können.
//   - Arbeitet komplett mit Unscaled Time (immun gegen SlowMo).
//
// UNSCALED TIME:
//   Das Turret muss während der Dash-SlowMo normal agieren. Daher:
//   - Eigener State-Timer mit Time.unscaledDeltaTime (GameDeltaTime)
//   - RotateTowardTargetUnscaled() für Rotation
//   - NpcLaserPointer mit useUnscaledTime = true
//   - AimProgress wird manuell per Unscaled-Timer berechnet
//
// STATES:
//   Idle     → Wartet auf dashenden Spieler in Reichweite mit Sichtlinie
//   Charging → Laser-Pointer aktiv, lädt auf (unscaled Timer)
//   Firing   → Sofort-Treffer-Laser abfeuern (tödlich für den Spieler)
//   Stunned  → Durch Thrown Sword gestunnt
//
// SETUP:
//   1. Leeres GameObject mit TurretNpc + NpcLaserPointer (useUnscaledTime=true)
//   2. firePoint-Transform zuweisen (Punkt von dem der Laser abgefeuert wird)
//   3. Kein NavMeshAgent nötig
//   4. Optional: NpcImpactTracker + NpcRagdollSwapper für Tod-Effekte
//
// ════════════════════════════════════════════════════════════════════════════

public class TurretNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Turret - Detection")]
    [Tooltip("Maximale Erkennungsreichweite zum Spieler.")]
    [SerializeField] private float detectionRange = 30f;

    [Header("Turret - Target")]
    [Tooltip("Zielpunkt am Spieler auf den das Turret zielt. " +
             "Wird automatisch von PlayerCore.LaserTarget gesetzt wenn leer.")]
    [SerializeField] private Transform laserTargetOverride;

    [Header("Turret - Charge")]
    [Tooltip("Dauer der Aufladung bevor gefeuert wird (in Echtzeit-Sekunden).")]
    [SerializeField] private float chargeDuration = 1.5f;

    [Header("Turret - Fire")]
    [Tooltip("Transform von dem der Laser abgefeuert wird.")]
    [SerializeField] private Transform firePoint;

    [Tooltip("Schaden des Lasers. Sollte hoch genug sein um den Spieler sofort zu töten.")]
    [SerializeField] private int laserDamage = 9999;

    [Tooltip("Layer-Maske für Line-of-Sight und Laser-Raycast (sollte Player + Hindernisse enthalten).")]
    [SerializeField] private LayerMask laserHitMask;

    [Tooltip("Maximale Reichweite des Feuerlasers.")]
    [SerializeField] private float laserMaxRange = 100f;

    [Header("Turret - Fire Beam Visual")]
    [Tooltip("Material für den Feuer-Laserstrahl. Wird zur Laufzeit instanziert.")]
    [SerializeField] private Material fireBeamMaterial;

    [Tooltip("Farbe des Feuerlasers.")]
    [SerializeField] private Color fireBeamColor = new Color(1f, 0.2f, 0.1f, 1f);

    [Tooltip("Anfangsbreite des Feuerlasers.")]
    [SerializeField] private float fireBeamWidth = 0.15f;

    [Tooltip("Dauer des Aufblitzens und Verblassens in Sekunden (Echtzeit).")]
    [SerializeField] private float fireBeamFadeDuration = 0.3f;

    [Header("Turret - Audio")]
    [SerializeField] private AudioClip chargeSound;
    [SerializeField] private AudioClip fireSound;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Hidden Base Fields
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Felder aus NpcBase die für das Turret irrelevant sind.
    /// Werden vom NpcDebugInspector / Custom Editor ausgeblendet.
    /// </summary>
    public override string[] HiddenBaseFields => new string[]
    {
        "moveSpeed",
        "stoppingDistance",
        "behaviorMode"
    };

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors (für States)
    // ════════════════════════════════════════════════════════════════════════

    public float DetectionRange => detectionRange;
    public float ChargeDuration => chargeDuration;
    public Transform FirePoint => firePoint;

    /// <summary>
    /// Der aufgelöste Zielpunkt am Spieler.
    /// Priorität: laserTargetOverride (Inspector) → PlayerCore.LaserTarget → playerTransform.
    /// </summary>
    public Vector3 LaserTargetPosition
    {
        get
        {
            if (resolvedLaserTarget != null)
                return resolvedLaserTarget.position;
            return TargetPosition + Vector3.up * 1f; // Fallback: Brusthöhe
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<TurretNpc> currentState;

    // ── Laser Target ──
    // Aufgelöster Zielpunkt am Spieler (aus PlayerCore.LaserTarget oder Override).
    private Transform resolvedLaserTarget;

    // ── Unscaled State Timer ──
    // NpcBase.stateTimer nutzt Time.deltaTime — wir brauchen unseren eigenen.
    private float unscaledStateTimer;
    private float unscaledAimTotalDuration;
    private bool isTrackingAimUnscaled;

    // ── Fire Beam ──
    private LineRenderer fireBeamRenderer;
    private float fireBeamTimer;
    private bool isFireBeamActive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        // BehaviorMode auf Stationary setzen BEVOR base.Awake() aufgerufen wird,
        // da NpcBase den NavMeshAgent in Awake konfiguriert.
        behaviorMode = BehaviorMode.Stationary;

        base.Awake();

        SetupFireBeamRenderer();
    }

    protected override void OnStart()
    {
        // Laser-Zielpunkt am Spieler auflösen
        if (laserTargetOverride != null)
        {
            resolvedLaserTarget = laserTargetOverride;
        }
        else if (playerCore != null && playerCore.LaserTarget != null)
        {
            resolvedLaserTarget = playerCore.LaserTarget;
        }
        // Fallback: LaserTargetPosition Property nutzt TargetPosition + Vector3.up

        ChangeState(new TurretStates.Idle());
    }

    /// <summary>
    /// Überschreibt NpcBase.Update() komplett, weil das Turret Unscaled Time braucht.
    /// NpcBase.Update() nutzt Time.deltaTime für Stun-Handling und AimProgress —
    /// beides muss hier mit Unscaled Time laufen.
    /// </summary>
    protected override void Update()
    {
        if (isDead) return;

        // Fire Beam Fade (läuft immer, unabhängig von State)
        UpdateFireBeam();

        // Aim Progress (unscaled)
        UpdateAimProgressUnscaled();

        // Stun-Handling (unscaled)
        if (isStunned)
        {
            HandleStunnedUnscaled();
            return;
        }

        // State-Update
        UpdateBehavior();
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
        ChangeState(new TurretStates.Stunned());
    }

    protected override void OnStunEnd()
    {
        ChangeState(new TurretStates.Idle());
    }

    /// <summary>
    /// Überschreibt Die() um den Death-State zu nutzen.
    /// Der Death-State kümmert sich um die sofortige Zerstörung.
    /// </summary>
    protected override void Die()
    {
        if (isDead) return;

        isDead = true;
        NpcRegistry.Unregister(this);
        isStunned = false;
        IsLaserActive = false;
        IsAimActive = false;
        ResetAimProgress();

        ChangeState(new TurretStates.Death());
    }

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Turret;
    public override int GetStateID() => currentState?.StateID ?? 0;

    /// <summary>
    /// Turret kann per Dash-Autoattack getroffen werden.
    /// </summary>
    public override bool CanBeAutoAttacked => true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Stun Handling (Unscaled Time)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Überschreibt das Stun-System für Unscaled Time.
    /// NpcBase.HandleStunned() nutzt Time.time — das funktioniert nicht
    /// wenn timeScale auf 0.1 steht.
    /// </summary>
    private void HandleStunnedUnscaled()
    {
        StopMovement();

        if (hasSwordEmbedded) return;

        // Stun-Ende prüfen mit unscaled time
        // stunEndTime wurde in NpcBase.ApplyStun() mit Time.time gesetzt,
        // was bei SlowMo zu kurzen Stuns führen würde.
        // Wir überschreiben ApplyStun um das zu korrigieren.
        if (Time.unscaledTime >= stunEndTime)
        {
            // EndStun manuell aufrufen (NpcBase.EndStun ist private)
            isStunned = false;
            IsLaserActive = false;
            IsAimActive = false;
            ResetAimProgress();

            OnStunEnd();
        }
    }

    /// <summary>
    /// Überschreibt ApplyStun um Unscaled Time für stunEndTime zu nutzen.
    /// </summary>
    public override void ApplyStun(float duration)
    {
        if (isDead) return;

        isStunned = true;
        stunEndTime = Time.unscaledTime + duration; // Unscaled!
        IsLaserActive = false;
        IsAimActive = false;
        ResetAimProgress();
        StopMovement();

        aimController?.DisableImmediate();

        animHandler?.PlayStunStart();

        OnStunStart();
    }

    /// <summary>
    /// Überschreibt OnSwordEmbedded um Unscaled Time zu nutzen.
    /// </summary>
    public override void OnSwordEmbedded()
    {
        if (isDead) return;

        hasSwordEmbedded = true;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;
        isStunned = true;
        stunEndTime = float.MaxValue; // Bleibt gestunnt bis Schwert entfernt wird
        IsLaserActive = false;
        IsAimActive = false;
        ResetAimProgress();

        StopMovement();

        aimController?.DisableImmediate();

        animHandler?.PlayStunStart();

        OnStunStart();
    }

    /// <summary>
    /// Überschreibt OnSwordRemoved um Unscaled Time zu nutzen.
    /// </summary>
    public override void OnSwordRemoved(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;

        if (damage > 0)
        {
            pendingSwordRemovalDamage = damage;
            hasPendingSwordDamage = true;
        }

        stunEndTime = Time.unscaledTime + residualStunDuration; // Unscaled!
    }

    /// <summary>
    /// Überschreibt OnSwordDashRemoval um Unscaled Time zu nutzen.
    /// </summary>
    public override void OnSwordDashRemoval(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        if (damage > 0)
        {
            currentHealth -= damage;
            lastDeathType = NpcDeathType.WholeBody;

            PlaySound(hitSound);

            if (currentHealth <= 0)
            {
                Die();
                return;
            }
        }

        stunEndTime = Time.unscaledTime + residualStunDuration; // Unscaled!
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<TurretNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unscaled State Timer
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Setzt den Unscaled State Timer. Nutze dies statt NpcBase.SetStateTimer().
    /// </summary>
    public void SetUnscaledStateTimer(float duration)
    {
        unscaledStateTimer = duration;
    }

    /// <summary>
    /// Aktualisiert den Unscaled State Timer.
    /// Gibt true zurück wenn der Timer abgelaufen ist.
    /// Nutzt GameDeltaTime: läuft bei SlowMo normal, stoppt bei Pause/HitStop.
    /// </summary>
    public bool UpdateUnscaledStateTimer()
    {
        unscaledStateTimer -= TimeManager.Instance.GameDeltaTime;
        return unscaledStateTimer <= 0f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unscaled Aim Progress
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Startet Aim-Tracking mit Unscaled Time.
    /// Der AimProgress wird automatisch in Update() berechnet.
    /// </summary>
    public void StartAimTrackingUnscaled(float totalDuration)
    {
        unscaledAimTotalDuration = Mathf.Max(totalDuration, 0.001f);
        isTrackingAimUnscaled = true;
        SetAimProgress(0f);
    }

    /// <summary>
    /// Stoppt das Unscaled Aim-Tracking und setzt den Progress zurück.
    /// </summary>
    public void ResetAimProgressUnscaled()
    {
        isTrackingAimUnscaled = false;
        unscaledAimTotalDuration = 0f;
        ResetAimProgress();
    }

    /// <summary>
    /// Aktualisiert den AimProgress basierend auf dem Unscaled State Timer.
    /// </summary>
    private void UpdateAimProgressUnscaled()
    {
        if (!isTrackingAimUnscaled) return;

        float elapsed = unscaledAimTotalDuration - unscaledStateTimer;
        float progress = Mathf.Clamp01(elapsed / unscaledAimTotalDuration);
        SetAimProgress(progress);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Detection & Combat
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prüft ob der Spieler in Erkennungsreichweite ist.
    /// </summary>
    public bool IsPlayerInRange()
    {
        return DistanceToTarget <= detectionRange;
    }

    /// <summary>
    /// Prüft ob das Turret freie Sicht zum Spieler hat.
    /// Gleiche Logik wie beim Soldier — Raycast von firePoint zum Spieler.
    /// </summary>
    public bool HasLineOfSight()
    {
        if (playerTransform == null) return false;

        Vector3 origin = firePoint != null ? firePoint.position : transform.position + Vector3.up;
        Vector3 targetPoint = LaserTargetPosition;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, laserHitMask))
        {
            return hit.collider.CompareTag("Player");
        }

        // Kein Hit = freie Sicht (nichts zwischen Turret und Spieler)
        return true;
    }

    /// <summary>
    /// Prüft alle Voraussetzungen für Charge/Fire:
    /// Spieler muss dashen, in Reichweite sein und freie Sicht haben.
    /// </summary>
    public bool CanEngagePlayer()
    {
        return IsPlayerDashing && IsPlayerInRange() && HasLineOfSight();
    }

    /// <summary>
    /// Feuert den tödlichen Laser auf den Spieler ab.
    /// 
    /// Der Laser ist ein Instant-Hit — die Charge-Phase hat bereits LOS geprüft,
    /// daher wird der Schaden DIREKT auf den Spieler appliziert.
    /// Der Raycast dient nur noch für den visuellen Endpunkt des Beams.
    /// </summary>
    public void FireLaser()
    {
        if (playerTransform == null || firePoint == null) return;

        Vector3 origin = firePoint.position;
        Vector3 targetPoint = LaserTargetPosition;
        Vector3 direction = (targetPoint - origin).normalized;

        PlaySound(fireSound);

        // ── Schaden direkt applizieren ──
        // Der Laser ist Instant-Hit und die Charge-Phase hat LOS bestätigt.
        // Raycast-basierter Schaden ist unzuverlässig weil der Spieler im Dash
        // extrem schnell unterwegs ist und der Collider zwischen Frames wandert.
        if (playerCore != null)
        {
            Vector3 attackDir = (playerTransform.position - origin).normalized;
            playerCore.TakeDamage(laserDamage, attackDir, "Turret", transform);
        }

        // ── Visueller Beam: Raycast für Endpunkt ──
        Vector3 endPoint;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, laserMaxRange, laserHitMask))
        {
            endPoint = hit.point;
        }
        else
        {
            endPoint = origin + direction * laserMaxRange;
        }

        ActivateFireBeam(origin, endPoint);
    }

    public void PlayChargeSound() => PlaySound(chargeSound);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Fire Beam Visual
    // ════════════════════════════════════════════════════════════════════════

    private void SetupFireBeamRenderer()
    {
        // Eigenes Child-GameObject für den Feuer-Laser, damit er nicht mit dem
        // LineRenderer des NpcLaserPointer auf dem gleichen GameObject kollidiert.
        var fireBeamGO = new GameObject("FireBeamRenderer");
        fireBeamGO.transform.SetParent(transform, worldPositionStays: false);

        fireBeamRenderer = fireBeamGO.AddComponent<LineRenderer>();
        fireBeamRenderer.positionCount = 2;
        fireBeamRenderer.startWidth = fireBeamWidth;
        fireBeamRenderer.endWidth = fireBeamWidth;
        fireBeamRenderer.useWorldSpace = true;
        fireBeamRenderer.enabled = false;

        fireBeamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fireBeamRenderer.receiveShadows = false;

        if (fireBeamMaterial != null)
        {
            fireBeamRenderer.material = new Material(fireBeamMaterial);
        }
        else
        {
            // Fallback-Shader: URP Unlit bevorzugt, dann Legacy Unlit
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            if (shader != null)
                fireBeamRenderer.material = new Material(shader);
            else
                Debug.LogWarning($"[TurretNpc] {gameObject.name}: Kein Fallback-Shader gefunden! " +
                                 "Weise ein fireBeamMaterial im Inspector zu.");
        }

        ApplyFireBeamColor(fireBeamColor);
    }

    private void ActivateFireBeam(Vector3 start, Vector3 end)
    {
        fireBeamRenderer.SetPosition(0, start);
        fireBeamRenderer.SetPosition(1, end);
        fireBeamRenderer.startWidth = fireBeamWidth;
        fireBeamRenderer.endWidth = fireBeamWidth;
        fireBeamRenderer.enabled = true;

        ApplyFireBeamColor(fireBeamColor);

        fireBeamTimer = fireBeamFadeDuration;
        isFireBeamActive = true;
    }

    private void UpdateFireBeam()
    {
        if (!isFireBeamActive) return;

        fireBeamTimer -= Time.unscaledDeltaTime;

        if (fireBeamTimer <= 0f)
        {
            fireBeamRenderer.enabled = false;
            isFireBeamActive = false;
            return;
        }

        // Fade out: Breite und Alpha nehmen ab
        float t = fireBeamTimer / fireBeamFadeDuration; // 1 → 0
        float currentWidth = fireBeamWidth * t;
        fireBeamRenderer.startWidth = currentWidth;
        fireBeamRenderer.endWidth = currentWidth;

        Color fadedColor = fireBeamColor;
        fadedColor.a = fireBeamColor.a * t;
        ApplyFireBeamColor(fadedColor);
    }

    private void ApplyFireBeamColor(Color color)
    {
        fireBeamRenderer.startColor = color;
        fireBeamRenderer.endColor = color;

        if (fireBeamRenderer.material != null)
            fireBeamRenderer.material.color = color;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers für States
    // ════════════════════════════════════════════════════════════════════════

    // Rotation (Unscaled) — existiert schon in NpcBase
    public new void RotateTowardTargetUnscaled() => base.RotateTowardTargetUnscaled();

    // Aim Progress — Wrapper für States
    public new void SetAimProgress(float progress) => base.SetAimProgress(progress);
    public new void ResetAimProgress() => base.ResetAimProgress();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        // Detection Range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Fire Point
        if (firePoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(firePoint.position, 0.15f);

            // Line of Sight Visualisierung
            if (Application.isPlaying && playerTransform != null)
            {
                Vector3 origin = firePoint.position;
                Vector3 targetPoint = LaserTargetPosition;

                Gizmos.color = HasLineOfSight() ? Color.green : Color.red;
                Gizmos.DrawLine(origin, targetPoint);
            }
        }
    }

    #endregion
}
