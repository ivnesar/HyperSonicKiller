using Animancer;
using UnityEngine;

/// <summary>
/// Manages all Soldier NPC animations via Animancer.
/// Replaces the old AnimatorController-based system.
/// 
/// States call typed methods (PlayAim, PlayFire, etc.) instead of
/// setting string-based Animator parameters.
/// 
/// Timing: Uses normal Time.timeScale (NPC animations slow down during DashSlowMo).
/// No special pause handling needed — Time.timeScale = 0 freezes everything automatically.
/// 
/// SETUP:
///   1. Add AnimancerComponent to the Soldier model (child with the Animator).
///   2. Remove the AnimatorController from the Animator (leave the Animator component).
///   3. Assign all ClipTransitions in the Inspector.
/// </summary>
public class SoldierAnimationManager : MonoBehaviour, INpcAnimationHandler
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent on the soldier model. Auto-found in children if left empty.")]
    [SerializeField] private AnimancerComponent animancer;

    // ── Base Clips (looping) ─────────────────────────────────────────────

    [Header("Base Clips (Looping)")]
    [SerializeField] private ClipTransition idle;
    [SerializeField] private ClipTransition walk;
    [SerializeField] private ClipTransition aimHold;
    [SerializeField] private ClipTransition stunned;

    // ── One-Shot Clips ───────────────────────────────────────────────────

    [Header("One-Shot Clips")]
    [SerializeField] private ClipTransition aim;
    [SerializeField] private ClipTransition fire;
    [SerializeField] private ClipTransition reload;

    // ── Movement ─────────────────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Speed threshold below which idle plays instead of walk.")]
    [SerializeField] private float walkThreshold = 0.1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The clip that should be playing when no one-shot is active.
    /// Set by state transition methods (PlayIdle, PlayWalk, etc.).
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
            Debug.LogError($"[SoldierAnimationManager] No AnimancerComponent found on {gameObject.name}!");
            enabled = false;
        }
    }

    private void Start()
    {
        // Start with idle
        SetBaseClip(idle);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region INpcAnimationHandler (called by NpcBase)
    // ════════════════════════════════════════════════════════════════════════

    public void PlayHitReaction()
    {
        // Soldier ist ein One-Shot-Kill — keine Hit-Reaktion nötig.
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
        // Tod wird komplett über NpcRagdollSwapper abgewickelt.
        // DisableForRagdoll() wird stattdessen von NpcBase aufgerufen.
    }

    public void UpdateMovement(float normalizedSpeed)
    {
        // Movement blending is handled by the states calling PlayIdle/PlayWalk.
        // This is here for NpcBase compatibility if needed but the states
        // have more control over which clip to play.
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
    #region Soldier-Specific Methods (called by SoldierStates)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Set base to idle loop. Called by Idle.Enter().</summary>
    public void PlayIdle()
    {
        SetBaseClip(idle);
    }

    /// <summary>Set base to walk loop. Called by MovingToRange.Enter().</summary>
    public void PlayWalk()
    {
        SetBaseClip(walk);
    }

    /// <summary>
    /// Play aim one-shot, then hold in aim stance.
    /// Called by Aiming.Enter().
    /// </summary>
    public void PlayAim()
    {
        currentBaseClip = aimHold;
        PlayOneShot(aim);
    }

    /// <summary>
    /// Set base to aim-hold loop (firing stance).
    /// Called by Firing.Enter().
    /// </summary>
    public void PlayFiringStance()
    {
        SetBaseClip(aimHold);
    }

    /// <summary>
    /// Play fire one-shot (instant, no fade). Returns to firing stance.
    /// Called per shot in Firing.Update().
    /// </summary>
    public void PlayFireShot()
    {
        PlayOneShotInstant(fire);
    }

    /// <summary>
    /// Play reload one-shot, then return to idle.
    /// Called by Reloading.Enter().
    /// </summary>
    public void PlayReload()
    {
        currentBaseClip = idle;
        PlayOneShot(reload);
    }

    /// <summary>
    /// Play stunned loop and clear combat animations.
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
    /// Plays a one-shot with the clip's FadeDuration. Returns to base clip on end.
    /// </summary>
    private void PlayOneShot(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        isPlayingOneShot = true;
        AnimancerState state = animancer.Play(clip);
        state.Events(this).OnEnd = OnOneShotEnded;
    }

    /// <summary>
    /// Plays a one-shot instantly (fadeDuration = 0). For combat reactions.
    /// Forces restart even if the same clip is already playing (important for
    /// rapid repeated calls like FireShot during a salvo).
    /// </summary>
    private void PlayOneShotInstant(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        isPlayingOneShot = true;
        AnimancerState state = animancer.Play(clip, 0f);
        state.Time = 0f; // Force restart wenn gleicher Clip schon läuft
        state.Events(this).OnEnd = OnOneShotEnded;
    }

    private void OnOneShotEnded()
    {
        isPlayingOneShot = false;

        // Return to the current base clip
        if (currentBaseClip != null && currentBaseClip.Clip != null)
        {
            animancer.Play(currentBaseClip);
        }
    }

    #endregion
}
