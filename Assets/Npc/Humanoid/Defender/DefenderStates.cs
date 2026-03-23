using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DEFENDER STATES (Animancer 2-Layer Version)
// ════════════════════════════════════════════════════════════════════════════
//
// Alle animator.SetBool Aufrufe sind durch typsichere
// AnimManager-Methoden ersetzt. Kein String-basierter Zugriff mehr.
//
// Idle → Approaching → InPosition → zurück
//
// ════════════════════════════════════════════════════════════════════════════

namespace DefenderStates
{
    // ─────────────────────────────────────────────────────────────────────
    // IDLE
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            npc.RotateTowardTarget();

            if (npc.IsCloseEnough())
                return new InPosition();

            if (npc.CanReachPlayer && npc.CurrentBehaviorMode == BehaviorMode.Pursuing)
                return new Approaching();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // APPROACHING
    // ─────────────────────────────────────────────────────────────────────
    public class Approaching : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Approaching";
        public override int StateID => 1;

        public override void Enter(DefenderNpc npc)
        {
            npc.AnimManager?.PlayWalk();
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            npc.RotateTowardTarget();

            if (!npc.CanReachPlayer)
            {
                npc.StopMovement();
                return new Idle();
            }

            if (npc.IsCloseEnough())
            {
                npc.StopMovement();
                return new InPosition();
            }

            npc.MoveTowardTarget();
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // IN POSITION
    // ─────────────────────────────────────────────────────────────────────
    public class InPosition : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "InPosition";
        public override int StateID => 2;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            npc.RotateTowardTarget();

            if (npc.IsTooFar())
                return new Idle();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // STUNNED
    // ─────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 3;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.AnimManager?.PlayStunnedFromCombat();
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc) => null;
    }
}
