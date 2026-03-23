using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GRENADIER NPC - Schießt Anti-Dash Granaten aus der Distanz
// ════════════════════════════════════════════════════════════════════════════
//
// Verhält sich wie der Soldier:
//   - Braucht freie Sichtlinie zum Spieler
//   - Gleicher State-Flow: Idle → MovingToRange → Aiming → Firing → Reloading
//   - AimIK über AimController für Oberkörper-Rotation zum Ziel
//
// Unterschiede zum Soldier:
//   - Einstellbare Magazingröße (Default 1, MGL-Style bis 6)
//   - Granate fliegt auf Parabelbahn zum Spieler-Standort
//   - Granate spawnt eine zeitbegrenzte Anti-Dash-Zone
//   - Größere Reichweite, längere Aim-Dauer, längeres Reload
//   - Kein Muzzle-FOV Aim-Assist (Granate fliegt sowieso zum Ziel)
//
// AIM-IK MIGRATION:
//   - Alte manuelle Aim-Bone-Rotation (LateUpdate, UpdateAimBoneRotation,
//     CalculateTargetPitch) komplett entfernt.
//   - Aiming wird jetzt über AimController gesteuert,
//     der die AimIK-Komponente von RootMotion Final IK wrapped.
//   - States setzen npc.IsAiming → GrenadierNpc leitet das an AimController weiter.
//
// RAGDOLL MIGRATION:
//   - NpcRagdollController entfernt → NpcImpactTracker + NpcRagdollSwapper stattdessen.
//   - Ragdoll-Physik läuft nur noch auf gespawnten Prefabs.
//
// ════════════════════════════════════════════════════════════════════════════

public class GrenadierNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Combat - Ranges")]
    [SerializeField] private float minShootingRange = 8f;
    [SerializeField] private float maxShootingRange = 25f;
    [SerializeField] private float preferredRange = 16f;

    [Header("Combat - Timing")]
    [Tooltip("Dauer der Aim-Phase bevor geschossen wird")]
    [SerializeField] private float aimDuration = 1.2f;

    [Tooltip("Anzahl Granaten im Magazin bevor nachgeladen werden muss")]
    [SerializeField] private int magazineSize = 1;

    [Tooltip("Zeit zwischen einzelnen Schüssen innerhalb eines Magazins (nur relevant bei magazineSize > 1)")]
    [SerializeField] private float timeBetweenShots = 0.8f;

    [Tooltip("Dauer des Reloads nach dem letzten Schuss")]
    [SerializeField] private float reloadDuration = 3.0f;

    [Header("Grenade Launcher")]
    [Tooltip("Mündungspunkt des Granatwerfers")]
    [SerializeField] private Transform muzzlePoint;

    [Tooltip("Prefab der Anti-Dash Granate")]
    [SerializeField] private AntiDashGrenade grenadePrefab;

    [Tooltip("Layer für Line-of-Sight Check")]
    [SerializeField] private LayerMask losCheckMask;

    [Header("Grenade Zone Settings")]
    [Tooltip("Radius der Anti-Dash Zone die am Einschlagspunkt entsteht")]
    [SerializeField] private float grenadeZoneRadius = 6f;

    [Tooltip("Dauer der Anti-Dash Zone in Sekunden")]
    [SerializeField] private float grenadeZoneDuration = 5f;

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
    public int MagazineSize => magazineSize;
    public float TimeBetweenShots => timeBetweenShots;
    public float ReloadDuration => reloadDuration;

    /// <summary>
    /// Typed animation manager reference for GrenadierStates.
    /// </summary>
    public GrenadierAnimationManager AnimManager { get; private set; }

    /// <summary>
    /// AimIK-Controller für Oberkörper-Rotation zum Ziel.
    /// </summary>
    public AimController AimController { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<GrenadierNpc> currentState;

    // Magazine Tracking (vom Firing-State benutzt)
    /// <summary>Wie viele Granaten bereits in dieser Salve abgefeuert wurden.</summary>
    public int ShotsFiredInMagazine { get; set; }

    /// <summary>Frühester Zeitpunkt für den nächsten Schuss (Time.time).</summary>
    public float NextShotTime { get; set; }

    /// <summary>
    /// Wird von States gesetzt um AimIK zu aktivieren/deaktivieren.
    /// True = AimIK blendet ein, False = AimIK blendet aus.
    /// </summary>
    public bool IsAiming { get; set; }

    // Dash-Erkennung für Target-Lock
    private PlayerCore playerCore;

    /// <summary>
    /// Wenn gesetzt, zielt der Grenadier auf diese Position statt auf die Live-Position.
    /// Aktiviert wenn der Spieler während Firing dasht.
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
        AnimManager = GetComponentInChildren<GrenadierAnimationManager>();
        if (AnimManager == null)
        {
            Debug.LogWarning($"[GrenadierNpc] No GrenadierAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }

        // AimController finden
        AimController = GetComponent<AimController>();
        if (AimController == null)
        {
            Debug.LogWarning($"[GrenadierNpc] No AimController found on {gameObject.name}! " +
                             "Aim-IK will not work. Add AimController component.");
        }
    }

    protected override void OnStart()
    {
        // PlayerCore-Referenz cachen für Dash-Erkennung
        if (playerTransform != null)
            playerCore = playerTransform.GetComponent<PlayerCore>();

        ChangeState(new GrenadierStates.Idle());
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
        ChangeState(new GrenadierStates.Stunned());
    }

    protected override void OnStunEnd() => ChangeState(new GrenadierStates.Idle());

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Grenadier;
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

    public void ChangeState(INpcState<GrenadierNpc> newState)
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
    /// Prüft ob der Grenadier freie Sicht zum Spieler hat.
    /// </summary>
    public bool HasLineOfSight()
    {
        if (playerTransform == null) return false;

        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.2f;
        Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, losCheckMask))
        {
            return hit.collider.CompareTag("Player");
        }

        return true;
    }

    /// <summary>
    /// Prüft ob der Grenadier schießen kann (in Reichweite UND freie Sicht).
    /// </summary>
    public bool CanShoot()
    {
        return IsInShootingRange() && HasLineOfSight();
    }

    /// <summary>
    /// Feuert eine Anti-Dash Granate auf die aktuelle Spielerposition.
    /// </summary>
    public void FireGrenade()
    {
        if (muzzlePoint == null || grenadePrefab == null) return;

        // Zielpunkt: aktuelle Spielerposition am Boden
        Vector3 target = EffectiveTargetPosition;

        // Granate instanziieren
        AntiDashGrenade grenade = Instantiate(grenadePrefab, muzzlePoint.position, Quaternion.identity);
        grenade.SetZoneParameters(grenadeZoneRadius, grenadeZoneDuration);
        grenade.Initialize(target);

        // Animation
        AnimManager?.PlayFireShot();

        // Sound & VFX
        PlaySound(fireSound);
        if (muzzleFlash != null)
            muzzleFlash.Play();
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
    /// Effektive Zielposition: gelockte Position oder Live-Spielerposition.
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

        // Line of Sight
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
