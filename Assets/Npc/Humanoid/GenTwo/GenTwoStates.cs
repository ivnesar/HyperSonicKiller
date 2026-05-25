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
            npc.gentwoLaserPointer?.ClearInterceptMode();
            npc.AnimManager?.PlayIdle();
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            // Reagiere nur wenn der Spieler dasht, in Range ist UND Sichtlinie besteht
            if (!npc.IsPlayerInAttackDash) return null;
            if (!npc.IsPlayerInRange) return null;
            if (!npc.HasLineOfSightToPlayer()) return null;

            // Versuche Intercept zu berechnen — wenn valide, starte Charge
            if (npc.TryCalculateIntercept())
            {
                return new Charging();
            }

            // Kein valider Intercept gefunden → bleibe idle
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // CHARGING - Warning phase, NOT cancellable (except stun/death)
    //
    // GenTwo hat einen validen Intercept-Punkt berechnet und lädt auf.
    // Der Charge wird NICHT abgebrochen, egal was der Spieler tut.
    // Nur Stun oder Tod unterbrechen den Charge.
    // ─────────────────────────────────────────────────────────────────────
    public class Charging : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Charging";
        public override int StateID => 1;

        public override void Enter(GenTwoNpc npc)
        {
            // Timer für Charge-Phase starten
            npc.SetUnscaledTimer(npc.ChargeDuration);

            // Audio & Animation
            npc.PlayChargeSound();
            npc.AnimManager?.PlayCharge();

            // Laser aktivieren und auf Intercept-Punkt zeigen
            npc.IsLaserActive = true;
            npc.SetAimProgressPublic(0f);

            if (npc.HasValidIntercept && npc.gentwoLaserPointer != null)
            {
                npc.gentwoLaserPointer.SetInterceptMode(npc.VisualInterceptPoint);
            }

            Debug.Log($"[GenTwo] {npc.name}: Charging — impact in " +
                      $"{npc.InterceptArrivalTime:F2}s, charge: {npc.ChargeDuration:F2}s, " +
                      $"flight: {npc.InterceptFlightTime:F2}s");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            // Drehe GenTwo zum Spieler (visuelles Feedback, ändert NICHT die Dash-Richtung)
            npc.RotateTowardTargetUnscaled();

            // Aim-Progress für Laser-Visualisierung aktualisieren
            float progress = npc.GetUnscaledTimerProgress(npc.ChargeDuration);
            npc.SetAimProgressPublic(progress);

            // Charge abgelaufen → Dash starten
            if (npc.UpdateUnscaledTimer())
            {
                return new Dashing();
            }

            // KEIN Abbruch — Charge läuft durch bis er fertig ist
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // DASHING - Flying toward intercept point
    //
    // GenTwo ist während des Dashes unangreifbar (handled in GenTwoNpc).
    // Er fliegt auf den einmalig berechneten mathematischen Intercept zu.
    // Surface-Kontakt stoppt ihn erst, nachdem der Intercept erreicht/überschritten wurde.
    // Die Dash-Richtung wurde einmalig in TryCalculateIntercept() berechnet und ändert sich nicht mehr.
    // ─────────────────────────────────────────────────────────────────────
    public class Dashing : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Dashing";
        public override int StateID => 2;

        public override void Enter(GenTwoNpc npc)
        {
            if (!npc.HasValidIntercept)
            {
                // Sollte nicht passieren, aber Safety-Check
                Debug.LogWarning($"[GenTwo] {npc.name}: Entered Dashing without valid intercept!");
                return;
            }

            // Dash vorbereiten
            npc.StartDash();
            npc.FaceDirection(npc.DashDirection);
            npc.PlayDashSound();

            // Animation
            npc.AnimManager?.PlayDashStart();

            // Laser: Progress 1 = Dash-Phase (exakte Flugbahn)
            npc.IsLaserActive = true;
            npc.SetAimProgressPublic(1f);

            Debug.Log($"[GenTwo] {npc.name}: DASH started! Direction: {npc.DashDirection}");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (!npc.HasValidIntercept)
            {
                // Safety: Falls ohne validen Intercept hier gelandet
                return new Idle();
            }

            // Bewegung + Kollisionsprüfung (Surface + Spieler)
            bool hitSurface = npc.ProcessDashMovement();

            if (hitSurface)
            {
                return new Recovery();
            }

            // Kein Re-Targeting — GenTwo fliegt bis Intercept + Surface-Stop/Failsafe
            return null;
        }

        public override void Exit(GenTwoNpc npc)
        {
            // Laser und Aim aufräumen
            npc.AnimManager?.PlayLanding();
            npc.IsLaserActive = false;
            npc.ResetAimProgressPublic();
            npc.gentwoLaserPointer?.ClearInterceptMode();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // RECOVERY - Stuck to surface, waiting before returning to Idle
    // ─────────────────────────────────────────────────────────────────────
    public class Recovery : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Recovery";
        public override int StateID => 3;

        public override void Enter(GenTwoNpc npc)
        {
            npc.SetUnscaledTimer(npc.RecoveryDuration);
            Debug.Log($"[GenTwo] {npc.name}: Recovering for {npc.RecoveryDuration}s " +
                      $"(OnWall: {npc.IsOnWall})");
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc)
        {
            if (npc.UpdateUnscaledTimer())
            {
                return new Idle();
            }

            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // STUNNED - External stun (sword hit, etc.)
    //
    // Entered via NpcBase.ApplyStun() → OnStunStart() → ChangeState(Stunned).
    // Exited via NpcBase.EndStun() → OnStunEnd() → ChangeState(Idle).
    // GenTwo can be stunned during Idle, Charging, and Recovery.
    // GenTwo CANNOT be stunned during Dashing (handled in damage overrides).
    // ─────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<GenTwoNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 4;

        public override void Enter(GenTwoNpc npc)
        {
            npc.ClearInterceptData();
            npc.IsLaserActive = false;
            npc.ResetAimProgressPublic();
            npc.gentwoLaserPointer?.ClearInterceptMode();
        }

        public override INpcState<GenTwoNpc> Update(GenTwoNpc npc) => null;
    }
}
