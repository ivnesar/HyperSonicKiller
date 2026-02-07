using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SOLDIER STATES
// ════════════════════════════════════════════════════════════════════════════
//
// Idle → MovingToRange → Aiming → Firing → Reloading → zurück
//
// Line-of-Sight wird geprüft in:
// - Aiming: Wechselt nur zu Firing wenn LOS frei
// - Firing: Bei LOS-Verlust → sofort Reloading (Salve abgebrochen)
//
// IsAiming wird gesetzt in:
// - Aiming.Enter() → true
// - Firing.Enter() → true (bleibt aktiv)
// - Alle anderen States → false
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
            npc.LockedTargetPosition = null;  // Sicherheit: Lock immer aufheben außerhalb Firing
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            // Kann schießen = in Reichweite UND freie Sicht
            if (npc.CanShoot())
                return new Aiming();

            // Nur in Reichweite aber keine Sicht → warten
            if (npc.IsInShootingRange() && !npc.HasLineOfSight())
                return null;

            // Außerhalb Reichweite → bewegen
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

            // Kann schießen → Aiming
            if (npc.CanShoot())
            {
                npc.StopMovement();
                return new Aiming();
            }

            // Bewegungslogik
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
            npc.IsAiming = true;  // Bone-Rotation aktivieren
            
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetTrigger("Aim");
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardTarget();

            // LOS verloren während Zielen → zurück zu Idle
            if (!npc.HasLineOfSight())
                return new Idle();

            // Außerhalb Reichweite → Idle (wird dann zu Moving wechseln)
            if (!npc.IsInShootingRange())
                return new Idle();

            // Timer abgelaufen → Feuern
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
            npc.IsAiming = true;  // Bone-Rotation bleibt aktiv
            
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsFiring", true);
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            // ── Dash-Lock: Position einfrieren wenn Spieler zu dashen beginnt ──
            if (npc.LockedTargetPosition == null && npc.IsPlayerDashing)
            {
                npc.LockedTargetPosition = npc.TargetPosition;
            }

            // Zur effektiven Zielposition drehen (gelockt oder live)
            npc.RotateTowardPosition(npc.EffectiveTargetPosition);

            // LOS verloren → Salve abbrechen, sofort nachladen
            if (!npc.HasLineOfSight())
                return new Reloading();

            // Schuss abfeuern
            if (Time.time >= npc.NextShotTime && npc.ShotsFiredInSalvo < npc.ShotsPerSalvo)
            {
                npc.FireShot();
                npc.ShotsFiredInSalvo++;
                npc.NextShotTime = Time.time + npc.TimeBetweenShots;
            }

            // Salve komplett → Nachladen
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
            npc.IsAiming = false;  // Bone-Rotation deaktivieren beim Nachladen
            npc.LockedTargetPosition = null;  // Dash-Lock aufheben → Spieler wieder verfolgen
            
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
            npc.IsAiming = false;  // Bone-Rotation deaktivieren
            
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsFiring", false);
                npc.NpcAnimator.SetBool("IsMoving", false);
            }
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc) => null;
    }
}
