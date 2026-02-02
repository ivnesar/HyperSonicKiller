using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DEFENDER STATES - Alle Zustände des Defender NPCs
// ════════════════════════════════════════════════════════════════════════════

namespace DefenderStates
{
    // ────────────────────────────────────────────────────────────────────────
    // IDLE - Wartet auf Spieler-Erkennung
    // ────────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<DefenderNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(DefenderNpc npc) => npc.StopMovement();

        public override INpcState<DefenderNpc> Update(DefenderNpc npc)
        {
            // Zum Spieler drehen wenn sichtbar
            if (npc.CanSeePlayer && npc.PlayerTransform != null)
                npc.RotateToward(npc.PlayerTransform.position, 0.5f);

            // Spieler erkannt → Verhalten abhängig vom Modus
            float distance = npc.GetDistanceToPlayer();

            if (npc.CanSeePlayer && distance <= npc.ApproachDistance * 5f)
            {
                if (npc.CurrentBehaviorMode == BehaviorMode.Pursuing)
                    return new Approaching();
                // Stationär: Nur drehen, im Idle bleiben
            }

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

            // Weiter auf Spieler zulaufen
            npc.MoveToward(npc.PlayerTransform.position);
            npc.RotateToward(npc.PlayerTransform.position);

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

            // Immer zum Spieler drehen
            npc.RotateToward(npc.PlayerTransform.position, 2f);

            float distance = npc.GetDistanceToPlayer();

            // Spieler zu weit weg → wieder verfolgen
            if (distance > npc.ReengageDistance)
            {
                if (npc.CurrentBehaviorMode == BehaviorMode.Pursuing)
                    return new Approaching();
                else
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