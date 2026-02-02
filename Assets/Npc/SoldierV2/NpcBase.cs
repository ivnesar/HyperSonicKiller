using UnityEngine;
using UnityEngine.AI;

// ════════════════════════════════════════════════════════════════════════════
// NPC BASE - Abstrakte Basisklasse für alle NPCs
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Verhaltensmodus für NPCs.
/// </summary>
public enum BehaviorMode
{
    Stationary,  // Bleibt an Startposition, greift nur an wenn Spieler in Reichweite
    Pursuing     // Verfolgt den Spieler aktiv
}

/// <summary>
/// NPC-Typ Identifikator.
/// </summary>
public enum NpcType
{
    Soldier,
    Defender
}

/// <summary>
/// Abstrakte Basisklasse für alle NPC-Gegner.
/// Handhabt: Movement, Stun, Health, Sword-Interaktion, Animation.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public abstract class NpcBase : MonoBehaviour, IEnemy
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Behavior")]
    [SerializeField] protected BehaviorMode behaviorMode = BehaviorMode.Pursuing;

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

    [Header("Death")]
    [SerializeField] protected bool useRagdollOnDeath = true;
    [SerializeField] protected float destroyDelay = 10f;

    [Header("Debug")]
    [SerializeField] protected bool showDebugInfo = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Components
    // ════════════════════════════════════════════════════════════════════════

    protected NavMeshAgent navAgent;
    protected Animator animator;
    protected AudioSource audioSource;
    protected NpcRagdollController ragdollController;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    protected int currentHealth;
    protected bool isDead;
    protected bool isStunned;
    protected float stunEndTime;
    protected bool hasSwordEmbedded;
    protected int pendingSwordRemovalDamage;
    protected bool hasPendingSwordDamage;
    protected float stateTimer;
    protected bool canSeePlayer;
    protected float lastVisibilityCheckTime;
    protected Vector3 startPosition; // Für stationären Modus

    private float currentSpeedVelocity;
    private const float VISIBILITY_CHECK_INTERVAL = 0.15f;
    private const float SPEED_SMOOTH_TIME = 0.1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public BehaviorMode CurrentBehaviorMode => behaviorMode;
    public Vector3 StartPosition => startPosition;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool IsStunned => isStunned;
    public float RemainingStunTime => isStunned ? Mathf.Max(0f, stunEndTime - Time.time) : 0f;
    public Transform Transform => transform;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    protected virtual void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        ragdollController = GetComponent<NpcRagdollController>();

        currentHealth = maxHealth;
        startPosition = transform.position;

        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
            navAgent.angularSpeed = rotationSpeed * 50f;
            navAgent.stoppingDistance = stoppingDistance;
            navAgent.autoBraking = true;
        }
    }

    protected virtual void Start()
    {
        if (playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        OnStart();
    }

    protected virtual void Update()
    {
        if (isDead) return;

        if (isStunned)
        {
            if (hasSwordEmbedded)
            {
                StopMovement();
                UpdateAnimator();
                return;
            }

            if (Time.time >= stunEndTime)
                EndStun();
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

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Abstract Methods
    // ════════════════════════════════════════════════════════════════════════

    protected abstract void OnStart();
    protected abstract void UpdateBehavior();
    protected abstract void OnStunEnd();
    protected abstract void OnStunStart();
    public abstract string GetCurrentStateName();
    public abstract NpcType GetNpcType();
    public abstract int GetStateID();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region IEnemy Implementation
    // ════════════════════════════════════════════════════════════════════════

    public virtual void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position, Vector3.zero);
    }

    public virtual void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (isDead) return;

        currentHealth -= Mathf.RoundToInt(damage);
        ragdollController?.RegisterMeleeImpact(hitPoint);
        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    public virtual void ApplyStun(float duration)
    {
        if (isDead) return;

        isStunned = true;
        stunEndTime = Time.time + duration;
        StopMovement();
        animator?.SetBool("IsStunned", true);
        OnStunStart();
    }

    public virtual void OnMeleeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        if (playerTransform != null)
            ragdollController?.RegisterMeleeImpact(playerTransform.position);

        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    public virtual void OnThrownSwordHit(int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        currentHealth -= damage;
        ragdollController?.RegisterThrownSwordImpact(swordDirection, hitPoint);

        if (currentHealth <= 0) Die();
    }

    public virtual void OnSwordEmbedded()
    {
        if (isDead) return;

        hasSwordEmbedded = true;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;
        isStunned = true;
        stunEndTime = float.MaxValue;

        StopMovement();
        animator?.SetBool("IsStunned", true);
        OnStunStart();
    }

    public virtual void OnSwordRemoved(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;

        if (damage > 0)
        {
            pendingSwordRemovalDamage = damage;
            hasPendingSwordDamage = true;
        }

        stunEndTime = Time.time + residualStunDuration;
    }

    public virtual void OnSwordDashRemoval(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        if (damage > 0)
        {
            currentHealth -= damage;
            animator?.SetTrigger("Hit");
            PlaySound(hitSound);

            if (currentHealth <= 0)
            {
                Die();
                return;
            }
        }

        stunEndTime = Time.time + residualStunDuration;
    }

    public virtual void OnBulletDamage(int damage, Vector3 bulletDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        currentHealth -= damage;
        ragdollController?.RegisterBulletImpact(bulletDirection, hitPoint);
        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Stun System
    // ════════════════════════════════════════════════════════════════════════

    protected void EndStun()
    {
        isStunned = false;

        if (navAgent != null && navAgent.enabled)
            navAgent.isStopped = false;

        animator?.SetBool("IsStunned", false);

        if (hasPendingSwordDamage)
            ApplyPendingSwordDamage();

        if (!isDead)
            OnStunEnd();
    }

    private void ApplyPendingSwordDamage()
    {
        if (!hasPendingSwordDamage || pendingSwordRemovalDamage <= 0) return;

        int damage = pendingSwordRemovalDamage;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        currentHealth -= damage;
        animator?.SetTrigger("Hit");
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Movement Helpers
    // ════════════════════════════════════════════════════════════════════════

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

        return !navAgent.pathPending &&
               navAgent.remainingDistance <= navAgent.stoppingDistance &&
               (!navAgent.hasPath || navAgent.velocity.sqrMagnitude < 0.01f);
    }

    protected void RotateToward(Vector3 targetPosition, float speedMultiplier = 1f)
    {
        Vector3 direction = targetPosition - transform.position;
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

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Timer
    // ════════════════════════════════════════════════════════════════════════

    protected void SetStateTimer(float duration) => stateTimer = duration;

    protected bool UpdateStateTimer()
    {
        stateTimer -= Time.deltaTime;
        return stateTimer <= 0f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Detection & Visibility
    // ════════════════════════════════════════════════════════════════════════

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

        if (Physics.Raycast(eyePosition, (targetPosition - eyePosition).normalized,
            out RaycastHit hit, distanceToPlayer, lineOfSightMask))
        {
            return hit.transform == playerTransform || hit.transform.IsChildOf(playerTransform);
        }

        return true;
    }

    protected float GetDistanceToPlayer()
    {
        return playerTransform == null ? float.MaxValue :
               Vector3.Distance(transform.position, playerTransform.position);
    }

    protected Vector3 GetDirectionToPlayer()
    {
        if (playerTransform == null) return Vector3.zero;

        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        return dir.normalized;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Health & Death
    // ════════════════════════════════════════════════════════════════════════

    protected virtual void Die()
    {
        if (isDead) return;

        isDead = true;
        isStunned = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        if (useRagdollOnDeath && ragdollController != null)
        {
            ragdollController.ActivateRagdollWithAccumulatedImpact();
            if (animator != null) animator.enabled = false;
        }
        else
        {
            animator?.SetTrigger("Die");
        }

        if (destroyDelay >= 0)
            Destroy(gameObject, destroyDelay);
    }

    public virtual void DieWithImpact(Vector3 impactDirection, float forceMagnitude, Vector3? impactPoint = null)
    {
        if (isDead) return;

        isDead = true;
        isStunned = false;
        currentHealth = 0;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        if (useRagdollOnDeath && ragdollController != null)
        {
            ragdollController.ActivateRagdollWithImpact(impactDirection, forceMagnitude, impactPoint);
            if (animator != null) animator.enabled = false;
        }
        else
        {
            animator?.SetTrigger("Die");
        }

        if (destroyDelay >= 0)
            Destroy(gameObject, destroyDelay);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Animator & Audio
    // ════════════════════════════════════════════════════════════════════════

    protected virtual void UpdateAnimator()
    {
        if (animator == null) return;

        float targetSpeed = (navAgent != null && navAgent.enabled)
            ? navAgent.velocity.magnitude / moveSpeed : 0f;

        float smoothedSpeed = Mathf.SmoothDamp(
            animator.GetFloat("MoveSpeed"),
            targetSpeed,
            ref currentSpeedVelocity,
            SPEED_SMOOTH_TIME
        );

        animator.SetFloat("MoveSpeed", smoothedSpeed);
        animator.SetInteger("StateID", GetStateID());
        animator.SetBool("IsStunned", isStunned);
        animator.SetBool("IsDead", isDead);
    }

    protected void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected virtual void OnDrawGizmosSelected()
    {
        // Detection Range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Field of View
        Gizmos.color = Color.blue;
        Vector3 leftBoundary = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, fieldOfView * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up, leftBoundary * detectionRange);
        Gizmos.DrawRay(transform.position + Vector3.up, rightBoundary * detectionRange);

        // Startposition für stationären Modus
        if (behaviorMode == BehaviorMode.Stationary)
        {
            Gizmos.color = Color.cyan;
            Vector3 pos = Application.isPlaying ? startPosition : transform.position;
            Gizmos.DrawWireSphere(pos, 0.5f);
        }

        // NavMesh Path
        if (navAgent != null && navAgent.enabled && navAgent.hasPath)
        {
            Gizmos.color = Color.green;
            var corners = navAgent.path.corners;
            for (int i = 0; i < corners.Length - 1; i++)
                Gizmos.DrawLine(corners[i], corners[i + 1]);
        }
    }

    protected virtual void OnGUI()
    {
        if (!showDebugInfo || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.5f);
        if (screenPos.z > 0)
        {
            string modeStr = behaviorMode == BehaviorMode.Stationary ? "[S]" : "[P]";
            GUI.Label(
                new Rect(screenPos.x - 50, Screen.height - screenPos.y, 100, 60),
                $"{GetNpcType()} {modeStr}\n{GetCurrentStateName()}\nHP: {currentHealth}/{maxHealth}"
            );
        }
    }

    #endregion
}