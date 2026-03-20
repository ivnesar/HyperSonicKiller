using Animancer;
using UnityEngine;

/// <summary>
/// Manages all Defender NPC animations via Animancer.
/// Follows the same pattern as SoldierAnimationManager.
/// 
/// The Defender has a simple animation set:
///   - Idle (looping) — standing with shield
///   - Walk (looping) — approaching with shield
///   - Stunned (looping) — stunned state
///   - Hit (one-shot) — flinch reaction
///   - Die (one-shot) — death (only when ragdoll is disabled)
///
/// SETUP:
///   1. Add AnimancerComponent to the Defender model (child with the Animator).
///   2. Remove the AnimatorController from the Animator (leave the Animator component).
///   3. Assign all ClipTransitions in the Inspector.
/// </summary>
public class DefenderAnimationManager : MonoBehaviour, INpcAnimationHandler
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent on the defender model. Auto-found in children if left empty.")]
    [SerializeField] private AnimancerComponent animancer;

    // ── Base Clips (looping) ─────────────────────────────────────────────

    [Header("Base Clips (Looping)")]
    [SerializeField] private ClipTransition idle;
    [SerializeField] private ClipTransition walk;
    [SerializeField] private ClipTransition stunned;

    // ── One-Shot Clips ───────────────────────────────────────────────────

    [Header("One-Shot Clips")]
    [SerializeField] private ClipTransition hit;
    [SerializeField] private ClipTransition die;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The clip that should be playing when no one-shot is active.
    /// One-shots return to this clip when they finish.
    /// </summary>
    private ClipTransition currentBaseClip;

    private bool isPlayingOneShot;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (animancer == null)
            animancer = GetComponentInChildren<AnimancerComponent>();

        if (animancer == null)
        {
            Debug.LogError($"[DefenderAnimationManager] No AnimancerComponent found on {gameObject.name}!");
            enabled = false;
        }
    }

    private void Start()
    {
        SetBaseClip(idle);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region INpcAnimationHandler (called by NpcBase)
    // ════════════════════════════════════════════════════════════════════════

    public void PlayHitReaction()
    {
        PlayOneShotInstant(hit);
    }

    public void PlayStunStart()
    {
        SetBaseClip(stunned);
    }

    public void PlayStunEnd()
    {
        SetBaseClip(idle);
    }

    public void PlayDeath()
    {
        if (die == null || die.Clip == null) return;

        isPlayingOneShot = false;
        animancer.Play(die, 0f);
    }

    public void UpdateMovement(float normalizedSpeed)
    {
        // Movement is handled by states calling PlayIdle/PlayWalk directly.
        // This exists for NpcBase compatibility.
    }

    public void DisableForRagdoll()
    {
        if (animancer != null)
        {
            animancer.Stop();
            if (animancer.Animator != null)
                animancer.Animator.enabled = false;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Defender-Specific Methods (called by DefenderStates)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Set base to idle loop. Called by Idle/InPosition.Enter().</summary>
    public void PlayIdle()
    {
        SetBaseClip(idle);
    }

    /// <summary>Set base to walk loop. Called by Approaching.Enter().</summary>
    public void PlayWalk()
    {
        SetBaseClip(walk);
    }

    /// <summary>
    /// Play stunned loop and clear any active one-shots.
    /// Called by Stunned.Enter().
    /// </summary>
    public void PlayStunnedFromCombat()
    {
        isPlayingOneShot = false;
        SetBaseClip(stunned);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Playback
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets the looping base clip. If no one-shot is active, plays it immediately.
    /// </summary>
    private void SetBaseClip(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        currentBaseClip = clip;

        if (!isPlayingOneShot)
        {
            animancer.Play(clip);
        }
    }

    /// <summary>
    /// Plays a one-shot instantly (fadeDuration = 0). For combat reactions.
    /// </summary>
    private void PlayOneShotInstant(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        isPlayingOneShot = true;
        AnimancerState state = animancer.Play(clip, 0f);
        state.Events(this).OnEnd = OnOneShotEnded;
    }

    private void OnOneShotEnded()
    {
        isPlayingOneShot = false;

        if (currentBaseClip != null && currentBaseClip.Clip != null)
        {
            animancer.Play(currentBaseClip);
        }
    }

    #endregion
}
