using UnityEngine;

/// <summary>
/// Drives the first-person arm Animator based on player state and combat events.
/// Sits on the Player GameObject, reads state from PlayerCore subsystems.
/// 
/// Subscribes to events for one-shot animations (Attack, Block, SwordThrow, SwordRecover)
/// and polls state each frame for continuous parameters (MoveSpeed, IsDashing, etc.).
/// 
/// ─────────────────────────────────────────────────────────────────────
/// REQUIRED ANIMATOR PARAMETERS (set these up in the Arm AnimatorController):
/// 
///   Float   MoveSpeed       — 0 = idle, > 0 = walking
///   Bool    IsDashing       — true during attack dash
///   Bool    IsSwordDashing  — true during sword dash
///   Bool    IsStuck         — true while stuck to wall
///   Bool    IsDead          — true when dead
///   Bool    IsExhausted     — true when block HP depleted
///   Bool    HasSword        — false while sword is thrown away
///   Trigger Attack          — fires per enemy hit during dash
///   Int     AttackVariant   — 0-3, randomized before each Attack trigger
///   Trigger Block           — fires per blocked hit
///   Int     BlockVariant    — 0-3, randomized before each Block trigger
///   Trigger SwordThrow      — fires when sword is thrown
///   Trigger SwordRecover    — fires when sword returns to player's hand
/// ─────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerArmAnimator : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animator Reference")]
    [Tooltip("Animator on the arm model. Auto-found in children if left empty.")]
    [SerializeField] private Animator armAnimator;

    [Header("Movement")]
    [Tooltip("How quickly MoveSpeed blends to its target value.")]
    [SerializeField] private float moveSpeedSmoothTime = 0.1f;

    [Header("Variants")]
    [Tooltip("Number of attack animation variants (0-indexed).")]
    [SerializeField] private int attackVariantCount = 4;

    [Tooltip("Number of block animation variants (0-indexed).")]
    [SerializeField] private int blockVariantCount = 4;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Animator Parameter Hashes
    // ════════════════════════════════════════════════════════════════════════

    private static readonly int HashMoveSpeed      = Animator.StringToHash("MoveSpeed");
    private static readonly int HashIsDashing       = Animator.StringToHash("IsDashing");
    private static readonly int HashIsSwordDashing  = Animator.StringToHash("IsSwordDashing");
    private static readonly int HashIsStuck         = Animator.StringToHash("IsStuck");
    private static readonly int HashIsDead          = Animator.StringToHash("IsDead");
    private static readonly int HashIsExhausted     = Animator.StringToHash("IsExhausted");
    private static readonly int HashHasSword        = Animator.StringToHash("HasSword");
    private static readonly int HashAttack          = Animator.StringToHash("Attack");
    private static readonly int HashAttackVariant   = Animator.StringToHash("AttackVariant");
    private static readonly int HashBlock           = Animator.StringToHash("Block");
    private static readonly int HashBlockVariant    = Animator.StringToHash("BlockVariant");
    private static readonly int HashSwordThrow      = Animator.StringToHash("SwordThrow");
    private static readonly int HashSwordRecover    = Animator.StringToHash("SwordRecover");

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private float moveSpeedVelocity; // used by SmoothDamp

    // Track last variant to avoid repeats
    private int lastAttackVariant = -1;
    private int lastBlockVariant = -1;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();

        if (armAnimator == null)
            armAnimator = GetComponentInChildren<Animator>();

        if (armAnimator == null)
        {
            Debug.LogError("[PlayerArmAnimator] No Animator found! Assign the arm Animator in the Inspector.");
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        SubscribeToEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void Update()
    {
        if (armAnimator == null) return;

        UpdateContinuousParameters();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Subscription
    // ════════════════════════════════════════════════════════════════════════

    private void SubscribeToEvents()
    {
        if (core.Combat != null)
        {
            core.Combat.OnAttack += HandleAttack;
            core.Combat.OnBlockedHit += HandleBlockedHit;
        }

        if (core.SwordThrow != null)
        {
            core.SwordThrow.OnSwordThrown += HandleSwordThrown;
            core.SwordThrow.OnSwordCaught += HandleSwordCaught;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (core == null) return;

        if (core.Combat != null)
        {
            core.Combat.OnAttack -= HandleAttack;
            core.Combat.OnBlockedHit -= HandleBlockedHit;
        }

        if (core.SwordThrow != null)
        {
            core.SwordThrow.OnSwordThrown -= HandleSwordThrown;
            core.SwordThrow.OnSwordCaught -= HandleSwordCaught;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Continuous Parameters (polled each frame)
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateContinuousParameters()
    {
        // MoveSpeed — smooth blend based on input magnitude
        float targetSpeed = core.Input.GetMoveInput().magnitude;
        float smoothedSpeed = Mathf.SmoothDamp(
            armAnimator.GetFloat(HashMoveSpeed),
            targetSpeed,
            ref moveSpeedVelocity,
            moveSpeedSmoothTime
        );
        armAnimator.SetFloat(HashMoveSpeed, smoothedSpeed);

        // State bools
        PlayerCore.PlayerState state = core.CurrentState;
        armAnimator.SetBool(HashIsDashing,      state == PlayerCore.PlayerState.Dashing);
        armAnimator.SetBool(HashIsSwordDashing,  state == PlayerCore.PlayerState.DashingToSword);
        armAnimator.SetBool(HashIsStuck,         state == PlayerCore.PlayerState.StuckToSurface);
        armAnimator.SetBool(HashIsDead,          state == PlayerCore.PlayerState.Dead);

        // Exhausted comes from combat, not player state
        bool isExhausted = core.Combat != null && core.Combat.IsExhausted;
        armAnimator.SetBool(HashIsExhausted, isExhausted);

        // HasSword — true when sword is in player's hand
        bool hasSword = core.SwordThrow == null || core.SwordThrow.HasSword;
        armAnimator.SetBool(HashHasSword, hasSword);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers (one-shot triggers)
    // ════════════════════════════════════════════════════════════════════════

    private void HandleAttack()
    {
        if (armAnimator == null) return;

        int variant = GetNonRepeatingVariant(attackVariantCount, ref lastAttackVariant);
        armAnimator.SetInteger(HashAttackVariant, variant);
        armAnimator.SetTrigger(HashAttack);
    }

    private void HandleBlockedHit()
    {
        if (armAnimator == null) return;

        int variant = GetNonRepeatingVariant(blockVariantCount, ref lastBlockVariant);
        armAnimator.SetInteger(HashBlockVariant, variant);
        armAnimator.SetTrigger(HashBlock);
    }

    private void HandleSwordThrown()
    {
        if (armAnimator == null) return;

        armAnimator.SetTrigger(HashSwordThrow);
    }

    private void HandleSwordCaught()
    {
        if (armAnimator == null) return;

        armAnimator.SetTrigger(HashSwordRecover);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a random variant index that is different from the last one used.
    /// </summary>
    private int GetNonRepeatingVariant(int variantCount, ref int lastVariant)
    {
        if (variantCount <= 1) return 0;

        int variant;
        do
        {
            variant = Random.Range(0, variantCount);
        }
        while (variant == lastVariant);

        lastVariant = variant;
        return variant;
    }

    #endregion
}
