using Animancer;
using UnityEngine;

/// <summary>
/// Manages all GenTwo NPC animations via Animancer.
/// Handles Ground/Wall variants for each state automatically.
/// 
/// GenTwo has two visual modes: sitting on ground vs clinging to wall.
/// Most states have separate clips for each mode. The manager picks
/// the correct variant based on the isOnWall flag, which is set by
/// GenTwoNpc.DetermineWallOrGround() when GenTwo lands on a surface.
/// 
/// Timing: Uses normal Time.timeScale (slows down during DashSlowMo).
/// 
/// SETUP:
///   1. Add AnimancerComponent to the GenTwo model (child with the Animator).
///   2. Remove the AnimatorController from the Animator.
///   3. Assign all ClipTransitions in the Inspector (Ground + Wall variants).
/// </summary>
public class GenTwoAnimationManager : MonoBehaviour, INpcAnimationHandler
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent on the GenTwo model. Auto-found in children if left empty.")]
    [SerializeField] private AnimancerComponent animancer;

    // ── Idle ─────────────────────────────────────────────────────────────

    [Header("Idle (Looping)")]
    [SerializeField] private ClipTransition idleGround;
    [SerializeField] private ClipTransition idleWall;

    // ── Charge ───────────────────────────────────────────────────────────

    [Header("Charge (Looping)")]
    [SerializeField] private ClipTransition chargeGround;
    [SerializeField] private ClipTransition chargeWall;

    // ── Dash ─────────────────────────────────────────────────────────────

    [Header("Dash Start (One-Shot)")]
    [SerializeField] private ClipTransition startDashGround;
    [SerializeField] private ClipTransition startDashWall;

    [Header("Dash (Shared)")]
    [SerializeField] private ClipTransition dash;
    [SerializeField] private ClipTransition dashAttack;

    // ── Landing ──────────────────────────────────────────────────────────

    [Header("Landing (One-Shot)")]
    [SerializeField] private ClipTransition landingGround;
    [SerializeField] private ClipTransition landingWall;

    // ── Stunned ──────────────────────────────────────────────────────────

    [Header("Stunned (Looping)")]
    [SerializeField] private ClipTransition stunnedGround;
    [SerializeField] private ClipTransition stunnedWall;

    // ── Hit / Death (Shared) ─────────────────────────────────────────────

    [Header("Hit / Death (Shared)")]
    [SerializeField] private ClipTransition hit;
    [SerializeField] private ClipTransition die;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// True when GenTwo is clinging to a wall (changes animation variants).
    /// Set by GenTwoNpc.DetermineWallOrGround() after landing.
    /// </summary>
    private bool isOnWall;

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
            Debug.LogError($"[GenTwoAnimationManager] No AnimancerComponent found on {gameObject.name}!");
            enabled = false;
        }
    }

    private void Start()
    {
        SetBaseClip(idleGround);
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
        PlayStunned();
    }

    public void PlayStunEnd()
    {
        PlayIdle();
    }

    public void PlayDeath()
    {
        if (die == null || die.Clip == null) return;

        isPlayingOneShot = false;
        animancer.Play(die, 0f);
    }

    public void UpdateMovement(float normalizedSpeed)
    {
        // GenTwo does not use NavMesh movement — ignore this.
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
    #region GenTwo-Specific Methods (called by GenTwoStates)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set on-wall flag. Determines which Ground/Wall variant is used
    /// for all subsequent animation calls.
    /// Called by GenTwoNpc.DetermineWallOrGround().
    /// </summary>
    public void SetOnWall(bool onWall)
    {
        isOnWall = onWall;
    }

    /// <summary>Play idle loop (ground or wall variant). Called by Idle.Enter().</summary>
    public void PlayIdle()
    {
        SetBaseClip(isOnWall ? idleWall : idleGround);
    }

    /// <summary>Play charge loop (ground or wall variant). Called by Charging.Enter().</summary>
    public void PlayCharge()
    {
        SetBaseClip(isOnWall ? chargeWall : chargeGround);
    }

    /// <summary>
    /// Play dash start one-shot, then transition to dash loop.
    /// Called by Dashing.Enter().
    /// </summary>
    public void PlayDashStart()
    {
        currentBaseClip = dash;

        ClipTransition startClip = isOnWall ? startDashWall : startDashGround;
        PlayOneShot(startClip);
    }

    /// <summary>
    /// Play the dash loop directly (skipping start animation).
    /// Can be called if dash start is not needed.
    /// </summary>
    public void PlayDash()
    {
        SetBaseClip(dash);
    }

    /// <summary>
    /// Play dash-attack one-shot (on player collision during dash).
    /// Returns to dash loop when finished.
    /// Called by GenTwoNpc.ProcessDashMovement().
    /// </summary>
    public void PlayDashAttack()
    {
        if (dashAttack == null || dashAttack.Clip == null) return;

        // Keep dash as base so we return to it
        currentBaseClip = dash;
        PlayOneShotInstant(dashAttack);
    }

    /// <summary>
    /// Play landing one-shot (ground or wall variant).
    /// Called by Dashing.Exit() after hitting a surface.
    /// </summary>
    public void PlayLanding()
    {
        // After landing, we stay in the landing pose until recovery is done.
        // The landing clip should NOT auto-return to a base clip.
        ClipTransition landClip = isOnWall ? landingWall : landingGround;

        if (landClip == null || landClip.Clip == null) return;

        isPlayingOneShot = false;
        animancer.Play(landClip, 0f);

        // Don't set OnEnd — landing holds until PlayRecoveryDone or PlayIdle is called.
    }

    /// <summary>Play stunned loop (ground or wall variant).</summary>
    public void PlayStunned()
    {
        isPlayingOneShot = false;
        SetBaseClip(isOnWall ? stunnedWall : stunnedGround);
    }

    /// <summary>
    /// Transition from landing/stunned back to idle.
    /// Called by Recovery.Update() when timer expires, and by GenTwoNpc.OnStunEnd().
    /// </summary>
    public void PlayRecoveryDone()
    {
        PlayIdle();
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
