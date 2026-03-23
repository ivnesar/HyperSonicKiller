using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SNIPER NPC - Einzelschuss mit hohem Schaden aus großer Distanz
// ════════════════════════════════════════════════════════════════════════════
//
// Verhält sich wie der Soldier:
//   - Braucht freie Sichtlinie zum Spieler
//   - Gleicher State-Flow: Idle → MovingToRange → Aiming → Firing → Reloading
//   - AimIK über AimController für Oberkörper-Rotation zum Ziel
//   - NpcLaserPointer-Kompatibel (IsLaserActive + AimProgress)
//
// Unterschiede zum Soldier:
//   - 1 Schuss pro Zyklus (kein Salvo)
//   - Hoher Schaden pro Treffer
//   - Sehr große Reichweite (20-50m)
//   - Deutlich längere Aim-Phase (mehr Warnung für den Spieler)
//   - Eigenes Projektil (SniperBullet): schneller, eigener Trail
//   - Höhere Basis-Accuracy (Scharfschütze trifft besser)
//
// AIM-IK MIGRATION:
//   - Alte manuelle Aim-Bone-Rotation (LateUpdate, UpdateAimBoneRotation,
//     CalculateTargetPitch) komplett entfernt.
//   - Aiming wird jetzt über AimController gesteuert,
//     der die AimIK-Komponente von RootMotion Final IK wrapped.
//   - States setzen npc.IsAiming → SniperNpc leitet das an AimController weiter.
//
// RAGDOLL MIGRATION:
//   - NpcRagdollController entfernt → NpcImpactTracker + NpcRagdollSwapper stattdessen.
//   - Ragdoll-Physik läuft nur noch auf gespawnten Prefabs.
//
// ════════════════════════════════════════════════════════════════════════════

public class SniperNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Combat - Ranges")]
    [SerializeField] private float minShootingRange = 20f;
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

    public float MinShootingRange => minShootingRange;
    public float MaxShootingRange => maxShootingRange;
    public float PreferredRange => preferredRange;
    public float AimDuration => aimDuration;
    public float ReloadDuration => reloadDuration;

    /// <summary>
    /// Typed animation manager reference for SniperStates.
    /// </summary>
    public SniperAnimationManager AnimManager { get; private set; }

    /// <summary>
    /// AimIK-Controller für Oberkörper-Rotation zum Ziel.
    /// </summary>
    public AimController AimController { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<SniperNpc> currentState;

    /// <summary>
    /// Wird von States gesetzt um AimIK zu aktivieren/deaktivieren.
    /// True = AimIK blendet ein, False = AimIK blendet aus.
    /// </summary>
    public bool IsAiming { get; set; }

    // Dash-Erkennung für Target-Lock
    private PlayerCore playerCore;

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

        // Typsichere Referenz auf den Animation Manager
        AnimManager = GetComponentInChildren<SniperAnimationManager>();
        if (AnimManager == null)
        {
            Debug.LogWarning($"[SniperNpc] No SniperAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }

        // AimController finden
        AimController = GetComponent<AimController>();
        if (AimController == null)
        {
            Debug.LogWarning($"[SniperNpc] No AimController found on {gameObject.name}! " +
                             "Aim-IK will not work. Add AimController component.");
        }
    }

    protected override void OnStart()
    {
        // PlayerCore-Referenz cachen für Dash-Erkennung
        if (playerTransform != null)
            playerCore = playerTransform.GetComponent<PlayerCore>();

        ChangeState(new SniperStates.Idle());
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
        ChangeState(new SniperStates.Stunned());
    }

    protected override void OnStunEnd() => ChangeState(new SniperStates.Idle());

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Sniper;
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
        float dist = DistanceToTarget;
        return dist >= minShootingRange && dist <= maxShootingRange;
    }

    /// <summary>
    /// Prüft ob der Sniper freie Sicht zum Spieler hat.
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
    /// Prüft ob der Sniper schießen kann (in Reichweite UND freie Sicht).
    /// </summary>
    public bool CanShoot()
    {
        return IsInShootingRange() && HasLineOfSight();
    }

    /// <summary>
    /// Feuert einen einzelnen Sniper-Schuss.
    /// Die Kugel wird immer direkt auf den Spieler gerichtet (kein Spread).
    /// </summary>
    public void FireShot()
    {
        if (muzzlePoint == null || bulletPrefab == null) return;

        // Immer direkt auf den Spieler zielen
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
