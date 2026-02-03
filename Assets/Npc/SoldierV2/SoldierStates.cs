using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SOLDIER STATES
// ════════════════════════════════════════════════════════════════════════════
//
// Idle → MovingToRange → Aiming → Firing → Reloading → zurück
//
// ════════════════════════════════════════════════════════════════════════════

namespace SoldierStates
{
    // ─────────────────────────────────────────────────────────────────────
    // IDLE
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (npc.IsInShootingRange())
                return new Aiming();

            if (npc.CurrentBehaviorMode == BehaviorMode.Pursuing && npc.CanReachPlayer)
                return new MovingToRange();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // MOVING TO RANGE
    // ─────────────────────────────────────────────────────────────────────
    public class MovingToRange : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Moving";
        public override int StateID => 1;

        public override void Enter(SoldierNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsMoving", true);
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (!npc.CanReachPlayer)
            {
                npc.StopMovement();
                return new Idle();
            }

            if (npc.IsInShootingRange())
            {
                npc.StopMovement();
                return new Aiming();
            }

            if (npc.DistanceToTarget > npc.PreferredRange)
            {
                npc.MoveTowardTarget();
            }
            else if (npc.DistanceToTarget < npc.MinShootingRange)
            {
                Vector3 retreatDir = -npc.GetDirectionToTarget();
                Vector3 retreatPos = npc.transform.position + retreatDir * 5f;
                npc.MoveToward(retreatPos, 0.7f);
            }

            return null;
        }

        public override void Exit(SoldierNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsMoving", false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // AIMING
    // ─────────────────────────────────────────────────────────────────────
    public class Aiming : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Aiming";
        public override int StateID => 2;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.AimDuration);
            
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetTrigger("Aim");
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (npc.UpdateStateTimer())
                return new Firing();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FIRING
    // ─────────────────────────────────────────────────────────────────────
    public class Firing : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Firing";
        public override int StateID => 3;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            npc.ShotsFiredInSalvo = 0;
            npc.NextShotTime = 0f;
            
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsFiring", true);
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (Time.time >= npc.NextShotTime && npc.ShotsFiredInSalvo < npc.ShotsPerSalvo)
            {
                npc.FireShot();
                npc.ShotsFiredInSalvo++;
                npc.NextShotTime = Time.time + npc.TimeBetweenShots;
            }

            if (npc.ShotsFiredInSalvo >= npc.ShotsPerSalvo)
                return new Reloading();

            return null;
        }

        public override void Exit(SoldierNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsFiring", false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // RELOADING
    // ─────────────────────────────────────────────────────────────────────
    public class Reloading : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Reloading";
        public override int StateID => 4;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.ReloadDuration);
            
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetTrigger("Reload");
            
            npc.PlayReloadSound();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (npc.UpdateStateTimer())
                return new Idle();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // STUNNED
    // ─────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 5;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsFiring", false);
                npc.NpcAnimator.SetBool("IsMoving", false);
            }
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc) => null;
    }
}
