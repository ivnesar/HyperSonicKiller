using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DEFENDER STATES
// ════════════════════════════════════════════════════════════════════════════
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
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsMoving", true);
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

        public override void Exit(DefenderNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsMoving", false);
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
            
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsGuarding", true);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            npc.RotateTowardTarget();

            if (npc.IsTooFar())
                return new Idle();

            return null;
        }

        public override void Exit(DefenderNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsGuarding", false);
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
            
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsGuarding", false);
                npc.NpcAnimator.SetBool("IsMoving", false);
            }
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc) => null;
    }
}
