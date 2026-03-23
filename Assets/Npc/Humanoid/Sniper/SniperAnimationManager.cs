using Animancer;
using UnityEngine;

/// <summary>
/// Manages all Sniper NPC animations via Animancer with a 2-layer system.
///
/// LAYER SETUP:
///   Layer 0 (Base):       Idle, Walk, Stunned — Fullbody, no mask.
///   Layer 1 (Upper Body): Aim, AimHold, Fire, Reload — masked to upper body only.
///
/// This prevents upper-body animations (like Reload) from rotating the hips/legs.
/// The base layer always controls the legs, the upper layer overrides only spine + arms.
///
/// Unterschied zum Soldier: Der Sniper hat keine FiringStance (kein Salvo-Feuer).
/// Er schießt genau einmal pro Zyklus, daher wird im Firing-State direkt
/// PlayFireShot() aufgerufen und danach sofort Reloading betreten.
///
/// SETUP:
///   1. Add AnimancerComponent to the Sniper model (child with the Animator).
///   2. Remove the AnimatorController from the Animator (leave the Animator component).
///   3. Assign all ClipTransitions in the Inspector.
///   4. Create an AvatarMask that includes Spine, Chest, UpperChest, Shoulders, Arms, Hands, Head.
///      Exclude Hips, Legs, Feet, Toes, Root.
///   5. Assign the AvatarMask to the "Upper Body Mask" field.
/// </summary>
public class SniperAnimationManager : MonoBehaviour, INpcAnimationHandler
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent on the sniper model. Auto-found in children if left empty.")]
    [SerializeField] private AnimancerComponent animancer;

    [Header("Upper Body Mask")]
    [Tooltip("AvatarMask for upper body layer. Must include Spine/Chest/Arms/Head, exclude Hips/Legs.")]
    [SerializeField] private AvatarMask upperBodyMask;

    // ── Base Clips (Layer 0 — Fullbody, looping) ─────────────────────────

    [Header("Base Clips — Layer 0 (Fullbody, Looping)")]
    [SerializeField] private ClipTransition idle;
    [SerializeField] private ClipTransition walk;
    [SerializeField] private ClipTransition stunned;

    // ── Upper Body Clips (Layer 1 — Masked) ──────────────────────────────

    [Header("Upper Body Clips — Layer 1 (Masked)")]
    [SerializeField] private ClipTransition aim;
    [SerializeField] private ClipTransition aimHold;
    [SerializeField] private ClipTransition fire;
    [SerializeField] private ClipTransition reload;

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
            Debug.LogError($"[SniperAnimationManager] No AnimancerComponent found on {gameObject.name}!");
            enabled = false;
            return;
        }

        if (upperBodyMask == null)
        {
            Debug.LogError($"[SniperAnimationManager] No Upper Body Mask assigned on {gameObject.name}! " +
                           "Upper body animations will affect the whole body.");
        }

        // Layer-Referenzen sofort in Awake holen,
        // damit sie bereit sind wenn NpcBase.Start() → OnStart() aufgerufen wird.
        EnsureLayersInitialized();
    }

    private void Start()
    {
        // Sicherheitshalber nochmal
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
    #region Sniper-Specific Methods (called by SniperStates)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Idle: Base=Idle, Upper=off.
    /// Called by Idle.Enter().
    /// </summary>
    public void PlayIdle()
    {
        ClearUpperLayer();
        baseLayer.Play(idle);
    }

    /// <summary>
    /// Walk: Base=Walk, Upper=off.
    /// Called by MovingToRange.Enter().
    /// </summary>
    public void PlayWalk()
    {
        ClearUpperLayer();
        baseLayer.Play(walk);
    }

    /// <summary>
    /// Play aim one-shot on upper layer, then hold in aim stance.
    /// Base layer keeps playing (idle/walk for legs).
    /// Die Aim-Phase ist beim Sniper deutlich länger als beim Soldier.
    /// Called by Aiming.Enter().
    /// </summary>
    public void PlayAim()
    {
        // Beine: Idle-Stance während Aiming
        baseLayer.Play(idle);

        // Oberkörper: Aim-OneShot → dann AimHold-Loop
        currentUpperBaseClip = aimHold;
        PlayUpperOneShot(aim);
    }

    /// <summary>
    /// Play fire one-shot on upper layer (instant, no fade).
    /// Returns to aimHold after (für den Fall dass der State noch kurz aktiv bleibt).
    /// Called by Firing state.
    /// </summary>
    public void PlayFireShot()
    {
        currentUpperBaseClip = aimHold;
        PlayUpperOneShotInstant(fire);
    }

    /// <summary>
    /// Play reload one-shot on upper layer, then return to idle.
    /// Base layer keeps idle (legs stay neutral).
    /// Called by Reloading.Enter().
    /// </summary>
    public void PlayReload()
    {
        baseLayer.Play(idle);

        // Nach Reload → Upper-Layer ausschalten (Idle hat keine Oberkörper-Pose)
        currentUpperBaseClip = null;
        PlayUpperOneShot(reload);
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
