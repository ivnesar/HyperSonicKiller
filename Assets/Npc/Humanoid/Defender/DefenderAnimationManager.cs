using Animancer;
using UnityEngine;

/// <summary>
/// Manages all Defender NPC animations via Animancer with a 2-layer system.
///
/// LAYER SETUP:
///   Layer 0 (Base):       Idle, Walk, Stunned — Fullbody, no mask.
///   Layer 1 (Upper Body): ShieldBlock (looping base), ShieldHitReaction (one-shot).
///
/// The upper layer is ALWAYS active during normal gameplay (Idle, Walk, InPosition)
/// with the ShieldBlock pose as the looping base clip. This means:
///   - Legs are controlled by the base layer (walk cycle, idle)
///   - Upper body is controlled by the upper layer (shield block pose)
///
/// When the player hits the shield, a ShieldHitReaction one-shot plays on the
/// upper layer. After the one-shot finishes, it automatically returns to ShieldBlock.
///
/// During Stunned, the upper layer is deactivated — the stunned animation
/// controls the entire body.
///
/// SETUP:
///   1. Add AnimancerComponent to the Defender model (child with the Animator).
///   2. Remove the AnimatorController from the Animator (leave the Animator component).
///   3. Assign all ClipTransitions in the Inspector.
///   4. Create an AvatarMask that includes Spine, Chest, UpperChest, Shoulders, Arms, Hands, Head.
///      Exclude Hips, Legs, Feet, Toes, Root.
///   5. Assign the AvatarMask to the "Upper Body Mask" field.
/// </summary>
public class DefenderAnimationManager : MonoBehaviour, INpcAnimationHandler
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent on the defender model. Auto-found in children if left empty.")]
    [SerializeField] private AnimancerComponent animancer;

    [Header("Upper Body Mask")]
    [Tooltip("AvatarMask for upper body layer. Must include Spine/Chest/Arms/Head, exclude Hips/Legs.")]
    [SerializeField] private AvatarMask upperBodyMask;

    // ── Base Clips (Layer 0 — Fullbody, looping) ─────────────────────────

    [Header("Base Clips — Layer 0 (Fullbody, Looping)")]
    [SerializeField] private ClipTransition idle;
    [SerializeField] private ClipTransition walk;
    [SerializeField] private ClipTransition stunned;

    // ── Upper Body Clips (Layer 1 — Masked) ─────────────────────────────

    [Header("Upper Body Clips — Layer 1 (Masked)")]
    [Tooltip("Looping shield block pose. Always active on upper layer during normal states.")]
    [SerializeField] private ClipTransition shieldBlock;

    [Tooltip("One-shot hit reaction when player hits the shield. Returns to ShieldBlock after.")]
    [SerializeField] private ClipTransition shieldHitReaction;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Layer 0 = Base (fullbody), Layer 1 = Upper Body (masked).</summary>
    private AnimancerLayer baseLayer;
    private AnimancerLayer upperLayer;

    /// <summary>True while a one-shot is playing on the upper layer.</summary>
    private bool isPlayingUpperOneShot;

    /// <summary>
    /// The looping clip that should be active on the upper layer when no one-shot plays.
    /// null = upper layer inactive (weight 0), base layer gets full control.
    /// </summary>
    private ClipTransition currentUpperBaseClip;

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
            return;
        }

        if (upperBodyMask == null)
        {
            Debug.LogWarning($"[DefenderAnimationManager] No Upper Body Mask assigned on {gameObject.name}. " +
                             "Upper layer will affect the whole body.");
        }

        EnsureLayersInitialized();
    }

    private void Start()
    {
        EnsureLayersInitialized();

        // Start: Idle auf Base-Layer, ShieldBlock auf Upper-Layer
        baseLayer.Play(idle);
        ActivateShieldLayer();
    }

    /// <summary>
    /// Holt Layer-Referenzen und setzt die Mask. Idempotent.
    /// </summary>
    private void EnsureLayersInitialized()
    {
        if (baseLayer != null) return;
        if (animancer == null) return;

        baseLayer = animancer.Graph.Layers[0];
        upperLayer = animancer.Graph.Layers[1];

        if (upperBodyMask != null)
        {
            upperLayer.Mask = upperBodyMask;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region INpcAnimationHandler (called by NpcBase)
    // ════════════════════════════════════════════════════════════════════════

    public void PlayStunStart()
    {
        // Stunned = Fullbody → Base-Layer, Upper-Layer aus
        ClearUpperLayer();
        baseLayer.Play(stunned);
    }

    public void PlayStunEnd()
    {
        baseLayer.Play(idle);
        ActivateShieldLayer();
    }

    public void UpdateMovement(float normalizedSpeed)
    {
        // Movement blending is handled by the states calling PlayIdle/PlayWalk.
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

    /// <summary>
    /// Idle: Base=Idle, Upper=ShieldBlock.
    /// Called by Idle.Enter() and InPosition.Enter().
    /// </summary>
    public void PlayIdle()
    {
        baseLayer.Play(idle);
        ActivateShieldLayer();
    }

    /// <summary>
    /// Walk: Base=Walk, Upper=ShieldBlock.
    /// Called by Approaching.Enter().
    /// </summary>
    public void PlayWalk()
    {
        baseLayer.Play(walk);
        ActivateShieldLayer();
    }

    /// <summary>
    /// Play stunned loop and clear upper layer (fullbody stunned).
    /// Called by Stunned.Enter().
    /// </summary>
    public void PlayStunnedFromCombat()
    {
        ClearUpperLayer();
        baseLayer.Play(stunned);
    }

    /// <summary>
    /// Plays the shield hit reaction as a one-shot on the upper layer.
    /// After the one-shot finishes, automatically returns to ShieldBlock.
    /// Called by ShieldHitReaction state or combat system.
    /// </summary>
    public void PlayShieldHitReaction()
    {
        PlayUpperOneShot(shieldHitReaction);
    }

    /// <summary>
    /// True while the shield hit reaction one-shot is still playing.
    /// Used by ShieldHitReaction state to know when the animation is done.
    /// </summary>
    public bool IsPlayingHitReaction => isPlayingUpperOneShot;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal — Shield Layer Management
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Activates the upper layer with ShieldBlock as the looping base clip.
    /// If an existing shield layer is already active, does nothing extra.
    /// </summary>
    private void ActivateShieldLayer()
    {
        if (shieldBlock == null || shieldBlock.Clip == null)
        {
            Debug.LogWarning($"[DefenderAnimationManager] No ShieldBlock clip assigned on {gameObject.name}!");
            return;
        }

        SetUpperBaseClip(shieldBlock);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal — Upper Layer Playback
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets the looping base clip on the upper layer.
    /// </summary>
    private void SetUpperBaseClip(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null)
        {
            ClearUpperLayer();
            return;
        }

        currentUpperBaseClip = clip;
        upperLayer.Weight = 1f;

        if (!isPlayingUpperOneShot)
        {
            upperLayer.Play(clip);
        }
    }

    /// <summary>
    /// Plays a one-shot on the upper layer with fade. Returns to upper base clip on end.
    /// </summary>
    private void PlayUpperOneShot(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        // Stelle sicher, dass ShieldBlock als Base-Clip gesetzt ist,
        // damit nach dem One-Shot dorthin zurückgekehrt wird.
        if (currentUpperBaseClip == null)
        {
            currentUpperBaseClip = shieldBlock;
        }

        isPlayingUpperOneShot = true;
        upperLayer.Weight = 1f;
        AnimancerState state = upperLayer.Play(clip);
        state.Events(this).OnEnd = OnUpperOneShotEnded;
    }

    /// <summary>
    /// Called when an upper layer one-shot finishes.
    /// Returns to upper base clip, or deactivates layer if none set.
    /// </summary>
    private void OnUpperOneShotEnded()
    {
        isPlayingUpperOneShot = false;

        if (currentUpperBaseClip != null && currentUpperBaseClip.Clip != null)
        {
            upperLayer.Play(currentUpperBaseClip);
        }
        else
        {
            ClearUpperLayer();
        }
    }

    /// <summary>
    /// Deactivates the upper layer completely.
    /// Base layer takes full control of all bones.
    /// </summary>
    private void ClearUpperLayer()
    {
        isPlayingUpperOneShot = false;
        currentUpperBaseClip = null;
        upperLayer.Weight = 0f;
        upperLayer.Stop();
    }

    #endregion
}
