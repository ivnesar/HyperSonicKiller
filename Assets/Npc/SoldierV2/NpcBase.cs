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
/// Handhabt: Movement, Stun, Health, Sword-Interaktion, Animation, Awareness.
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
    [SerializeField] protected LayerMask lineOfSightMask;

    [Header("Tracking FOV (für aktives Verfolgen)")]
    [Tooltip("Fester FOV für aktives Tracking - Spieler muss hier drin sein damit NPC ihn anvisieren kann")]
    [SerializeField] protected float trackingFOV = 120f;

    [Header("Detection FOV (für Wahrnehmung)")]
    [Tooltip("FOV wenn Spieler weit entfernt ist - nur für initiale Erkennung")]
    [SerializeField] protected float detectionFOVFar = 60f;
    [Tooltip("FOV wenn Spieler sehr nah ist (360 = Rundumwahrnehmung) - 'spürt' dass jemand da ist")]
    [SerializeField] protected float detectionFOVNear = 360f;
    [Tooltip("Distanz ab der volle Nahbereichs-FOV gilt")]
    [SerializeField] protected float detectionNearDistance = 2f;
    [Tooltip("Distanz ab der minimaler Fernbereichs-FOV gilt")]
    [SerializeField] protected float detectionFarDistance = 15f;

    [Header("Awareness")]
    [Tooltip("Verzögerung bevor der NPC reagiert wenn der Spieler aus dem Sichtfeld verschwindet")]
    [SerializeField] protected float reactionDelay = 0.3f;
    [Tooltip("Rotationsgeschwindigkeit-Multiplikator wenn Spieler nicht sichtbar ist (0.3 = 30% der normalen Geschwindigkeit)")]
    [SerializeField] protected float blindRotationMultiplier = 0.3f;

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
    protected Vector3 startPosition;

    // Visibility & Awareness
    protected bool canSeePlayer;            // Tracking: Kann aktiv verfolgen
    protected bool canDetectPlayer;         // Detection: Weiß dass Spieler da ist
    protected float lastVisibilityCheckTime;
    protected Vector3 lastKnownPlayerPosition;
    protected float lostSightTime;              // Wann wurde der Spieler zuletzt aus den Augen verloren
    protected bool hadPlayerInSight;            // War der Spieler jemals im Sichtfeld

    // Pathfinding
    protected bool hasValidPathToPlayer;
    protected float lastPathCheckTime;
    protected const float PATH_CHECK_INTERVAL = 0.5f;

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

    /// <summary>
    /// Letzte bekannte Position des Spielers (wird nur aktualisiert wenn Spieler sichtbar).
    /// </summary>
    public Vector3 LastKnownPlayerPosition => lastKnownPlayerPosition;

    /// <summary>
    /// True wenn die Reaktionsverzögerung abgelaufen ist und der NPC reagieren darf.
    /// </summary>
    public bool CanReactToPlayerLoss => Time.time >= lostSightTime + reactionDelay;

    /// <summary>
    /// True wenn der NPC den Spieler aktiv verfolgen kann (im Tracking-FOV).
    /// lastKnownPlayerPosition wird nur aktualisiert wenn dies true ist.
    /// </summary>
    public bool CanSeePlayer => canSeePlayer;

    /// <summary>
    /// True wenn der NPC den Spieler wahrnimmt (im Detection-FOV).
    /// Wird für initiale Erkennung und "Spüren" von nahen Gegnern verwendet.
    /// </summary>
    public bool CanDetectPlayer => canDetectPlayer;

    /// <summary>
    /// True wenn ein gültiger NavMesh-Pfad zum Spieler existiert.
    /// </summary>
    public bool HasValidPathToPlayer => hasValidPathToPlayer;

    /// <summary>
    /// True wenn der NPC die letzte bekannte Position sehen kann aber der Spieler nicht dort ist.
    /// Bedeutet: Spieler ist entkommen, Bewegung zur letzten Position ist sinnlos.
    /// </summary>
    public bool HasLostPlayer => HasLostPlayerAtLastKnownPosition();

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
        lastKnownPlayerPosition = transform.position + transform.forward * 5f; // Default: vor dem NPC

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

        // Initiale Position speichern falls Spieler schon existiert
        if (playerTransform != null)
            lastKnownPlayerPosition = playerTransform.position;

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
        UpdatePathCache();
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

    /// <summary>
    /// Rotiert den NPC zu einer Zielposition.
    /// Wenn der Spieler nicht sichtbar ist, wird langsamer rotiert.
    /// </summary>
    protected void RotateToward(Vector3 targetPosition, float speedMultiplier = 1f)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            // Langsamere Rotation wenn Spieler nicht sichtbar
            float effectiveMultiplier = canSeePlayer ? speedMultiplier : speedMultiplier * blindRotationMultiplier;

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * effectiveMultiplier * Time.deltaTime
            );
        }
    }

    /// <summary>
    /// Rotiert zur letzten bekannten Spielerposition.
    /// Nützlich wenn der Spieler gerade aus dem Sichtfeld verschwunden ist.
    /// </summary>
    protected void RotateTowardLastKnownPosition(float speedMultiplier = 1f)
    {
        RotateToward(lastKnownPlayerPosition, speedMultiplier);
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
            bool wasVisible = canSeePlayer;
            
            // Zwei separate Checks
            canSeePlayer = CheckTrackingLineOfSight();      // Enges FOV - für aktives Verfolgen
            canDetectPlayer = CheckDetectionLineOfSight(); // Dynamisches FOV - für Wahrnehmung

            // Position nur aktualisieren wenn im TRACKING-FOV (nicht Detection!)
            if (canSeePlayer && playerTransform != null)
            {
                lastKnownPlayerPosition = playerTransform.position;
                hadPlayerInSight = true;
            }
            // Spieler gerade aus TRACKING-Sicht verloren → Timer starten
            else if (wasVisible && !canSeePlayer)
            {
                lostSightTime = Time.time;
            }

            lastVisibilityCheckTime = Time.time;
        }
    }

    /// <summary>
    /// Prüft periodisch ob ein gültiger Pfad zum Spieler existiert.
    /// </summary>
    protected void UpdatePathCache()
    {
        if (Time.time - lastPathCheckTime >= PATH_CHECK_INTERVAL)
        {
            hasValidPathToPlayer = CheckPathToPlayer();
            lastPathCheckTime = Time.time;
        }
    }

    /// <summary>
    /// Prüft ob ein vollständiger NavMesh-Pfad zum Spieler existiert.
    /// </summary>
    protected bool CheckPathToPlayer()
    {
        if (playerTransform == null || navAgent == null || !navAgent.enabled)
            return false;

        NavMeshPath path = new NavMeshPath();
        navAgent.CalculatePath(playerTransform.position, path);
        
        return path.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>
    /// Prüft ob ein vollständiger NavMesh-Pfad zu einer Position existiert.
    /// </summary>
    protected bool CheckPathToPosition(Vector3 targetPosition)
    {
        if (navAgent == null || !navAgent.enabled)
            return false;

        NavMeshPath path = new NavMeshPath();
        navAgent.CalculatePath(targetPosition, path);
        
        return path.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>
    /// Tracking Check: Kann der NPC den Spieler aktiv anvisieren?
    /// Verwendet festen trackingFOV.
    /// </summary>
    protected bool CheckTrackingLineOfSight()
    {
        if (playerTransform == null) return false;

        Vector3 directionToPlayer = playerTransform.position - transform.position;
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > detectionRange) return false;

        // Fester Tracking-FOV
        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        if (angle > trackingFOV * 0.5f) return false;

        // Raycast für Hindernisse
        Vector3 eyePosition = transform.position + Vector3.up * 1.5f;
        Vector3 targetPosition = playerTransform.position + Vector3.up * 1f;

        if (Physics.Raycast(eyePosition, (targetPosition - eyePosition).normalized,
            out RaycastHit hit, distanceToPlayer, lineOfSightMask))
        {
            return hit.transform == playerTransform || hit.transform.IsChildOf(playerTransform);
        }

        return true;
    }

    /// <summary>
    /// Detection Check: Nimmt der NPC den Spieler wahr?
    /// Verwendet dynamischen FOV basierend auf Distanz.
    /// </summary>
    protected bool CheckDetectionLineOfSight()
    {
        if (playerTransform == null) return false;

        Vector3 directionToPlayer = playerTransform.position - transform.position;
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > detectionRange) return false;

        // Dynamischer Detection-FOV
        float currentFov = CalculateDynamicDetectionFOV(distanceToPlayer);
        
        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        if (angle > currentFov * 0.5f) return false;

        // Raycast für Hindernisse
        Vector3 eyePosition = transform.position + Vector3.up * 1.5f;
        Vector3 targetPosition = playerTransform.position + Vector3.up * 1f;

        if (Physics.Raycast(eyePosition, (targetPosition - eyePosition).normalized,
            out RaycastHit hit, distanceToPlayer, lineOfSightMask))
        {
            return hit.transform == playerTransform || hit.transform.IsChildOf(playerTransform);
        }

        return true;
    }

    /// <summary>
    /// Berechnet den Detection-FOV basierend auf der Distanz.
    /// Je näher der Spieler, desto größer der FOV (bis zu 360°).
    /// </summary>
    protected float CalculateDynamicDetectionFOV(float distance)
    {
        if (distance <= detectionNearDistance)
            return detectionFOVNear;
        
        if (distance >= detectionFarDistance)
            return detectionFOVFar;

        // Linear interpolieren zwischen Near und Far
        float t = (distance - detectionNearDistance) / (detectionFarDistance - detectionNearDistance);
        return Mathf.Lerp(detectionFOVNear, detectionFOVFar, t);
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

    /// <summary>
    /// Gibt die Richtung zur letzten bekannten Spielerposition zurück.
    /// </summary>
    protected Vector3 GetDirectionToLastKnownPosition()
    {
        Vector3 dir = lastKnownPlayerPosition - transform.position;
        dir.y = 0f;
        return dir.normalized;
    }

    /// <summary>
    /// Prüft ob der NPC eine bestimmte Position sehen kann (Line of Sight).
    /// </summary>
    protected bool CanSeePosition(Vector3 targetPosition)
    {
        Vector3 eyePosition = transform.position + Vector3.up * 1.5f;
        Vector3 direction = targetPosition - eyePosition;
        float distance = direction.magnitude;

        // Zu weit weg
        if (distance > detectionRange) return false;

        // Raycast - wenn nichts getroffen wird, ist die Position sichtbar
        if (Physics.Raycast(eyePosition, direction.normalized, out RaycastHit hit, distance, lineOfSightMask))
        {
            // Etwas blockiert die Sicht
            return false;
        }

        return true;
    }

    /// <summary>
    /// Prüft ob der NPC die letzte bekannte Spielerposition sehen kann,
    /// aber der Spieler nicht dort ist (= Spieler ist entkommen).
    /// </summary>
    protected bool HasLostPlayerAtLastKnownPosition()
    {
        // Spieler ist sichtbar → nicht verloren
        if (canSeePlayer) return false;

        // Kann die letzte bekannte Position sehen?
        if (!CanSeePosition(lastKnownPlayerPosition)) return false;

        // Position sichtbar aber Spieler nicht → verloren
        return true;
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

        // Tracking FOV (fester Kegel - grün)
        Gizmos.color = Color.green;
        Vector3 leftTracking = Quaternion.Euler(0, -trackingFOV * 0.5f, 0) * transform.forward;
        Vector3 rightTracking = Quaternion.Euler(0, trackingFOV * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up, leftTracking * detectionRange * 0.5f);
        Gizmos.DrawRay(transform.position + Vector3.up, rightTracking * detectionRange * 0.5f);

        // Detection FOV - Far (äußerer Kegel - blau)
        Gizmos.color = Color.blue;
        Vector3 leftDetectionFar = Quaternion.Euler(0, -detectionFOVFar * 0.5f, 0) * transform.forward;
        Vector3 rightDetectionFar = Quaternion.Euler(0, detectionFOVFar * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up, leftDetectionFar * detectionFarDistance);
        Gizmos.DrawRay(transform.position + Vector3.up, rightDetectionFar * detectionFarDistance);

        // Detection FOV - Near (innerer Bereich - cyan)
        if (detectionFOVNear < 360f)
        {
            Gizmos.color = Color.cyan;
            Vector3 leftDetectionNear = Quaternion.Euler(0, -detectionFOVNear * 0.5f, 0) * transform.forward;
            Vector3 rightDetectionNear = Quaternion.Euler(0, detectionFOVNear * 0.5f, 0) * transform.forward;
            Gizmos.DrawRay(transform.position + Vector3.up, leftDetectionNear * detectionNearDistance);
            Gizmos.DrawRay(transform.position + Vector3.up, rightDetectionNear * detectionNearDistance);
        }
        else
        {
            // 360° Nahbereich als Kreis darstellen
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up, detectionNearDistance);
        }

        // Startposition für stationären Modus
        if (behaviorMode == BehaviorMode.Stationary)
        {
            Gizmos.color = Color.magenta;
            Vector3 pos = Application.isPlaying ? startPosition : transform.position;
            Gizmos.DrawWireSphere(pos, 0.5f);
        }

        // Letzte bekannte Spielerposition (nur im Play Mode)
        if (Application.isPlaying && hadPlayerInSight)
        {
            Gizmos.color = canSeePlayer ? Color.green : Color.red;
            Gizmos.DrawWireSphere(lastKnownPlayerPosition, 0.3f);
            Gizmos.DrawLine(transform.position + Vector3.up, lastKnownPlayerPosition + Vector3.up);
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
            // 👁 = Tracking (kann anvisieren), 👂 = Detection only (spürt), ? = nichts
            string sightStr = canSeePlayer ? "👁" : (canDetectPlayer ? "👂" : "?");
            GUI.Label(
                new Rect(screenPos.x - 50, Screen.height - screenPos.y, 100, 60),
                $"{GetNpcType()} {modeStr} {sightStr}\n{GetCurrentStateName()}\nHP: {currentHealth}/{maxHealth}"
            );
        }
    }

    #endregion
}
