using UnityEngine;

/// <summary>
/// Soldier NPC - Schießt auf den Spieler aus der Distanz.
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
    [SerializeField] private LayerMask lineOfSightMask;

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
    public Animator NpcAnimator => animator;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<SoldierNpc> currentState;
    public int ShotsFiredInSalvo { get; set; }
    public float NextShotTime { get; set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStart()
    {
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
    protected override void OnStunEnd() => ChangeState(new SoldierStates.Idle());

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

    public void FireShot()
    {
        if (muzzlePoint == null || bulletPrefab == null) return;

        Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
        Vector3 direction = (targetPoint - muzzlePoint.position).normalized;
        direction = ApplySpread(direction);

        var bullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        if (bullet != null)
            bullet.Initialize(direction, damagePerShot, transform, lineOfSightMask);

        if (animator != null)
            animator.SetTrigger("Fire");
        
        PlaySound(fireSound);
        
        if (muzzleFlash != null)
            muzzleFlash.Play();
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
    }

    #endregion
}
