using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SNIPER STATES
// ════════════════════════════════════════════════════════════════════════════
//
// AIM-IK:
// - States setzen npc.IsAimActive (von NpcBase) um AimIK ein-/auszuschalten.
// - NpcBase leitet den Wert automatisch an den AimController weiter.
//
// AIM PROGRESS:
// - Aiming.Enter(): StartAimTracking() → Wiggle beginnt bei maxRadius
// - Firing.Enter(): SetAimProgress(1) → eingelockt
// - Idle/MovingToRange/Reloading.Enter(): ResetAimProgress() → Progress = 0
//
// Idle → MovingToRange → Aiming → Firing → Reloading → zurück
//
// ════════════════════════════════════════════════════════════════════════════

namespace SniperStates
{
    // ─────────────────────────────────────────────────────────────────────
    // IDLE
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<SniperNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(SniperNpc npc)
        {
            npc.StopMovement();
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.LockedTargetPosition = null;
            npc.ResetAimProgress();

            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<SniperNpc> Update(SniperNpc npc)
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
    public class MovingToRange : NpcStateBase<SniperNpc>
    {
        public override string StateName => "Moving";
        public override int StateID => 1;

        public override void Enter(SniperNpc npc)
        {
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.ResetAimProgress();

            npc.AnimManager?.PlayWalk();
        }

        public override INpcState<SniperNpc> Update(SniperNpc npc)
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

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // AIMING (lange Aim-Phase für mehr Spieler-Warnung)
    // ─────────────────────────────────────────────────────────────────────
    public class Aiming : NpcStateBase<SniperNpc>
    {
        public override string StateName => "Aiming";
        public override int StateID => 2;

        public override void Enter(SniperNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.AimDuration);
            npc.StartAimTracking(npc.AimDuration);
            npc.IsAimActive = true;
            npc.IsLaserActive = true;

            npc.AnimManager?.PlayAim();
        }

        public override INpcState<SniperNpc> Update(SniperNpc npc)
        {
            npc.RotateTowardTarget();

            // Spieler dasht → Target verloren, Aiming abbrechen
            if (npc.IsPlayerDashing)
                return new Idle();

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
    // FIRING (Einzelschuss)
    // ─────────────────────────────────────────────────────────────────────
    public class Firing : NpcStateBase<SniperNpc>
    {
        public override string StateName => "Firing";
        public override int StateID => 3;

        private bool hasFired;

        public override void Enter(SniperNpc npc)
        {
            npc.StopMovement();
            npc.IsAimActive = true;
            npc.IsLaserActive = true;
            npc.SetAimProgress(1f);
            hasFired = false;
        }

        public override INpcState<SniperNpc> Update(SniperNpc npc)
        {
            // Dash-Lock: Position einfrieren wenn Spieler zu dashen beginnt
            if (npc.LockedTargetPosition == null && npc.IsPlayerDashing)
            {
                npc.LockedTargetPosition = npc.TargetPosition;
            }

            npc.RotateTowardPosition(npc.EffectiveTargetPosition);

            if (!hasFired)
            {
                npc.FireShot();
                hasFired = true;

                return new Reloading();
            }

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // RELOADING
    // ─────────────────────────────────────────────────────────────────────
    public class Reloading : NpcStateBase<SniperNpc>
    {
        public override string StateName => "Reloading";
        public override int StateID => 4;

        public override void Enter(SniperNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.ReloadDuration);
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.LockedTargetPosition = null;
            npc.ResetAimProgress();

            npc.AnimManager?.PlayReload();
            npc.PlayReloadSound();
        }

        public override INpcState<SniperNpc> Update(SniperNpc npc)
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
    public class Stunned : NpcStateBase<SniperNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 5;

        public override void Enter(SniperNpc npc)
        {
            npc.StopMovement();
            npc.IsAimActive = false;
            npc.IsLaserActive = false;

            npc.AnimManager?.PlayStunnedFromCombat();
        }

        public override INpcState<SniperNpc> Update(SniperNpc npc) => null;
    }
}
