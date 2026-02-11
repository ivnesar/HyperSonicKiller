# Anti-Dash Drone - Setup Instructions

## Files to Add
- `AntiDashDroneNpc.cs` → `Assets/Npc/AntiDashDrone/`
- `AntiDashDroneStates.cs` → `Assets/Npc/AntiDashDrone/`

## Required Change in NpcBase.cs (1 line)

Add `AntiDashDrone` to the `NpcType` enum:

```csharp
public enum NpcType
{
    Soldier,
    Defender,
    GenOne,
    GenTwo,
    AntiDashDrone    // ← Add this line
}
```

## Replaces Old Script

This replaces the deprecated `MineNPC.cs` in `Assets/Npc/DashDrone/`.
The old script used the outdated `INpcInteraction` interface and did not inherit from `NpcBase`.
You can safely delete `MineNPC.cs` after switching to this.

## Unity Scene Setup

### 1. Create the Drone GameObject

```
AntiDashDrone (Empty GameObject)
├── Model (your drone mesh/prefab — optional)
└── Billboard (Quad)
```

**On the root "AntiDashDrone" GameObject:**
- Add `AntiDashDroneNpc` component
- Add `AudioSource` (optional, for sounds)
- **Do NOT add NavMeshAgent** (it gets disabled anyway)
- **Do NOT add NpcRagdollController** (drone explodes, no ragdoll)

### 2. Setup the Billboard (Child Quad)

1. Create a child: **3D Object → Quad**
2. Name it `Billboard`
3. Set local position to `(0, 0, 0)` (centered on drone)
4. Assign a material with:
   - Your transparent warning texture
   - Shader: `Unlit/Transparent` or similar
   - Rendering mode: Transparent / Fade
5. Drag this Quad into the `Billboard Transform` field on `AntiDashDroneNpc`

> The billboard automatically scales to match `effectRadius * 2` and always faces the camera.
> It is only visible during the **Idle** state (hidden when stunned or dead).

### 3. Configure Inspector Values

| Field | Default | Description |
|-------|---------|-------------|
| **Effect Radius** | 8 | Radius of the no-dash zone (meters) |
| **Dash Cancel Delay** | 0.1 | Unscaled seconds before an active dash is cancelled |
| **Billboard Transform** | — | Drag the Billboard child Quad here |
| **Explosion Effect Prefab** | — | Optional: particle effect spawned on death |
| **Max Health** | 100 | From NpcBase (adjust as needed) |
| **Destroy Delay** | 1 | Seconds before GameObject is destroyed after death |

### 4. Ignored NpcBase Fields

These inherited fields are **not used** by the Anti-Dash Drone but still appear in the Inspector:
- `Behavior Mode` — drone is always stationary
- `Move Speed` / `Stopping Distance` — no movement
- `Max Rotation Speed` — no rotation
- `Use Ragdoll On Death` — drone explodes instead

Just leave them at defaults, they have no effect.

## How It Works

```
[Idle] ←──────────────────────────── [Stunned]
  │      (stun ends)                    ▲
  │                                     │
  │ Anti-dash zone active          (sword hit/stun)
  │ Billboard visible
  │ Player enters radius:
  │   → New dashes blocked
  │   → Active dashes cancelled
  │     after 0.1s (unscaled)
  │ Player exits radius:
  │   → Dashes re-enabled
  │
  └──── Health <= 0 ────▶ [Dead] (explosion + destroy)
```

### Zone Behavior Details

1. **Player enters radius:** `PlayerDash.SetDashEnabled(false)` — no new dashes possible
2. **Player already dashing when entering:** Dash is cancelled after `dashCancelDelay` (unscaled time)
3. **Player exits radius:** `PlayerDash.SetDashEnabled(true)` — dashes re-enabled
4. **Drone stunned:** Zone disabled, dashes re-enabled, billboard hidden
5. **Drone destroyed:** Zone disabled, dashes re-enabled, explosion spawned

### Important: Unscaled Time

The dash cancel delay uses `Time.unscaledDeltaTime` because the player sets
`Time.timeScale = 0.1f` during dashes. Without unscaled time, the 0.1s delay
would take 1 real second.

### Safety

- `OnDestroy()` always re-enables player dash (prevents permanent dash lock on drone destruction)
- Zone is explicitly disabled on stun and death transitions
