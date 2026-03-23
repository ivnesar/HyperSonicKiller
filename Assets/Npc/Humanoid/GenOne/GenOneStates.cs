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
//   │      │ Cooldown complete              ┌──────────┤        │
//   │      │                                │          │        │
//   │      │                                ▼          ▼        │
//   │      ├─────────────────────── WallStuck    Kein Endpunkt  │
//   │      │                        /Grounded    → zurück Idle  │
//   │      │                                                    │
//   │    Stunned ◀── nur aus Idle erreichbar                   │
//   │                                                           │
//   └───────────────────────────────────────────────────────────┘
//
// Dash-Endpunkt-Logik:
//   GenOne castet durch Spielerkopf (pos + up) zur Wand dahinter.
//   Kollision mit Oberflächen wird nur akzeptiert wenn sie HINTER
//   dem Spieler liegt. Kein Endpunkt = Dash wird abgebrochen.
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
    // DASHING - Fliegt zum Spieler mit Homing, stoppt an Wand hinter Spieler
    // ─────────────────────────────────────────────────────────────────────────
    public class Dashing : NpcStateBase<GenOneNpc>
    {
        public override string StateName => "Dashing";
        public override int StateID => 1;

        public override void Enter(GenOneNpc npc)
        {
            npc.StartDash();

            // Sofort-Check: Wenn schon beim Start kein Endpunkt → nicht dashen
            // (wird im ersten Update abgefangen)
        }

        public override INpcState<GenOneNpc> Update(GenOneNpc npc)
        {
            // ── 1. Bewegung mit Homing + Endpunkt-Neuberechnung ──
            npc.UpdateDashMovement();

            // ── 2. Fallback: Kein gültiger Endpunkt → Dash abbrechen ──
            if (!npc.HasDashEndpoint)
            {
                Debug.Log("[GenOneStates.Dashing] No valid endpoint - aborting dash!");
                npc.EndDashInAir();
                return new Idle();
            }

            // ── 3. Prüfe Spieler-Kollision (stoppt Dash NICHT) ──
            if (npc.CheckPlayerCollision(out Collider playerCollider))
            {
                npc.ProcessPlayerHit();
                // GenOne fliegt weiter bis zur Oberfläche hinter dem Spieler
            }

            // ── 4. Prüfe Oberflächen-Kollision (nur hinter Spieler) ──
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
