using UnityEngine;
using System;

/// <summary>
/// Handles melee combat and automatic blocking.
/// Block is passive - player automatically blocks incoming damage as long as BlockHP > 0.
/// When BlockHP is depleted, guard breaks and player is stunned.
/// 
/// NEW: When disarmed (sword is thrown), pressing Attack will dash to the sword instead.
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
        Attacking,
        Stunned,        // Guard broken
        Disarmed        // Sword is thrown (can't attack, but can sword-dash)
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action<CombatState> OnCombatStateChanged;
    public event Action OnAttack;
    public event Action OnBlockedHit;           // Fired when a hit is automatically blocked
    public event Action OnGuardBroken;
    public event Action OnGuardRestored;
    public event Action<float, float> OnBlockHPChanged;  // (current, max)
    
    // NEW: Sword dash event
    public event Action OnSwordDashInitiated;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector - Attack Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Melee Attack")]
    [SerializeField] private float attackRange = 3f;
    [SerializeField] private float attackAngle = 30f;
    [SerializeField] private int meleeDamage = 50;
    [SerializeField] private float attackCooldown = 0.5f;
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
    private float lastAttackTime;
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
    /// Returns true if the player can currently auto-block (has BlockHP and isn't stunned/disarmed).
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

    public bool CanAttack => currentState == CombatState.Idle && 
                             Time.time >= lastAttackTime + attackCooldown;

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
    }

    private void OnDestroy()
    {
        if (swordThrow != null)
        {
            swordThrow.OnSwordThrown -= HandleSwordThrown;
            swordThrow.OnSwordCaught -= HandleSwordCaught;
        }
    }

    private void Update()
    {
        if (core.IsDead) return;

        HandleCombatInput();
        HandleBlockRegeneration();
        HandleStunRecovery();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Input Handling
    // ════════════════════════════════════════════════════════════════════════

    private void HandleCombatInput()
    {
        switch (currentState)
        {
            case CombatState.Idle:
                HandleIdleInput();
                break;

            case CombatState.Disarmed:
                HandleDisarmedInput();
                break;

            case CombatState.Stunned:
            case CombatState.Attacking:
                // Cannot act
                break;
        }
    }

    private void HandleIdleInput()
    {
        if (!core.CanAttack) return;

        // Only attack input needed - blocking is automatic
        if (core.Input.GetActionDown("Attack") && CanAttack && HasSword)
        {
            PerformMeleeAttack();
        }
    }
    
    /// <summary>
    /// NEW: When disarmed, Attack button triggers sword dash instead of melee.
    /// </summary>
    private void HandleDisarmedInput()
    {
        // Check if sword is stuck somewhere (can dash to it)
        if (core.Input.GetActionDown("Attack"))
        {
            TrySwordDash();
        }
    }
    
    /// <summary>
    /// Attempts to initiate a sword dash.
    /// </summary>
    private void TrySwordDash()
    {
        // Must have a stuck sword to dash to
        if (swordThrow == null || !swordThrow.IsSwordStuck)
        {
            Debug.Log("[PlayerCombat] Cannot sword dash - sword is not stuck");
            return;
        }
        
        // Ask dash system to perform the sword dash
        if (dash != null && dash.TryStartSwordDash())
        {
            OnSwordDashInitiated?.Invoke();
            Debug.Log("[PlayerCombat] Sword dash initiated!");
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Attack Logic
    // ════════════════════════════════════════════════════════════════════════

    private void PerformMeleeAttack()
    {
        lastAttackTime = Time.time;
        SetState(CombatState.Attacking);
        OnAttack?.Invoke();

        // Find enemies in range
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, enemyLayer);

        foreach (var col in hits)
        {
            Vector3 toEnemy = (col.transform.position - core.CameraTransform.position).normalized;
            float angle = Vector3.Angle(core.CameraTransform.forward, toEnemy);

            if (angle <= attackAngle)
            {
                Debug.Log("meele: "+col.transform.name);
                // Calculate hit point (closest point on collider)
                Vector3 hitPoint = col.ClosestPoint(core.CameraTransform.position);
                Vector3 hitDirection = core.CameraTransform.forward;

                // Try IEnemy first (new unified interface)
                if (col.TryGetComponent<IEnemy>(out var enemy))
                {
                    enemy.OnMeleeDamage(meleeDamage);
                }
                // Fallback to IDamageable for non-enemy destructibles
                else if (col.TryGetComponent<IDamageable>(out var damageable))
                {
                    damageable.TakeDamage(meleeDamage, hitPoint, hitDirection);
                }
            }
        }

        // Return to idle immediately (instant attack)
        SetState(CombatState.Idle);
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
        // Can't block if no BlockHP, stunned, or disarmed
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
        // Only regen when idle and after delay
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

        // Also reset sword throw if present
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