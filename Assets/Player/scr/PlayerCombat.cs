using UnityEngine;
using System;

/// <summary>
/// Handles player combat state.
/// Manual melee attacks are removed; attacks are automatic during dash.
/// Block HP has been removed. HP is now the only defensive resource.
/// Exhausted still exists, but is only triggered by special cases via ForceExhaust().
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
        Exhausted,      // Special-case state: can move/dash, but can't attack/throw
        Disarmed        // Sword is thrown
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action<CombatState> OnCombatStateChanged;
    public event Action OnAttack;                           // Fired during dash attack
    public event Action OnExhausted;                        // Fired when entering Exhausted state
    public event Action OnExhaustionRecovered;              // Fired when Exhausted ends

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Melee Attack (Auto during Dash)")]
    [Tooltip("These values are now in PlayerDash - kept here for reference.")]
    [SerializeField] private int meleeDamageReference = 50;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Exhaustion Settings")]
    [Tooltip("Duration of special-case exhaustion in seconds.")]
    [SerializeField] private float exhaustionDuration = 1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private PlayerSwordThrow swordThrow;
    private PlayerDash dash;

    private CombatState currentState = CombatState.Idle;
    private float exhaustionEndTime;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public CombatState CurrentState => currentState;

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

    public float ExhaustionDuration => exhaustionDuration;

    /// <summary>
    /// Remaining time in Exhausted state, in seconds.
    /// Returns 0 when the player is not exhausted.
    /// Used by PlayerArmAnimator to time the exit animation.
    /// </summary>
    public float RemainingExhaustionTime =>
        currentState == CombatState.Exhausted
            ? Mathf.Max(0f, exhaustionEndTime - Time.time)
            : 0f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        swordThrow = GetComponent<PlayerSwordThrow>();
        dash = GetComponent<PlayerDash>();
    }

    private void Start()
    {
        if (swordThrow != null)
        {
            swordThrow.OnSwordThrown += HandleSwordThrown;
            swordThrow.OnSwordCaught += HandleSwordCaught;
        }

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

        HandleExhaustionRecovery();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Attack Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDashStarted()
    {
        // Set attacking state when dash begins (only if not exhausted/disarmed).
        if (currentState == CombatState.Idle)
        {
            SetState(CombatState.Attacking);
        }
    }

    private void HandleDashCompleted(bool hitSurface, bool hitWall, bool isStickyLanding)
    {
        // Return to idle after dash completes (if was attacking).
        if (currentState == CombatState.Attacking)
        {
            SetState(CombatState.Idle);
        }

        // If Exhausted, stay Exhausted until the timer recovers.
        // If Disarmed, stay Disarmed until the sword returns.
    }

    private void HandleDashAttackHit(IEnemy enemy)
    {
        // Fire attack event for each hit (for sound/visual effects).
        OnAttack?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Exhaustion Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Triggers the Exhausted state. This is only called by special cases.
    /// HP reaching 0 is handled by PlayerHealth and causes death instead.
    /// </summary>
    private void TriggerExhaustion(float duration)
    {
        if (core.IsDead) return;

        exhaustionEndTime = Time.time + Mathf.Max(0f, duration);
        SetState(CombatState.Exhausted);
        OnExhausted?.Invoke();

        // Player can still move and dash, but can't deal damage or throw.
        Debug.Log($"[PlayerCombat] Exhausted! Recovery in {duration}s");
    }

    /// <summary>
    /// Handles recovery from Exhausted state.
    /// </summary>
    private void HandleExhaustionRecovery()
    {
        if (currentState != CombatState.Exhausted) return;

        if (Time.time >= exhaustionEndTime)
        {
            SetState(HasSword ? CombatState.Idle : CombatState.Disarmed);
            OnExhaustionRecovered?.Invoke();

            Debug.Log("[PlayerCombat] Exhaustion recovered.");
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sword Throw Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleSwordThrown()
    {
        // Exhausted has priority over Disarmed for animation/gameplay lockout.
        if (currentState == CombatState.Exhausted) return;

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
        swordThrow?.ResetState();
        SetState(CombatState.Idle);
    }

    /// <summary>
    /// Force the player into Exhausted state.
    /// Called by special cases, e.g. DefenderShield when a thrown sword is deflected.
    /// </summary>
    public void ForceExhaust()
    {
        ForceExhaust(exhaustionDuration);
    }

    /// <summary>
    /// Force the player into Exhausted state with custom duration.
    /// </summary>
    public void ForceExhaust(float duration)
    {
        TriggerExhaustion(duration);
    }

    #endregion
}
