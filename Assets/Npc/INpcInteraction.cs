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
    /// </summary>
    void OnThrowStun(float duration);

    /// <summary>
    /// Called when the embedded sword is recalled/removed from the NPC.
    /// </summary>
    void OnSwordRemoved();
}