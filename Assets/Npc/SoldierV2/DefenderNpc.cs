using UnityEngine;

/// <summary>
/// Defender NPC - Protective combatant that:
/// 1. Finds the soldier closest to the player
/// 2. Positions itself between the player and that soldier
/// 3. Blocks incoming attacks from the player
/// 4. Counters with a melee attack if block is successful
/// 
/// REFACTORED: State-Logik ist jetzt in DefenderStates.cs ausgelagert.
/// Diese Klasse ist nur noch der Koordinator und hält die Konfiguration.
/// UPDATED: Migrated from INpcInteraction to IEnemy interface.
/// </summary>
public class DefenderNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════════
    #region Inspector Fields - Defender Configuration
    // ════════════════════════════════════════════════════════════════════════════

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

    [Header("Audio/VFX")]
    [SerializeField] private AudioClip blockSound;
    [SerializeField] private AudioClip perfectBlockSound;
    [SerializeField] private AudioClip counterSound;
    [SerializeField] private ParticleSystem blockEffect;
    [SerializeField] private ParticleSystem perfectBlockEffect;

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region Public Config Accessors (readonly für States)
    // ════════════════════════════════════════════════════════════════════════════

    // Protection
    public float ProtectDistance => protectDistance;
    public float RepositionThreshold => repositionThreshold;
    public float SoldierSearchInterval => soldierSearchInterval;

    // Blocking
    public float BlockDetectionRange => blockDetectionRange;
    public float BlockAngle => blockAngle;
    public float BlockDuration => blockDuration;
    public float BlockCooldown => blockCooldown;
    public float PerfectBlockWindow => perfectBlockWindow;

    // Counter
    public float CounterDuration => counterDuration;
    public float CounterRange => counterRange;
    public int CounterDamage => counterDamage;

    // Base class accessors
    public Transform PlayerTransform => playerTransform;
    public bool CanSeePlayer => canSeePlayer;
    public Animator NpcAnimator => animator;

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════════

    private INpcState<DefenderNpc> currentState;

    // Shared state data (von States verwendet)
    public NpcBase ProtectedSoldier { get; set; }
    public float NextSoldierSearchTime { get; set; }
    public float LastBlockTime { get; set; }
    public float BlockStartTime { get; set; }
    public bool WasAttackBlocked { get; set; }
    public bool WasPerfectBlock { get; set; }

    // Player reference cache
    private PlayerCore playerCore;

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════════

    protected override void OnStart()
    {
        // Cache PlayerCore reference
        if (playerTransform != null)
        {
            playerCore = playerTransform.GetComponent<PlayerCore>();
        }

        FindSoldierToProtect();
        
        if (ProtectedSoldier != null)
        {
            ChangeState(new DefenderStates.MovingToProtect());
        }
        else
        {
            ChangeState(new DefenderStates.Idle());
        }
    }

    protected override void UpdateBehavior()
    {
        // Periodisch nach Soldiers suchen
        if (Time.time >= NextSoldierSearchTime)
        {
            FindSoldierToProtect();
            NextSoldierSearchTime = Time.time + soldierSearchInterval;
        }

        if (currentState == null) return;

        var nextState = currentState.Update(this);
        if (nextState != null)
        {
            ChangeState(nextState);
        }
    }

    protected override void OnStunStart()
    {
        ChangeState(new DefenderStates.Stunned());
    }

    protected override void OnStunEnd()
    {
        if (ProtectedSoldier != null && !ProtectedSoldier.IsDead)
        {
            ChangeState(new DefenderStates.MovingToProtect());
        }
        else
        {
            ChangeState(new DefenderStates.Idle());
        }
    }

    public override string GetCurrentStateName()
    {
        return currentState?.StateName ?? "None";
    }

    public override NpcType GetNpcType() => NpcType.Defender;

    public override int GetStateID()
    {
        return currentState?.StateID ?? 0;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<DefenderNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);

        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] State → {newState?.StateName}");
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region Protection Logic
    // ════════════════════════════════════════════════════════════════════════════

    public void FindSoldierToProtect()
    {
        NpcBase nearest = NpcManager.Instance?.GetSoldierClosestToPlayer();

        if (nearest != null && nearest != ProtectedSoldier)
        {
            ProtectedSoldier = nearest;
        }
        else if (nearest == null)
        {
            ProtectedSoldier = null;
        }
    }

    public Vector3 GetInterceptPosition()
    {
        if (ProtectedSoldier == null || playerTransform == null)
        {
            return transform.position;
        }

        Vector3 soldierPos = ProtectedSoldier.transform.position;
        Vector3 directionToPlayer = (playerTransform.position - soldierPos).normalized;

        return soldierPos + directionToPlayer * protectDistance;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region Block & Counter Actions
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prüft ob der Defender einen Block starten sollte.
    /// </summary>
    public bool ShouldStartBlocking()
    {
        if (Time.time - LastBlockTime < blockCooldown) return false;
        if (playerTransform == null) return false;
        if (GetDistanceToPlayer() > blockDetectionRange) return false;

        float angle = Vector3.Angle(transform.forward, GetDirectionToPlayer());
        return angle <= blockAngle * 0.5f;
    }

    /// <summary>
    /// Wird extern aufgerufen wenn ein Angriff diesen Defender trifft.
    /// Gibt true zurück wenn geblockt wurde.
    /// </summary>
    public bool TryBlockAttack()
    {
        // Kann nur im Guarding oder Blocking State blocken
        if (currentState is not DefenderStates.Guarding && 
            currentState is not DefenderStates.Blocking)
        {
            return false;
        }

        // Falls im Guarding, wechsle zu Blocking
        if (currentState is DefenderStates.Guarding)
        {
            ChangeState(new DefenderStates.Blocking());
        }

        float timeSinceBlockStart = Time.time - BlockStartTime;
        WasPerfectBlock = timeSinceBlockStart <= perfectBlockWindow;
        WasAttackBlocked = true;

        if (WasPerfectBlock)
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

    /// <summary>
    /// Führt den Counter-Angriff aus.
    /// </summary>
    public void TryDealCounterDamage()
    {
        if (playerTransform == null) return;
        if (GetDistanceToPlayer() > counterRange) return;

        float angle = Vector3.Angle(transform.forward, GetDirectionToPlayer());
        if (angle > 45f) return;

        if (playerCore != null)
        {
            playerCore.TakeDamage(counterDamage);
        }
        else
        {
            var pc = playerTransform.GetComponent<PlayerCore>();
            if (pc != null)
            {
                pc.TakeDamage(counterDamage);
                playerCore = pc;
            }
        }
    }

    public void PlayBlockSound()
    {
        PlaySound(blockSound);
    }

    public void PlayCounterSound()
    {
        PlaySound(counterSound);
    }

    /// <summary>
    /// Override: Blockt potentiell eingehenden Nahkampfschaden.
    /// UPDATED: Renamed from OnMeeleDamage to OnMeleeDamage (fixed typo).
    /// </summary>
    public override void OnMeleeDamage(int damage)
    {
        if (TryBlockAttack())
        {
            return; // Attack was blocked
        }

        base.OnMeleeDamage(damage);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region Movement Helpers (Public für States)
    // ════════════════════════════════════════════════════════════════════════════

    public new void MoveToward(Vector3 position, float speedMultiplier = 1f)
    {
        base.MoveToward(position, speedMultiplier);
    }

    public new void StopMovement()
    {
        base.StopMovement();
    }

    public new void RotateToward(Vector3 position, float speedMultiplier = 1f)
    {
        base.RotateToward(position, speedMultiplier);
    }

    public new float GetDistanceToPlayer()
    {
        return base.GetDistanceToPlayer();
    }

    public new Vector3 GetDirectionToPlayer()
    {
        return base.GetDirectionToPlayer();
    }

    public new bool HasReachedDestination()
    {
        return base.HasReachedDestination();
    }

    public new void SetStateTimer(float duration)
    {
        base.SetStateTimer(duration);
    }

    public new bool UpdateStateTimer()
    {
        return base.UpdateStateTimer();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    #region Debug Visualization
    // ════════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // Block Range
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, blockDetectionRange);

        // Counter Range
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, counterRange);

        // Verbindung zum geschützten Soldier
        if (ProtectedSoldier != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, ProtectedSoldier.transform.position);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(GetInterceptPosition(), 0.5f);
        }

        // Block Cone
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Vector3 leftBlock = Quaternion.Euler(0, -blockAngle * 0.5f, 0) * transform.forward;
        Vector3 rightBlock = Quaternion.Euler(0, blockAngle * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up, leftBlock * blockDetectionRange);
        Gizmos.DrawRay(transform.position + Vector3.up, rightBlock * blockDetectionRange);
    }

    #endregion
}