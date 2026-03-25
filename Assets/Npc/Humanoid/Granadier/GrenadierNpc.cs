using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GRENADIER NPC - Schießt Anti-Dash Granaten aus der Distanz
// ════════════════════════════════════════════════════════════════════════════
//
// Verhält sich wie der Soldier:
//   - Braucht freie Sichtlinie zum Spieler
//   - Gleicher State-Flow: Idle → MovingToRange → Aiming → Firing → Reloading
//
// Unterschiede zum Soldier:
//   - Einstellbare Magazingröße (Default 1, MGL-Style bis 6)
//   - Granate fliegt auf Parabelbahn zum Spieler-Standort
//   - Granate spawnt eine zeitbegrenzte Anti-Dash-Zone
//   - Größere Reichweite, längere Aim-Dauer, längeres Reload
//   - Kein Muzzle-FOV Aim-Assist (Granate fliegt sowieso zum Ziel)
//
// AIM-IK:
//   - AimIK wird zentral über NpcBase gesteuert (IsAimActive + AimController).
//   - States setzen npc.IsAimActive = true/false.
//   - Der Grenadier überschreibt GetAimTargetPosition() um die LockedTargetPosition
//     zu berücksichtigen (Dash-Lock).
//   - Dash-Override (smooth Blend-Out) läuft automatisch im AimController.
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

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<GrenadierNpc> currentState;

    /// <summary>Wie viele Granaten bereits in dieser Salve abgefeuert wurden.</summary>
    public int ShotsFiredInMagazine { get; set; }

    /// <summary>Frühester Zeitpunkt für den nächsten Schuss (Time.time).</summary>
    public float NextShotTime { get; set; }

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

        AnimManager = GetComponentInChildren<GrenadierAnimationManager>();
        if (AnimManager == null)
        {
            Debug.LogWarning($"[GrenadierNpc] No GrenadierAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }
    }

    protected override void OnStart()
    {
        ChangeState(new GrenadierStates.Idle());
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
        ChangeState(new GrenadierStates.Stunned());
    }

    protected override void OnStunEnd() => ChangeState(new GrenadierStates.Idle());

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Grenadier;
    public override int GetStateID() => currentState?.StateID ?? 0;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region AimIK — Target Override
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Überschreibt die Zielposition für den AimController.
    /// Nutzt die gelockte Position (Dash-Lock) falls aktiv.
    /// </summary>
    protected override Vector3 GetAimTargetPosition()
    {
        return EffectiveTargetPosition;
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

    public bool CanShoot()
    {
        return IsInShootingRange() && HasLineOfSight();
    }

    public void FireGrenade()
    {
        if (muzzlePoint == null || grenadePrefab == null) return;

        Vector3 target = EffectiveTargetPosition;

        AntiDashGrenade grenade = Instantiate(grenadePrefab, muzzlePoint.position, Quaternion.identity);
        grenade.SetZoneParameters(grenadeZoneRadius, grenadeZoneDuration);
        grenade.Initialize(target);

        AnimManager?.PlayFireShot();

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

    public Vector3 EffectiveTargetPosition => LockedTargetPosition ?? TargetPosition;

    public void RotateTowardPosition(Vector3 worldPosition) => RotateToward(worldPosition);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, minShootingRange);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, preferredRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxShootingRange);

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
