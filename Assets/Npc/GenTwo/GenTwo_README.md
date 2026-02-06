# GenTwo NPC - Setup Instructions

## Files to Add
- `GenTwoNpc.cs` - The NPC class
- `GenTwoStates.cs` - State machine states

## Required Change in NpcBase.cs (1 line)

Add `GenTwo` to the `NpcType` enum in `NpcBase.cs`:

```csharp
public enum NpcType
{
    Soldier,
    Defender,
    GenOne,
    GenTwo    // ← Add this line
}
```

## Unity Scene Setup

1. **Create a GenTwo GameObject:**
   - Create an Empty GameObject (or use a character model)
   - Add the `GenTwoNpc` component
   - Add `NpcRagdollController` (optional, for death ragdoll)
   - Add `AudioSource` (optional, for sounds)
   - Add an `Animator` on the model child (optional)
   - **Do NOT add a NavMeshAgent** (GenTwo disables it anyway)

2. **Configure Inspector Values:**
   - `Detection Range`: 30 (default) — how far GenTwo reacts to player dashes
   - `Charge Duration`: 1.0 — warning time before dashing (runs in unscaled time)
   - `Dash Speed`: 25 — base speed
   - `Dash Speed Multiplier`: 1.3 — only active while player is dashing
   - `Player Dash Speed`: 20 — **must match PlayerDash.dashSpeed!**
   - `Player Dash Max Distance`: 15 — **must match PlayerDash.dashMaxDistance!**
   - `Player Hit Radius`: 1.2 — collision radius for hitting the player
   - `Surface Layer Mask`: Set to your "Solid" / wall/floor layer
   - `Recovery Duration`: 1.5 — time stuck after impact (runs in unscaled time)

3. **Important Layer Setup:**
   - `Surface Layer Mask` must include all walls and floors GenTwo can collide with
   - The Player must have the "Player" tag (already used by NpcBase)

## How It Works

**IMPORTANT — Timing:** GenTwo uses `Time.unscaledDeltaTime` for ALL timing (charge, dash, recovery).
This is critical because the player sets `Time.timeScale = 0.1f` during their dash.
Without unscaled time, GenTwo's 1s charge would take 10 real seconds.

```
[Idle] ──(player dashes in range)──> [Charging] ──(1s)──> [Dashing] ──(hits wall)──> [Recovery] ──(1.5s)──> [Idle]
                                         │                     │
                                    (player stops          (passes through
                                     dashing)               player: damage
                                         │                 only if player
                                         v                 is dashing)
                                      [Idle]

Any state can be interrupted by stun → [Stunned] → [Idle]
```

## Animator Parameters (optional)
If using animations, set up these parameters:
- `IsCharging` (Bool) - True during charge phase
- `IsDashing` (Bool) - True during dash
- `Charge` (Trigger) - Fired when charge starts
- `Dash` (Trigger) - Fired when dash starts  
- `Impact` (Trigger) - Fired when hitting a surface
- `IsStunned` (Bool) - Managed by NpcBase
- `Hit` (Trigger) - Managed by NpcBase
