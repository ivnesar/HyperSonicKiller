using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SNIPER NPC - Einzelschuss mit hohem Schaden aus großer Distanz
// ════════════════════════════════════════════════════════════════════════════
//
// Verhält sich wie der Soldier:
//   - Braucht freie Sichtlinie zum Spieler
//   - Gleicher State-Flow: Idle → MovingToRange → Aiming → Firing → Reloading
//   - NpcLaserPointer-Kompatibel (IsLaserActive + AimProgress)
//
// Unterschiede zum Soldier:
//   - 1 Schuss pro Zyklus (kein Salvo)
//   - Hoher Schaden pro Treffer
//   - Sehr große Reichweite (bis 50m)
//   - Deutlich längere Aim-Phase (mehr Warnung für den Spieler)
//   - Eigenes Projektil (SniperBullet): schneller, eigener Trail
//   - Höhere Basis-Accuracy (Scharfschütze trifft besser)
//
// AIM-IK:
//   - AimIK wird zentral über NpcBase gesteuert (IsAimActive + AimController).
//   - States setzen npc.IsAimActive = true/false.
//   - Der Sniper überschreibt GetAimTargetPosition() um die LockedTargetPosition
//     zu berücksichtigen (Dash-Lock).
//   - Dash-Override (smooth Blend-Out) läuft automatisch im AimController.
//
// ════════════════════════════════════════════════════════════════════════════

public class SniperNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Combat - Ranges")]
    [SerializeField] private float maxShootingRange = 50f;
    [SerializeField] private float preferredRange = 35f;

    [Header("Combat - Timing")]
    [Tooltip("Dauer der Aim-Phase bevor geschossen wird (länger = mehr Warnung)")]
    [SerializeField] private float aimDuration = 2.0f;

    [Tooltip("Dauer des Reloads nach dem Schuss")]
    [SerializeField] private float reloadDuration = 2.5f;

    [Header("Combat - Damage")]
    [Tooltip("Schaden pro Treffer (deutlich höher als Soldier)")]
    [SerializeField] private int damagePerShot = 80;

    [Header("Weapon")]
    [Tooltip("Mündungspunkt des Scharfschützengewehrs")]
    [SerializeField] private Transform muzzlePoint;

    [Tooltip("Prefab der Sniper-Kugel")]
    [SerializeField] private SniperBullet bulletPrefab;

    [Tooltip("Layer für Line-of-Sight und Bullet-Hit Check")]
    [SerializeField] private LayerMask bulletHitMask;

    [Header("Audio/VFX")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField] private ParticleSystem muzzleFlash;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors
    // ════════════════════════════════════════════════════════════════════════

    public float MaxShootingRange => maxShootingRange;
    public float PreferredRange => preferredRange;
    public float AimDuration => aimDuration;
    public float ReloadDuration => reloadDuration;

    /// <summary>
    /// Typed animation manager reference for SniperStates.
    /// </summary>
    public SniperAnimationManager AnimManager { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<SniperNpc> currentState;

    /// <summary>
    /// Wenn gesetzt, zielt der Sniper auf diese Position statt auf die Live-Position.
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

        AnimManager = GetComponentInChildren<SniperAnimationManager>();
        if (AnimManager == null)
        {
            Debug.LogWarning($"[SniperNpc] No SniperAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }
    }

    protected override void OnStart()
    {
        ChangeState(new SniperStates.Idle());
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
        ChangeState(new SniperStates.Stunned());
    }

    protected override void OnStunEnd() => ChangeState(new SniperStates.Idle());

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Sniper;
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

    public void ChangeState(INpcState<SniperNpc> newState)
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
        return DistanceToTarget <= maxShootingRange;
    }

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

    public bool CanShoot()
    {
        return IsInShootingRange() && HasLineOfSight();
    }

    public void FireShot()
    {
        if (muzzlePoint == null || bulletPrefab == null) return;

        Vector3 targetPoint = EffectiveTargetPosition + Vector3.up * 1f;
        Vector3 direction = (targetPoint - muzzlePoint.position).normalized;

        var bullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        if (bullet != null)
            bullet.Initialize(direction, damagePerShot, transform, bulletHitMask);

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
