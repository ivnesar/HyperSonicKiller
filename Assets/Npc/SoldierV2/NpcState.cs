using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// NPC STATE SYSTEM - Lightweight State Pattern für NPC-Verhalten
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Generisches Interface für NPC-States.
/// T ist der konkrete NPC-Typ (z.B. SoldierNpc, DefenderNpc).
/// </summary>
public interface INpcState<T> where T : NpcBase
{
    /// <summary>
    /// Wird einmalig aufgerufen wenn der State betreten wird.
    /// </summary>
    void Enter(T npc);

    /// <summary>
    /// Wird jeden Frame aufgerufen. Gibt den nächsten State zurück,
    /// oder null wenn der aktuelle State beibehalten werden soll.
    /// </summary>
    INpcState<T> Update(T npc);

    /// <summary>
    /// Wird aufgerufen wenn der State verlassen wird.
    /// </summary>
    void Exit(T npc);

    /// <summary>
    /// Name des States für Debugging.
    /// </summary>
    string StateName { get; }

    /// <summary>
    /// Numerische ID für Animator-Integration.
    /// </summary>
    int StateID { get; }
}

/// <summary>
/// Abstrakte Basis-Klasse für States mit Default-Implementierungen.
/// Reduziert Boilerplate in konkreten States.
/// </summary>
public abstract class NpcStateBase<T> : INpcState<T> where T : NpcBase
{
    public abstract string StateName { get; }
    public abstract int StateID { get; }

    /// <summary>
    /// Override in Subklassen für Enter-Logik.
    /// Default: nichts tun.
    /// </summary>
    public virtual void Enter(T npc) { }

    /// <summary>
    /// MUSS in Subklassen implementiert werden.
    /// Gibt den nächsten State zurück, oder null um im aktuellen zu bleiben.
    /// </summary>
    public abstract INpcState<T> Update(T npc);

    /// <summary>
    /// Override in Subklassen für Exit-Logik.
    /// Default: nichts tun.
    /// </summary>
    public virtual void Exit(T npc) { }
}
