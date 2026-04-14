using UnityEngine;

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
            npc.IsLaserActive = false;
            npc.ResetAimProgressPublic();
            npc.LaserPointer?.ClearInterceptMode();
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

            // Intercept-Punkt EINMALIG berechnen — bleibt für den ganzen Angriff fix.
            // Auch bei ungültigem Punkt wird weiter geladen (Abbruch erst bei Dash-Start).
            bool hasPoint = npc.PreCalculateIntercept();

            npc.IsLaserActive = true;
            npc.SetAimProgressPublic(0f);

            // Laser und Impact Sphere nur aktivieren wenn ein gültiger Punkt existiert
            if (hasPoint && npc.LaserPointer != null)
            {
                npc.LaserPointer.SetInterceptMode(npc.LastInterceptPoint);
            }
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            npc.RotateTowardTargetUnscaled();

            // Nur abbrechen wenn der Spieler aufhört zu dashen, out of range oder LOS verloren
            if (!npc.IsPlayerDashing || !npc.IsPlayerInRange || !npc.HasLineOfSightToPlayer())
            {
                return new Idle();
            }

            float progress = npc.GetUnscaledTimerProgress(npc.ChargeDuration);
            npc.SetAimProgressPublic(progress);

            if (npc.UpdateUnscaledTimer())
            {
                return new Dashing();
            }

            return null;
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
            // Richtung zum fixen Intercept-Punkt berechnen (+ Wall-Check)
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

            npc.AnimManager?.PlayDashStart();

            // Progress = 1 triggert die Dash-Phase im Laser (exakte Flugbahn)
            npc.IsLaserActive = true;
            npc.SetAimProgressPublic(1f);

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
            npc.AnimManager?.PlayLanding();
            npc.IsLaserActive = false;
            npc.ResetAimProgressPublic();
            npc.LaserPointer?.ClearInterceptMode();
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
            if (npc.UpdateUnscaledTimer()) return new Idle();
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
            npc.IsLaserActive = false;
            npc.ResetAimProgressPublic();
            npc.LaserPointer?.ClearInterceptMode();
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc) => null;
    }
}