using UnityEngine;

/// <summary>
/// Interface for NPC interaction with player combat systems.
/// Implement this on any NPC that can receive damage, stuns, or other effects from the player.
/// </summary>
public interface INpcInteraction
{
    /// <summary>
    /// Called when the NPC takes melee damage from the player's sword.
    /// </summary>
    void OnMeeleDamage(int amount);

    /// <summary>
    /// Called when the thrown sword embeds in this NPC, stunning them.
    /// Damage is applied after the stun ends (delayed damage mechanic).
    /// </summary>
    /// <param name="duration">Base stun duration (used for residual stun after sword removal)</param>
    /// <param name="damage">Damage to apply when stun ends</param>
    /// <param name="swordDirection">Direction the sword was traveling (for ragdoll impact)</param>
    /// <param name="hitPoint">World position where the sword hit</param>
    void OnThrowStun(float duration, int damage, Vector3 swordDirection, Vector3 hitPoint);

    /// <summary>
    /// Called when the embedded sword is recalled/removed from the NPC.
    /// </summary>
    void OnSwordRemoved();
}