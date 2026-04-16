using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SOLDIER STATES (Animancer Version)
// ════════════════════════════════════════════════════════════════════════════
//
// Alle animator.SetTrigger/SetBool Aufrufe sind durch typsichere
// AnimManager-Methoden ersetzt. Kein String-basierter Zugriff mehr.
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
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.LockedTargetPosition = null;
            npc.ResetAimProgress();

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
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.ResetAimProgress();

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
            npc.StartAimTracking(npc.AimDuration);
            npc.IsAimActive = true;
            npc.IsLaserActive = true;

            npc.AnimManager?.PlayAim();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            // ── Early-Fire-Reaktion auf Dash ──
            // Wenn der Spieler zu dashen beginnt, entscheidet die aktuelle Ausrichtung:
            //   - Ausgerichtet → sofort schießen (dramatisch, verfehlt meist wegen Dash-Lock)
            //   - Nicht ausgerichtet → Zielvorgang abbrechen, zurück zu Idle
            if (npc.IsPlayerDashing)
            {
                if (npc.IsAimedAtPlayer())
                {
                    // Position sofort einfrieren — Kugeln fliegen an letzter bekannter Stelle vorbei
                    npc.LockedTargetPosition = npc.TargetPosition;
                    return new Firing();
                }
                else
                {
                    return new Idle();
                }
            }

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
            npc.IsAimActive = true;
            npc.IsLaserActive = true;
            npc.SetAimProgress(1f); // Eingelockt — kein Wiggle während dem Feuern

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
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.LockedTargetPosition = null;
            npc.ResetAimProgress();

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
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            // ResetAimProgress wird bereits von NpcBase.ApplyStun() aufgerufen

            npc.AnimManager?.PlayStunnedFromCombat();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc) => null;
    }
}
