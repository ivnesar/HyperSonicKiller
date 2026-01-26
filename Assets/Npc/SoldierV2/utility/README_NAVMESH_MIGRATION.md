# NPC System - NavMesh Migration Guide

## Overview

This updated version of the NPC system replaces **CharacterController** with **NavMeshAgent** for movement. NavMesh provides superior pathfinding, obstacle avoidance, and more natural navigation around your game environment.

---

## What Changed?

### ✅ Updated Files
- **NpcBase.cs** - Base class now uses NavMeshAgent instead of CharacterController
- **SoldierNpc.cs** - Soldier movement updated to use NavMesh pathfinding
- **DefenderNpc.cs** - Defender positioning updated to use NavMesh navigation

### 📋 Unchanged Files
- **NpcConfiguration.cs** - No changes needed
- **NpcManager.cs** - No changes needed
- **NpcSpawner.cs** - No changes needed
- **CombatIntegrationExample.cs** - No changes needed

---

## Migration Steps

### Step 1: Setup NavMesh in Your Scene

1. **Bake NavMesh Surface**
   - Select your ground/floor objects
   - In the Navigation window (Window → AI → Navigation)
   - Mark objects as "Navigation Static"
   - Click "Bake" to generate the NavMesh

2. **Verify NavMesh Coverage**
   - Enable NavMesh display in Scene view
   - Ensure blue overlay covers walkable areas
   - Check that areas where NPCs spawn are covered

### Step 2: Update NPC Prefabs

For each NPC prefab (Soldier, Defender):

1. **Remove Old Component**
   ```
   Remove: CharacterController component
   ```

2. **Add New Component**
   ```
   Add: NavMeshAgent component
   ```

3. **Configure NavMeshAgent Settings**
   ```
   Agent Type: Humanoid (or create custom)
   Base Offset: 0
   Speed: Will be set by script (default: 4)
   Angular Speed: Will be set by script (default: 500)
   Acceleration: 8
   Stopping Distance: 0.5
   Auto Braking: true
   Auto Repath: true
   Obstacle Avoidance: Quality Level 3
   Priority: 50
   ```

### Step 3: Replace Scripts

1. **Delete old scripts** from your NPC prefabs
2. **Add new scripts** (same names, updated implementation)
3. **Reassign references** in the Inspector if needed

### Step 4: Test Movement

1. **Place NPCs in scene** on NavMesh surface
2. **Enter Play Mode**
3. **Verify NPCs**:
   - Navigate toward player
   - Avoid obstacles automatically
   - Find paths around complex geometry
   - Stop at appropriate distances

---

## Key Differences from CharacterController

### Movement Behavior

| CharacterController | NavMeshAgent |
|---------------------|--------------|
| Manual movement via `.Move()` | Automatic pathfinding via `.SetDestination()` |
| No obstacle avoidance | Built-in obstacle avoidance |
| Simple gravity handling | Follows NavMesh height automatically |
| Direct position control | Path-based movement |

### Code Changes

**Old (CharacterController):**
```csharp
Vector3 move = direction * moveSpeed * Time.deltaTime;
characterController.Move(move);
ApplyGravity(); // Manual gravity
```

**New (NavMeshAgent):**
```csharp
navAgent.SetDestination(targetPosition);
navAgent.speed = moveSpeed;
// Gravity handled by NavMesh
// Obstacle avoidance automatic
```

---

## New Helper Methods

### In NpcBase.cs

```csharp
// Movement
MoveToward(Vector3 targetPosition, float speedMultiplier = 1f)
StopMovement()
HasReachedDestination()
GetRemainingDistance()

// Rotation (optional - NavMesh can handle this)
RotateToward(Vector3 targetPosition, float speedMultiplier = 1f)
FaceTarget(Vector3 targetPosition)
SetAutoRotation(bool enabled)
```

### Usage Examples

**Moving to a position:**
```csharp
MoveToward(playerTransform.position);
```

**Checking if reached:**
```csharp
if (HasReachedDestination())
{
    TransitionToState(NewState);
}
```

**Stopping movement:**
```csharp
StopMovement(); // Stops and clears path
```

**Manual rotation control:**
```csharp
SetAutoRotation(false); // Disable NavMesh rotation
RotateToward(target.position, 2f); // Manual control
```

---

## Important NavMesh Concepts

### 1. NavMesh Agent States

- **isStopped**: Controls whether agent moves
  - `true` = stopped, won't follow path
  - `false` = active, follows path

- **hasPath**: Whether agent has a valid path
  - Check before using `remainingDistance`

- **pathPending**: Whether path is being calculated
  - Wait for this to be false before checking distance

### 2. Destination Checking

