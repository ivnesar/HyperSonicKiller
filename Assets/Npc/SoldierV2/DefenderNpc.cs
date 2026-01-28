using UnityEngine;

/// <summary>
/// Defender NPC - Protective combatant that:
/// 1. Finds the soldier closest to the player
/// 2. Positions itself between the player and that soldier
/// 3. Blocks incoming attacks from the player
/// 4. Counters with a melee attack if block is successful
/// 
/// REFACTORED: Uses NpcBase shared utilities for state timing and audio.
/// UPDATED: Compatible with new PlayerCore system.
/// </summary>
public class DefenderNpc : NpcBase
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region State Enum
    // ────────────────────────────────────────────────────────────────────────────────

    public enum DefenderState
    {
        Idle,
        MovingToProtect,
        Guarding,
        Blocking,
        Countering,
        Stunned
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields - Defender Specific
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Protection Behavior")]
    [SerializeField] private float protectDistance = 2.5f;
    [SerializeField] private float repositionThreshold = 1.5f;
    [SerializeField] private float soldierSearchInterval = 0.5f;

    [Header("Blocking")]
    [SerializeField] private float blockDetectionRange = 4f;
    [SerializeField] private float blockAngle = 90f;
    [SerializeField] private float blockDuration = 0.8f;
    [SerializeField] private float blockCooldown = 0.3f;
    [SerializeField] private float perfectBlockWindow = 0.15f;

    [Header("Counter Attack")]
    [SerializeField] private float counterDuration = 0.6f;
    [SerializeField] private float counterRange = 2.5f;
    [SerializeField] private int counterDamage = 25;

    [Header("Defender Audio/VFX")]
    [SerializeField] private AudioClip blockSound;
    [SerializeField] private AudioClip perfectBlockSound;
    [SerializeField] private AudioClip counterSound;
    [SerializeField] private ParticleSystem blockEffect;
    [SerializeField] private ParticleSystem perfectBlockEffect;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime State
    // ────────────────────────────────────────────────────────────────────────────────

    public DefenderState currentState = DefenderState.Idle;

    private NpcBase protectedSoldier;
    private float nextSoldierSearchTime;
    private float lastBlockTime;
    private float blockStartTime;
    private bool wasAttackBlocked;
    private bool wasPerfectBlock;

    // ADDED: Cache for PlayerCore reference
    private PlayerCore playerCore;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region NpcBase Implementation
    // ────────────────────────────────────────────────────────────────────────────────

    protected override void OnStart()
    {
        // ADDED: Cache PlayerCore reference
        if (playerTransform != null)
        {
            playerCore = playerTransform.GetComponent<PlayerCore>();
        }

        FindSoldierToProtect();
        TransitionToState(protectedSoldier != null ? DefenderState.MovingToProtect : DefenderState.Idle);
    }

    protected override void UpdateBehavior()
    {
        // Periodically search for soldiers to protect
        if (Time.time >= nextSoldierSearchTime)
        {
            FindSoldierToProtect();
            nextSoldierSearchTime = Time.time + soldierSearchInterval;
        }

        switch (currentState)
        {
            case DefenderState.Idle:
                UpdateIdle();
                break;

            case DefenderState.MovingToProtect:
                UpdateMovingToProtect();
                break;

            case DefenderState.Guarding:
                UpdateGuarding();
                break;

            case DefenderState.Blocking:
                UpdateBlocking();
                break;

            case DefenderState.Countering:
                UpdateCountering();
                break;
        }
    }

    protected override void OnStunEnd()
    {
        if (protectedSoldier != null && !protectedSoldier.IsDead)
        {
            TransitionToState(DefenderState.MovingToProtect);
        }
        else
        {
            TransitionToState(DefenderState.Idle);
        }
    }

    protected override void OnStunStart()
    {
        TransitionToState(DefenderState.Stunned);
    }

    public override string GetCurrentStateName() => currentState.ToString();
    public override NpcType GetNpcType() => NpcType.Defender;

    public override int GetStateID()
    {
        return currentState switch
        {
            DefenderState.Idle => 0,
            DefenderState.MovingToProtect => 1,
            DefenderState.Guarding => 2,
            DefenderState.Blocking => 3,
            DefenderState.Countering => 4,
            DefenderState.Stunned => 5,
            _ => 0
        };
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Updates
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateIdle()
    {
        StopMovement();

        if (canSeePlayer && playerTransform != null)
        {
            RotateToward(playerTransform.position, 0.5f);
        }

        if (protectedSoldier != null && !protectedSoldier.IsDead)
        {
            TransitionToState(DefenderState.MovingToProtect);
        }
    }

    private void UpdateMovingToProtect()
    {
        if (protectedSoldier == null || protectedSoldier.IsDead)
        {
            FindSoldierToProtect();
            if (protectedSoldier == null)
            {
                TransitionToState(DefenderState.Idle);
                return;
            }
        }

        Vector3 targetPosition = GetInterceptPosition();

        if (HasReachedDestination())
        {
            TransitionToState(DefenderState.Guarding);
        }
        else
        {
            MoveToward(targetPosition);
            if (playerTransform != null)
            {
                RotateToward(playerTransform.position);
            }
        }
    }

    private void UpdateGuarding()
    {
        StopMovement();

        if (playerTransform != null)
        {
            RotateToward(playerTransform.position, 2f);
        }

        // Check if repositioning needed
        if (protectedSoldier != null && !protectedSoldier.IsDead)
        {
            Vector3 idealPosition = GetInterceptPosition();
            if (Vector3.Distance(transform.position, idealPosition) > repositionThreshold)
            {
                TransitionToState(DefenderState.MovingToProtect);
                return;
            }
        }
        else
        {
            FindSoldierToProtect();
            if (protectedSoldier == null)
            {
                TransitionToState(DefenderState.Idle);
                return;
            }
        }

        if (ShouldStartBlocking())
        {
            TransitionToState(DefenderState.Blocking);
        }
    }

    private void UpdateBlocking()
    {
        StopMovement();

        if (playerTransform != null)
        {
            RotateToward(playerTransform.position, 3f);
        }

        if (UpdateStateTimer())
        {
            TransitionToState(wasAttackBlocked ? DefenderState.Countering : DefenderState.Guarding);
        }
    }

    private void UpdateCountering()
    {
        StopMovement();

        if (playerTransform != null)
        {
            RotateToward(playerTransform.position, 4f);
        }

        if (UpdateStateTimer())
        {
            TransitionToState(DefenderState.Guarding);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Transitions
    // ────────────────────────────────────────────────────────────────────────────────

    private void TransitionToState(DefenderState newState)
    {
        // Exit current state
        if (currentState == DefenderState.Blocking)
        {
            wasAttackBlocked = false;
            wasPerfectBlock = false;
        }

        currentState = newState;

        // Enter new state
        switch (newState)
        {
            case DefenderState.Idle:
                StopMovement();
                animator?.SetBool("IsGuarding", false);
                break;

            case DefenderState.MovingToProtect:
                animator?.SetBool("IsMoving", true);
                animator?.SetBool("IsGuarding", false);
                break;

            case DefenderState.Guarding:
                StopMovement();
                animator?.SetBool("IsMoving", false);
                animator?.SetBool("IsGuarding", true);
                break;

            case DefenderState.Blocking:
                SetStateTimer(blockDuration);
                blockStartTime = Time.time;
                lastBlockTime = Time.time;
                wasAttackBlocked = false;
                wasPerfectBlock = false;
                StopMovement();
                animator?.SetBool("IsBlocking", true);
                animator?.SetTrigger("Block");
                break;

            case DefenderState.Countering:
                SetStateTimer(counterDuration);
                StopMovement();
                animator?.SetBool("IsBlocking", false);
                animator?.SetTrigger("Counter");
                TryDealCounterDamage();
                PlaySound(counterSound);
                break;

            case DefenderState.Stunned:
                StopMovement();
                animator?.SetBool("IsGuarding", false);
                animator?.SetBool("IsBlocking", false);
                break;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Protection Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private void FindSoldierToProtect()
    {
        NpcBase nearest = NpcManager.Instance?.GetSoldierClosestToPlayer();

        if (nearest != null && nearest != protectedSoldier)
        {
            protectedSoldier = nearest;
        }
        else if (nearest == null)
        {
            protectedSoldier = null;
        }
    }

    private Vector3 GetInterceptPosition()
    {
        if (protectedSoldier == null || playerTransform == null)
        {
            return transform.position;
        }

        Vector3 soldierPos = protectedSoldier.transform.position;
        Vector3 directionToPlayer = (playerTransform.position - soldierPos).normalized;

        return soldierPos + directionToPlayer * protectDistance;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Block & Counter Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private bool ShouldStartBlocking()
    {
        if (Time.time - lastBlockTime < blockCooldown) return false;
        if (playerTransform == null) return false;
        if (GetDistanceToPlayer() > blockDetectionRange) return false;

        float angle = Vector3.Angle(transform.forward, GetDirectionToPlayer());
        return angle <= blockAngle * 0.5f;
    }

    /// <summary>
    /// Called externally when an attack hits this defender. Returns true if blocked.
    /// </summary>
    public bool TryBlockAttack()
    {
        if (currentState != DefenderState.Guarding && currentState != DefenderState.Blocking)
        {
            return false;
        }

        if (currentState == DefenderState.Guarding)
        {
            TransitionToState(DefenderState.Blocking);
        }

        float timeSinceBlockStart = Time.time - blockStartTime;
        wasPerfectBlock = timeSinceBlockStart <= perfectBlockWindow;
        wasAttackBlocked = true;

        if (wasPerfectBlock)
        {
            PlaySound(perfectBlockSound);
            perfectBlockEffect?.Play();
        }
        else
        {
            PlaySound(blockSound);
            blockEffect?.Play();
        }

        return true;
    }

    private void TryDealCounterDamage()
    {
        if (playerTransform == null) return;
        if (GetDistanceToPlayer() > counterRange) return;

        float angle = Vector3.Angle(transform.forward, GetDirectionToPlayer());
        if (angle > 45f) return;

        // UPDATED: Use PlayerCore instead of FPSPlayerController
        if (playerCore != null)
        {
            playerCore.TakeDamage(counterDamage);
        }
        else
        {
            // Fallback: Try to get component if not cached
            var pc = playerTransform.GetComponent<PlayerCore>();
            if (pc != null)
            {
                pc.TakeDamage(counterDamage);
                playerCore = pc; // Cache for future use
            }
        }
    }

    /// <summary>
    /// Override melee damage to potentially block it.
    /// </summary>
    public override void OnMeeleDamage(int amount)
    {
        if (TryBlockAttack())
        {
            return; // Attack was blocked
        }

        base.OnMeeleDamage(amount);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug Visualization
    // ────────────────────────────────────────────────────────────────────────────────

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, blockDetectionRange);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, counterRange);

        if (protectedSoldier != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, protectedSoldier.transform.position);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(GetInterceptPosition(), 0.5f);
        }

        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Vector3 leftBlock = Quaternion.Euler(0, -blockAngle * 0.5f, 0) * transform.forward;
        Vector3 rightBlock = Quaternion.Euler(0, blockAngle * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up, leftBlock * blockDetectionRange);
        Gizmos.DrawRay(transform.position + Vector3.up, rightBlock * blockDetectionRange);
    }

    #endregion
}