/// <summary>
/// Base interface for NPC animation handlers.
/// Covers only the animation calls that NpcBase needs.
/// NPC-type-specific animations (Fire, Charge, DashStart, etc.)
/// go on the concrete managers (SoldierAnimationManager, GenTwoAnimationManager)
/// and are accessed via typed properties on the concrete NPC classes.
/// 
/// Replaces all direct Animator access in NpcBase with type-safe method calls.
/// No more string-based triggers or bool parameters.
/// 
/// NOTE: PlayHitReaction and PlayDeath were removed because all NPCs
/// are one-shot kills with ragdoll death (handled by NpcRagdollSwapper).
/// </summary>
public interface INpcAnimationHandler
{
    /// <summary>Transition to the stunned animation loop.</summary>
    void PlayStunStart();

    /// <summary>Exit stunned state, return to idle.</summary>
    void PlayStunEnd();

    /// <summary>
    /// Update the movement animation based on navAgent speed.
    /// normalizedSpeed: 0 = idle, 1 = full walk speed.
    /// Called every frame from NpcBase.
    /// </summary>
    void UpdateMovement(float normalizedSpeed);

    /// <summary>
    /// Stop the AnimancerGraph and disable the underlying Animator.
    /// Called by NpcBase.Die() before ragdoll activation.
    /// After this call, physics-based ragdoll takes over the bones.
    /// </summary>
    void DisableForRagdoll();
}
