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

    // Bug 3: Referenz auf den aktuell laufenden One-Shot-State (null = kein One-Shot aktiv).
    // Ersetzt das alte bool isPlayingOneShot. Dadurch können wir veraltete OnEnd-Callbacks
    // erkennen und ignorieren, wenn inzwischen ein anderer Clip läuft.
    private AnimancerState activeOneShot;

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
            return;
        }

        ValidateClips();
    }

    /// <summary>
    /// Bug 3: Meldet beim Start jeden nicht zugewiesenen Clip-Slot.
    /// Häufigste Ursache für "Animation bleibt hängen": eine fehlende Ground- ODER
    /// Wall-Variante. Da isOnWall über den ganzen Zyklus erhalten bleibt, wirkt der
    /// Fehler dann scheinbar zufällig (nur nach einer Wand-Landung).
    /// </summary>
    private void ValidateClips()
    {
        void Check(ClipTransition c, string slot)
        {
            if (c == null || c.Clip == null)
                Debug.LogWarning($"[GenTwoAnimationManager] Clip-Slot '{slot}' ist nicht zugewiesen " +
                                 $"auf {gameObject.name}! Die zugehörige Animation wird nicht abgespielt.");
        }

        Check(idleGround, "idleGround");           Check(idleWall, "idleWall");
        Check(chargeGround, "chargeGround");       Check(chargeWall, "chargeWall");
        Check(startDashGround, "startDashGround"); Check(startDashWall, "startDashWall");
        Check(dash, "dash");                       Check(dashAttack, "dashAttack");
        Check(landingGround, "landingGround");     Check(landingWall, "landingWall");
        Check(stunnedGround, "stunnedGround");     Check(stunnedWall, "stunnedWall");
    }

    private void Start()
    {
        SetBaseClip(idleGround);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region INpcAnimationHandler (called by NpcBase)
    // ════════════════════════════════════════════════════════════════════════

    public void PlayStunStart()
    {
        PlayStunned();
    }

    public void PlayStunEnd()
    {
        PlayIdle();
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

        // Bug 3: activeOneShot löschen, damit kein nachhallender Dash-/DashAttack-Callback
        // die Landing-Pose wieder überschreibt.
        activeOneShot = null;

        if (landClip == null || landClip.Clip == null)
        {
            // Kein Landing-Clip → wenigstens nicht in der Dash-Pose hängen bleiben.
            FallBackToBaseClip();
            return;
        }

        animancer.Play(landClip, 0f);

        // Don't set OnEnd — landing holds until PlayIdle is called.
    }

    /// <summary>Play stunned loop (ground or wall variant).</summary>
    public void PlayStunned()
    {
        // SetBaseClip räumt activeOneShot jetzt selbst auf und spielt sofort ab.
        SetBaseClip(isOnWall ? stunnedWall : stunnedGround);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Playback
    // ════════════════════════════════════════════════════════════════════════

    private void SetBaseClip(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        currentBaseClip = clip;

        // Ein Base-Clip aus einem State-Wechsel (Idle/Charge/Stunned) soll SOFORT greifen.
        // Ein evtl. noch laufender One-Shot wird verworfen — sonst konnte die alte Animation
        // hängen bleiben, wenn isPlayingOneShot fälschlich true blieb (Bug 3).
        activeOneShot = null;
        animancer.Play(clip);
    }

    private void PlayOneShot(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null)
        {
            // Kein One-Shot-Clip zugewiesen → direkt zum Base-Clip, statt die vorige
            // Animation weiterlaufen zu lassen (Bug 3: fehlende Ground/Wall-Variante).
            FallBackToBaseClip();
            return;
        }

        AnimancerState state = animancer.Play(clip);
        activeOneShot = state;
        state.Events(this).OnEnd = () => OnOneShotEnded(state);
    }

    private void PlayOneShotInstant(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null)
        {
            FallBackToBaseClip();
            return;
        }

        AnimancerState state = animancer.Play(clip, 0f);
        activeOneShot = state;
        state.Events(this).OnEnd = () => OnOneShotEnded(state);
    }

    private void OnOneShotEnded(AnimancerState endedState)
    {
        // Veralteten Callback ignorieren: Läuft inzwischen ein anderer Clip (z.B. Landing
        // oder ein neuer One-Shot), darf dieser Callback nichts mehr überschreiben (Bug 3).
        if (endedState != activeOneShot) return;

        FallBackToBaseClip();
    }

    /// <summary>
    /// Beendet den One-Shot-Zustand und kehrt zum aktuellen Base-Clip zurück.
    /// </summary>
    private void FallBackToBaseClip()
    {
        activeOneShot = null;

        if (currentBaseClip != null && currentBaseClip.Clip != null)
        {
            animancer.Play(currentBaseClip);
        }
    }

    #endregion
}
