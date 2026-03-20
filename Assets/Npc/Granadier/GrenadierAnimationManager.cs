using Animancer;
using UnityEngine;

/// <summary>
/// Manages all Grenadier NPC animations via Animancer.
/// Strukturell identisch zum SoldierAnimationManager, aber als eigene Klasse
/// damit der Grenadier eigene Animationsclips haben kann.
///
/// Der Grenadier hat keinen "Fire-Loop" wie der Soldier — er schießt
/// nur eine einzelne Granate pro Salve, daher gibt es PlayFireShot()
/// aber keine PlayFiringStance().
///
/// SETUP:
///   1. Add AnimancerComponent to the Grenadier model (child with the Animator).
///   2. Remove the AnimatorController from the Animator (leave the Animator component).
///   3. Assign all ClipTransitions in the Inspector.
/// </summary>
public class GrenadierAnimationManager : MonoBehaviour, INpcAnimationHandler
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent on the grenadier model. Auto-found in children if left empty.")]
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
    [SerializeField] private ClipTransition hit;
    [SerializeField] private ClipTransition die;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Der Clip der laufen soll wenn kein One-Shot aktiv ist.
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
            Debug.LogError($"[GrenadierAnimationManager] No AnimancerComponent found on {gameObject.name}!");
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
        // Movement wird von den States gesteuert (PlayIdle/PlayWalk)
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
    #region Grenadier-Specific Methods (called by GrenadierStates)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Set base to idle loop.</summary>
    public void PlayIdle()
    {
        SetBaseClip(idle);
    }

    /// <summary>Set base to walk loop.</summary>
    public void PlayWalk()
    {
        SetBaseClip(walk);
    }

    /// <summary>
    /// Play aim one-shot, then hold in aim stance.
    /// </summary>
    public void PlayAim()
    {
        currentBaseClip = aimHold;
        PlayOneShot(aim);
    }

    /// <summary>
    /// Play fire one-shot (Einzelschuss). Returns to idle after.
    /// </summary>
    public void PlayFireShot()
    {
        currentBaseClip = idle;
        PlayOneShotInstant(fire);
    }

    /// <summary>
    /// Play reload one-shot, then return to idle.
    /// </summary>
    public void PlayReload()
    {
        currentBaseClip = idle;
        PlayOneShot(reload);
    }

    /// <summary>
    /// Play stunned loop and clear combat animations.
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

    private void SetBaseClip(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        currentBaseClip = clip;

        if (!isPlayingOneShot)
        {
            animancer.Play(clip);
        }
    }

    private void PlayOneShot(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        isPlayingOneShot = true;
        AnimancerState state = animancer.Play(clip);
        state.Events(this).OnEnd = OnOneShotEnded;
    }

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
