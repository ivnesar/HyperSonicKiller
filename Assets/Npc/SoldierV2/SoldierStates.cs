using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SOLDIER STATES - Alle Zustände des Soldier NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// States:
// - Idle: Wartet, sucht, kein Pfad - alle "Warte-Situationen"
// - MovingToRange: Bewegt sich in Schussreichweite
// - Aiming: Zielt auf Spieler
// - Firing: Feuert Salve
// - Reloading: Lädt nach
// - Stunned: Bewegungsunfähig
//
// ════════════════════════════════════════════════════════════════════════════

namespace SoldierStates
{
    // ────────────────────────────────────────────────────────────────────────
    // IDLE - Sammel-State für alle Warte-Situationen
    // ────────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(SoldierNpc npc) => npc.StopMovement();

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            float distance = npc.GetDistanceToPlayer();
            bool inRange = distance >= npc.MinShootingRange && distance <= npc.MaxShootingRange;

            // Immer: Langsam zur letzten bekannten Position drehen
            if (npc.CanSeePlayer && npc.PlayerTransform != null)
                npc.RotateToward(npc.PlayerTransform.position, 0.5f);
            else
                npc.RotateTowardLastKnownPosition(0.3f);

            // Kann schießen? (Sicht + Reichweite, auch ohne Pfad)
            if (npc.CanSeePlayer && inRange)
            {
                return new Aiming();
            }

            // Spieler erkannt + Pfad vorhanden → Bewegen
            if (npc.CanDetectPlayer && npc.HasValidPathToPlayer)
            {
                // Reaktionsverzögerung beachten (wenn Spieler gerade aus Sicht verschwunden)
                if (npc.CanSeePlayer || npc.CanReactToPlayerLoss)
                {
                    return new MovingToRange();
                }
            }

            // Sonst: Weiter warten
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // MOVING TO RANGE - Bewegt sich in Schussreichweite
    // ────────────────────────────────────────────────────────────────────────
    public class MovingToRange : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "MovingToRange";
        public override int StateID => 1;

        public override void Enter(SoldierNpc npc) => npc.NextRepositionCheckTime = 0f;

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            if (npc.PlayerTransform == null) return new Idle();

            float distance = npc.GetDistanceToPlayer();

            // Rotation: Wenn sichtbar → zum Spieler, sonst → zur letzten bekannten Position
            if (npc.CanSeePlayer)
                npc.RotateToward(npc.PlayerTransform.position);
            else
                npc.RotateTowardLastKnownPosition();

            // Periodischer Check
            if (Time.time < npc.NextRepositionCheckTime) return null;
            npc.NextRepositionCheckTime = Time.time + npc.RepositionCheckInterval;

            bool inRange = distance >= npc.MinShootingRange && distance <= npc.MaxShootingRange;

            // In Reichweite mit Sicht → Zielen
            if (inRange && npc.CanSeePlayer)
            {
                npc.StopMovement();
                return new Aiming();
            }

            // Spieler verloren? (Kann letzte Position sehen aber Spieler nicht dort)
            if (npc.HasLostPlayer)
            {
                npc.StopMovement();
                return new Idle();
            }

            // Kein gültiger Pfad mehr → zurück zu Idle
            if (!npc.HasValidPathToPlayer)
            {
                npc.StopMovement();
                return new Idle();
            }

            // Stationär: Nur drehen, nicht bewegen
            if (npc.CurrentBehaviorMode == BehaviorMode.Stationary)
            {
                npc.StopMovement();

                if (npc.CanSeePlayer && distance <= npc.MaxShootingRange)
                    return new Aiming();

                return null;
            }

            // Pursuing: Bewegungslogik
            if (distance > npc.PreferredShootingRange || !npc.CanSeePlayer)
            {
                Vector3 target = npc.CanSeePlayer ? npc.PlayerTransform.position : npc.LastKnownPlayerPosition;
                npc.MoveToward(target);
            }
            else if (distance < npc.MinShootingRange)
            {
                Vector3 retreatTarget = npc.transform.position - npc.GetDirectionToPlayer() * 5f;
                npc.MoveToward(retreatTarget, 0.7f);
            }
            else
            {
                Vector3 strafeDir = Vector3.Cross(npc.GetDirectionToPlayer(), Vector3.up);
                if (Random.value > 0.8f) strafeDir = -strafeDir;
                npc.MoveToward(npc.transform.position + strafeDir * 3f, 0.8f);
            }

            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // AIMING - Zielt auf den Spieler
    // ────────────────────────────────────────────────────────────────────────
    public class Aiming : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Aiming";
        public override int StateID => 2;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.AimDuration);
            npc.NpcAnimator?.SetTrigger("Aim");
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardLastKnownPosition(2f);

            if (npc.UpdateStateTimer())
            {
                return new Firing();
            }

            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // FIRING - Feuert eine Salve ab
    // ────────────────────────────────────────────────────────────────────────
    public class Firing : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Firing";
        public override int StateID => 3;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            npc.ShotsFiredInSalvo = 0;
            npc.NextShotTime = 0f;
            npc.NpcAnimator?.SetBool("IsFiring", true);
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            // Rotation nur wenn Spieler sichtbar
            if (npc.CanSeePlayer && npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 2f);
            }

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
            npc.ShotsFiredInSalvo = 0;
            npc.NpcAnimator?.SetBool("IsFiring", false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // RELOADING - Lädt die Waffe nach
    // ────────────────────────────────────────────────────────────────────────
    public class Reloading : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Reloading";
        public override int StateID => 4;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            npc.SetStateTimer(npc.ReloadDuration);
            npc.NpcAnimator?.SetTrigger("Reload");
            npc.PlayReloadSound();
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            npc.RotateTowardLastKnownPosition(0.5f);

            if (npc.UpdateStateTimer())
            {
                float distance = npc.GetDistanceToPlayer();
                bool inRange = distance >= npc.MinShootingRange && distance <= npc.MaxShootingRange;

                // Direkt wieder zielen wenn möglich
                if (inRange && npc.CanSeePlayer)
                    return new Aiming();

                // Sonst zurück zu Idle (übernimmt alle Warte-Logik)
                return new Idle();
            }

            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // STUNNED - Bewegungsunfähig
    // ────────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 5;

        public override void Enter(SoldierNpc npc) => npc.StopMovement();

        public override INpcState<SoldierNpc> Update(SoldierNpc npc) => null;
    }
}
