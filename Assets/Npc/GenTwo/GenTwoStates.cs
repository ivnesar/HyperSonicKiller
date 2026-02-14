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
//         Has Ground/Wall animation variant via IsOnWall bool.
// - Charging: Player is dashing AND in range. GenTwo charges for ~1s
//             (visual warning). If player stops dashing during charge,
//             returns to Idle. Has Ground/Wall animation variant.
// - Dashing: GenTwo flies toward the calculated intercept point.
//            Uses segmented raycasting for anti-tunneling.
//            Ends when hitting a wall/floor.
//            Damages player ONLY if player is in Dashing state.
//            DashAttack animation triggered on player collision.
// - Recovery: GenTwo is stuck to surface. Waits before returning to Idle.
//             Has Ground/Wall animation variant (LandingGround/LandingWall).
// - Stunned: External stun (sword hit etc). Handled by NpcBase.
//            Has Ground/Wall animation variant.
//
// Animator Parameters used by states:
//   IsCharging   (Bool)    - Idle ⇄ Charge
//   DashStart    (Trigger) - Charge → StartDash
//   IsDashing    (Bool)    - StartDash → Dash loop
//   DashAttack   (Trigger) - Dash ⇄ DashAttack (set in ProcessDashMovement)
//   Land         (Trigger) - Dash → Landing
//   Stunned      (Trigger) - AnyState → Stunned
//   RecoveryDone (Trigger) - Landing/Stunned → Idle
//   IsOnWall     (Bool)    - switches Ground/Wall variants (set in DetermineWallOrGround)
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
            {
                npc.NpcAnimator.SetBool("IsCharging", false);
                npc.NpcAnimator.SetBool("IsDashing", false);
            }
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
            }
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            // Keep rotating toward the player during charge
            npc.RotateTowardTargetUnscaled();

            // If player stops dashing, leaves range, or LOS breaks → abort
            if (!npc.IsPlayerDashing || !npc.IsPlayerInRange || !npc.HasLineOfSightToPlayer())
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
                // DashStart trigger: Charge → StartDash transition
                // IsDashing bool: StartDash → Dash loop transition
                npc.NpcAnimator.SetTrigger("DashStart");
                npc.NpcAnimator.SetBool("IsDashing", true);
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
            {
                npc.NpcAnimator.SetBool("IsDashing", false);

                // Land trigger: Dash → LandingGround/LandingWall
                // (IsOnWall is already set by DetermineWallOrGround in ProcessDashMovement)
                npc.NpcAnimator.SetTrigger("Land");
            }
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
            // Stuck in place - no movement, no rotation
            // Just wait for timer (unscaled so it works during slow-mo)

            if (npc.UpdateUnscaledTimer())
            {
                // RecoveryDone trigger: Landing → IdleGround/IdleWall
                if (npc.NpcAnimator != null)
                    npc.NpcAnimator.SetTrigger("RecoveryDone");

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

                // Stunned trigger: AnyState → StunnedGround/StunnedWall
                npc.NpcAnimator.SetTrigger("Stunned");
            }
        }

        // Stun handling is done by NpcBase.HandleStunned()
        // This state just exists so the state machine reflects it
        // OnStunEnd() triggers RecoveryDone → Idle transition
        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc) => null;
    }
}
