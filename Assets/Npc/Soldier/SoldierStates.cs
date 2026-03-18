using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SOLDIER STATES (Animancer Version)
// ════════════════════════════════════════════════════════════════════════════
//
// Alle animator.SetTrigger/SetBool Aufrufe sind durch typsichere
// AnimManager-Methoden ersetzt. Kein String-basierter Zugriff mehr.
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
            npc.IsAiming = false;
            npc.IsLaserActive = false;
            npc.LockedTargetPosition = null;

            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (npc.CanShoot())
                return new Aiming();

            if (npc.IsInShootingRange() && !npc.HasLineOfSight())
                return null;

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
            npc.IsAiming = false;
            npc.IsLaserActive = false;

            npc.AnimManager?.PlayWalk();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (!npc.CanReachPlayer)
            {
                npc.StopMovement();
                return new Idle();
            }

            if (npc.CanShoot())
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
            // Walk-Animation wird vom nächsten State überschrieben
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
            npc.IsAiming = true;
            npc.IsLaserActive = true;

            npc.AnimManager?.PlayAim();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            if (!npc.HasLineOfSight())
                return new Idle();

            if (!npc.IsInShootingRange())
                return new Idle();

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
            npc.IsAiming = true;
            npc.IsLaserActive = true;

            npc.AnimManager?.PlayFiringStance();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            // Dash-Lock: Position einfrieren wenn Spieler zu dashen beginnt
            if (npc.LockedTargetPosition == null && npc.IsPlayerDashing)
            {
                npc.LockedTargetPosition = npc.TargetPosition;
            }

            npc.RotateTowardPosition(npc.EffectiveTargetPosition);

            if (!npc.HasLineOfSight())
                return new Reloading();

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
            // Firing-Animation wird vom nächsten State überschrieben
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
            npc.IsAiming = false;
            npc.IsLaserActive = false;
            npc.LockedTargetPosition = null;

            npc.AnimManager?.PlayReload();
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
            npc.IsAiming = false;
            npc.IsLaserActive = false;

            npc.AnimManager?.PlayStunnedFromCombat();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc) => null;
    }
}
