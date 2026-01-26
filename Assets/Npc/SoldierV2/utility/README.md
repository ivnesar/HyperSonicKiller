# NPC Enemy Behavior Architecture

A clean, extensible enemy AI system for Unity using enum-based state machines.

## Architecture Overview

```
                    ┌─────────────────────────────────────┐
                    │         NpcBase (abstract)          │
                    │  • CharacterController movement     │
                    │  • INpcInteraction implementation   │
                    │  • Health & stun system             │
                    │  • Detection & line-of-sight        │
                    │  • Debug visualization              │
                    └──────────────┬──────────────────────┘
                                   │
            ┌──────────────────────┴──────────────────────┐
            │                                             │
   ┌────────▼────────┐                          ┌────────▼────────┐
   │   SoldierNpc    │                          │   DefenderNpc   │
   │                 │                          │                 │
   │  States:        │                          │  States:        │
   │  • Idle         │                          │  • Idle         │
   │  • MovingToRange│                          │  • MovingToProtect
   │  • Aiming       │                          │  • Guarding     │
   │  • Firing       │                          │  • Blocking     │
   │  • Reloading    │                          │  • Countering   │
   │  • Stunned      │                          │  • Stunned      │
   └─────────────────┘                          └─────────────────┘
```

## Files

| File | Description |
|------|-------------|
| `NpcBase.cs` | Abstract base class with shared functionality |
| `SoldierNpc.cs` | Ranged enemy - shoots salvos at player |
| `DefenderNpc.cs` | Protective enemy - blocks attacks, counters |
| `NpcManager.cs` | Singleton that tracks all NPCs for queries |
| `NpcConfiguration.cs` | ScriptableObject for tuning NPC stats |
| `INpcInteraction.cs` | Interfaces for combat interaction |
| `NpcSpawner.cs` | Utility for spawning/testing |
| `CombatIntegrationExample.cs` | Integration guide with player systems |

## Setup Instructions

### 1. Create NPC Prefabs

Both enemy types use the same base setup:

1. Create a new GameObject
2. Add components:
   - `CharacterController`
   - `SoldierNpc` OR `DefenderNpc`
   - `Animator` (on child with model)
   - `AudioSource` (optional)

3. Configure the inspector:
   - Assign `Player Transform` (or leave empty for auto-find via "Player" tag)
   - Set `Line Of Sight Mask` to layers that block vision
   - Adjust combat parameters as needed

4. Save as prefab

### 2. Scene Setup

1. **Player**: Ensure player has the "Player" tag
2. **NpcManager**: Will auto-create, or add empty GameObject with `NpcManager` component
3. **Spawner** (optional): Add `NpcSpawner` and assign prefabs

### 3. Integrate with Player Combat

The key integration point is in your `SwordCombatSystem`. When detecting a melee hit:

```csharp
private void OnMeleeHit(Collider hitCollider)
{
    // Check if defender can block
    if (hitCollider.TryGetComponent<DefenderNpc>(out var defender))
    {
        if (defender.TryBlockAttack())
        {
            // Attack was blocked!
            return;
        }
    }
    
    // Normal damage
    if (hitCollider.TryGetComponent<INpcInteraction>(out var npc))
    {
        npc.OnMeeleDamage(damage);
    }
}
```

See `CombatIntegrationExample.cs` for more detailed integration patterns.

## Behavior Details

### Soldier Behavior Loop

```
[Idle] → (player detected) → [MovingToRange]
                                    ↓
                              (in range + LOS)
                                    ↓
                               [Aiming] ← telegraph for player
                                    ↓
                               [Firing] → fires salvo
                                    ↓
                              [Reloading]
                                    ↓
                                 (loop)
```

**Key Parameters:**
- `preferredShootingRange`: Ideal engagement distance
- `minShootingRange`: Too close, will back up
- `shotsPerSalvo`: Bullets before reload
- `baseAccuracy`: Hit chance (1.0 = perfect)

### Defender Behavior Loop

```
[Idle] → (soldier found) → [MovingToProtect]
                                  ↓
                          (in position)
                                  ↓
                            [Guarding] ←─────────┐
                                  ↓              │
                          (attack incoming)      │
                                  ↓              │
                            [Blocking]           │
                                  ↓              │
                          (attack blocked)       │
                                  ↓              │
                           [Countering] ─────────┘
```

**Key Parameters:**
- `protectDistance`: How far in front of soldier to stand
- `blockAngle`: Frontal arc that can block (degrees)
- `perfectBlockWindow`: Timing for perfect block bonus
- `counterDamage`: Damage dealt by counter-attack

## Extending the System

### Adding a New Enemy Type

1. Create a new script inheriting from `NpcBase`:

```csharp
public class SniperNpc : NpcBase
{
    public enum SniperState { Idle, FindingPosition, Aiming, Firing, Relocating, Stunned }
    
    private SniperState currentState;
    
    protected override void OnStart() { /* init */ }
    protected override void UpdateBehavior() { /* state machine */ }
    protected override void OnStunEnd() { /* recover */ }
    public override string GetCurrentStateName() => currentState.ToString();
    public override NpcType GetNpcType() => NpcType.Sniper; // Add to enum
}
```

2. Add the new type to `NpcType` enum in `NpcBase.cs`
3. Update `NpcManager` to track the new type if needed

### Using ScriptableObject Configuration

1. Create configuration: Right-click → Create → Game → NPC Configuration
2. Tune values in inspector
3. Add to NPC:

```csharp
[SerializeField] private NpcConfiguration config;

protected override void OnStart()
{
    if (config != null)
    {
        maxHealth = config.maxHealth;
        moveSpeed = config.moveSpeed;
        // etc.
    }
}
```

## Animation Integration

Expected animator parameters (create as needed):

| Parameter | Type | Usage |
|-----------|------|-------|
| `MoveSpeed` | Float | Blend tree for walk/run |
| `IsMoving` | Bool | Moving state |
| `IsGuarding` | Bool | Defender guard stance |
| `IsBlocking` | Bool | Active block |
| `IsStunned` | Bool | Stunned state |
| `IsFiring` | Bool | Firing salvo |
| `Hit` | Trigger | Damage reaction |
| `Die` | Trigger | Death animation |
| `Aim` | Trigger | Start aiming |
| `Fire` | Trigger | Single shot |
| `Reload` | Trigger | Reload animation |
| `Block` | Trigger | Block start |
| `Counter` | Trigger | Counter-attack |

## Debug Features

- **Debug Info**: Enable `showDebugInfo` to see state labels above NPCs
- **Gizmos**: Select NPC to see detection range, shooting ranges, block angles
- **Spawner**: Press 1/2/3 to spawn Soldier/Defender/Pair during play

## Tips

1. **Stun Duration**: The thrown sword stuns for 3 seconds by default. Defenders can't block while stunned.

2. **Defender Priority**: Defenders protect the soldier closest to the player. Kill soldiers to leave defenders without purpose.

3. **Line of Sight**: NPCs check LOS every 0.15s. Adjust `VISIBILITY_CHECK_INTERVAL` if needed.

4. **Layer Masks**: Set `lineOfSightMask` to include level geometry but exclude triggers/effects.
