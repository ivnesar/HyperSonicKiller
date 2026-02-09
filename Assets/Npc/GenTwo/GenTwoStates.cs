using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GENTWO STATES
// ════════════════════════════════════════════════════════════════════════════
//
// Idle → Charging → Dashing → Recovery → Idle
//
// State flow:
// - Idle: Dormant. Does NOT rotate toward player. Waits for player to
//         dash within detection range. Detection is 360° (occlusion only).
// - Charging: Player is dashing AND in range. GenTwo charges for ~1s
//             (visual warning). If player stops dashing during charge,
//             returns to Idle.
// - Dashing: GenTwo flies toward the calculated intercept point.
//            Uses segmented raycasting for anti-tunneling.
//            Ends when hitting a wall/floor.
//            Damages player ONLY if player is in Dashing state.
// - Recovery: GenTwo is stuck to surface. Waits before returning to Idle.
// - Stunned: External stun (sword hit etc). Handled by NpcBase.
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

            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsCharging", false);
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            // No rotation — GenTwo is dormant until player dashes
            // Detection is 360° (occlusion-based, not directional)

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

            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsCharging", true);
                npc.NpcAnimator.SetTrigger("Charge");
            }
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            // Keep rotating toward the player during charge
            npc.RotateTowardTargetUnscaled();

            // If player stops dashing during our charge → abort
            if (!npc.IsPlayerDashing)
            {
                return new Idle();
            }

            // If player leaves detection range during charge → abort
            if (!npc.IsPlayerInRange)
            {
                return new Idle();
            }

            // If we lose line of sight during charge → abort
            if (!npc.HasLineOfSightToPlayer())
            {
                return new Idle();
            }

            // Timer done → calculate intercept and dash!
            if (npc.UpdateUnscaledTimer())
            {
                return new Dashing();
            }

            return null;
        }

        public override void Exit(GenTwoNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsCharging", false);
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
            // Calculate intercept direction ONCE - no adjustments after this
            Vector3 interceptDir = npc.CalculateInterceptDirection();

            // If intercept is blocked by a wall → abort immediately
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

            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsDashing", true);
                npc.NpcAnimator.SetTrigger("Dash");
            }

            Debug.Log($"[GenTwo] {npc.name}: Dash started! Direction: {interceptDir}");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            // If dash was aborted in Enter (no valid path), go back to Idle
            if (abortDash)
            {
                return new Idle();
            }

            // ProcessDashMovement returns true when GenTwo hits a surface
            bool hitSurface = npc.ProcessDashMovement();

            if (hitSurface)
            {
                return new Recovery();
            }

            return null;
        }

        public override void Exit(GenTwoNpc npc)
        {
            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetBool("IsDashing", false);
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

            if (npc.NpcAnimator != null)
                npc.NpcAnimator.SetTrigger("Impact");

            Debug.Log($"[GenTwo] {npc.name}: Recovering for {npc.RecoveryDuration}s");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            // Stuck in place - no movement, no rotation
            // Just wait for timer (unscaled so it works during slow-mo)

            if (npc.UpdateUnscaledTimer())
            {
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
            if (npc.NpcAnimator != null)
            {
                npc.NpcAnimator.SetBool("IsDashing", false);
                npc.NpcAnimator.SetBool("IsCharging", false);
            }
        }

        // Stun handling is done by NpcBase.HandleStunned()
        // This state just exists so the state machine reflects it
        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc) => null;
    }
}
