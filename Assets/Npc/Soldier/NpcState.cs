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
    void Enter(T npc);
    INpcState<T> Update(T npc);
    void Exit(T npc);
    string StateName { get; }
    int StateID { get; }
}

/// <summary>
/// Abstrakte Basis-Klasse für States mit Default-Implementierungen.
/// </summary>
public abstract class NpcStateBase<T> : INpcState<T> where T : NpcBase
{
    public abstract string StateName { get; }
    public abstract int StateID { get; }

    public virtual void Enter(T npc) { }
    public abstract INpcState<T> Update(T npc);
    public virtual void Exit(T npc) { }
}