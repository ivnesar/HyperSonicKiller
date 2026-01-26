using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Abstract base class for all NPC enemies.
/// Handles shared functionality: movement, stunned state, health, sword interaction, and animation.
/// Subclasses implement specific behavior via the state machine pattern.
/// 
/// REFACTORED: Now includes animator control (previously NPCanimatorController).
/// UPDATED: Integrated with NpcRagdollController for death ragdolls.
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
    
    // Residual stun after sword removal
    [Header("Sword Stun")]
    [SerializeField] protected float residualStunAfterSwordRemoval = 2f;

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

        // Add AudioSource if missing
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        currentHealth = maxHealth;

        // Configure NavMeshAgent
        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
            navAgent.angularSpeed = rotationSpeed * 50f;
            navAgent.stoppingDistance = stoppingDistance;
            navAgent.autoBraking = true;
        }

        // Warn if ragdoll is enabled but controller is missing
        if (useRagdollOnDeath && ragdollController == null)
        {
            Debug.LogWarning($"[{gameObject.name}] useRagdollOnDeath is true but NpcRagdollController component is missing!");
        }
    }

    protected virtual void Start()
    {
        // Auto-find player if not assigned
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

        // Register with NPC manager
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
                EndStun();
            }
            else
            {
                StopMovement();
                UpdateAnimator();
                return;
            }
        }

        // Update visibility cache periodically
        UpdateVisibilityCache();

        // Let subclass handle behavior
        UpdateBehavior();

        // Update animator
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

    /// <summary>
    /// Called once during Start. Use for subclass-specific initialization.
    /// </summary>
    protected abstract void OnStart();

    /// <summary>
    /// Main behavior update. Called every frame when not stunned/dead.
    /// </summary>
    protected abstract void UpdateBehavior();

    /// <summary>
    /// Called when stun ends. Subclass should transition to appropriate state.
    /// </summary>
    protected abstract void OnStunEnd();
    
    /// <summary>
    /// Called when stun starts. Subclass should transition to Stunned state.
    /// </summary>
    protected abstract void OnStunStart();

    /// <summary>
    /// Returns the current state name for debugging purposes.
    /// </summary>
    public abstract string GetCurrentStateName();

    /// <summary>
    /// Returns the NPC type identifier.
    /// </summary>
    public abstract NpcType GetNpcType();

    /// <summary>
    /// Returns the current state as an integer for animator.
    /// </summary>
    public abstract int GetStateID();

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region INpcInteraction Implementation
    // ────────────────────────────────────────────────────────────────────────────────

    public virtual void OnMeeleDamage(int amount)
    {
        if (isDead) return;

        currentHealth -= amount;

        // Register impact for ragdoll
        if (playerTransform != null)
        {
            ragdollController?.RegisterMeleeImpact(playerTransform.position);
        }

        // Trigger hit reaction
        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

        if (currentHealth <= 0)
        {
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
            Die();
        }
    }

    public virtual void OnSwordRemoved()
    {
        if (!hasSwordEmbedded) return;
        
        hasSwordEmbedded = false;
        
        // Start residual stun timer
        stunEndTime = Time.time + residualStunAfterSwordRemoval;
        
        Debug.Log($"[{gameObject.name}] Sword removed - residual stun for {residualStunAfterSwordRemoval}s");
    }

    /// <summary>
    /// Called when NPC is hit by a bullet.
    /// </summary>
    public virtual void OnBulletDamage(int amount, Vector3 bulletDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        currentHealth -= amount;

        // Register with ragdoll controller
        ragdollController?.RegisterBulletImpact(bulletDirection, hitPoint);

        // Trigger hit reaction
        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

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
        
        // Let subclass transition to Stunned state
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

    /// <summary>
    /// Sets the state timer to a duration.
    /// </summary>
    protected void SetStateTimer(float duration)
    {
        stateTimer = duration;
    }

    /// <summary>
    /// Decrements state timer and returns true if expired.
    /// </summary>
    protected bool UpdateStateTimer()
    {
        stateTimer -= Time.deltaTime;
        return stateTimer <= 0f;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Movement Helpers - NavMesh Based
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the NPC toward a target position using NavMeshAgent.
    /// </summary>
    protected void MoveToward(Vector3 targetPosition, float speedMultiplier = 1f)
    {
        if (navAgent == null || !navAgent.enabled || isStunned) return;

        navAgent.SetDestination(targetPosition);
        navAgent.isStopped = false;
        navAgent.speed = moveSpeed * speedMultiplier;
    }

    /// <summary>
    /// Stops the NPC's movement.
    /// </summary>
    protected void StopMovement()
    {
        if (navAgent == null || !navAgent.enabled) return;

        navAgent.isStopped = true;
        navAgent.ResetPath();
    }

    /// <summary>
    /// Checks if the NPC has reached its destination.
    /// </summary>
    protected bool HasReachedDestination()
    {
        if (navAgent == null || !navAgent.enabled) return true;

        if (!navAgent.pathPending && navAgent.remainingDistance <= navAgent.stoppingDistance)
        {
            if (!navAgent.hasPath || navAgent.velocity.sqrMagnitude < 0.01f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Smoothly rotates the NPC to face a target position.
    /// </summary>
    protected void RotateToward(Vector3 targetPosition, float speedMultiplier = 1f)
    {
        Vector3 direction = (targetPosition - transform.position);
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * speedMultiplier * Time.deltaTime
            );
        }
    }

    /// <summary>
    /// Instantly faces a target position.
    /// </summary>
    protected void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position);
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Animator Integration
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates animator parameters. Called every frame.
    /// </summary>
    protected virtual void UpdateAnimator()
    {
        if (animator == null) return;

        // Movement speed (smoothed)
        float targetSpeed = 0f;
        if (navAgent != null && navAgent.enabled)
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

        // State ID
        animator.SetInteger("StateID", GetStateID());

        // Status flags
        animator.SetBool("IsStunned", isStunned);
        animator.SetBool("IsDead", isDead);
    }

    /// <summary>
    /// Plays a sound if available.
    /// </summary>
    protected void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Detection & Visibility
    // ────────────────────────────────────────────────────────────────────────────────

    protected void UpdateVisibilityCache()
    {
        if (Time.time - lastVisibilityCheckTime >= VISIBILITY_CHECK_INTERVAL)
        {
            canSeePlayer = CheckLineOfSightToPlayer();
            lastVisibilityCheckTime = Time.time;
        }
    }

    protected bool CheckLineOfSightToPlayer()
    {
        if (playerTransform == null) return false;

        Vector3 directionToPlayer = playerTransform.position - transform.position;
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > detectionRange) return false;

        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        if (angle > fieldOfView * 0.5f) return false;

        Vector3 eyePosition = transform.position + Vector3.up * 1.5f;
        Vector3 targetPosition = playerTransform.position + Vector3.up * 1f;
        Vector3 rayDirection = (targetPosition - eyePosition).normalized;

        if (Physics.Raycast(eyePosition, rayDirection, out RaycastHit hit, distanceToPlayer, lineOfSightMask))
        {
            return hit.transform == playerTransform || hit.transform.IsChildOf(playerTransform);
        }

        return true;
    }

    protected float GetDistanceToPlayer()
    {
        if (playerTransform == null) return float.MaxValue;
        return Vector3.Distance(transform.position, playerTransform.position);
    }

    protected Vector3 GetDirectionToPlayer()
    {
        if (playerTransform == null) return Vector3.zero;

        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        return dir.normalized;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Health & Death
    // ────────────────────────────────────────────────────────────────────────────────

    protected virtual void Die()
    {
        if (isDead) return; // Prevent multiple death calls
        
        isDead = true;
        isStunned = false;

        // Disable NavMeshAgent
        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        // Handle ragdoll or animation death
        if (useRagdollOnDeath && ragdollController != null)
        {
            // Activate ragdoll with accumulated impact
            ragdollController.ActivateRagdollWithAccumulatedImpact();
            
            // Disable animator (ragdoll controller does this, but just to be safe)
            if (animator != null)
            {
                animator.enabled = false;
            }
        }
        else
        {
            // Fall back to death animation
            animator?.SetTrigger("Die");
        }

        // Destroy after delay (if not set to -1)
        if (destroyDelay >= 0)
        {
            Destroy(gameObject, destroyDelay);
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Died!");
        }
    }

    /// <summary>
    /// Kills the NPC with a specific impact force and direction.
    /// Useful for special death scenarios.
    /// </summary>
    public virtual void DieWithImpact(Vector3 impactDirection, float forceMagnitude, Vector3? impactPoint = null)
    {
        if (isDead) return;
        
        isDead = true;
        isStunned = false;
        currentHealth = 0;

        // Disable NavMeshAgent
        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        // Activate ragdoll with specific impact
        if (useRagdollOnDeath && ragdollController != null)
        {
            ragdollController.ActivateRagdollWithImpact(impactDirection, forceMagnitude, impactPoint);
            
            if (animator != null)
            {
                animator.enabled = false;
            }
        }
        else
        {
            animator?.SetTrigger("Die");
        }

        // Destroy after delay
        if (destroyDelay >= 0)
        {
            Destroy(gameObject, destroyDelay);
        }
    }

    public bool IsDead => isDead;
    public bool IsStunned => isStunned;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug Visualization
    // ────────────────────────────────────────────────────────────────────────────────

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.blue;
        Vector3 leftBoundary = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, fieldOfView * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up, leftBoundary * detectionRange);
        Gizmos.DrawRay(transform.position + Vector3.up, rightBoundary * detectionRange);

        if (navAgent != null && navAgent.enabled && navAgent.hasPath)
        {
            Gizmos.color = Color.green;
            Vector3[] corners = navAgent.path.corners;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Gizmos.DrawLine(corners[i], corners[i + 1]);
            }
        }
    }

    protected virtual void OnGUI()
    {
        if (!showDebugInfo || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.5f);
        if (screenPos.z > 0)
        {
            GUI.Label(
                new Rect(screenPos.x - 50, Screen.height - screenPos.y, 100, 60),
                $"{GetNpcType()}\n{GetCurrentStateName()}\nHP: {currentHealth}/{maxHealth}"
            );
        }
    }

    #endregion
}

// ────────────────────────────────────────────────────────────────────────────────
#region NPC Type Enum
// ────────────────────────────────────────────────────────────────────────────────

public enum NpcType
{
    Soldier,
    Defender
}

#endregion