using UnityEngine;

/// <summary>
/// Soldier NPC - Schießt auf den Spieler aus der Distanz.
/// Benötigt freie Sichtlinie zum Schießen.
///
/// AIM-IK MIGRATION:
/// - Alte manuelle Aim-Bone-Rotation (LateUpdate, UpdateAimBoneRotation,
///   CalculateTargetPitch) komplett entfernt.
/// - Aiming wird jetzt über AimController gesteuert,
///   der die AimIK-Komponente von RootMotion Final IK wrapped.
/// - States setzen npc.IsAiming → SoldierNpc leitet das an AimController weiter.
///
/// ANIMANCER MIGRATION:
/// - NpcAnimator Property entfernt → AnimManager (SoldierAnimationManager) stattdessen.
/// - States rufen typsichere Methoden auf AnimManager auf (z.B. AnimManager.PlayFire()).
/// - FireShot() nutzt AnimManager.PlayFireShot() statt animator.SetTrigger("Fire").
/// </summary>
public class SoldierNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Combat - Ranges")]
    [SerializeField] private float minShootingRange = 6f;
    [SerializeField] private float maxShootingRange = 18f;
    [SerializeField] private float preferredRange = 12f;

    [Header("Combat - Timing")]
    [SerializeField] private float aimDuration = 0.6f;
    [SerializeField] private float timeBetweenShots = 0.15f;
    [SerializeField] private int shotsPerSalvo = 5;
    [SerializeField] private float reloadDuration = 2.0f;

    [Header("Combat - Accuracy")]
    [SerializeField] private float baseAccuracy = 0.85f;
    [SerializeField] private float accuracySpreadAngle = 5f;

    [Header("Combat - Damage")]
    [SerializeField] private int damagePerShot = 10;

    [Header("Weapon")]
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private SoldierBullet bulletPrefab;
    [Tooltip("Layer für Line-of-Sight Check (sollte Player + Hindernisse enthalten)")]
    [SerializeField] private LayerMask bulletHitMask;
    
    [Tooltip("FOV-Winkel der Mündung in Grad. Wenn Spieler innerhalb dieses Winkels ist, wird direkt auf ihn gezielt.")]
    [SerializeField] private float muzzleAimAssistFOV = 5f;

    [Header("Audio/VFX")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField] private ParticleSystem muzzleFlash;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors
    // ════════════════════════════════════════════════════════════════════════

    public float MinShootingRange => minShootingRange;
    public float MaxShootingRange => maxShootingRange;
    public float PreferredRange => preferredRange;
    public float AimDuration => aimDuration;
    public float TimeBetweenShots => timeBetweenShots;
    public int ShotsPerSalvo => shotsPerSalvo;
    public float ReloadDuration => reloadDuration;

    /// <summary>
    /// Typed animation manager reference for SoldierStates.
    /// </summary>
    public SoldierAnimationManager AnimManager { get; private set; }

    /// <summary>
    /// AimIK-Controller für Oberkörper-Rotation zum Ziel.
    /// </summary>
    public AimController AimController { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<SoldierNpc> currentState;
    public int ShotsFiredInSalvo { get; set; }
    public float NextShotTime { get; set; }

    /// <summary>
    /// Wird von States gesetzt um AimIK zu aktivieren/deaktivieren.
    /// True = AimIK blendet ein, False = AimIK blendet aus.
    /// </summary>
    public bool IsAiming { get; set; }

    // ── Target Lock (Dash-Reaktion) ──
    private PlayerCore playerCore;
    
    /// <summary>
    /// Wenn gesetzt, schießt/zielt der Soldier auf diese Position statt auf die Live-Position.
    /// Wird aktiviert wenn der Spieler während Firing dasht, und beim Reloading zurückgesetzt.
    /// </summary>
    public Vector3? LockedTargetPosition { get; set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();

        // Typsichere Referenz auf den Animation Manager
        AnimManager = GetComponentInChildren<SoldierAnimationManager>();
        if (AnimManager == null)
        {
            Debug.LogWarning($"[SoldierNpc] No SoldierAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }

        // AimController finden
        AimController = GetComponent<AimController>();
        if (AimController == null)
        {
            Debug.LogWarning($"[SoldierNpc] No AimController found on {gameObject.name}! " +
                             "Aim-IK will not work. Add AimController component.");
        }
    }

    protected override void OnStart()
    {
        // PlayerCore-Referenz cachen für Dash-Erkennung
        if (playerTransform != null)
            playerCore = playerTransform.GetComponent<PlayerCore>();
        
        ChangeState(new SoldierStates.Idle());
    }

    protected override void UpdateBehavior()
    {
        if (currentState == null) return;

        var nextState = currentState.Update(this);
        if (nextState != null)
            ChangeState(nextState);

        // AimController jeden Frame mit der aktuellen Zielposition füttern
        UpdateAimController();
    }

    protected override void OnStunStart()
    {
        AimController?.DisableImmediate();
        ChangeState(new SoldierStates.Stunned());
    }

    protected override void OnStunEnd() => ChangeState(new SoldierStates.Idle());

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Soldier;
    public override int GetStateID() => currentState?.StateID ?? 0;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region AimIK Steuerung
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aktualisiert den AimController basierend auf IsAiming und der Zielposition.
    /// Wird jeden Frame aus UpdateBehavior() aufgerufen.
    /// </summary>
    private void UpdateAimController()
    {
        if (AimController == null) return;

        // Weight-Steuerung: States setzen IsAiming, wir leiten es weiter
        if (IsAiming)
            AimController.EnableAim();
        else
            AimController.DisableAim();

        // Zielposition aktualisieren (nutzt gelockte Position falls aktiv)
        AimController.SetTargetPosition(EffectiveTargetPosition);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<SoldierNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Combat
    // ════════════════════════════════════════════════════════════════════════

    public bool IsInShootingRange()
    {
        float dist = DistanceToTarget;
        return dist >= minShootingRange && dist <= maxShootingRange;
    }

    /// <summary>
    /// Prüft ob der Soldier freie Sicht zum Spieler hat.
    /// Nutzt Raycast von der Mündung zur Spieler-Brust.
    /// </summary>
    public bool HasLineOfSight()
    {
        if (playerTransform == null) return false;
        
        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.2f;
        Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, bulletHitMask))
        {
            return hit.collider.CompareTag("Player");
        }

        return true;
    }

    /// <summary>
    /// Prüft ob der Soldier schießen kann (in Reichweite UND freie Sicht).
    /// </summary>
    public bool CanShoot()
    {
        return IsInShootingRange() && HasLineOfSight();
    }

    public void FireShot()
    {
        if (muzzlePoint == null || bulletPrefab == null) return;

        Vector3 direction = CalculateFireDirection();
        direction = ApplySpread(direction);

        var bullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        if (bullet != null)
            bullet.Initialize(direction, damagePerShot, transform, bulletHitMask);

        AnimManager?.PlayFireShot();
        
        PlaySound(fireSound);
        
        if (muzzleFlash != null)
            muzzleFlash.Play();
    }

    /// <summary>
    /// Berechnet die Schussrichtung.
    /// Wenn der Spieler innerhalb des Muzzle-FOV ist → direkt zum Spieler zielen.
    /// Ansonsten → in Laufrichtung schießen (kann verfehlen).
    /// </summary>
    private Vector3 CalculateFireDirection()
    {
        Vector3 muzzleForward = muzzlePoint.forward;
        
        Vector3 targetPoint = EffectiveTargetPosition + Vector3.up * 1f;
        Vector3 directionToTarget = (targetPoint - muzzlePoint.position).normalized;
        
        float angleToTarget = Vector3.Angle(muzzleForward, directionToTarget);
        
        if (angleToTarget <= muzzleAimAssistFOV)
        {
            return directionToTarget;
        }
        
        return muzzleForward;
    }

    private Vector3 ApplySpread(Vector3 direction)
    {
        float spread = Random.value <= baseAccuracy 
            ? accuracySpreadAngle * 0.2f 
            : accuracySpreadAngle;

        return Quaternion.Euler(
            Random.Range(-spread, spread),
            Random.Range(-spread, spread),
            0
        ) * direction;
    }

    public void PlayReloadSound() => PlaySound(reloadSound);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers für States
    // ════════════════════════════════════════════════════════════════════════

    public new void MoveTowardTarget(float speed = 1f) => base.MoveTowardTarget(speed);
    public void MoveToward(Vector3 position, float speed = 1f) => base.MoveToward(position, speed);
    public new void StopMovement() => base.StopMovement();
    public new void RotateTowardTarget() => base.RotateTowardTarget();
    public new void SetStateTimer(float t) => base.SetStateTimer(t);
    public new bool UpdateStateTimer() => base.UpdateStateTimer();
    public new Vector3 GetDirectionToTarget() => base.GetDirectionToTarget();
    
    public new void StartAimTracking(float duration) => base.StartAimTracking(duration);
    public new void SetAimProgress(float progress) => base.SetAimProgress(progress);
    public new void ResetAimProgress() => base.ResetAimProgress();

    // ── Target Lock Helpers ──

    /// <summary>
    /// Gibt die effektive Zielposition zurück:
    /// Gelockte Position (während Dash-Lock) oder Live-Spielerposition.
    /// </summary>
    public Vector3 EffectiveTargetPosition => LockedTargetPosition ?? TargetPosition;

    /// <summary>
    /// True wenn der Spieler gerade dasht (Attack-Dash oder Sword-Dash).
    /// </summary>
    public bool IsPlayerDashing
    {
        get
        {
            if (playerCore == null) return false;
            return playerCore.CurrentState == PlayerCore.PlayerState.Dashing
                || playerCore.CurrentState == PlayerCore.PlayerState.DashingToSword;
        }
    }

    /// <summary>
    /// Rotiert den NPC zu einer beliebigen Weltposition (statt immer zum Spieler).
    /// </summary>
    public void RotateTowardPosition(Vector3 worldPosition) => RotateToward(worldPosition);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Death — AimIK abschalten
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AimIK sofort abschalten bevor NpcBase den Ragdoll-Swap durchführt.
    /// Verhindert dass der IK-Solver auf einen toten/ragdolled NPC wirkt.
    /// </summary>
    protected override void Die()
    {
        AimController?.DisableImmediate();
        base.Die();
    }

    public override void DieWithImpact(Vector3 impactDirection, float forceMagnitude, Vector3? impactPoint = null)
    {
        AimController?.DisableImmediate();
        base.DieWithImpact(impactDirection, forceMagnitude, impactPoint);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // Schussreichweiten
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, minShootingRange);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, preferredRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxShootingRange);

        // Line of Sight Visualisierung
        if (Application.isPlaying && playerTransform != null)
        {
            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.2f;
            Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
            
            Gizmos.color = HasLineOfSight() ? Color.green : Color.red;
            Gizmos.DrawLine(origin, targetPoint);
        }
    }

    #endregion
}
