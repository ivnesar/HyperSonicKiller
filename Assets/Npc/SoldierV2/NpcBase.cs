using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Abstract base class for all NPC enemies.
/// Handles shared functionality: movement, stunned state, health, sword interaction, and animation.
/// Subclasses implement specific behavior via the state machine pattern.
/// 
/// UPDATED: Thrown sword damage is now applied AFTER the stun duration ends.
/// This allows weak enemies to collapse while tough enemies survive.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public abstract class NpcBase : MonoBehaviour, INpcInteraction
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields - Shared
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] protected Transform playerTransform;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 100;

    [Header("Movement")]
    [SerializeField] protected float moveSpeed = 4f;
    [SerializeField] protected float rotationSpeed = 10f;
    [SerializeField] protected float stoppingDistance = 0.5f;

    [Header("Detection")]
    [SerializeField] protected float detectionRange = 25f;
    [SerializeField] protected float fieldOfView = 120f;
    [SerializeField] protected LayerMask lineOfSightMask;

    [Header("Audio")]
    [SerializeField] protected AudioClip hitSound;

    [Header("Death Settings")]
    [Tooltip("If true, NPC will ragdoll on death. Requires NpcRagdollController component.")]
    [SerializeField] protected bool useRagdollOnDeath = true;

    [Tooltip("Time before destroying the GameObject after death (set to -1 to never destroy)")]
    [SerializeField] protected float destroyDelay = 10f;

    [Header("Sword Stun")]
    [Tooltip("Base residual stun duration after sword removal (can be overridden by sword)")]
    [SerializeField] protected float residualStunAfterSwordRemoval = 2f;

    [Header("Debug")]
    [SerializeField] protected bool showDebugInfo = true;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Components
    // ────────────────────────────────────────────────────────────────────────────────

    protected NavMeshAgent navAgent;
    protected Animator animator;
    protected AudioSource audioSource;
    protected NpcRagdollController ragdollController;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime State - Shared
    // ────────────────────────────────────────────────────────────────────────────────

    protected int currentHealth;
    protected bool isDead;

    // Stun system
    protected bool isStunned;
    protected float stunEndTime;

    // Sword embedding
    protected bool hasSwordEmbedded;

    // Pending throw damage (applied after stun ends)
    protected int pendingThrowDamage;
    protected Vector3 pendingImpactDirection;
    protected Vector3 pendingHitPoint;
    protected bool hasPendingThrowDamage;

    // State timer (shared utility for subclasses)
    protected float stateTimer;

    // Cache for player visibility checks
    protected bool canSeePlayer;
    protected float lastVisibilityCheckTime;
    protected const float VISIBILITY_CHECK_INTERVAL = 0.15f;

    // Animator smoothing
    private float currentSpeedVelocity;
    private const float SPEED_SMOOTH_TIME = 0.1f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        audioSource = GetComponent<AudioSource>();
        ragdollController = GetComponent<NpcRagdollController>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        currentHealth = maxHealth;

        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
            navAgent.angularSpeed = rotationSpeed * 50f;
            navAgent.stoppingDistance = stoppingDistance;
            navAgent.autoBraking = true;
        }

        if (useRagdollOnDeath && ragdollController == null)
        {
            Debug.LogWarning($"[{gameObject.name}] useRagdollOnDeath is true but NpcRagdollController component is missing!");
        }
    }

    protected virtual void Start()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] No player found! Assign playerTransform manually.");
            }
        }

        NpcManager.Instance?.RegisterNpc(this);
        OnStart();
    }

    protected virtual void Update()
    {
        if (isDead) return;

        // Handle stun timer
        if (isStunned)
        {
            // Stay stunned indefinitely while sword is embedded
            if (hasSwordEmbedded)
            {
                StopMovement();
                UpdateAnimator();
                return;
            }

            // Sword removed - check residual stun timer
            if (Time.time >= stunEndTime)
            {
                // Apply pending throw damage BEFORE ending stun
                ApplyPendingThrowDamage();
                
                // Only end stun if still alive
                if (!isDead)
                {
                    EndStun();
                }
            }
            else
            {
                StopMovement();
                UpdateAnimator();
                return;
            }
        }

        UpdateVisibilityCache();
        UpdateBehavior();
        UpdateAnimator();
    }

    protected virtual void OnDestroy()
    {
        NpcManager.Instance?.UnregisterNpc(this);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Abstract Methods - Subclass Implementation
    // ────────────────────────────────────────────────────────────────────────────────

    protected abstract void OnStart();
    protected abstract void UpdateBehavior();
    protected abstract void OnStunEnd();
    protected abstract void OnStunStart();
    public abstract string GetCurrentStateName();
    public abstract NpcType GetNpcType();
    public abstract int GetStateID();

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region INpcInteraction Implementation
    // ────────────────────────────────────────────────────────────────────────────────

    public virtual void OnMeeleDamage(int amount)
    {
        if (isDead) return;

        currentHealth -= amount;

        if (playerTransform != null)
        {
            ragdollController?.RegisterMeleeImpact(playerTransform.position);
        }

        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

        if (currentHealth <= 0)
        {
<<<<<<< HEAD
=======
            Die();
        }
    }

    public virtual void OnThrowStun(float duration)
    {
        if (isDead) return;

        hasSwordEmbedded = true;
        
        // Enter stunned state - will stay stunned indefinitely while sword is embedded
        isStunned = true;
        stunEndTime = float.MaxValue; // No timeout while embedded
        
        StopMovement();
        animator?.SetBool("IsStunned", true);
        
        // Let subclass transition to Stunned state
        OnStunStart();
        
        Debug.Log($"[{gameObject.name}] Sword embedded - stunned indefinitely");
    }

    /// <summary>
    /// Called when thrown sword hits and should register impact for ragdoll.
    /// </summary>
    public virtual void OnThrowDamage(int amount, Vector3 swordDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        currentHealth -= amount;

        // Register with ragdoll controller
        ragdollController?.RegisterThrownSwordImpact(swordDirection, hitPoint);

        if (currentHealth <= 0)
        {
>>>>>>> parent of ee61753 (add player meele damage)
            Die();
        }
    }

    /// <summary>
    /// Called when thrown sword embeds. Damage is stored and applied after stun ends.
    /// </summary>
    public virtual void OnThrowStun(float duration, int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        hasSwordEmbedded = true;

        // Store pending damage - will be applied when stun ends
        pendingThrowDamage = damage;
        pendingImpactDirection = swordDirection;
        pendingHitPoint = hitPoint;
        hasPendingThrowDamage = true;
        
        // Store the duration for use when sword is removed
        residualStunAfterSwordRemoval = duration;

        // Enter stunned state - stays stunned indefinitely while sword is embedded
        isStunned = true;
        stunEndTime = float.MaxValue;

        StopMovement();
        animator?.SetBool("IsStunned", true);

        OnStunStart();

        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Sword embedded - stunned indefinitely (pending damage: {damage})");
        }
    }

    public virtual void OnSwordRemoved()
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;

        // Start residual stun timer - damage will be applied when this expires
        stunEndTime = Time.time + residualStunAfterSwordRemoval;

        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Sword removed - residual stun for {residualStunAfterSwordRemoval}s, then {pendingThrowDamage} damage");
        }
    }

    /// <summary>
    /// Called when NPC is hit by a bullet.
    /// </summary>
    public virtual void OnBulletDamage(int amount, Vector3 bulletDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        currentHealth -= amount;

        ragdollController?.RegisterBulletImpact(bulletDirection, hitPoint);

        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Delayed Throw Damage System
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the stored throw damage. Called when stun ends.
    /// </summary>
    protected virtual void ApplyPendingThrowDamage()
    {
        if (!hasPendingThrowDamage) return;

        hasPendingThrowDamage = false;

        currentHealth -= pendingThrowDamage;

        // Register impact for ragdoll
        ragdollController?.RegisterThrownSwordImpact(pendingImpactDirection, pendingHitPoint);

        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Applied throw damage: {pendingThrowDamage}, health: {currentHealth}/{maxHealth}");
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Stun System
    // ────────────────────────────────────────────────────────────────────────────────

    protected void ApplyStun(float duration)
    {
        isStunned = true;
        stunEndTime = Time.time + duration;

        StopMovement();
        animator?.SetBool("IsStunned", true);

        OnStunStart();
    }

    protected void EndStun()
    {
        isStunned = false;

        if (navAgent != null && navAgent.enabled)
        {
            navAgent.isStopped = false;
        }

        animator?.SetBool("IsStunned", false);
        OnStunEnd();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Timer Utilities
    // ────────────────────────────────────────────────────────────────────────────────

    protected void SetStateTimer(float duration)
    {
        stateTimer = duration;
    }

    protected bool UpdateStateTimer()
    {
        stateTimer -= Time.deltaTime;
        return stateTimer <= 0f;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Movement Helpers - NavMesh Based
    // ────────────────────────────────────────────────────────────────────────────────

    protected void MoveToward(Vector3 targetPosition, float speedMultiplier = 1f)
    {
        if (navAgent == null || !navAgent.enabled || isStunned) return;

        navAgent.SetDestination(targetPosition);
        navAgent.isStopped = false;
        navAgent.speed = moveSpeed * speedMultiplier;
    }

    protected void StopMovement()
    {
        if (navAgent == null || !navAgent.enabled) return;

        navAgent.isStopped = true;
        navAgent.ResetPath();
    }

    protected bool HasReachedDestination()
    {
        if (navAgent == null || !navAgent.enabled) return true;
        if (navAgent.pathPending) return false;

        return navAgent.remainingDistance <= navAgent.stoppingDistance;
    }

    protected void FaceTarget(Vector3 targetPosition, float speedMultiplier = 1f)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0;

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * speedMultiplier * Time.deltaTime
            );
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Detection Helpers
    // ────────────────────────────────────────────────────────────────────────────────

    protected void UpdateVisibilityCache()
    {
        if (Time.time - lastVisibilityCheckTime < VISIBILITY_CHECK_INTERVAL) return;

        lastVisibilityCheckTime = Time.time;
        canSeePlayer = CheckLineOfSight();
    }

    protected bool CheckLineOfSight()
    {
        if (playerTransform == null) return false;

        Vector3 toPlayer = playerTransform.position - transform.position;

        if (toPlayer.magnitude > detectionRange) return false;

        Vector3 directionToPlayer = toPlayer.normalized;
        float angle = Vector3.Angle(transform.forward, directionToPlayer);

        if (angle > fieldOfView * 0.5f) return false;

        Vector3 eyePosition = transform.position + Vector3.up * 1.5f;
        Vector3 targetPosition = playerTransform.position + Vector3.up * 1f;

        if (Physics.Linecast(eyePosition, targetPosition, lineOfSightMask))
        {
            return false;
        }

        return true;
    }

    protected float GetDistanceToPlayer()
    {
        if (playerTransform == null) return float.MaxValue;
        return Vector3.Distance(transform.position, playerTransform.position);
    }

    protected bool IsPlayerInRange(float range)
    {
        return GetDistanceToPlayer() <= range;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Death & Damage
    // ────────────────────────────────────────────────────────────────────────────────

    protected virtual void Die()
    {
        if (isDead) return;

        isDead = true;
        isStunned = false;

        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        if (useRagdollOnDeath && ragdollController != null)
        {
            ragdollController.ActivateRagdollWithAccumulatedImpact();

            if (animator != null)
            {
                animator.enabled = false;
            }
        }
        else
        {
            animator?.SetTrigger("Die");
        }

        if (destroyDelay >= 0)
        {
            Destroy(gameObject, destroyDelay);
        }

        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Died!");
        }
    }

    public virtual void DieWithImpact(Vector3 impactDirection, float forceMagnitude, Vector3? hitPoint = null)
    {
        if (isDead) return;

        ragdollController?.RegisterCustomImpact(impactDirection, forceMagnitude, hitPoint);
        Die();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Animation
    // ────────────────────────────────────────────────────────────────────────────────

    protected virtual void UpdateAnimator()
    {
        if (animator == null) return;

        float targetSpeed = 0f;
        if (navAgent != null && navAgent.enabled && !navAgent.isStopped)
        {
            targetSpeed = navAgent.velocity.magnitude / moveSpeed;
        }

        float smoothedSpeed = Mathf.SmoothDamp(
            animator.GetFloat("MoveSpeed"),
            targetSpeed,
            ref currentSpeedVelocity,
            SPEED_SMOOTH_TIME
        );

        animator.SetFloat("MoveSpeed", smoothedSpeed);
        animator.SetBool("IsMoving", targetSpeed > 0.1f);
        animator.SetInteger("StateID", GetStateID());
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Audio
    // ────────────────────────────────────────────────────────────────────────────────

    protected void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug Visualization
    // ────────────────────────────────────────────────────────────────────────────────

    protected virtual void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Field of view
        Vector3 leftBoundary = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, fieldOfView * 0.5f, 0) * transform.forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, leftBoundary * detectionRange);
        Gizmos.DrawRay(transform.position, rightBoundary * detectionRange);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public Getters
    // ────────────────────────────────────────────────────────────────────────────────

    public bool IsDead => isDead;
    public bool IsStunned => isStunned;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public float HealthPercent => (float)currentHealth / maxHealth;

    #endregion
}

// ────────────────────────────────────────────────────────────────────────────────
#region Enums
// ────────────────────────────────────────────────────────────────────────────────

public enum NpcType
{
    Soldier,
    Defender,
    Sniper,
    Heavy
}

#endregion