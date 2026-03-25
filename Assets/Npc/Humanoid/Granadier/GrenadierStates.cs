using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GRENADIER STATES (Animancer 2-Layer Version)
// ════════════════════════════════════════════════════════════════════════════
//
// AIM-IK:
// - States setzen npc.IsAimActive (von NpcBase) um AimIK ein-/auszuschalten.
// - NpcBase leitet den Wert automatisch an den AimController weiter.
//
// AIM PROGRESS:
// - Aiming.Enter(): StartAimTracking() → Wiggle beginnt bei maxRadius
// - Firing.Enter(): SetAimProgress(1) → Laser eingelockt, kein Wiggle
// - Idle/MovingToRange/Reloading.Enter(): ResetAimProgress() → Progress = 0
//
// Idle → MovingToRange → Aiming → Firing → Reloading → zurück
//
// ════════════════════════════════════════════════════════════════════════════

namespace GrenadierStates
{
    // ─────────────────────────────────────────────────────────────────────
    // IDLE
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<GrenadierNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(GrenadierNpc npc)
        {
            npc.StopMovement();
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.LockedTargetPosition = null;
            npc.ResetAimProgress();

            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<GrenadierNpc> Update(GrenadierNpc npc)
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
    public class MovingToRange : NpcStateBase<GrenadierNpc>
    {
        public override string StateName => "Moving";
        public override int StateID => 1;

        public override void Enter(GrenadierNpc npc)
        {
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.ResetAimProgress();

            npc.AnimManager?.PlayWalk();
        }

        public override INpcState<GrenadierNpc> Update(GrenadierNpc npc)
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
    }

    // ─────────────────────────────────────────────────────────────────────
    // AIMING
    // ─────────────────────────────────────────────────────────────────────
    public class Aiming : NpcStateBase<GrenadierNpc>
    {
        public override string StateName => "Aiming";
        public override int StateID => 2;

        public override void Enter(GrenadierNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.AimDuration);
            npc.StartAimTracking(npc.AimDuration);
            npc.IsAimActive = true;
            npc.IsLaserActive = true;

            npc.AnimManager?.PlayAim();
        }

        public override INpcState<GrenadierNpc> Update(GrenadierNpc npc)
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
    // FIRING (Magazin-basiert: 1 bis N Granaten)
    // ─────────────────────────────────────────────────────────────────────
    public class Firing : NpcStateBase<GrenadierNpc>
    {
        public override string StateName => "Firing";
        public override int StateID => 3;

        public override void Enter(GrenadierNpc npc)
        {
            npc.StopMovement();
            npc.ShotsFiredInMagazine = 0;
            npc.NextShotTime = 0f;
            npc.IsAimActive = true;
            npc.IsLaserActive = true;
            npc.SetAimProgress(1f);

            npc.AnimManager?.PlayFiringStance();
        }

        public override INpcState<GrenadierNpc> Update(GrenadierNpc npc)
        {
            // Dash-Lock: Position einfrieren wenn Spieler zu dashen beginnt
            if (npc.LockedTargetPosition == null && npc.IsPlayerDashing)
            {
                npc.LockedTargetPosition = npc.TargetPosition;
            }

            npc.RotateTowardPosition(npc.EffectiveTargetPosition);

            if (!npc.HasLineOfSight())
                return new Reloading();

            if (Time.time >= npc.NextShotTime && npc.ShotsFiredInMagazine < npc.MagazineSize)
            {
                npc.FireGrenade();
                npc.ShotsFiredInMagazine++;
                npc.NextShotTime = Time.time + npc.TimeBetweenShots;

                // Nach jedem Schuss: Zielposition zurücksetzen,
                // damit die nächste Granate die aktuelle Spielerposition nutzt.
                npc.LockedTargetPosition = null;
            }

            if (npc.ShotsFiredInMagazine >= npc.MagazineSize)
                return new Reloading();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // RELOADING
    // ─────────────────────────────────────────────────────────────────────
    public class Reloading : NpcStateBase<GrenadierNpc>
    {
        public override string StateName => "Reloading";
        public override int StateID => 4;

        public override void Enter(GrenadierNpc npc)
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

        public override INpcState<GrenadierNpc> Update(GrenadierNpc npc)
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
    public class Stunned : NpcStateBase<GrenadierNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 5;

        public override void Enter(GrenadierNpc npc)
        {
            npc.StopMovement();
            npc.IsAimActive = false;
            npc.IsLaserActive = false;

            npc.AnimManager?.PlayStunnedFromCombat();
        }

        public override INpcState<GrenadierNpc> Update(GrenadierNpc npc) => null;
    }
}
