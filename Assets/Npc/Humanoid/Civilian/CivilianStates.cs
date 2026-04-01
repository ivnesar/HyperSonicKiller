using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// CIVILIAN STATES
// ════════════════════════════════════════════════════════════════════════════
//
// STATES:
//   SetDressing → Loopt die Set-Dressing-Animation. Nur für SetDresser-Modus.
//   Idle        → Wartet am Fluchtpunkt. Prüft ob Spieler in detectionDistance kommt.
//   Fleeing     → Rennt panisch vom Spieler weg zu einem zufälligen NavMesh-Punkt.
//   Fallen      → NPC ist hingefallen (Spieler zu nah). Permanenter Endzustand.
//   Stunned     → Von NpcBase gesteuert (Sword-Embed etc.)
//
// FALLEN (gilt für beide Verhaltensmodi):
//   - Wird von CivilianNpc.UpdateBehavior() getriggert wenn DistanceToTarget <= fallTriggerDistance
//   - Spielt Fall-Animation, dann FallIdle-Loop
//   - NPC dreht sich smooth zum Spieler während der Fall-Animation
//   - Kann nicht verlassen werden, aber NPC kann noch getötet werden (IEnemy bleibt aktiv)
//
// ════════════════════════════════════════════════════════════════════════════

namespace CivilianStates
{
    // ─────────────────────────────────────────────────────────────────────
    // SET DRESSING — Loopt eine Animation (SetDresser-Modus)
    // ─────────────────────────────────────────────────────────────────────
    public class SetDressing : NpcStateBase<CivilianNpc>
    {
        public override string StateName => "SetDressing";
        public override int StateID => -1;

        public override void Enter(CivilianNpc npc)
        {
            npc.AnimManager?.PlaySetDressing();
        }

        public override INpcState<CivilianNpc> Update(CivilianNpc npc)
        {
            // Fallen-Check läuft in CivilianNpc.UpdateBehavior()
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // IDLE — Wartet am Fluchtpunkt
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<CivilianNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(CivilianNpc npc)
        {
            npc.StopMovement();
            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<CivilianNpc> Update(CivilianNpc npc)
        {
            // Spieler erkannt UND innerhalb detection distance → fliehen
            if (npc.IsPlayerDetected())
                return new Fleeing();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FLEEING — Rennt panisch vom Spieler weg
    // ─────────────────────────────────────────────────────────────────────
    public class Fleeing : NpcStateBase<CivilianNpc>
    {
        public override string StateName => "Fleeing";
        public override int StateID => 1;

        /// <summary>Timer bis zum nächsten chaotischen Richtungswechsel.</summary>
        private float directionChangeTimer;

        public override void Enter(CivilianNpc npc)
        {
            npc.AnimManager?.PlayPanicRun();
            npc.PlayPanicSoundImmediate();

            FindNewFleeTarget(npc);
            ResetDirectionChangeTimer(npc);
        }

        public override INpcState<CivilianNpc> Update(CivilianNpc npc)
        {
            // Rotation: in Bewegungsrichtung drehen
            npc.RotateTowardMovementDirection();

            // Ziel erreicht?
            if (npc.HasReachedDestination())
            {
                // Nur in Idle wechseln wenn der NPC weit genug vom Spieler weg ist.
                if (npc.DistanceToLastKnownPosition() >= npc.FleeDistance)
                {
                    return new Idle();
                }
                else
                {
                    // Noch zu nah am Spieler → neuen Fluchtpunkt suchen
                    FindNewFleeTarget(npc);
                    ResetDirectionChangeTimer(npc);
                }
            }

            // Chaotischer Richtungswechsel
            directionChangeTimer -= Time.deltaTime;
            if (directionChangeTimer <= 0f)
            {
                FindNewFleeTarget(npc);
                ResetDirectionChangeTimer(npc);
            }

            // Gelegentlich Panik-Sound
            npc.TryPlayPanicSound();

            return null;
        }

        private void FindNewFleeTarget(CivilianNpc npc)
        {
            if (npc.TryFindFleePoint(out Vector3 point))
            {
                npc.MoveToFleePoint(point);
            }
        }

        private void ResetDirectionChangeTimer(CivilianNpc npc)
        {
            directionChangeTimer = Random.Range(
                npc.MinDirectionChangeInterval,
                npc.MaxDirectionChangeInterval
            );
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FALLEN — NPC ist hingefallen (permanenter Endzustand)
    // ─────────────────────────────────────────────────────────────────────
    public class Fallen : NpcStateBase<CivilianNpc>
    {
        public override string StateName => "Fallen";
        public override int StateID => 3;

        /// <summary>True sobald die Fall-Animation beendet ist und FallIdle läuft.</summary>
        private bool fallAnimationDone;

        public override void Enter(CivilianNpc npc)
        {
            npc.StopMovement();
            fallAnimationDone = false;

            // Fall-Animation starten (OneShot → callback wenn fertig)
            if (npc.AnimManager != null)
            {
                npc.AnimManager.PlayFall(() =>
                {
                    fallAnimationDone = true;
                    npc.AnimManager.PlayFallIdle();
                });
            }
            else
            {
                // Kein AnimManager → sofort als "fertig" markieren
                fallAnimationDone = true;
            }
        }

        public override INpcState<CivilianNpc> Update(CivilianNpc npc)
        {
            // Smooth zum Spieler drehen während der Fall-Animation
            if (!fallAnimationDone)
            {
                npc.RotateTowardPlayer();
            }

            // Permanenter Endzustand — kein Transition
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // STUNNED — Von NpcBase gesteuert
    // ─────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<CivilianNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 2;

        public override void Enter(CivilianNpc npc)
        {
            npc.StopMovement();
            npc.AnimManager?.PlayStunnedFromPanic();
        }

        public override INpcState<CivilianNpc> Update(CivilianNpc npc) => null;
    }
}
