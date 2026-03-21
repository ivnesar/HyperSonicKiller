// ════════════════════════════════════════════════════════════════════════════
// NPC DEATH TYPE - Bestimmt wie der NPC stirbt und welche Ragdolls gespawnt werden
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Art des Todes, bestimmt welches Ragdoll-Setup beim Swap verwendet wird.
/// Wird automatisch in den Damage-Methoden von NpcBase gesetzt.
/// </summary>
public enum NpcDeathType
{
    /// <summary>
    /// NPC bleibt in einem Stück (Kugeln, Explosionen, Thrown Sword, etc.).
    /// Spawnt ein einzelnes Fullbody-Ragdoll-Prefab.
    /// </summary>
    WholeBody,

    /// <summary>
    /// NPC wird zerschnitten (Melee-Angriff).
    /// Spawnt zwei Ragdoll-Hälften aus einem SlicedRagdollPair.
    /// </summary>
    Sliced
}
