using Animancer;
using UnityEngine;

/// <summary>
/// Drives the first-person arm animations using Animancer.
/// Replaces the old Animator-based system with direct clip playback.
/// Sits on the Player GameObject, reads state from PlayerCore subsystems.
/// 
/// Subscribes to events for one-shot animations (Attack, Block, SwordThrow, SwordRecover)
/// and polls state each frame for continuous clips (Idle, Walk, Dash, etc.).
/// 
/// One-shots REPLACE the base animation for their duration, then return to
/// the appropriate base clip via OnEnd callback.
/// 
/// ─────────────────────────────────────────────────────────────────────
/// TIMING:
///   Arm animations run in REAL TIME (not affected by DashSlowMo).
///   But they DO pause during Pause and HitStop.
///   This is achieved by:
///     1. Setting the Animator to UnscaledTime (ignores Time.timeScale)
///     2. Manually pausing/unpausing the AnimancerGraph when
///        TimeManager.IsGameTimeFrozen changes (Pause + HitStop)
/// ─────────────────────────────────────────────────────────────────────
/// 
/// SETUP:
///   1. Add AnimancerComponent to the arm model (replaces AnimatorController).
///      The Animator component stays, but needs NO Controller assigned.
///   2. Assign all ClipTransitions in the Inspector (drag AnimationClips in).
///   3. Adjust FadeDuration on each ClipTransition for smooth blending.
///   4. The Animator's UpdateMode will be set to UnscaledTime automatically.
/// ─────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerArmAnimator : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent on the arm model. Auto-found in children if left empty.")]
    [SerializeField] private AnimancerComponent animancer;

    // ── Base Clips (looping, polled each frame) ──────────────────────────

    [Header("Base Clips (Looping)")]
    [SerializeField] private ClipTransition idle;
    [SerializeField] private ClipTransition walk;
    [SerializeField] private ClipTransition dash;
    [SerializeField] private ClipTransition swordDash;
    [SerializeField] private ClipTransition stuck;
    [SerializeField] private ClipTransition dead;
    [SerializeField] private ClipTransition exhausted;

    [Tooltip("Idle animation when sword is thrown (no sword in hand).")]
    [SerializeField] private ClipTransition disarmedIdle;

    // ── One-Shot Clips (event-triggered, replace base for their duration) ──

    [Header("Attack Variants (One-Shot)")]
    [Tooltip("Multiple attack clips for variety. Randomly picked, won't repeat.")]
    [SerializeField] private ClipTransition[] attackVariants;

    [Header("Block Variants (One-Shot)")]
    [Tooltip("Multiple block reaction clips for variety. Randomly picked, won't repeat.")]
    [SerializeField] private ClipTransition[] blockVariants;

    [Header("Sword Actions (One-Shot)")]
    [SerializeField] private ClipTransition swordThrow;
    [SerializeField] private ClipTransition swordRecover;

    // ── Movement Threshold ───────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Input magnitude above this switches from Idle to Walk.")]
    [SerializeField] private float walkThreshold = 0.1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;

    // Track last variant to avoid repeats
    private int lastAttackVariant = -1;
    private int lastBlockVariant = -1;

    // True while a one-shot is playing (prevents base clip override)
    private bool isPlayingOneShot;

    // Tracks whether the graph was paused by us (to avoid conflicts)
    private bool wasFrozenLastFrame;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();

        if (animancer == null)
            animancer = GetComponentInChildren<AnimancerComponent>();

        if (animancer == null)
        {
            Debug.LogError("[PlayerArmAnimator] No AnimancerComponent found! " +
                           "Add one to the arm model and assign it in the Inspector.");
            enabled = false;
            return;
        }

        // ── WICHTIG: Animator auf UnscaledTime setzen ──
        // Damit ignorieren die Arm-Animationen Time.timeScale (= DashSlowMo).
        // Pause und HitStop werden stattdessen manuell über PauseGraph/UnpauseGraph gesteuert.
        Animator animator = animancer.Animator;
        if (animator != null)
        {
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
    }

    private void Start()
    {
        SubscribeToEvents();

        // Play idle as starting animation
        if (idle.Clip != null)
            animancer.Play(idle);
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void Update()
    {
        if (animancer == null) return;

        // ── Pause/HitStop Handling ──
        // Prüfe jeden Frame ob TimeManager das Spiel einfriert.
        // IsGameTimeFrozen ist true bei Pause und HitStop, aber NICHT bei DashSlowMo.
        UpdateGraphFreezeState();

        // Wenn eingefroren, keine Animation-Updates
        if (wasFrozenLastFrame) return;

        // One-shots override base clips — don't interrupt them
        if (isPlayingOneShot) return;

        UpdateBaseAnimation();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Time Freeze (Pause / HitStop)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pausiert oder startet den Animancer-Graph basierend auf TimeManager.IsGameTimeFrozen.
    /// 
    /// Da der Animator auf UnscaledTime läuft, ignoriert er Time.timeScale.
    /// Das ist gewollt für DashSlowMo (Arme sollen in Echtzeit animieren).
    /// Aber bei Pause und HitStop SOLLEN die Animationen stoppen.
    /// 
    /// Deshalb: IsGameTimeFrozen = true  → Graph pausieren
    ///          IsGameTimeFrozen = false → Graph fortsetzen
    /// </summary>
    private void UpdateGraphFreezeState()
    {
        bool isFrozen = TimeManager.Instance.IsGameTimeFrozen;

        if (isFrozen && !wasFrozenLastFrame)
        {
            // Gerade eingefroren → Graph pausieren
            animancer.Graph.PauseGraph();
        }
        else if (!isFrozen && wasFrozenLastFrame)
        {
            // Gerade aufgetaut → Graph fortsetzen
            animancer.Graph.UnpauseGraph();
        }

        wasFrozenLastFrame = isFrozen;
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
    #region Base Animation (polled each frame)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines which base clip should play based on the current player state.
    /// Called every frame unless a one-shot is active or the game is frozen.
    /// </summary>
    private void UpdateBaseAnimation()
    {
        ClipTransition target = GetTargetBaseClip();

        if (target == null || target.Clip == null) return;

        // TryPlay only starts the clip if it isn't already playing.
        // This avoids restarting looping clips every frame.
        animancer.TryPlay(target);
    }

    /// <summary>
    /// Maps the current PlayerState + CombatState to the correct base clip.
    /// Priority: Dead > State-specific > Combat-specific > Movement
    /// </summary>
    private ClipTransition GetTargetBaseClip()
    {
        // ── Highest priority: Death ──
        if (core.IsDead)
            return dead;

        // ── State-specific overrides ──
        switch (core.CurrentState)
        {
            case PlayerCore.PlayerState.Dashing:
                return dash;

            case PlayerCore.PlayerState.DashingToSword:
                return swordDash;

            case PlayerCore.PlayerState.StuckToSurface:
                return stuck;
        }

        // ── Combat-state overrides ──
        if (core.Combat != null)
        {
            if (core.Combat.IsExhausted)
                return exhausted;

            if (core.Combat.IsDisarmed && disarmedIdle.Clip != null)
                return disarmedIdle;
        }

        // ── Normal movement: Idle vs Walk ──
        float inputMagnitude = core.Input.GetMoveInput().magnitude;
        return inputMagnitude > walkThreshold ? walk : idle;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers (one-shot clips)
    // ════════════════════════════════════════════════════════════════════════

    private void HandleAttack()
    {
        PlayOneShotVariantInstant(attackVariants, ref lastAttackVariant);
    }

    private void HandleBlockedHit()
    {
        PlayOneShotVariantInstant(blockVariants, ref lastBlockVariant);
    }

    private void HandleSwordThrown()
    {
        PlayOneShot(swordThrow);
    }

    private void HandleSwordCaught()
    {
        PlayOneShot(swordRecover);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region One-Shot Playback
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Plays a one-shot clip that replaces the current base animation.
    /// When the clip ends, isPlayingOneShot is cleared and
    /// UpdateBaseAnimation resumes control.
    /// Uses the clip's own FadeDuration for a smooth transition.
    /// </summary>
    private void PlayOneShot(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        isPlayingOneShot = true;

        AnimancerState state = animancer.Play(clip);

        // When the one-shot finishes, return to base animation
        state.Events(this).OnEnd = OnOneShotEnded;
    }

    /// <summary>
    /// Plays a one-shot clip INSTANTLY — no fade, no blending.
    /// The previous animation is immediately replaced on the same frame.
    /// Used for Attack and Block where snappy feedback matters.
    /// </summary>
    private void PlayOneShotInstant(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        isPlayingOneShot = true;

        // fadeDuration = 0 → harter Schnitt, keine Überblendung
        AnimancerState state = animancer.Play(clip, 0f);

        state.Events(this).OnEnd = OnOneShotEnded;
    }

    /// <summary>
    /// Picks a random variant (avoiding repeats) and plays it as a one-shot
    /// with the clip's own FadeDuration.
    /// </summary>
    private void PlayOneShotVariant(ClipTransition[] variants, ref int lastVariant)
    {
        if (variants == null || variants.Length == 0) return;

        int index = GetNonRepeatingIndex(variants.Length, ref lastVariant);
        PlayOneShot(variants[index]);
    }

    /// <summary>
    /// Picks a random variant (avoiding repeats) and plays it INSTANTLY.
    /// Used for Attack and Block variants.
    /// </summary>
    private void PlayOneShotVariantInstant(ClipTransition[] variants, ref int lastVariant)
    {
        if (variants == null || variants.Length == 0) return;

        int index = GetNonRepeatingIndex(variants.Length, ref lastVariant);
        PlayOneShotInstant(variants[index]);
    }

    /// <summary>
    /// Callback when any one-shot finishes. Clears the flag so
    /// UpdateBaseAnimation takes over again next frame.
    /// </summary>
    private void OnOneShotEnded()
    {
        isPlayingOneShot = false;

        // Immediately transition to the correct base clip
        // so there's no single-frame gap
        UpdateBaseAnimation();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a random index that is different from the last one used.
    /// </summary>
    private int GetNonRepeatingIndex(int count, ref int lastIndex)
    {
        if (count <= 1) return 0;

        int index;
        do
        {
            index = Random.Range(0, count);
        }
        while (index == lastIndex);

        lastIndex = index;
        return index;
    }

    #endregion
}
