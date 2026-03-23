using Animancer;
using UnityEngine;

/// <summary>
/// Manages all Defender NPC animations via Animancer with a 2-layer system.
///
/// LAYER SETUP:
///   Layer 0 (Base):       Idle, Walk, Stunned — Fullbody, no mask.
///   Layer 1 (Upper Body): Reserved for future use (e.g. shield bash, block reaction).
///
/// Currently the Defender only uses fullbody animations (Idle, Walk, Stunned).
/// The upper layer is prepared but inactive — it can be used later for
/// upper-body overrides without affecting the legs (e.g. shield raise while walking).
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
    [Tooltip("AvatarMask for upper body layer. Must include Spine/Chest/Arms/Head, exclude Hips/Legs. " +
             "Currently reserved for future use (shield reactions while walking).")]
    [SerializeField] private AvatarMask upperBodyMask;

    // ── Base Clips (Layer 0 — Fullbody, looping) ─────────────────────────

    [Header("Base Clips — Layer 0 (Fullbody, Looping)")]
    [SerializeField] private ClipTransition idle;
    [SerializeField] private ClipTransition walk;
    [SerializeField] private ClipTransition stunned;

    // ── Upper Body Clips (Layer 1 — Masked, reserved) ───────────────────

    [Header("Upper Body Clips — Layer 1 (Masked, reserved for future use)")]
    [Tooltip("Optional: Shield block/raise animation. Not used yet.")]
    [SerializeField] private ClipTransition shieldBlock;

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
    /// null = upper layer inactive (weight 0), legs get full control.
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
                             "Upper layer will affect the whole body if used later.");
        }

        // Layer-Referenzen sofort in Awake holen,
        // damit sie bereit sind wenn NpcBase.Start() → OnStart() aufgerufen wird.
        EnsureLayersInitialized();
    }

    private void Start()
    {
        // Sicherheitshalber nochmal — falls Awake-Reihenfolge ungewöhnlich war
        EnsureLayersInitialized();

        // Start: Idle auf Base-Layer, Upper-Layer inaktiv
        baseLayer.Play(idle);
        upperLayer.Weight = 0f;
    }

    /// <summary>
    /// Holt Layer-Referenzen und setzt die Mask. Idempotent — kann mehrfach aufgerufen werden.
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
    /// Idle: Base=Idle, Upper=off.
    /// Called by Idle.Enter() and InPosition.Enter().
    /// </summary>
    public void PlayIdle()
    {
        ClearUpperLayer();
        baseLayer.Play(idle);
    }

    /// <summary>
    /// Walk: Base=Walk, Upper=off.
    /// Called by Approaching.Enter().
    /// </summary>
    public void PlayWalk()
    {
        ClearUpperLayer();
        baseLayer.Play(walk);
    }

    /// <summary>
    /// Play stunned loop and clear upper layer.
    /// Called by Stunned.Enter().
    /// </summary>
    public void PlayStunnedFromCombat()
    {
        ClearUpperLayer();
        baseLayer.Play(stunned);
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

        isPlayingUpperOneShot = true;
        upperLayer.Weight = 1f;
        AnimancerState state = upperLayer.Play(clip);
        state.Events(this).OnEnd = OnUpperOneShotEnded;
    }

    /// <summary>
    /// Plays a one-shot on the upper layer instantly (no fade).
    /// Forces restart even if same clip is already playing.
    /// </summary>
    private void PlayUpperOneShotInstant(ClipTransition clip)
    {
        if (clip == null || clip.Clip == null) return;

        isPlayingUpperOneShot = true;
        upperLayer.Weight = 1f;
        AnimancerState state = upperLayer.Play(clip, 0f);
        state.Time = 0f;
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