**Correct way to check if reached:**
```csharp
if (!navAgent.pathPending && 
    navAgent.remainingDistance <= navAgent.stoppingDistance &&
    (!navAgent.hasPath || navAgent.velocity.sqrMagnitude < 0.01f))
{
    // Reached destination
}
```

This is wrapped in `HasReachedDestination()` helper.

### 3. Performance Optimization

**Periodic Updates:**
```csharp
if (Time.time >= nextRepositionCheckTime)
{
    nextRepositionCheckTime = Time.time + repositionCheckInterval;
    // Update destination here
}
```

Don't call `SetDestination()` every frame - only when needed!

---

## Troubleshooting

### Problem: NPC falls through floor
**Solution:** 
- Ensure ground has NavMesh baked
- Check Base Offset on NavMeshAgent (usually 0)
- Verify NPC starts on NavMesh (blue area in Scene view)

### Problem: NPC doesn't move
**Check:**
- NavMesh exists in scene (blue overlay visible)
- NavMeshAgent component is enabled
- `navAgent.isStopped` is false
- Destination is on NavMesh
- Agent is on NavMesh when spawned

### Problem: NPC rotates strangely
**Solutions:**
- Use `SetAutoRotation(false)` for manual control
- Adjust `navAgent.angularSpeed`
- Use `RotateToward()` during states that need precise facing

### Problem: NPC gets stuck on obstacles
**Check:**
- Obstacle is marked as Navigation Static
- NavMesh Obstacle component on dynamic objects
- Carve option enabled on obstacles
- Agent radius in Navigation settings

### Problem: NPC takes weird paths
**Solutions:**
- Rebake NavMesh with better settings
- Adjust Agent Radius in Navigation window
- Check for NavMesh gaps (gray areas)
- Increase NavMesh resolution for tight spaces

---

## Advanced: Dynamic NavMesh Obstacles

For dynamic obstacles (doors, moving platforms):

1. **Add NavMeshObstacle component**
2. **Configure:**
   ```
   Shape: Box or Capsule
   Carve: true
   Move Threshold: 0.1
   Time to Stationary: 0.5
   Carve Only Stationary: true
   ```

3. **NPCs will automatically avoid** when pathfinding

---

## Performance Tips

1. **Limit SetDestination calls**
   - Use timers/intervals for updates
   - Don't call every frame

2. **Adjust Avoidance Quality**
   - Use lower quality for background NPCs
   - Reserve high quality for important enemies

3. **Use Stopping Distance**
   - Prevents constant micro-adjustments
   - Set to ~0.5 - 1.0 for humanoid characters

4. **Disable when not needed**
   - Disable NavMeshAgent when NPC is dead
   - Disable during cutscenes/scripted events

---

## Integration with Existing Systems

### Animator Integration

The base class automatically updates animator speed:

```csharp
protected void UpdateAnimator()
{
    float currentSpeed = navAgent.velocity.magnitude;
    float normalizedSpeed = currentSpeed / moveSpeed;
    animator.SetFloat("MoveSpeed", normalizedSpeed);
}
```

Your animator should have a "MoveSpeed" parameter (0-1 range).

### Stun System

NavMeshAgent is automatically stopped during stun:

```csharp
protected void ApplyStun(float duration)
{
    isStunned = true;
    navAgent.isStopped = true;
    navAgent.ResetPath();
}
```

### Combat System

See **CombatIntegrationExample.cs** - integration points remain the same!

---

## Configuration Tips

### Soldier Configuration
```
moveSpeed: 4-5 (patrols/advances)
rotationSpeed: 8-12 (smooth turning)
stoppingDistance: 0.5 (stops shooting range)
```

### Defender Configuration  
```
moveSpeed: 5-6 (needs to intercept quickly)
rotationSpeed: 10-15 (reactive turning)
stoppingDistance: 0.3 (precise positioning)
```

---

## Debug Visualization

The Gizmos now include NavMesh path visualization:

```csharp
// Shows active NavMesh path in green
if (navAgent.hasPath)
{
    Gizmos.DrawLine(corner[i], corner[i+1]);
}
```

**To view:**
1. Select NPC in Hierarchy
2. Ensure Gizmos enabled in Scene view
3. Green lines show current path
4. Yellow sphere shows detection range
5. Blue lines show FOV

---

## Next Steps

After migration:

1. ✅ Test basic movement
2. ✅ Test combat positioning
3. ✅ Test obstacle avoidance
4. ✅ Tune NavMesh settings for your level
5. ✅ Optimize NavMesh resolution
6. ✅ Add NavMesh obstacles to dynamic objects
7. ✅ Test with multiple NPCs

---

## Questions?

Common issues and solutions documented in "Troubleshooting" section above.

For complex navigation scenarios, consult Unity's NavMesh documentation:
https://docs.unity3d.com/Manual/nav-NavigationSystem.html

Happy coding! 🎮
