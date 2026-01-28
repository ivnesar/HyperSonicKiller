using UnityEngine;
using System;

/// <summary>
/// Handles melee combat and blocking.
/// Sword throw is handled separately by PlayerSwordThrow.
/// Block HP acts as a shield - when broken, player is stunned briefly.
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
        Blocking,
        Stunned,        // Guard broken
        Disarmed        // Sword is thrown (can't attack/block)
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action<CombatState> OnCombatStateChanged;
    public event Action OnAttack;
    public event Action OnBlockStart;
    public event Action OnBlockEnd;
    public event Action OnGuardBroken;
    public event Action OnGuardRestored;
    public event Action<float, float> OnBlockHPChanged;  // (current, max)

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

    [Header("Block / Shield")]
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
    public bool IsBlocking => currentState == CombatState.Blocking;
    public bool IsStunned => currentState == CombatState.Stunned;
    public bool IsDisarmed => currentState == CombatState.Disarmed;
    public bool HasSword => swordThrow == null || swordThrow.HasSword;

    public float CurrentBlockHP => currentBlockHP;
    public float MaxBlockHP => maxBlockHP;
    public float BlockHPPercent => currentBlockHP / maxBlockHP;

    public bool CanAttack => currentState == CombatState.Idle && 
                             Time.time >= lastAttackTime + attackCooldown;
    public bool CanBlock => currentState == CombatState.Idle || 
                            currentState == CombatState.Blocking;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        swordThrow = GetComponent<PlayerSwordThrow>();
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

            case CombatState.Blocking:
                // Stop blocking when button released
                if (!core.Input.GetAction("Block"))
                {
                    SetState(CombatState.Idle);
                }
                break;

            case CombatState.Disarmed:
                // Can't do anything, waiting for sword to return
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

        // Priority: Block > Attack
        if (core.Input.GetAction("Block") && core.CanBlock && HasSword)
        {
            SetState(CombatState.Blocking);
        }
        else if (core.Input.GetActionDown("Attack") && CanAttack && HasSword)
        {
            PerformMeleeAttack();
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
                // Calculate hit point (closest point on collider)
                Vector3 hitPoint = col.ClosestPoint(core.CameraTransform.position);
                Vector3 hitDirection = core.CameraTransform.forward;

                // Try IDamageable first
                if (col.TryGetComponent<IDamageable>(out var damageable))
                {
                    damageable.TakeDamage(meleeDamage, hitPoint, hitDirection);
                }
                // Legacy support
                else if (col.TryGetComponent<INpcInteraction>(out var npc))
                {
                    npc.OnMeeleDamage(meleeDamage);
                }
            }
        }

        // Return to idle immediately (instant attack)
        SetState(CombatState.Idle);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Block Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by PlayerCore when player takes damage while blocking.
    /// Returns remaining damage that wasn't blocked.
    /// </summary>
    public float TakeBlockDamage(float damage)
    {
        if (!IsBlocking) return damage;

        lastDamageTime = Time.time;

        float absorbed = Mathf.Min(damage, currentBlockHP);
        float overflow = damage - absorbed;

        currentBlockHP -= absorbed;
        OnBlockHPChanged?.Invoke(currentBlockHP, maxBlockHP);

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
        // Can't be blocking when sword is thrown
        if (currentState == CombatState.Blocking)
        {
            SetState(CombatState.Idle);
        }

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

        CombatState oldState = currentState;
        currentState = newState;

        // State transition events
        if (oldState == CombatState.Blocking)
            OnBlockEnd?.Invoke();

        if (newState == CombatState.Blocking)
            OnBlockStart?.Invoke();

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
        if (IsBlocking || currentState == CombatState.Idle)
        {
            BreakGuard();
        }
    }

    #endregion
}