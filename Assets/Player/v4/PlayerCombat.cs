using UnityEngine;
using System;

/// <summary>
/// Unified combat system: Attack, Block, and Sword Throw.
/// Contains the SINGLE source of truth for Block HP (no duplication).
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
        Stunned,      // Guard broken
        SwordThrown   // Sword is not in hand
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
    public event Action OnSwordThrown;
    public event Action OnSwordRecalled;

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
    #region Inspector - Throw Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Sword Throw")]
    [SerializeField] private GameObject heldSwordVisual;
    [SerializeField] private GameObject thrownSwordPrefab;
    [SerializeField] private Transform throwOrigin;
    [SerializeField] private float throwForce = 40f;
    [SerializeField] private float maxThrowDistance = 100f;
    [SerializeField] private LayerMask throwableLayers = -1;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;

    // Combat state
    private CombatState currentState = CombatState.Idle;
    private float lastAttackTime;
    private float lastDamageTime;
    private float stunEndTime;

    // Block HP (THE single source of truth)
    private float currentBlockHP;

    // Thrown sword reference
    private GameObject currentThrownSword;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public CombatState CurrentState => currentState;
    public bool IsBlocking => currentState == CombatState.Blocking;
    public bool IsStunned => currentState == CombatState.Stunned;
    public bool HasSword => currentState != CombatState.SwordThrown;

    public float CurrentBlockHP => currentBlockHP;
    public float MaxBlockHP => maxBlockHP;
    public float BlockHPPercent => currentBlockHP / maxBlockHP;

    public bool CanAttack => currentState == CombatState.Idle && Time.time >= lastAttackTime + attackCooldown;
    public bool CanBlock => currentState == CombatState.Idle || currentState == CombatState.Blocking;
    public bool CanThrow => currentState == CombatState.Idle;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        currentBlockHP = maxBlockHP;
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

            case CombatState.SwordThrown:
                // Recall sword
                if (core.Input.GetActionDown("ThrowSword"))
                {
                    RecallSword();
                }
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

        // Priority: Block > Attack > Throw
        if (core.Input.GetAction("Block") && core.CanBlock)
        {
            SetState(CombatState.Blocking);
        }
        else if (core.Input.GetActionDown("Attack") && CanAttack)
        {
            PerformMeleeAttack();
        }
        else if (core.Input.GetActionDown("ThrowSword") && CanThrow)
        {
            ThrowSword();
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
                // Deal damage via interface
                if (col.TryGetComponent<IDamageable>(out var target))
                {
                    target.TakeDamage(meleeDamage);
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
        // Only regen when not blocking, not stunned, and after delay
        if (currentState == CombatState.Blocking) return;
        if (currentState == CombatState.Stunned) return;
        if (currentBlockHP >= maxBlockHP) return;
        if (Time.time < lastDamageTime + blockRegenDelay) return;

        currentBlockHP = Mathf.MoveTowards(currentBlockHP, maxBlockHP, blockRegenRate * Time.deltaTime);
        OnBlockHPChanged?.Invoke(currentBlockHP, maxBlockHP);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Throw Logic
    // ════════════════════════════════════════════════════════════════════════

    private void ThrowSword()
    {
        SetState(CombatState.SwordThrown);
        OnSwordThrown?.Invoke();

        // Hide held sword
        if (heldSwordVisual != null)
            heldSwordVisual.SetActive(false);

        // Spawn thrown sword
        Vector3 spawnPos = throwOrigin != null ? throwOrigin.position : core.CameraTransform.position;
        Quaternion spawnRot = core.CameraTransform.rotation;

        currentThrownSword = Instantiate(thrownSwordPrefab, spawnPos, spawnRot);

        // Initialize thrown sword (if it has the right component)
        if (currentThrownSword.TryGetComponent<IThrownSword>(out var thrown))
        {
            thrown.Initialize(core.CameraTransform.forward, throwForce, maxThrowDistance, throwableLayers);
            thrown.OnRecalled += HandleSwordReturned;
        }
        else
        {
            // Fallback: just apply velocity
            if (currentThrownSword.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.linearVelocity = core.CameraTransform.forward * throwForce;
            }
        }
    }

    private void RecallSword()
    {
        if (currentThrownSword == null)
        {
            // Sword got destroyed somehow, just restore
            HandleSwordReturned();
            return;
        }

        if (currentThrownSword.TryGetComponent<IThrownSword>(out var thrown))
        {
            thrown.Recall(throwOrigin != null ? throwOrigin : transform);
        }
        else
        {
            // Fallback: just destroy and restore
            Destroy(currentThrownSword);
            HandleSwordReturned();
        }
    }

    private void HandleSwordReturned()
    {
        if (currentThrownSword != null)
        {
            if (currentThrownSword.TryGetComponent<IThrownSword>(out var thrown))
            {
                thrown.OnRecalled -= HandleSwordReturned;
            }
            Destroy(currentThrownSword);
            currentThrownSword = null;
        }

        // Show held sword
        if (heldSwordVisual != null)
            heldSwordVisual.SetActive(true);

        SetState(CombatState.Idle);
        OnSwordRecalled?.Invoke();
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

        if (currentThrownSword != null)
        {
            Destroy(currentThrownSword);
            currentThrownSword = null;
        }

        if (heldSwordVisual != null)
            heldSwordVisual.SetActive(true);

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

// NOTE: Interfaces (IThrownSword, IDamageable, INpcInteraction) are now defined in GameInterfaces.cs
// to avoid duplicate definitions across the project.