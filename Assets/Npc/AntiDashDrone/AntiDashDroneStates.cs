using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// ANTI-DASH DRONE STATES
// ════════════════════════════════════════════════════════════════════════════
//
// Idle → (stunned) → Stunned → (stun ends) → Idle
// Any state → Dead (on health <= 0)
//
// State flow:
// - Idle:    Anti-dash zone is active, billboard visible.
//            Checks player proximity and manages dash blocking.
// - Stunned: Zone disabled, billboard hidden. Handled by NpcBase stun system.
// - Dead:    Zone disabled, billboard hidden. Drone explodes.
//
// ════════════════════════════════════════════════════════════════════════════

namespace AntiDashDroneStates
{
    // ─────────────────────────────────────────────────────────────────────
    // IDLE - Zone active, monitoring player
    // ─────────────────────────────────────────────────────────────────────
    public class Idle : NpcStateBase<AntiDashDroneNpc>
    {
        public override string StateName => "Idle";
        public override int StateID => 0;

        public override void Enter(AntiDashDroneNpc npc)
        {
            npc.SetBillboardVisible(true);
        }

        public override INpcState<AntiDashDroneNpc> Update(AntiDashDroneNpc npc)
        {
            // No rotation — drone is stationary
            // No movement — drone hovers in place

            // Check player proximity and manage dash blocking
            npc.UpdateZoneCheck();

            return null;
        }

        public override void Exit(AntiDashDroneNpc npc)
        {
            // Disable zone when leaving Idle (entering Stunned or Dead)
            npc.DisableZone();
            npc.SetBillboardVisible(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // STUNNED - Zone disabled, waiting for stun to end
    // ─────────────────────────────────────────────────────────────────────
    public class Stunned : NpcStateBase<AntiDashDroneNpc>
    {
        public override string StateName => "Stunned";
        public override int StateID => 1;

        public override void Enter(AntiDashDroneNpc npc)
        {
            // Zone is already disabled by Idle.Exit()
        }

        // Stun handling is done by NpcBase.HandleStunned()
        // This state just exists so the state machine reflects it
        public override INpcState<AntiDashDroneNpc> Update(AntiDashDroneNpc npc) => null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // DEAD - Drone destroyed (entered via Die() override, not state machine)
    // ─────────────────────────────────────────────────────────────────────
    public class Dead : NpcStateBase<AntiDashDroneNpc>
    {
        public override string StateName => "Dead";
        public override int StateID => 2;

        // Die() handles everything (explosion, cleanup, destroy)
        // This state exists to prevent further state transitions
        public override INpcState<AntiDashDroneNpc> Update(AntiDashDroneNpc npc) => null;
    }
}
