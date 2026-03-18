using UnityEngine;
using System;

/// <summary>
/// Handles combat state and automatic blocking.
/// 
/// UPDATED: Manual melee attack removed - attacks are now automatic during dash.
/// Block is passive - player automatically blocks incoming damage as long as BlockHP > 0.
/// UPDATED: Stunned state replaced with Exhausted state:
///   - Exhausted allows movement and dashing (without damage)
///   - Exhausted prevents throwing sword and attacking
///   - Triggered when BlockHP reaches 0 (bullets, shield reflection, etc.)
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerCombat : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Enums
    // ════════════════════════════════════════════════════════════════════════

    public enum CombatState
    {
        Idle,
        Attacking,      // Triggered by dash
        Exhausted,      // BlockHP = 0: can move/dash, but can't attack/throw
        Disarmed        // Sword is thrown
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action<CombatState> OnCombatStateChanged;
    public event Action OnAttack;                           // Fired during dash attack
    public event Action OnBlockedHit;                       // Fired when a hit is blocked
    public event Action OnExhausted;                        // Fired when entering Exhausted state
    public event Action OnExhaustionRecovered;              // Fired when Exhausted ends
    public event Action<float, float> OnBlockHPChanged;     // (current, max)

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector - Attack Settings (for reference/tuning)
    // ════════════════════════════════════════════════════════════════════════

    [Header("Melee Attack (Auto during Dash)")]
    [Tooltip("These values are now in PlayerDash - kept here for reference")]
    [SerializeField] private int meleeDamageReference = 50;
    [SerializeField] private LayerMask enemyLayer;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector - Block Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Passive Block / Shield")]
    [SerializeField] private float maxBlockHP = 100f;
    [SerializeField] private float blockRegenDelay = 1f;
    [SerializeField] private float blockRegenRate = 30f;
    
    [Header("Exhaustion Settings")]
    [Tooltip("Duration of exhaustion when BlockHP reaches 0")]
    [SerializeField] private float exhaustionDuration = 1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private PlayerSwordThrow swordThrow;
    private PlayerDash dash;

    // Combat state
    private CombatState currentState = CombatState.Idle;
    private float lastDamageTime;
    private float exhaustionEndTime;

    // Block HP
    private float currentBlockHP;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public CombatState CurrentState => currentState;
    
    /// <summary>
    /// Returns true if the player can currently auto-block.
    /// </summary>
    public bool CanAutoBlock => currentBlockHP > 0 && 
                                currentState != CombatState.Exhausted && 
                                currentState != CombatState.Disarmed &&
                                HasSword;
    
    /// <summary>
    /// For PlayerCore compatibility - returns true if player would block the next hit.
    /// </summary>
    public bool IsBlocking => CanAutoBlock;
    
    /// <summary>
    /// Returns true if player is currently exhausted (can't attack/throw).
    /// </summary>
    public bool IsExhausted => currentState == CombatState.Exhausted;
    
    /// <summary>
    /// Returns true if player's sword is thrown.
    /// </summary>
    public bool IsDisarmed => currentState == CombatState.Disarmed;
    
    /// <summary>
    /// Returns true if player has their sword.
    /// </summary>
    public bool HasSword => swordThrow == null || swordThrow.HasSword;
    
    /// <summary>
    /// Returns true if player can currently deal damage during dash.
    /// False when Exhausted or Disarmed.
    /// </summary>
    public bool CanDealDashDamage => currentState != CombatState.Exhausted && 
                                     currentState != CombatState.Disarmed;

    /// <summary>
    /// Returns true if player can throw their sword.
    /// False when Exhausted, Disarmed, or Attacking.
    /// </summary>
    public bool CanThrowSword => currentState == CombatState.Idle && HasSword;

    public float CurrentBlockHP => currentBlockHP;
    public float MaxBlockHP => maxBlockHP;
    public float BlockHPPercent => currentBlockHP / maxBlockHP;
    public float ExhaustionDuration => exhaustionDuration;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        swordThrow = GetComponent<PlayerSwordThrow>();
        dash = GetComponent<PlayerDash>();
        currentBlockHP = maxBlockHP;
    }

    private void Start()
    {
        // Subscribe to sword throw events
        if (swordThrow != null)
        {
            swordThrow.OnSwordThrown += HandleSwordThrown;
            swordThrow.OnSwordCaught += HandleSwordCaught;
        }
        
        // Subscribe to dash attack events
        if (dash != null)
        {
            dash.OnEnemyHitDuringDash += HandleDashAttackHit;
            dash.OnDashStarted += HandleDashStarted;
            dash.OnDashCompleted += HandleDashCompleted;
        }
    }

    private void OnDestroy()
    {
        if (swordThrow != null)
        {
            swordThrow.OnSwordThrown -= HandleSwordThrown;
            swordThrow.OnSwordCaught -= HandleSwordCaught;
        }
        
        if (dash != null)
        {
            dash.OnEnemyHitDuringDash -= HandleDashAttackHit;
            dash.OnDashStarted -= HandleDashStarted;
            dash.OnDashCompleted -= HandleDashCompleted;
        }
    }

    private void Update()
    {
        if (core.IsDead) return;

        HandleBlockRegeneration();
        HandleExhaustionRecovery();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Attack Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDashStarted()
    {
        // Set attacking state when dash begins (only if not exhausted)
        if (currentState == CombatState.Idle)
        {
            SetState(CombatState.Attacking);
        }
        // Note: If Exhausted, player can still dash but won't enter Attacking state
    }

    private void HandleDashCompleted(bool hitSurface, bool hitWall, bool isStickyLanding)
    {
        // Return to idle after dash completes (if was attacking)
        if (currentState == CombatState.Attacking)
        {
            SetState(CombatState.Idle);
        }
        // If Exhausted, stay Exhausted (will recover via timer)
    }

    private void HandleDashAttackHit(IEnemy enemy)
    {
        // Fire attack event for each hit (for sound/visual effects)
        OnAttack?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Automatic Block Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by PlayerCore when player takes damage.
    /// Automatically blocks if possible, returns remaining damage that wasn't blocked.
    /// </summary>
    public float TakeBlockDamage(float damage)
    {
        if (!CanAutoBlock) return damage;

        lastDamageTime = Time.time;

        float absorbed = Mathf.Min(damage, currentBlockHP);
        float overflow = damage - absorbed;

        currentBlockHP -= absorbed;
        OnBlockHPChanged?.Invoke(currentBlockHP, maxBlockHP);
        OnBlockedHit?.Invoke();
        

        if (currentBlockHP <= 0)
        {
            TriggerExhaustion();
            return overflow;
        }

        return 0f;
    }

    /// <summary>
    /// Triggers the Exhausted state. Called when BlockHP reaches 0.
    /// </summary>
    private void TriggerExhaustion()
    {
        currentBlockHP = 0;
        exhaustionEndTime = Time.time + exhaustionDuration;
        SetState(CombatState.Exhausted);
        OnExhausted?.Invoke();

        // Note: Unlike old Stunned state, dash remains ENABLED during Exhaustion
        // Player can still move and dash, just can't deal damage

        Debug.Log($"[PlayerCombat] Exhausted! Recovery in {exhaustionDuration}s");
    }

    /// <summary>
    /// Handles recovery from Exhausted state.
    /// </summary>
    private void HandleExhaustionRecovery()
    {
        if (currentState != CombatState.Exhausted) return;

        if (Time.time >= exhaustionEndTime)
        {
            // Instantly restore BlockHP to full
            currentBlockHP = maxBlockHP;
            OnBlockHPChanged?.Invoke(currentBlockHP, maxBlockHP);

            SetState(CombatState.Idle);
            OnExhaustionRecovered?.Invoke();

            Debug.Log("[PlayerCombat] Exhaustion recovered! BlockHP restored.");
        }
    }

    /// <summary>
    /// Handles gradual BlockHP regeneration when in Idle state.
    /// </summary>
    private void HandleBlockRegeneration()
    {
        // Only regenerate in Idle state
        if (currentState != CombatState.Idle) return;
        if (currentBlockHP >= maxBlockHP) return;
        if (Time.time < lastDamageTime + blockRegenDelay) return;

        currentBlockHP = Mathf.MoveTowards(currentBlockHP, maxBlockHP, blockRegenRate * Time.deltaTime);
        OnBlockHPChanged?.Invoke(currentBlockHP, maxBlockHP);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sword Throw Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleSwordThrown()
    {
        SetState(CombatState.Disarmed);
    }

    private void HandleSwordCaught()
    {
        if (currentState == CombatState.Disarmed)
        {
            SetState(CombatState.Idle);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    private void SetState(CombatState newState)
    {
        if (currentState == newState) return;

        currentState = newState;
        OnCombatStateChanged?.Invoke(newState);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reset combat state (e.g., on revive).
    /// </summary>
    public void ResetCombat()
    {
        currentBlockHP = maxBlockHP;
        OnBlockHPChanged?.Invoke(currentBlockHP, maxBlockHP);

        swordThrow?.ResetState();

        SetState(CombatState.Idle);
    }

    /// <summary>
    /// Force the player into Exhausted state.
    /// Called by DefenderShield when thrown sword is deflected.
    /// </summary>
    public void ForceExhaust()
    {
        // Can be called from any state except Dead
        if (core.IsDead) return;
        
        TriggerExhaustion();
    }

    /// <summary>
    /// Force the player into Exhausted state with custom duration.
    /// </summary>
    /// <param name="duration">Custom exhaustion duration</param>
    public void ForceExhaust(float duration)
    {
        if (core.IsDead) return;
        
        currentBlockHP = 0;
        exhaustionEndTime = Time.time + duration;
        SetState(CombatState.Exhausted);
        OnExhausted?.Invoke();

        Debug.Log($"[PlayerCombat] Force exhausted for {duration}s");
    }

    #endregion
}