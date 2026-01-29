using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SOLDIER STATES - Alle Zustände des Soldier NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// Jeder State ist eine eigene Klasse mit klarer Verantwortung:
// - Enter():  Setup wenn der State betreten wird
// - Update(): Logik pro Frame, gibt nächsten State zurück (oder null)
// - Exit():   Cleanup wenn der State verlassen wird
//
// ════════════════════════════════════════════════════════════════════════════

namespace SoldierStates
{
    // ────────────────────────────────────────────────────────────────────────
    // IDLE - Wartet auf Spieler-Erkennung
    // ────────────────────────────────────────────────────────────────────────
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
            float distance = npc.GetDistanceToPlayer();

            // Spieler erkannt? → Bewegung starten
            if (distance <= npc.DetectionRange && npc.CanSeePlayer)
            {
                return new MovingToRange();
            }

            return null; // Im Idle bleiben
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // MOVING TO RANGE - Bewegt sich in Schussreichweite
    // ────────────────────────────────────────────────────────────────────────
    public class MovingToRange : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "MovingToRange";
        public override int StateID => 1;

        public override void Enter(SoldierNpc npc)
        {
            npc.NextRepositionCheckTime = 0f;
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            if (npc.PlayerTransform == null) return null;

            float distance = npc.GetDistanceToPlayer();
            npc.RotateToward(npc.PlayerTransform.position);

            // Periodischer Check für Repositionierung
            if (Time.time < npc.NextRepositionCheckTime) return null;
            npc.NextRepositionCheckTime = Time.time + npc.RepositionCheckInterval;

            // Prüfe ob wir in guter Schussposition sind
            bool inRange = distance >= npc.MinShootingRange && 
                          distance <= npc.MaxShootingRange;

            if (inRange && npc.CanSeePlayer)
            {
                npc.StopMovement();
                return new Aiming();
            }

            // Bewegungslogik
            if (distance > npc.PreferredShootingRange || !npc.CanSeePlayer)
            {
                // Zu weit weg oder keine Sicht → Annähern
                npc.MoveToward(npc.PlayerTransform.position);
            }
            else if (distance < npc.MinShootingRange)
            {
                // Zu nah → Zurückweichen
                Vector3 retreatTarget = npc.transform.position - npc.GetDirectionToPlayer() * 5f;
                npc.MoveToward(retreatTarget, 0.7f);
            }
            else
            {
                // Gute Distanz aber keine Sicht → Seitlich bewegen
                Vector3 strafeDir = Vector3.Cross(npc.GetDirectionToPlayer(), Vector3.up);
                if (Random.value > 0.8f) strafeDir = -strafeDir;
                npc.MoveToward(npc.transform.position + strafeDir * 3f, 0.8f);
            }

            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // AIMING - Zielt auf den Spieler bevor geschossen wird
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
            // Weiter auf Spieler ausrichten
            if (npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 2f);
            }

            // Timer abgelaufen?
            if (npc.UpdateStateTimer())
            {
                if (npc.CanSeePlayer)
                {
                    return new Firing();
                }
                else
                {
                    // Sicht verloren → neu positionieren
                    return new MovingToRange();
                }
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
            // Weiter auf Spieler ausrichten (schneller als beim Aimen)
            if (npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 3f);
            }

            // Zeit für nächsten Schuss?
            if (Time.time >= npc.NextShotTime && npc.ShotsFiredInSalvo < npc.ShotsPerSalvo)
            {
                npc.FireShot();
                npc.ShotsFiredInSalvo++;
                npc.NextShotTime = Time.time + npc.TimeBetweenShots;
            }

            // Salve komplett?
            if (npc.ShotsFiredInSalvo >= npc.ShotsPerSalvo)
            {
                return new Reloading();
            }

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
            // Langsam auf Spieler ausrichten während Reload
            if (npc.PlayerTransform != null)
            {
                npc.RotateToward(npc.PlayerTransform.position, 0.5f);
            }

            // Timer abgelaufen?
            if (npc.UpdateStateTimer())
            {
                float distance = npc.GetDistanceToPlayer();
                bool inRange = distance >= npc.MinShootingRange && 
                              distance <= npc.MaxShootingRange;

                if (inRange && npc.CanSeePlayer)
                {
                    return new Aiming();
                }
                else
                {
                    return new MovingToRange();
                }
            }

            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // STUNNED - Bewegungsunfähig (durch Schwert getroffen)
    // ────────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<SoldierNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 5;

        public override void Enter(SoldierNpc npc)
        {
            npc.StopMovement();
            // Animator wird bereits in NpcBase gesetzt
        }

        public override INpcState<SoldierNpc> Update(SoldierNpc npc)
        {
            // Stun-Ende wird von NpcBase gehandhabt
            // NpcBase ruft OnStunEnd() auf, was ChangeState() aufruft
            return null;
        }
    }
}
