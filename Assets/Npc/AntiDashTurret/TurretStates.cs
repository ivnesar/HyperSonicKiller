using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// TURRET STATES
// ════════════════════════════════════════════════════════════════════════════
//
// Alle States arbeiten mit Unscaled Time über TurretNpc-Hilfsmethoden.
//
// WICHTIG — Unscaled Time:
//   - SetUnscaledStateTimer() / UpdateUnscaledStateTimer() statt SetStateTimer()
//   - RotateTowardTargetUnscaled() statt RotateTowardTarget()
//   - StartAimTrackingUnscaled() statt StartAimTracking()
//
// FLOW:
//   Idle → Charging → Firing → Idle (Schleife)
//               ↓ (Spieler verlässt Dash/Range/LOS)
//             Idle
//
//   Stunned → Idle (nach Stun-Ende)
//   Death   → Sofortige Zerstörung
//
// ════════════════════════════════════════════════════════════════════════════

namespace TurretStates
{
    // ─────────────────────────────────────────────────────────────────────
    // IDLE
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<TurretNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(TurretNpc npc)
        {
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.ResetAimProgressUnscaled();
        }

        public override INpcState<TurretNpc> Update(TurretNpc npc)
        {
            npc.RotateTowardTargetUnscaled();

            // Alle Voraussetzungen erfüllt? → Charge starten
            if (npc.CanEngagePlayer())
                return new Charging();

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // CHARGING
    // ─────────────────────────────────────────────────────────────────────
    public class Charging : NpcStateBase<TurretNpc>
    {
        public override string StateName => "Charging";
        public override int StateID => 1;

        public override void Enter(TurretNpc npc)
        {
            npc.SetUnscaledStateTimer(npc.ChargeDuration);
            npc.StartAimTrackingUnscaled(npc.ChargeDuration);
            npc.IsAimActive = false; // Turret hat kein AimIK
            npc.IsLaserActive = true;

            npc.PlayChargeSound();
        }

        public override INpcState<TurretNpc> Update(TurretNpc npc)
        {
            npc.RotateTowardTargetUnscaled();

            // Abbruch: Spieler nicht mehr im Dash, außer Range oder keine Sichtlinie
            if (!npc.CanEngagePlayer())
                return new Idle();

            // Timer abgelaufen → Feuern
            if (npc.UpdateUnscaledStateTimer())
                return new Firing();

            return null;
        }

        public override void Exit(TurretNpc npc)
        {
            // Laser-Pointer wird vom nächsten State gesteuert
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FIRING
    // ─────────────────────────────────────────────────────────────────────
    public class Firing : NpcStateBase<TurretNpc>
    {
        public override string StateName => "Firing";
        public override int StateID => 2;

        private bool hasFired;

        public override void Enter(TurretNpc npc)
        {
            npc.IsLaserActive = false; // Laser-Pointer aus während dem Schuss
            npc.SetAimProgress(1f);    // Eingelockt
            hasFired = false;
        }

        public override INpcState<TurretNpc> Update(TurretNpc npc)
        {
            if (!hasFired)
            {
                npc.FireLaser();
                hasFired = true;
            }

            // Sofort zurück zu Idle nach dem Schuss
            return new Idle();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // STUNNED
    // ─────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<TurretNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 3;

        public override void Enter(TurretNpc npc)
        {
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.ResetAimProgressUnscaled();
        }

        // Update gibt immer null zurück — Stun-Ende wird von TurretNpc.HandleStunnedUnscaled() gehandelt.
        public override INpcState<TurretNpc> Update(TurretNpc npc) => null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // DEATH
    // ─────────────────────────────────────────────────────────────────────
    public class Death : NpcStateBase<TurretNpc>
    {
        public override string StateName => "Death";
        public override int StateID => 4;

        public override void Enter(TurretNpc npc)
        {
            npc.IsAimActive = false;
            npc.IsLaserActive = false;
            npc.ResetAimProgressUnscaled();

            // Sofort zerstören
            Object.Destroy(npc.gameObject);
        }

        public override INpcState<TurretNpc> Update(TurretNpc npc) => null;
    }
}
