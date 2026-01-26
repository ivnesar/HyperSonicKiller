using UnityEngine;

/// <summary>
/// INTEGRATION EXAMPLE
/// 
/// This file shows how to integrate the Defender's blocking system with your existing
/// SwordCombatSystem. You don't need to use this file directly - it's a reference
/// for modifying your existing code.
/// </summary>

/*
================================================================================
OPTION 1: Modify SwordCombatSystem.cs - Add to your attack hit detection
================================================================================

In your SwordCombatSystem, when you detect a hit on an enemy during an attack,
add this check BEFORE dealing damage:

```csharp
private void ProcessMeleeHit(Collider hitCollider)
{
    // First, check if this is a defender that might block
    if (hitCollider.TryGetComponent<DefenderNpc>(out var defender))
    {
        // Attempt to block - if blocked, don't deal damage
        if (defender.TryBlockAttack())
        {
            Debug.Log("Attack was blocked by defender!");
            
            // Optional: Play blocked sound/effect
            // Optional: Apply small stagger to player
            // Optional: Give player a small window to dodge the counter
            
            return; // Don't deal damage
        }
    }
    
    // Normal damage handling - attack wasn't blocked
    if (hitCollider.TryGetComponent<INpcInteraction>(out var npc))
    {
        npc.OnMeeleDamage(meleeDamage);
    }
}
```

================================================================================
OPTION 2: Use the IBlockCapable interface for cleaner code
================================================================================

```csharp
private void ProcessMeleeHit(Collider hitCollider)
{
    // Check for block capability first
    if (hitCollider.TryGetComponent<IBlockCapable>(out var blocker))
    {
        if (blocker.TryBlockAttack())
        {
            OnAttackBlocked?.Invoke(); // Event for feedback
            return;
        }
    }
    
    // Deal damage
    if (hitCollider.TryGetComponent<INpcInteraction>(out var npc))
    {
        npc.OnMeeleDamage(meleeDamage);
    }
}
```

================================================================================
OPTION 3: Event-based integration (most decoupled)
================================================================================

Add these events to SwordCombatSystem:

```csharp
public class SwordCombatSystem : MonoBehaviour
{
    // Events
    public static event System.Action<Vector3> OnPlayerAttackStarted;
    public static event System.Action OnPlayerAttackEnded;
    
    // In your attack start method:
    private void StartAttack()
    {
        isAttacking = true;
        OnPlayerAttackStarted?.Invoke(transform.position);
        // ... rest of attack logic
    }
    
    // In your attack end method:
    private void EndAttack()
    {
        isAttacking = false;
        OnPlayerAttackEnded?.Invoke();
    }
    
    // Public property for NPCs to check
    public bool IsAttacking => isAttacking;
    public Vector3 AttackDirection => transform.forward;
}
```

Then in DefenderNpc, subscribe to these events:

```csharp
private void OnEnable()
{
    SwordCombatSystem.OnPlayerAttackStarted += OnPlayerAttackDetected;
}

private void OnDisable()
{
    SwordCombatSystem.OnPlayerAttackStarted -= OnPlayerAttackDetected;
}

private void OnPlayerAttackDetected(Vector3 attackOrigin)
{
    // Check if we should react to this attack
    float distance = Vector3.Distance(transform.position, attackOrigin);
    if (distance <= blockDetectionRange && currentState == DefenderState.Guarding)
    {
        TransitionToState(DefenderState.Blocking);
    }
}
```

================================================================================
ADDING PLAYER DAMAGE METHOD
================================================================================

Add this to your FPSPlayerController:

```csharp
public class FPSPlayerController : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    private int currentHealth;
    
    public event System.Action<int, int> OnHealthChanged; // current, max
    public event System.Action OnPlayerDied;
    
    private void Start()
    {
        currentHealth = maxHealth;
    }
    
    public void TakeDamage(int amount)
    {
        if (isDead) return;
        
        currentHealth -= amount;
        currentHealth = Mathf.Max(0, currentHealth);
        
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        
        // Screen shake, damage flash, etc.
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    public void ApplyKnockback(Vector3 force)
    {
        // If using CharacterController:
        // Store knockback velocity and apply over time
        
        // If using Rigidbody:
        // rb.AddForce(force, ForceMode.Impulse);
    }
    
    private void Die()
    {
        isDead = true;
        OnPlayerDied?.Invoke();
        // Handle death
    }
}
```

================================================================================
DEFENDER BLOCK FEEDBACK TO PLAYER
================================================================================

For juice, you might want the player to feel the block. Add to SwordCombatSystem:

```csharp
public void OnAttackBlocked()
{
    // Screen shake
    CameraShake.Shake(0.1f, 0.05f);
    
    // Weapon recoil
    StartCoroutine(WeaponRecoilCoroutine());
    
    // Sound
    AudioSource.PlayClipAtPoint(blockedSound, transform.position);
    
    // Brief hitstop
    Time.timeScale = 0.1f;
    StartCoroutine(ResetTimeScale(0.05f));
    
    // Optional: Small stagger that leaves player vulnerable to counter
    canAttack = false;
    Invoke(nameof(EnableAttack), 0.3f);
}

private IEnumerator ResetTimeScale(float delay)
{
    yield return new WaitForSecondsRealtime(delay);
    Time.timeScale = 1f;
}
```

*/

/// <summary>
/// Example component showing full integration. 
/// Attach this to an empty GameObject in your scene to see the wiring.
/// </summary>
public class CombatIntegrationExample : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SwordCombatSystem playerCombat;
    [SerializeField] private FPSPlayerController playerController;

    private void OnValidate()
    {
        if (playerCombat == null)
            playerCombat = FindFirstObjectByType<SwordCombatSystem>();
        if (playerController == null)
            playerController = FindFirstObjectByType<FPSPlayerController>();
    }

    // This is just a documentation component - the actual integration
    // happens in your SwordCombatSystem and FPSPlayerController scripts.
}
