using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DEFENDER STATES - Alle Zustände des Defender NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// States:
// - Idle: Wartet, kein Pfad - alle "Warte-Situationen"
// - Approaching: Läuft auf Spieler zu
// - InPosition: Nah beim Spieler, wartet
// - Stunned: Bewegungsunfähig
//
// ════════════════════════════════════════════════════════════════════════════

namespace DefenderStates
{
    // ────────────────────────────────────────────────────────────────────────
    // IDLE - Sammel-State für alle Warte-Situationen
    // ────────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(DefenderNpc npc) => npc.StopMovement();

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Immer: Langsam zur letzten bekannten Position drehen
            if (npc.CanSeePlayer && npc.PlayerTransform != null)
                npc.RotateToward(npc.PlayerTransform.position, 0.5f);
            else
                npc.RotateTowardLastKnownPosition(0.3f);

            float distance = npc.GetDistanceToPlayer();

            // Spieler erkannt + Pfad vorhanden + Pursuing-Modus → Bewegen
            if (npc.CanDetectPlayer && npc.HasValidPathToPlayer)
            {
                if (npc.CurrentBehaviorMode == BehaviorMode.Pursuing)
                {
                    // Reaktionsverzögerung beachten
                    if (npc.CanSeePlayer || npc.CanReactToPlayerLoss)
                    {
                        return new Approaching();
                    }
                }
            }

            // Sonst: Weiter warten
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // APPROACHING - Läuft auf den Spieler zu
    // ────────────────────────────────────────────────────────────────────────
    public class Approaching : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Approaching";
        public override int StateID => 1;

        public override void Enter(DefenderNpc npc)
        {
            npc.NpcAnimator?.SetBool("IsMoving", true);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            if (npc.PlayerTransform == null) return new Idle();

            float distance = npc.GetDistanceToPlayer();

            // Nah genug → In Position bleiben
            if (distance <= npc.ApproachDistance)
            {
                npc.StopMovement();
                return new InPosition();
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

            // Bewegung zur aktuellen Position (wenn sichtbar) oder letzten bekannten Position
            Vector3 target = npc.CanSeePlayer ? npc.PlayerTransform.position : npc.LastKnownPlayerPosition;
            npc.MoveToward(target);
            npc.RotateToward(target);

            return null;
        }

        public override void Exit(DefenderNpc npc)
        {
            npc.NpcAnimator?.SetBool("IsMoving", false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // IN POSITION - Steht nah beim Spieler, wartet
    // ────────────────────────────────────────────────────────────────────────
    public class InPosition : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "InPosition";
        public override int StateID => 2;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.NpcAnimator?.SetBool("IsGuarding", true);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            if (npc.PlayerTransform == null) return new Idle();

            // Zur letzten bekannten Position drehen
            npc.RotateTowardLastKnownPosition(2f);

            float distance = npc.GetDistanceToPlayer();

            // Spieler zu weit weg?
            if (distance > npc.ReengageDistance)
            {
                // Reaktionsverzögerung beachten
                if (!npc.CanSeePlayer && !npc.CanReactToPlayerLoss)
                    return null; // Noch warten

                // Zurück zu Idle (übernimmt Pfad-Check etc.)
                return new Idle();
            }

            return null;
        }

        public override void Exit(DefenderNpc npc)
        {
            npc.NpcAnimator?.SetBool("IsGuarding", false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // STUNNED - Bewegungsunfähig
    // ────────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 3;

        public override void Enter(DefenderNpc npc)
        {
            npc.StopMovement();
            npc.NpcAnimator?.SetBool("IsGuarding", false);
            npc.NpcAnimator?.SetBool("IsMoving", false);
        }

        public override INpcState<DefenderNpc> Update(DefenderNpc npc) => null;
    }
}
