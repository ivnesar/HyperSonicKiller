using UnityEngine;

/// <summary>
/// Soldier NPC - Ranged combatant that:
/// 1. Uses NavMesh to move to get line of sight and into shooting range
/// 2. Fires bullets by directly instantiating bullet GameObjects
/// 3. Reloads
/// 4. Repeat
/// 
/// REFACTORED: Uses NpcBase shared utilities for state timing and audio.
/// </summary>
public class SoldierNpc : NpcBase
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region State Enum
    // ────────────────────────────────────────────────────────────────────────────────

    public enum SoldierState
    {
        Idle,
        MovingToRange,
        Aiming,
        Firing,
        Reloading,
        Stunned
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields - Soldier Specific
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Combat - Ranges")]
    [SerializeField] private float preferredShootingRange = 12f;
    [SerializeField] private float minShootingRange = 6f;
    [SerializeField] private float maxShootingRange = 18f;

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

    [Header("Weapon Setup")]
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private GameObject bulletPrefab;

    [Header("Soldier Audio/VFX")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField] private ParticleSystem muzzleFlash;

    [Header("NavMesh Movement")]
    [SerializeField] private float repositionCheckInterval = 0.5f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime State
    // ────────────────────────────────────────────────────────────────────────────────

    public SoldierState currentState = SoldierState.Idle;

    private float nextShotTime;
    private int shotsFiredInSalvo;
    private float nextRepositionCheckTime;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region NpcBase Implementation
    // ────────────────────────────────────────────────────────────────────────────────

    protected override void OnStart()
    {
        if (bulletPrefab == null)
        {
            Debug.LogError($"[{gameObject.name}] No bullet prefab assigned!");
        }

        TransitionToState(SoldierState.Idle);
    }

    protected override void UpdateBehavior()
    {
        switch (currentState)
        {
            case SoldierState.Idle:
                UpdateIdle();
                break;

            case SoldierState.MovingToRange:
                UpdateMovingToRange();
                break;

            case SoldierState.Aiming:
                UpdateAiming();
                break;

            case SoldierState.Firing:
                UpdateFiring();
                break;

            case SoldierState.Reloading:
                UpdateReloading();
                break;
        }
    }

    protected override void OnStunEnd()
    {
        TransitionToState(SoldierState.MovingToRange);
    }
    
    protected override void OnStunStart()
    {
        TransitionToState(SoldierState.Stunned);
    }

    public override string GetCurrentStateName() => currentState.ToString();
    public override NpcType GetNpcType() => NpcType.Soldier;

    public override int GetStateID()
    {
        return currentState switch
        {
            SoldierState.Idle => 0,
            SoldierState.MovingToRange => 1,
            SoldierState.Aiming => 2,
            SoldierState.Firing => 3,
            SoldierState.Reloading => 4,
            SoldierState.Stunned => 5,
            _ => 0
        };
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Updates
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateIdle()
    {
        float distanceToPlayer = GetDistanceToPlayer();

        if (distanceToPlayer <= detectionRange && canSeePlayer)
        {
            TransitionToState(SoldierState.MovingToRange);
        }
    }

    private void UpdateMovingToRange()
    {
        if (playerTransform == null) return;

        float distanceToPlayer = GetDistanceToPlayer();
        RotateToward(playerTransform.position);

        if (Time.time >= nextRepositionCheckTime)
        {
            nextRepositionCheckTime = Time.time + repositionCheckInterval;

            bool inRange = distanceToPlayer >= minShootingRange && distanceToPlayer <= maxShootingRange;

            if (inRange && canSeePlayer)
            {
                StopMovement();
                TransitionToState(SoldierState.Aiming);
                return;
            }

            if (distanceToPlayer > preferredShootingRange || !canSeePlayer)
            {
                MoveToward(playerTransform.position);
            }
            else if (distanceToPlayer < minShootingRange)
            {
                Vector3 retreatTarget = transform.position - GetDirectionToPlayer() * 5f;
                MoveToward(retreatTarget, 0.7f);
            }
            else
            {
                // Strafe to find line of sight
                Vector3 strafeDir = Vector3.Cross(GetDirectionToPlayer(), Vector3.up);
                if (Random.value > 0.8f) strafeDir = -strafeDir;
                MoveToward(transform.position + strafeDir * 3f, 0.8f);
            }
        }
    }

    private void UpdateAiming()
    {
        StopMovement();

        if (playerTransform != null)
        {
            RotateToward(playerTransform.position, 2f);
        }

        if (UpdateStateTimer())
        {
            if (canSeePlayer)
            {
                TransitionToState(SoldierState.Firing);
            }
            else
            {
                TransitionToState(SoldierState.MovingToRange);
            }
        }
    }

    private void UpdateFiring()
    {
        StopMovement();

        if (playerTransform != null)
        {
            RotateToward(playerTransform.position, 3f);
        }

        if (Time.time >= nextShotTime && shotsFiredInSalvo < shotsPerSalvo)
        {
            FireShot();
            shotsFiredInSalvo++;
            nextShotTime = Time.time + timeBetweenShots;
        }

        if (shotsFiredInSalvo >= shotsPerSalvo)
        {
            TransitionToState(SoldierState.Reloading);
        }
    }

    private void UpdateReloading()
    {
        StopMovement();

        if (playerTransform != null)
        {
            RotateToward(playerTransform.position, 0.5f);
        }

        if (UpdateStateTimer())
        {
            float distanceToPlayer = GetDistanceToPlayer();
            bool inRange = distanceToPlayer >= minShootingRange && distanceToPlayer <= maxShootingRange;

            TransitionToState(inRange && canSeePlayer ? SoldierState.Aiming : SoldierState.MovingToRange);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Transitions
    // ────────────────────────────────────────────────────────────────────────────────

    private void TransitionToState(SoldierState newState)
    {
        // Exit current state
        if (currentState == SoldierState.Firing)
        {
            shotsFiredInSalvo = 0;
        }

        currentState = newState;

        // Enter new state
        switch (newState)
        {
            case SoldierState.Idle:
                StopMovement();
                break;

            case SoldierState.MovingToRange:
                nextRepositionCheckTime = 0f;
                break;

            case SoldierState.Aiming:
                SetStateTimer(aimDuration);
                StopMovement();
                animator?.SetTrigger("Aim");
                break;

            case SoldierState.Firing:
                shotsFiredInSalvo = 0;
                nextShotTime = 0f;
                StopMovement();
                animator?.SetBool("IsFiring", true);
                break;

            case SoldierState.Reloading:
                SetStateTimer(reloadDuration);
                StopMovement();
                animator?.SetBool("IsFiring", false);
                animator?.SetTrigger("Reload");
                PlaySound(reloadSound);
                break;

            case SoldierState.Stunned:
                StopMovement();
                break;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Combat - Shooting
    // ────────────────────────────────────────────────────────────────────────────────

    private void FireShot()
    {
        if (muzzlePoint == null || bulletPrefab == null) return;

        Vector3 targetPoint = playerTransform != null
            ? playerTransform.position + Vector3.up * 1f
            : transform.forward * 100f;

        Vector3 perfectDirection = (targetPoint - muzzlePoint.position).normalized;
        Vector3 shotDirection = ApplyAccuracySpread(perfectDirection);

        muzzleFlash?.Play();
        PlaySound(fireSound);

        GameObject bulletObj = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        SoldierBullet bulletComponent = bulletObj.GetComponent<SoldierBullet>();

        if (bulletComponent != null)
        {
            bulletComponent.Initialize(shotDirection, damagePerShot, transform, lineOfSightMask);
        }
        else
        {
            Destroy(bulletObj);
        }

        animator?.SetTrigger("Fire");
    }

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

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug Visualization
    // ────────────────────────────────────────────────────────────────────────────────

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