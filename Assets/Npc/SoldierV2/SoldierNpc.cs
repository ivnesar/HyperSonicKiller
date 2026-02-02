using UnityEngine;

/// <summary>
/// Soldier NPC - Ranged Combatant.
/// Verhaltensweisen:
/// - Stationary: Bleibt an Position, schießt wenn Spieler in Reichweite
/// - Pursuing: Verfolgt Spieler um Line-of-Sight und Schussreichweite zu bekommen
/// </summary>
public class SoldierNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Combat - Ranges")]
    [SerializeField] private float preferredShootingRange = 12f;
    [SerializeField] private float minShootingRange = 6f;
    [SerializeField] private float maxShootingRange = 18f;

    [Header("Combat - Timing")]
    [SerializeField] private float aimDuration = 0.6f;
    [SerializeField] private float timeBetweenShots = 0.15f;
    [SerializeField] private int shotsPerSalvo = 5;
    [SerializeField] private float reloadDuration = 2.0f;
    [SerializeField] private float repositionCheckInterval = 0.5f;

    [Header("Combat - Accuracy")]
    [SerializeField] private float baseAccuracy = 0.85f;
    [SerializeField] private float accuracySpreadAngle = 5f;

    [Header("Combat - Damage")]
    [SerializeField] private int damagePerShot = 10;

    [Header("Weapon")]
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private SoldierBullet bulletPrefab;

    [Header("Audio/VFX")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField] private ParticleSystem muzzleFlash;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors (für States)
    // ════════════════════════════════════════════════════════════════════════

    public float PreferredShootingRange => preferredShootingRange;
    public float MinShootingRange => minShootingRange;
    public float MaxShootingRange => maxShootingRange;
    public float AimDuration => aimDuration;
    public float TimeBetweenShots => timeBetweenShots;
    public int ShotsPerSalvo => shotsPerSalvo;
    public float ReloadDuration => reloadDuration;
    public float RepositionCheckInterval => repositionCheckInterval;
    public float BaseAccuracy => baseAccuracy;
    public float AccuracySpreadAngle => accuracySpreadAngle;
    public int DamagePerShot => damagePerShot;

    public Transform PlayerTransform => playerTransform;
    public float DetectionRange => detectionRange;
    public bool CanSeePlayer => canSeePlayer;
    public Animator NpcAnimator => animator;
    public LayerMask LineOfSightMask => lineOfSightMask;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<SoldierNpc> currentState;

    // State-spezifische Daten
    public float NextShotTime { get; set; }
    public int ShotsFiredInSalvo { get; set; }
    public float NextRepositionCheckTime { get; set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStart()
    {
        if (bulletPrefab == null)
            Debug.LogError($"[{gameObject.name}] No bullet prefab assigned!");

        ChangeState(new SoldierStates.Idle());
    }

    protected override void UpdateBehavior()
    {
        if (currentState == null) return;

        var nextState = currentState.Update(this);
        if (nextState != null)
            ChangeState(nextState);
    }

    protected override void OnStunStart() => ChangeState(new SoldierStates.Stunned());

    protected override void OnStunEnd() => ChangeState(new SoldierStates.MovingToRange());

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";

    public override NpcType GetNpcType() => NpcType.Soldier;

    public override int GetStateID() => currentState?.StateID ?? 0;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<SoldierNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);

        if (showDebugInfo)
            Debug.Log($"[{gameObject.name}] State → {newState?.StateName}");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Combat Actions
    // ════════════════════════════════════════════════════════════════════════

    public void FireShot()
    {
        Vector3 targetPoint = playerTransform != null
            ? playerTransform.position + Vector3.up * 1f
            : transform.forward * 100f;

        Vector3 perfectDirection = (targetPoint - muzzlePoint.position).normalized;
        Vector3 shotDirection = ApplyAccuracySpread(perfectDirection);

        var bullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        bullet?.Initialize(shotDirection, damagePerShot, transform, lineOfSightMask);

        animator?.SetTrigger("Fire");
    }

    public void PlayReloadSound() => PlaySound(reloadSound);

    private Vector3 ApplyAccuracySpread(Vector3 perfectDirection)
    {
        float spreadAngle = Random.value <= baseAccuracy
            ? accuracySpreadAngle * 0.2f
            : accuracySpreadAngle;

        Quaternion spread = Quaternion.Euler(
            Random.Range(-spreadAngle, spreadAngle),
            Random.Range(-spreadAngle, spreadAngle),
            0
        );
        return spread * perfectDirection;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Movement Helpers (Public für States)
    // ════════════════════════════════════════════════════════════════════════

    public new void MoveToward(Vector3 position, float speedMultiplier = 1f) =>
        base.MoveToward(position, speedMultiplier);

    public new void StopMovement() => base.StopMovement();

    public new void RotateToward(Vector3 position, float speedMultiplier = 1f) =>
        base.RotateToward(position, speedMultiplier);

    public new float GetDistanceToPlayer() => base.GetDistanceToPlayer();

    public new Vector3 GetDirectionToPlayer() => base.GetDirectionToPlayer();

    public new void SetStateTimer(float duration) => base.SetStateTimer(duration);

    public new bool UpdateStateTimer() => base.UpdateStateTimer();

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
        Gizmos.DrawWireSphere(transform.position, preferredShootingRange);

        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, maxShootingRange);

        if (muzzlePoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(muzzlePoint.position, muzzlePoint.forward * 3f);
        }
    }

    #endregion
}