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
// ANIMANCER MIGRATION:
// - Alle direkten Animator-Zugriffe (SetTrigger, SetBool, SetFloat)
//   sind durch INpcAnimationHandler-Aufrufe ersetzt.
// - Das 'animator' Feld existiert noch für Subsysteme wie NpcRagdollController,
//   wird aber NICHT mehr für Animationssteuerung verwendet.
// - Konkrete NPCs (SoldierNpc, GenTwoNpc) finden ihren AnimationManager
//   über GetComponentInChildren<T>() und exponieren ihn typsicher.
//
// AIM PROGRESS:
// - Generisches System für Laser-Wiggle und andere Aim-Indikatoren.
// - Subklassen rufen SetAimProgress() oder StartAimTracking() auf.
// - NpcLaserPointer liest AimProgress um den Wiggle-Radius zu steuern.
// - 0 = Aim gerade gestartet (max Wiggle), 1 = eingelockt (kein Wiggle).
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
    Defender,
    GenOne, 
    GenTwo,
    AntiDashDrone,
    ProxyMine,
    Scientist,
    Grenadier,
    Sniper
}

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

    [Header("Camera Snap Target")]
    [Tooltip("Transform auf den die Kamera bei Treffer snappt (z.B. Head- oder Chest-Bone). Wenn leer, wird transform.position + Vector3.up als Fallback genutzt.")]
    [SerializeField] protected Transform snapTarget;

    [Header("Debug")]
    [SerializeField] protected bool showDebugInfo = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Components
    // ════════════════════════════════════════════════════════════════════════

    protected NavMeshAgent navAgent;
    protected AudioSource audioSource;
    protected NpcRagdollController ragdollController;
    protected Transform playerTransform;

    /// <summary>
    /// Animation handler — alle Animationssteuerung läuft hierüber.
    /// Wird automatisch via GetComponentInChildren gefunden.
    /// Konkrete NPCs haben zusätzlich eine typsichere Referenz auf ihren
    /// spezifischen Manager (z.B. SoldierAnimationManager).
    /// </summary>
    protected INpcAnimationHandler animHandler;

    /// <summary>
    /// Rohe Animator-Referenz. Wird NICHT für Animationssteuerung verwendet
    /// (dafür gibt es animHandler). Existiert für Subsysteme die den Animator
    /// direkt brauchen (z.B. NpcRagdollController Zustandsprüfung).
    /// </summary>
    protected Animator animator;

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

    // Aim Progress Tracking
    private float aimTotalDuration;
    private float aimProgress;
    private bool isTrackingAim;

    // Movement Smoothing (für UpdateAnimator)
    private float currentSpeedVelocity;
    private const float SPEED_SMOOTH_TIME = 0.1f;
    private float smoothedSpeed;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Felder die im Inspector versteckt werden sollen.
    /// Subklassen überschreiben dies mit den Namen der irrelevanten Base-Felder.
    /// Nur visuell — die Werte bleiben serialisiert.
    /// </summary>
    public virtual string[] HiddenBaseFields => null;

    public BehaviorMode CurrentBehaviorMode => behaviorMode;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool IsStunned => isStunned;
    public float RemainingStunTime => isStunned ? Mathf.Max(0f, stunEndTime - Time.time) : 0f;
    public Transform Transform => transform;

    /// <summary>
    /// Snap-Ziel für die Kamera bei Treffer.
    /// Gibt das zugewiesene Transform zurück, oder null wenn keins gesetzt ist.
    /// </summary>
    public Transform SnapTarget => snapTarget;

    /// <summary>
    /// Steuert den Warnlaser (NpcLaserPointer).
    /// Subklassen setzen dies auf true, wenn der NPC sich auf einen Angriff vorbereitet.
    /// Die NpcLaserPointer-Komponente liest diesen Wert in LateUpdate.
    /// </summary>
    public bool IsLaserActive { get; set; }

    /// <summary>
    /// Fortschritt der Zielerfassung: 0 = gerade angefangen, 1 = eingelockt.
    /// Wird von NpcLaserPointer gelesen um den Wiggle-Radius zu steuern.
    /// 
    /// Nutzung durch Subklassen:
    ///   Option A: StartAimTracking(duration) → Progress wird automatisch berechnet
    ///             basierend auf stateTimer (synchron mit SetStateTimer).
    ///   Option B: SetAimProgress(value) → manuell setzen (z.B. für Firing-State).
    /// </summary>
    public float AimProgress => aimProgress;

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
        NpcRegistry.Register(this);

        navAgent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        ragdollController = GetComponent<NpcRagdollController>();

        // Animation handler finden (AnimancerComponent-basierter Manager)
        animHandler = GetComponentInChildren<INpcAnimationHandler>();

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

        // Aim Progress automatisch aktualisieren
        UpdateAimProgress();

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

    public void RotateTowardTarget()
    {
        if (playerTransform != null)
            RotateToward(playerTransform.position);
    }
    
    /// <summary>
    /// Rotates toward target using unscaled time (works during slow-mo).
    /// </summary>
    public void RotateTowardTargetUnscaled()
    {
        if (isStunned || playerTransform == null) return;

        Vector3 direction = playerTransform.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        float maxAngle = maxRotationSpeed * TimeManager.Instance.GameDeltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxAngle);
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
    #region Aim Progress
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Startet automatisches Aim-Tracking, synchron mit dem State Timer.
    /// Rufe dies im State.Enter() auf, NACH SetStateTimer().
    /// Der Progress wird automatisch in Update() berechnet:
    ///   progress = 1 - (stateTimer / totalDuration)
    /// 
    /// Beispiel in Aiming.Enter():
    ///   npc.SetStateTimer(npc.AimDuration);
    ///   npc.StartAimTracking(npc.AimDuration);
    /// </summary>
    protected void StartAimTracking(float totalDuration)
    {
        aimTotalDuration = Mathf.Max(totalDuration, 0.001f);
        aimProgress = 0f;
        isTrackingAim = true;
    }

    /// <summary>
    /// Setzt den Aim-Progress manuell (0-1).
    /// Deaktiviert automatisches Tracking.
    /// Nützlich z.B. im Firing-State wo der Progress auf 1 stehen soll.
    /// </summary>
    protected void SetAimProgress(float progress)
    {
        aimProgress = Mathf.Clamp01(progress);
        isTrackingAim = false;
    }

    /// <summary>
    /// Setzt den Aim-Progress auf 0 zurück und deaktiviert Tracking.
    /// Rufe dies auf wenn der NPC nicht mehr zielt (z.B. Idle, Reloading).
    /// </summary>
    protected void ResetAimProgress()
    {
        aimProgress = 0f;
        aimTotalDuration = 0f;
        isTrackingAim = false;
    }

    /// <summary>
    /// Wird jeden Frame in Update() aufgerufen.
    /// Berechnet den Progress automatisch wenn Tracking aktiv ist.
    /// </summary>
    private void UpdateAimProgress()
    {
        if (!isTrackingAim) return;

        // Progress = wie weit sind wir durch die Aim-Phase
        // stateTimer zählt von totalDuration runter nach 0
        float elapsed = aimTotalDuration - stateTimer;
        aimProgress = Mathf.Clamp01(elapsed / aimTotalDuration);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Stun System
    // ════════════════════════════════════════════════════════════════════════

    private void HandleStunned()
    {
        StopMovement();

        if (hasSwordEmbedded) return;

        if (Time.time >= stunEndTime)
            EndStun();
    }

    private void EndStun()
    {
        isStunned = false;

        if (navAgent != null && navAgent.enabled)
            navAgent.isStopped = false;

        animHandler?.PlayStunEnd();

        if (hasPendingSwordDamage)
            ApplyPendingSwordDamage();

        if (!isDead)
            OnStunEnd();
    }

    protected void ApplyPendingSwordDamage()
    {
        if (!hasPendingSwordDamage || pendingSwordRemovalDamage <= 0) return;

        int damage = pendingSwordRemovalDamage;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        currentHealth -= damage;
        
        animHandler?.PlayHitReaction();
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
        
        animHandler?.PlayHitReaction();
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    public virtual void ApplyStun(float duration)
    {
        if (isDead) return;

        isStunned = true;
        stunEndTime = Time.time + duration;
        IsLaserActive = false;
        ResetAimProgress();
        StopMovement();
        
        animHandler?.PlayStunStart();
        
        OnStunStart();
    }

    public virtual void OnMeleeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        
        if (playerTransform != null && ragdollController != null)
            ragdollController.RegisterMeleeImpact(playerTransform.position);

        animHandler?.PlayHitReaction();
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
        IsLaserActive = false;
        ResetAimProgress();

        StopMovement();
        
        animHandler?.PlayStunStart();
        
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
            
            animHandler?.PlayHitReaction();
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
        
        animHandler?.PlayHitReaction();
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
        NpcRegistry.Unregister(this);
        isStunned = false;
        IsLaserActive = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;
        ResetAimProgress();

        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        if (useRagdollOnDeath && ragdollController != null)
        {
            ragdollController.ActivateRagdollWithAccumulatedImpact();
            animHandler?.DisableForRagdoll();
        }
        else
        {
            animHandler?.PlayDeath();
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
        ResetAimProgress();

        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        if (useRagdollOnDeath && ragdollController != null)
        {
            ragdollController.ActivateRagdollWithImpact(impactDirection, forceMagnitude, impactPoint);
            animHandler?.DisableForRagdoll();
        }
        else
        {
            animHandler?.PlayDeath();
        }

        if (destroyDelay >= 0)
            Destroy(gameObject, destroyDelay);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Animator & Audio
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates movement animation via the animation handler.
    /// Override in subclasses that don't use NavMesh movement (e.g. GenTwo).
    /// </summary>
    protected virtual void UpdateAnimator()
    {
        if (animHandler == null) return;

        float targetSpeed = (navAgent != null && navAgent.enabled)
            ? navAgent.velocity.magnitude / moveSpeed : 0f;

        smoothedSpeed = Mathf.SmoothDamp(
            smoothedSpeed,
            targetSpeed,
            ref currentSpeedVelocity,
            SPEED_SMOOTH_TIME
        );

        animHandler.UpdateMovement(smoothedSpeed);
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

    protected virtual void OnDestroy()
    {
        NpcRegistry.Unregister(this);
    }

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

    #endregion
}
