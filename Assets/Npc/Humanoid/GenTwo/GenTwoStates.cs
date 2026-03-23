using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GENTWO STATES (Animancer Version)
// ════════════════════════════════════════════════════════════════════════════
//
// Alle animator.SetTrigger/SetBool Aufrufe sind durch typsichere
// AnimManager-Methoden ersetzt. Kein String-basierter Zugriff mehr.
//
// Ground/Wall-Varianten werden automatisch vom GenTwoAnimationManager
// anhand des isOnWall-Flags ausgewählt.
//
// Idle → Charging → Dashing → Recovery → Idle
//
// ════════════════════════════════════════════════════════════════════════════

namespace GenTwoStates
{
    // ─────────────────────────────────────────────────────────────────────
    // IDLE - Dormant, waiting for player to dash
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(GenTwoNpc npc)
        {
            npc.ClearInterceptData();
            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (npc.IsPlayerDashing && npc.IsPlayerInRange && npc.HasLineOfSightToPlayer())
            {
                return new Charging();
            }

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // CHARGING - Preparing to dash (warning phase for the player)
    // ─────────────────────────────────────────────────────────────────────
    public class Charging : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Charging";
        public override int StateID => 1;

        public override void Enter(GenTwoNpc npc)
        {
            npc.SetUnscaledTimer(npc.ChargeDuration);
            npc.PlayChargeSound();
            npc.AnimManager?.PlayCharge();
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            npc.RotateTowardTargetUnscaled();

            if (!npc.IsPlayerDashing || !npc.IsPlayerInRange || !npc.HasLineOfSightToPlayer())
            {
                return new Idle();
            }

            if (npc.UpdateUnscaledTimer())
            {
                return new Dashing();
            }

            return null;
        }

        public override void Exit(GenTwoNpc npc)
        {
            // Charge-Animation wird vom nächsten State überschrieben
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // DASHING - Flying toward intercept point like a projectile
    // ─────────────────────────────────────────────────────────────────────
    public class Dashing : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Dashing";
        public override int StateID => 2;

        private bool abortDash;

        public override void Enter(GenTwoNpc npc)
        {
            Vector3 interceptDir = npc.CalculateInterceptDirection();

            if (interceptDir == Vector3.zero)
            {
                abortDash = true;
                Debug.Log($"[GenTwo] {npc.name}: Dash aborted — no valid intercept path");
                return;
            }

            abortDash = false;
            npc.SetDashDirection(interceptDir);
            npc.FaceDirection(interceptDir);
            npc.PlayDashSound();

            // DashStart one-shot → transitions to Dash loop automatically
            npc.AnimManager?.PlayDashStart();

            Debug.Log($"[GenTwo] {npc.name}: Dash started! Direction: {interceptDir}");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (abortDash)
            {
                return new Idle();
            }

            bool hitSurface = npc.ProcessDashMovement();

            if (hitSurface)
            {
                return new Recovery();
            }

            return null;
        }

        public override void Exit(GenTwoNpc npc)
        {
            // Landing-Animation — DetermineWallOrGround() hat bereits
            // SetOnWall() aufgerufen, also wählt PlayLanding() die richtige Variante.
            npc.AnimManager?.PlayLanding();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // RECOVERY - Stuck to surface, waiting to reactivate
    // ─────────────────────────────────────────────────────────────────────
    public class Recovery : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Recovery";
        public override int StateID => 3;

        public override void Enter(GenTwoNpc npc)
        {
            npc.SetUnscaledTimer(npc.RecoveryDuration);

            Debug.Log($"[GenTwo] {npc.name}: Recovering for {npc.RecoveryDuration}s (OnWall: {npc.IsOnWall})");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (npc.UpdateUnscaledTimer())
            {
                npc.AnimManager?.PlayRecoveryDone();
                return new Idle();
            }

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // STUNNED - External stun (sword hit, etc.)
    // ─────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 4;

        public override void Enter(GenTwoNpc npc)
        {
            npc.AnimManager?.PlayStunned();
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc) => null;
    }
}
