using UnityEngine;
using UnityEngine.AI;

// ════════════════════════════════════════════════════════════════════════════
// NPC BASE - Einfache Basisklasse für alle NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// KONZEPT:
// - NPC weiß IMMER wo der Spieler ist
// - Rotation begrenzt durch maxRotationSpeed
// - Einfaches Grundverhalten als Basis für Erweiterungen
//
// ════════════════════════════════════════════════════════════════════════════

public enum BehaviorMode
{
    Stationary,  // Bleibt an Position, greift nur an wenn Spieler in Reichweite
    Pursuing     // Verfolgt den Spieler aktiv
}

public enum NpcType
{
    Soldier,
    Defender
}

[RequireComponent(typeof(NavMeshAgent))]
public abstract class NpcBase : MonoBehaviour, IEnemy
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Behavior")]
    [SerializeField] protected BehaviorMode behaviorMode = BehaviorMode.Pursuing;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 100;

    [Header("Movement")]
    [SerializeField] protected float moveSpeed = 4f;
    [SerializeField] protected float stoppingDistance = 0.5f;

    [Header("Rotation")]
    [Tooltip("Maximale Rotationsgeschwindigkeit in Grad pro Sekunde")]
    [SerializeField] protected float maxRotationSpeed = 180f;

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
    protected Transform playerTransform;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    // Health & Combat
    protected int currentHealth;
    protected bool isDead;
    protected bool isStunned;
    protected float stunEndTime;
    protected bool hasSwordEmbedded;
    protected int pendingSwordRemovalDamage;
    protected bool hasPendingSwordDamage;

    // Pathfinding
    private bool canReachPlayer;
    private float lastPathCheckTime;
    private const float PATH_CHECK_INTERVAL = 0.3f;

    // State Timer (für Subklassen)
    protected float stateTimer;

    // Animation
    private float currentSpeedVelocity;
    private const float SPEED_SMOOTH_TIME = 0.1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public BehaviorMode CurrentBehaviorMode => behaviorMode;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool IsStunned => isStunned;
    public float RemainingStunTime => isStunned ? Mathf.Max(0f, stunEndTime - Time.time) : 0f;
    public Transform Transform => transform;

    /// <summary>
    /// Position des Spielers. NPC weiß IMMER wo der Spieler ist.
    /// </summary>
    public Vector3 TargetPosition => playerTransform != null ? playerTransform.position : transform.position;

    /// <summary>
    /// Distanz zum Spieler.
    /// </summary>
    public float DistanceToTarget => playerTransform != null 
        ? Vector3.Distance(transform.position, playerTransform.position) 
        : 0f;

    /// <summary>
    /// True wenn NPC zum Spieler laufen kann (gültiger NavMesh-Pfad).
    /// </summary>
    public bool CanReachPlayer => canReachPlayer;

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

        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
            navAgent.stoppingDistance = stoppingDistance;
            navAgent.updateRotation = false;
        }
    }

    protected virtual void Start()
    {
        // Spieler finden
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            playerTransform = player.transform;

        // Initialer Pfad-Check
        UpdatePathCheckImmediate();

        OnStart();
    }

    protected virtual void Update()
    {
        if (isDead) return;

        // Pfad-Check (periodisch)
        UpdatePathCheck();

        // Stun-Handling
        if (isStunned)
        {
            HandleStunned();
            return;
        }

        // Verhalten (Subklasse)
        UpdateBehavior();
        UpdateAnimator();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Pathfinding
    // ════════════════════════════════════════════════════════════════════════

    private void UpdatePathCheckImmediate()
    {
        if (navAgent == null || !navAgent.enabled || playerTransform == null)
        {
            canReachPlayer = false;
            return;
        }

        NavMeshPath path = new NavMeshPath();
        navAgent.CalculatePath(playerTransform.position, path);
        canReachPlayer = (path.status == NavMeshPathStatus.PathComplete);
        lastPathCheckTime = Time.time;
    }

    private void UpdatePathCheck()
    {
        if (Time.time - lastPathCheckTime < PATH_CHECK_INTERVAL) return;
        UpdatePathCheckImmediate();
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

    protected void MoveTowardTarget(float speedMultiplier = 1f)
    {
        if (playerTransform != null)
            MoveToward(playerTransform.position, speedMultiplier);
    }

    protected void StopMovement()
    {
        if (navAgent == null || !navAgent.enabled) return;
        navAgent.isStopped = true;
        navAgent.ResetPath();
    }

    protected void RotateToward(Vector3 targetPosition)
    {
        if (isStunned) return;
        
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        float maxAngle = maxRotationSpeed * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxAngle);
    }

    protected void RotateTowardTarget()
    {
        if (playerTransform != null)
            RotateToward(playerTransform.position);
    }

    protected Vector3 GetDirectionToTarget()
    {
        if (playerTransform == null) return transform.forward;
        
        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        return dir.normalized;
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
    #region Stun System
    // ════════════════════════════════════════════════════════════════════════

    private void HandleStunned()
    {
        StopMovement();
        UpdateAnimator();

        if (hasSwordEmbedded) return;

        if (Time.time >= stunEndTime)
            EndStun();
    }

    private void EndStun()
    {
        isStunned = false;

        if (navAgent != null && navAgent.enabled)
            navAgent.isStopped = false;

        if (animator != null)
            animator.SetBool("IsStunned", false);

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
        
        if (animator != null)
            animator.SetTrigger("Hit");
        
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
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
        
        if (ragdollController != null)
            ragdollController.RegisterMeleeImpact(hitPoint);
        
        if (animator != null)
            animator.SetTrigger("Hit");
        
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    public virtual void ApplyStun(float duration)
    {
        if (isDead) return;

        isStunned = true;
        stunEndTime = Time.time + duration;
        StopMovement();
        
        if (animator != null)
            animator.SetBool("IsStunned", true);
        
        OnStunStart();
    }

    public virtual void OnMeleeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        
        if (playerTransform != null && ragdollController != null)
            ragdollController.RegisterMeleeImpact(playerTransform.position);

        if (animator != null)
            animator.SetTrigger("Hit");
        
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    public virtual void OnThrownSwordHit(int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        currentHealth -= damage;
        
        if (ragdollController != null)
            ragdollController.RegisterThrownSwordImpact(swordDirection, hitPoint);

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
        
        if (animator != null)
            animator.SetBool("IsStunned", true);
        
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
            
            if (animator != null)
                animator.SetTrigger("Hit");
            
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
        
        if (ragdollController != null)
            ragdollController.RegisterBulletImpact(bulletDirection, hitPoint);
        
        if (animator != null)
            animator.SetTrigger("Hit");
        
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
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
            if (animator != null)
                animator.SetTrigger("Die");
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
            if (animator != null)
                animator.SetTrigger("Die");
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
        if (!Application.isPlaying || playerTransform == null) return;

        // Linie zum Spieler
        Gizmos.color = canReachPlayer ? Color.green : Color.red;
        Gizmos.DrawLine(transform.position + Vector3.up, playerTransform.position + Vector3.up);
        Gizmos.DrawWireSphere(playerTransform.position, 0.4f);

        // NavMesh Path
        if (navAgent != null && navAgent.enabled && navAgent.hasPath)
        {
            Gizmos.color = Color.cyan;
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
            string mode = behaviorMode == BehaviorMode.Stationary ? "S" : "P";
            GUI.Label(
                new Rect(screenPos.x - 50, Screen.height - screenPos.y, 120, 50),
                $"{GetNpcType()}[{mode}]\n{GetCurrentStateName()}\nHP:{currentHealth}"
            );
        }
    }

    #endregion
}
