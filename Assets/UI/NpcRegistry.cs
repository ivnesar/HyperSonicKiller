using System.Collections.Generic;

// ════════════════════════════════════════════════════════════════════════════
// NPC REGISTRY - Statische Liste aller lebenden NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// Wird von EnemyBoundingBoxUI genutzt, um effizient alle NPCs zu iterieren
// ohne FindGameObjectsWithTag() jeden Frame aufzurufen.
//
// SETUP: Kein manuelles Setup nötig. NpcBase registriert sich automatisch.
//
// ════════════════════════════════════════════════════════════════════════════

public static class NpcRegistry
{
    private static readonly HashSet<NpcBase> aliveNpcs = new();

    /// <summary>
    /// All currently registered (alive) NPCs.
    /// </summary>
    public static IReadOnlyCollection<NpcBase> AliveNpcs => aliveNpcs;

    public static void Register(NpcBase npc) => aliveNpcs.Add(npc);
    public static void Unregister(NpcBase npc) => aliveNpcs.Remove(npc);
    public static void Clear() => aliveNpcs.Clear();
}
