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
// - Das 'animator' Feld existiert noch für Subsysteme die den Animator
//   direkt brauchen, wird aber NICHT für Animationssteuerung verwendet.
// - Konkrete NPCs (SoldierNpc, GenTwoNpc) finden ihren AnimationManager
//   über GetComponentInChildren<T>() und exponieren ihn typsicher.
//
// AIM PROGRESS:
// - Generisches System für Laser-Wiggle und andere Aim-Indikatoren.
// - Subklassen rufen SetAimProgress() oder StartAimTracking() auf.
// - NpcLaserPointer liest AimProgress um den Wiggle-Radius zu steuern.
// - LaserPointer_Dash liest AimProgress für Farbverlauf und Breite.
// - 0 = Aim gerade gestartet (max Wiggle), 1 = eingelockt (kein Wiggle).
//
// AIM-IK STEUERUNG (zentral für alle NPCs):
// - AimController wird automatisch gefunden (wenn vorhanden auf dem Prefab).
// - States setzen IsAimActive = true/false um AimIK ein-/auszuschalten.
// - NpcBase leitet IsAimActive und TargetPosition jeden Frame an den AimController weiter.
// - Dash-Override läuft im AimController (smooth Blend-Out wenn Spieler dasht).
// - NPCs ohne AimController (z.B. AntiDashDrone, ProxyMine) sind nicht betroffen.
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
    Civilian,
    Grenadier,
    Sniper,
    Turret
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

    [Header("Overlay")]
    [Tooltip("Anzeigename im UI-Overlay. Wenn leer, wird der NpcType verwendet.")]
    [SerializeField] protected string displayName = "";

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Components
    // ════════════════════════════════════════════════════════════════════════

    protected NavMeshAgent navAgent;
    protected AudioSource audioSource;
    protected NpcImpactTracker impactTracker;
    protected NpcRagdollSwapper ragdollSwapper;
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
    /// direkt brauchen.
    /// </summary>
    protected Animator animator;

    /// <summary>
    /// AimIK-Controller für Oberkörper-Rotation zum Ziel.
    /// Automatisch gefunden — null wenn kein AimController auf dem Prefab liegt.
    /// </summary>
    protected AimController aimController;

    /// <summary>
    /// Laser-Pointer für visuelle Warnstrahlen (Standard-Modus, FOV-basiert).
    /// Automatisch gefunden — null wenn kein NpcLaserPointer auf dem Prefab liegt.
    /// NPCs die LaserPointer_Dash nutzen (z.B. GenTwo) finden und verwalten
    /// ihre eigene Referenz in der Subklasse.
    /// </summary>
    protected NpcLaserPointer laserPointer;

    /// <summary>
    /// Cached PlayerCore-Referenz für Dash-Erkennung und andere Spieler-Queries.
    /// Wird in Start() einmalig gesucht und steht allen Subklassen zur Verfügung.
    /// </summary>
    protected PlayerCore playerCore;

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

    // Death Type Tracking — wird in Damage-Methoden gesetzt, von Die() gelesen
    protected NpcDeathType lastDeathType = NpcDeathType.WholeBody;

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

    // Overlay
    private Renderer cachedRenderer;

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
    /// Whether this NPC can be hit by the player's dash auto-attack.
    /// Override and return false for NPCs that should be dashed through.
    /// Does NOT affect sword throw, bullet, or explosion damage.
    /// </summary>
    public virtual bool CanBeAutoAttacked => true;

    /// <summary>
    /// Snap-Ziel für die Kamera bei Treffer.
    /// Gibt das zugewiesene Transform zurück, oder null wenn keins gesetzt ist.
    /// </summary>
    public Transform SnapTarget => snapTarget;

    /// <summary>
    /// Name der im Overlay angezeigt wird.
    /// Fallback auf NpcType wenn kein displayName gesetzt ist.
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(displayName) 
        ? GetNpcType().ToString() 
        : displayName;

    /// <summary>
    /// Renderer für Bounding-Box-Berechnung.
    /// Wird beim ersten Zugriff gecacht.
    /// </summary>
    public Renderer BoundsRenderer
    {
        get
        {
            if (cachedRenderer == null)
                cachedRenderer = GetComponentInChildren<Renderer>();
            return cachedRenderer;
        }
    }

    /// <summary>
    /// Steuert den Warnlaser (NpcLaserPointer oder LaserPointer_Dash).
    /// Subklassen setzen dies auf true, wenn der NPC sich auf einen Angriff vorbereitet.
    /// Die Laser-Komponente liest diesen Wert in LateUpdate.
    /// </summary>
    public bool IsLaserActive { get; set; }

    /// <summary>
    /// Steuert den AimIK-Controller.
    /// States setzen dies auf true/false um AimIK ein-/auszuschalten.
    /// NpcBase leitet den Wert jeden Frame an den AimController weiter.
    /// 
    /// Wird automatisch in ApplyStun() und Die() auf false gesetzt.
    /// </summary>
    public bool IsAimActive { get; set; }

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

    /// <summary>
    /// True wenn der Spieler gerade dasht (Attack-Dash oder Sword-Dash).
    /// Steht allen Subklassen zur Verfügung.
    /// </summary>
    public bool IsPlayerDashing
    {
        get
        {
            if (playerCore == null) return false;
            return playerCore.CurrentState == PlayerCore.PlayerState.Dashing
                || playerCore.CurrentState == PlayerCore.PlayerState.DashingToSword;
        }
    }

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
        impactTracker = GetComponent<NpcImpactTracker>();
        ragdollSwapper = GetComponent<NpcRagdollSwapper>();

        // Animation handler finden (AnimancerComponent-basierter Manager)
        animHandler = GetComponentInChildren<INpcAnimationHandler>();

        // AimController finden (optional — nicht alle NPCs haben einen)
        aimController = GetComponent<AimController>();

        // LaserPointer finden (optional — nicht alle NPCs haben einen)
        laserPointer = GetComponent<NpcLaserPointer>();

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
        {
            playerTransform = player.transform;
            playerCore = player.GetComponent<PlayerCore>();
        }

        // PlayerCore an AimController weitergeben für Dash-Erkennung
        if (aimController != null && playerCore != null)
        {
            aimController.SetPlayerCore(playerCore);
        }

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

        // AimIK-Steuerung (zentral für alle NPCs)
        UpdateAimController();

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
    #region AimIK Steuerung
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Zentrale AimIK-Steuerung, wird jeden Frame aus Update() aufgerufen.
    /// Leitet IsAimActive und die Zielposition an den AimController weiter.
    /// 
    /// Subklassen können GetAimTargetPosition() überschreiben, um eine
    /// andere Zielposition zu verwenden (z.B. LockedTargetPosition beim Soldier).
    /// 
    /// Der Dash-Override läuft intern im AimController — hier muss nichts
    /// extra geprüft werden.
    /// </summary>
    private void UpdateAimController()
    {
        if (aimController == null) return;

        // Weight-Steuerung: States setzen IsAimActive, wir leiten es weiter
        if (IsAimActive)
            aimController.EnableAim();
        else
            aimController.DisableAim();

        // Zielposition aktualisieren
        aimController.SetTargetPosition(GetAimTargetPosition());
    }

    /// <summary>
    /// Gibt die Zielposition für den AimController zurück.
    /// Default: Live-Spielerposition.
    /// Subklassen können dies überschreiben für spezielle Logik
    /// (z.B. Soldier mit LockedTargetPosition bei Dash-Lock).
    /// </summary>
    protected virtual Vector3 GetAimTargetPosition()
    {
        return TargetPosition;
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
        lastDeathType = NpcDeathType.WholeBody;
        
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
        lastDeathType = NpcDeathType.WholeBody;
        
        if (impactTracker != null)
            impactTracker.RegisterMeleeImpact(hitPoint);
        
        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    public virtual void ApplyStun(float duration)
    {
        if (isDead) return;

        isStunned = true;
        stunEndTime = Time.time + duration;
        IsLaserActive = false;
        IsAimActive = false;
        ResetAimProgress();
        StopMovement();

        aimController?.DisableImmediate();
        
        animHandler?.PlayStunStart();
        
        OnStunStart();
    }

    public virtual void OnMeleeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        lastDeathType = NpcDeathType.Sliced;
        
        if (playerTransform != null && impactTracker != null)
            impactTracker.RegisterMeleeImpact(playerTransform.position);

        PlaySound(hitSound);

        if (currentHealth <= 0) Die();
    }

    public virtual void OnThrownSwordHit(int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        if (isDead) return;

        currentHealth -= damage;
        lastDeathType = NpcDeathType.WholeBody;
        
        if (impactTracker != null)
            impactTracker.RegisterThrownSwordImpact(swordDirection, hitPoint);

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
        IsAimActive = false;
        ResetAimProgress();

        StopMovement();

        aimController?.DisableImmediate();
        
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
            lastDeathType = NpcDeathType.WholeBody;
            
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
        lastDeathType = NpcDeathType.WholeBody;
        
        if (impactTracker != null)
            impactTracker.RegisterBulletImpact(bulletDirection, hitPoint);
        
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
        IsAimActive = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;
        ResetAimProgress();

        aimController?.DisableImmediate();

        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        // ── Ragdoll Swap: Neues System (ersetzt NPC durch Ragdoll-Prefabs) ──
        if (ragdollSwapper != null && ragdollSwapper.IsConfigured)
        {
            // Animation stoppen BEVOR Bones kopiert werden
            // (DisableForRagdoll stoppt Animancer, die Pose bleibt stehen)
            animHandler?.DisableForRagdoll();

            // Impact-Daten vom ImpactTracker holen (falls vorhanden)
            Vector3 impactDir = Vector3.forward;
            float impactMag = 0f;
            Vector3? impactPoint = null;

            if (impactTracker != null)
            {
                GetAccumulatedImpact(out impactDir, out impactMag, out impactPoint);
            }

            // Swap durchführen — spawnt Ragdolls und zerstört dieses GameObject
            ragdollSwapper.PerformSwap(lastDeathType, impactDir, impactPoint);

            // WICHTIG: Nach PerformSwap wird dieses GameObject zerstört.
            // Kein Code nach dieser Zeile wird ausgeführt.
            return;
        }

        // ── Fallback: Kein Swapper vorhanden (z.B. AntiDashDrone, ProxyMine) ──

        if (destroyDelay >= 0)
            Destroy(gameObject, destroyDelay);
    }

    public virtual void DieWithImpact(Vector3 impactDirection, float forceMagnitude, Vector3? impactPoint = null)
    {
        if (isDead) return;

        isDead = true;
        isStunned = false;
        currentHealth = 0;
        IsLaserActive = false;
        IsAimActive = false;
        ResetAimProgress();

        aimController?.DisableImmediate();

        if (navAgent != null)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        // ── Ragdoll Swap: Neues System ──
        if (ragdollSwapper != null && ragdollSwapper.IsConfigured)
        {
            animHandler?.DisableForRagdoll();
            ragdollSwapper.PerformSwap(lastDeathType, impactDirection, impactPoint);
            return;
        }

        // ── Fallback: Kein Swapper vorhanden ──

        if (destroyDelay >= 0)
            Destroy(gameObject, destroyDelay);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Ragdoll Swap Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Liest die akkumulierten Impact-Daten aus dem NpcImpactTracker.
    /// Wird von Die() genutzt um die Daten an den NpcRagdollSwapper weiterzugeben.
    /// </summary>
    private void GetAccumulatedImpact(out Vector3 direction, out float magnitude, out Vector3? point)
    {
        if (impactTracker != null && impactTracker.HasAccumulatedImpact)
        {
            direction = impactTracker.AccumulatedImpactForce.normalized;
            magnitude = impactTracker.AccumulatedImpactForce.magnitude;
            point = impactTracker.LastImpactPoint;
        }
        else
        {
            direction = Vector3.forward;
            magnitude = 0f;
            point = null;
        }
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
