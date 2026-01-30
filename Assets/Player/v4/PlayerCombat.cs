using UnityEngine;
using System;

/// <summary>
/// Handles combat state and automatic blocking.
/// 
/// UPDATED: Manual melee attack removed - attacks are now automatic during dash.
/// Block is passive - player automatically blocks incoming damage as long as BlockHP > 0.
/// When BlockHP is depleted, guard breaks and player is stunned.
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
        Attacking,      // Now triggered by dash, not manual input
        Stunned,        // Guard broken
        Disarmed        // Sword is thrown
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action<CombatState> OnCombatStateChanged;
    public event Action OnAttack;                           // Fired during dash attack
    public event Action OnBlockedHit;                       // Fired when a hit is blocked
    public event Action OnGuardBroken;
    public event Action OnGuardRestored;
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
    [SerializeField] private float guardBreakStunDuration = 2f;

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
    private float stunEndTime;

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
                                currentState != CombatState.Stunned && 
                                currentState != CombatState.Disarmed &&
                                HasSword;
    
    /// <summary>
    /// For PlayerCore compatibility - returns true if player would block the next hit.
    /// </summary>
    public bool IsBlocking => CanAutoBlock;
    
    public bool IsStunned => currentState == CombatState.Stunned;
    public bool IsDisarmed => currentState == CombatState.Disarmed;
    public bool HasSword => swordThrow == null || swordThrow.HasSword;

    public float CurrentBlockHP => currentBlockHP;
    public float MaxBlockHP => maxBlockHP;
    public float BlockHPPercent => currentBlockHP / maxBlockHP;

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
        HandleStunRecovery();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Attack Event Handlers (NEW)
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDashStarted()
    {
        // Set attacking state when dash begins
        if (currentState == CombatState.Idle)
        {
            SetState(CombatState.Attacking);
        }
    }

    private void HandleDashCompleted(bool hitSurface, bool hitWall)
    {
        // Return to idle after dash completes (if not disarmed/stunned)
        if (currentState == CombatState.Attacking)
        {
            SetState(CombatState.Idle);
        }
    }

    private void HandleDashAttackHit(IEnemy enemy)
    {
        // Fire attack event for each hit (for sound/visual effects)
        OnAttack?.Invoke();
        
        Debug.Log($"[PlayerCombat] Dash attack hit: {enemy.Transform.name}");
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

        Debug.Log($"[PlayerCombat] Auto-blocked {absorbed} damage. BlockHP: {currentBlockHP}/{maxBlockHP}");

        if (currentBlockHP <= 0)
        {
            BreakGuard();
            return overflow;
        }

        return 0f;
    }

    private void BreakGuard()
    {
        currentBlockHP = 0;
        stunEndTime = Time.time + guardBreakStunDuration;
        SetState(CombatState.Stunned);
        OnGuardBroken?.Invoke();

        // Disable dash during stun
        core.Dash?.SetDashEnabled(false);
        core.Dash?.ForceCancelDash();

        Debug.Log($"[PlayerCombat] Guard broken! Stunned for {guardBreakStunDuration}s");
    }

    private void HandleStunRecovery()
    {
        if (currentState != CombatState.Stunned) return;

        if (Time.time >= stunEndTime)
        {
            currentBlockHP = maxBlockHP;
            OnBlockHPChanged?.Invoke(currentBlockHP, maxBlockHP);

            core.Dash?.SetDashEnabled(true);
            SetState(CombatState.Idle);
            OnGuardRestored?.Invoke();

            Debug.Log("[PlayerCombat] Guard restored!");
        }
    }

    private void HandleBlockRegeneration()
    {
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
    /// Force guard break (e.g., from special enemy attack).
    /// </summary>
    public void ForceGuardBreak()
    {
        if (currentState == CombatState.Idle)
        {
            BreakGuard();
        }
    }

    #endregion
}