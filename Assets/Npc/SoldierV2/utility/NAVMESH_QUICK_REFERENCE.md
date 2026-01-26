# NavMesh Quick Reference

## Key Code Changes At-a-Glance

### Component Requirement
```csharp
// OLD
[RequireComponent(typeof(CharacterController))]

// NEW
[RequireComponent(typeof(NavMeshAgent))]
```

### Component Declaration
```csharp
// OLD
protected CharacterController characterController;
protected float verticalVelocity;
protected float gravity = -20f;

// NEW
protected NavMeshAgent navAgent;
// No gravity needed - NavMesh handles vertical movement
```

### Initialization
```csharp
// OLD
characterController = GetComponent<CharacterController>();

// NEW
navAgent = GetComponent<NavMeshAgent>();
navAgent.speed = moveSpeed;
navAgent.angularSpeed = rotationSpeed * 50f;
navAgent.stoppingDistance = stoppingDistance;
```

### Movement
```csharp
// OLD
Vector3 direction = (target - transform.position).normalized;
Vector3 move = direction * moveSpeed * Time.deltaTime;
characterController.Move(move);

// NEW
navAgent.SetDestination(target);
navAgent.speed = moveSpeed;
```

### Stopping
```csharp
// OLD
// Just don't call Move()

// NEW
navAgent.isStopped = true;
navAgent.ResetPath();
```

### Checking if Reached Destination
```csharp
// OLD
if (Vector3.Distance(transform.position, target) < threshold)

// NEW
if (!navAgent.pathPending && 
    navAgent.remainingDistance <= navAgent.stoppingDistance &&
    (!navAgent.hasPath || navAgent.velocity.sqrMagnitude < 0.01f))
```

### Gravity
```csharp
// OLD
void ApplyGravity()
{
    if (characterController.isGrounded)
        verticalVelocity = -2f;
    else
        verticalVelocity += gravity * Time.deltaTime;
    characterController.Move(Vector3.up * verticalVelocity * Time.deltaTime);
}

// NEW
// Not needed! NavMesh handles this automatically
```

### Disabling on Death
```csharp
// OLD
characterController.enabled = false;

// NEW
navAgent.enabled = false;
```

### Rotation Control
```csharp
// Automatic (default)
navAgent.updateRotation = true;

// Manual control
navAgent.updateRotation = false;
RotateToward(target.position); // Use base class helper
```

## Common Patterns

### Move and Stop Pattern
```csharp
// Start moving
MoveToward(targetPosition);

// Stop when reached
if (HasReachedDestination())
{
    StopMovement();
}
```

### Repositioning Check Pattern
```csharp
if (Time.time >= nextCheckTime)
{
    nextCheckTime = Time.time + checkInterval;
    
    if (ShouldReposition())
    {
        MoveToward(newPosition);
    }
}
```

### Animator Update Pattern
```csharp
// Automatic in base class UpdateAnimator()
float speed = navAgent.velocity.magnitude / moveSpeed;
animator.SetFloat("MoveSpeed", speed);
```

## Inspector Setup Checklist

### Scene Setup
- [ ] Ground marked as Navigation Static
- [ ] NavMesh baked (blue overlay visible)
- [ ] NPCs spawn on NavMesh surface

### NPC Prefab
- [ ] Remove CharacterController component
- [ ] Add NavMeshAgent component
- [ ] Update scripts to new versions
- [ ] Test in play mode

### NavMeshAgent Settings
```
Speed: Set by script
Angular Speed: Set by script (typically 500)
Acceleration: 8
Stopping Distance: 0.5
Auto Braking: ✓
Auto Repath: ✓
Obstacle Avoidance Quality: 3
Priority: 50
```

## Troubleshooting Quick Fixes

| Problem | Quick Fix |
|---------|-----------|
| Falls through floor | Check NavMesh exists at spawn point |
| Doesn't move | Verify `isStopped = false` and destination on NavMesh |
| Stuck on obstacles | Mark obstacle as Navigation Static and rebake |
| Weird rotation | Try `SetAutoRotation(false)` for manual control |
| Jittery movement | Increase `stoppingDistance` to 0.5-1.0 |

## Performance Optimization

```csharp
// ❌ DON'T: Call every frame
void Update()
{
    navAgent.SetDestination(player.position);
}

// ✅ DO: Call periodically
void Update()
{
    if (Time.time >= nextUpdateTime)
    {
        nextUpdateTime = Time.time + updateInterval;
        navAgent.SetDestination(player.position);
    }
}
```

## Remember

1. **NavMesh must be baked** before NPCs can navigate
2. **SetDestination only when needed**, not every frame
3. **Check pathPending** before using remainingDistance
4. **Obstacles need NavMeshObstacle** component to be avoided
5. **Dead NPCs** should have `navAgent.enabled = false`
