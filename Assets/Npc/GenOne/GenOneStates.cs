using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GEN ONE STATES
// ════════════════════════════════════════════════════════════════════════════
//
// State Flow:
//
//   ┌───────────────────────────────────────────────────────────┐
//   │                                                           │
//   │    Idle ──(Spieler dasht + in Range + LOS)──▶ Dashing    │
//   │      ▲                                           │        │
//   │      │                                           │        │
//   │      │ Cooldown complete                         │        │
//   │      │                                           ▼        │
//   │      └────────────────────────────────── WallStuck       │
//   │                                            /Grounded      │
//   │                                                           │
//   │    Stunned ◀── nur aus Idle erreichbar                   │
//   │                                                           │
//   └───────────────────────────────────────────────────────────┘
//
// ════════════════════════════════════════════════════════════════════════════

namespace GenOneStates
{
    // ─────────────────────────────────────────────────────────────────────────
    // IDLE - Wartet auf Spieler-Dash
    // ─────────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<GenOneNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(GenOneNpc npc)
        {
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsDashing", false);
                npc.NpcAnimator.SetBool("IsStuckToWall", false);
            }
        }

        public override INpcState<GenOneNpc> Update(GenOneNpc npc)
        {
            // Zum Spieler drehen (optional, für visuelles Feedback)
            npc.RotateTowardTarget();

            // Prüfe ob Dash aktiviert werden soll
            if (npc.CanActivateDash())
            {
                return new Dashing();
            }

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DASHING - Fliegt zum Spieler mit Homing
    // ─────────────────────────────────────────────────────────────────────────
    public class Dashing : NpcStateBase<GenOneNpc>
    {
        public override string StateName => "Dashing";
        public override int StateID => 1;

        public override void Enter(GenOneNpc npc)
        {
            npc.StartDash();
        }

        public override INpcState<GenOneNpc> Update(GenOneNpc npc)
        {
            // Bewegung mit Homing (unscaled time)
            npc.UpdateDashMovement();

            // 1. Prüfe Spieler-Kollision
            if (npc.CheckPlayerCollision(out Collider playerCollider))
            {
                // Treffer verarbeiten (frontal vs seitlich/hinten)
                npc.ProcessPlayerHit();

                // GenOne fliegt weiter bis zur nächsten Oberfläche
                // (ProcessPlayerHit entscheidet ob Spieler oder GenOne Schaden nimmt)
            }

            // 2. Prüfe Oberflächen-Kollision
            if (npc.CheckSurfaceCollision(out RaycastHit hit))
            {
                npc.EndDash(hit);
                return new WallStuck();
            }

            return null;
        }

        public override void Exit(GenOneNpc npc)
        {
            // Falls Dash aus anderem Grund beendet wird
            if (npc.IsDashing)
            {
                npc.EndDashInAir();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WALLSTUCK - An Wand oder Boden, wartet auf Cooldown
    // ─────────────────────────────────────────────────────────────────────────
    public class WallStuck : NpcStateBase<GenOneNpc>
    {
        public override string StateName => "WallStuck";
        public override int StateID => 2;

        public override void Enter(GenOneNpc npc)
        {
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsStuckToWall", npc.IsStuckToWall);
            }
        }

        public override INpcState<GenOneNpc> Update(GenOneNpc npc)
        {
            // Warte auf Cooldown
            if (npc.IsCooldownComplete)
            {
                // Cooldown vorbei - zurück zu Idle
                npc.Unstick();
                return new Idle();
            }

            // Zum Spieler drehen während Wartezeit
            npc.RotateTowardTarget();

            return null;
        }

        public override void Exit(GenOneNpc npc)
        {
            npc.Unstick();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STUNNED - Durch Schwert oder andere Stun-Quelle
    // ─────────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<GenOneNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 3;

        public override void Enter(GenOneNpc npc)
        {
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsStunned", true);
                npc.NpcAnimator.SetBool("IsDashing", false);
                npc.NpcAnimator.SetBool("IsStuckToWall", false);
            }
        }

        public override INpcState<GenOneNpc> Update(GenOneNpc npc)
        {
            // Stun wird von NpcBase.HandleStunned() verwaltet
            // State wechselt automatisch über OnStunEnd()
            return null;
        }

        public override void Exit(GenOneNpc npc)
        {
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsStunned", false);
            }
        }
    }
}
