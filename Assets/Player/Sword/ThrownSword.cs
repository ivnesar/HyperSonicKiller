using UnityEngine;
using System;

/// <summary>
/// Thrown sword projectile that flies like an arrow and sticks to surfaces.
/// Uses segmented spherecasting for reliable collision detection at high speeds.
/// 
/// UPDATED: Now deals damage when sword is recalled/removed from an embedded enemy.
/// UPDATED: Two removal modes:
///   - Normal recall (RMB): Uses OnSwordRemoved() - damage after stun
///   - Sword dash: Uses OnSwordDashRemoval() - damage IMMEDIATELY
/// </summary>
public class ThrownSword : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Fired when sword reaches the player after recall.</summary>
    public event Action OnReturnedToPlayer;

    /// <summary>Fired when sword hits something and sticks.</summary>
    public event Action<GameObject> OnHitTarget;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Collision")]
    [SerializeField] private float swordRadius = 0.1f;
    [SerializeField] private int maxSegmentsPerFrame = 4;

    [Header("Visuals")]
    [SerializeField] private TrailRenderer trail;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    // Flight state
    private bool isFlying;
    private bool isStuck;
    private bool isReturning;

    // Flight parameters (set on Initialize)
    private float postRemovalStunDuration;
    private int removalDamage;
    private Vector3 flyDirection;
    private float flySpeed;
    private float returnSpeed;
    private LayerMask hitMask;

    // Return target
    private Transform returnTarget;
    private float catchDistance = 1f;

    // Enemy tracking - uses IEnemy for full sword interaction
    private IEnemy embeddedEnemy;
    private GameObject hitObject;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public bool IsStuck => isStuck;
    public bool IsReturning => isReturning;
    public bool IsFlying => isFlying;
    public GameObject HitObject => hitObject;
    
    /// <summary>
    /// Returns the enemy the sword is currently embedded in (null if not in an enemy).
    /// </summary>
    public IEnemy EmbeddedEnemy => embeddedEnemy;
    
    /// <summary>
    /// Returns true if sword is currently embedded in an enemy.
    /// </summary>
    public bool IsEmbeddedInEnemy => embeddedEnemy != null && isStuck;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (isReturning)
        {
            UpdateReturn();
        }
        else if (isFlying)
        {
            UpdateFlight();
        }
        // If stuck, do nothing - sword stays in place (parented to hit object)
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialize and launch the sword as a projectile.
    /// </summary>
    /// <param name="direction">Flight direction</param>
    /// <param name="speed">Flight speed</param>
    /// <param name="recallSpeed">Return flight speed</param>
    /// <param name="stunDuration">Residual stun duration after removal</param>
    /// <param name="damageOnRemoval">Damage dealt when sword is removed from enemy</param>
    /// <param name="collisionMask">Layer mask for collision detection</param>
    public void Initialize(Vector3 direction, float speed, float recallSpeed, float stunDuration, int damageOnRemoval, LayerMask collisionMask)
    {
        flyDirection = direction.normalized;
        flySpeed = speed;
        returnSpeed = recallSpeed;
        postRemovalStunDuration = stunDuration;
        removalDamage = damageOnRemoval;
        hitMask = collisionMask;

        isFlying = true;
        isStuck = false;
        isReturning = false;

        embeddedEnemy = null;
        hitObject = null;

        // Orient sword to fly forward
        transform.rotation = Quaternion.LookRotation(flyDirection);

        // Enable trail if present
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }
    }

    /// <summary>
    /// Recall the sword to return to the player.
    /// Deals damage and applies residual stun to embedded enemy.
    /// </summary>
    public void Recall(Transform target, float catchDist = 1f)
    {
        if (isReturning) return;

        returnTarget = target;
        catchDistance = catchDist;
        isReturning = true;
        isStuck = false;
        isFlying = false;

        // Detach from any parent (was stuck to something)
        transform.SetParent(null);

        // Notify embedded enemy that sword is being removed and deal damage
        if (embeddedEnemy != null)
        {
            // Deal removal damage and apply residual stun
            embeddedEnemy.OnSwordRemoved(removalDamage, postRemovalStunDuration);
            
            Debug.Log($"[ThrownSword] Removed from enemy - Dealt {removalDamage} damage, {postRemovalStunDuration}s residual stun");
            
            embeddedEnemy = null;
        }

        // Re-enable trail for return flight
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }
    }

    /// <summary>
    /// Apply damage when sword is removed via sword dash (player dashes to sword).
    /// Deals extra damage on top of normal removal damage.
    /// Damage is applied IMMEDIATELY (not after stun).
    /// Called by PlayerSwordThrow.ForceRecallWithDashDamage().
    /// </summary>
    /// <param name="extraDamage">Additional damage from the dash attack</param>
    /// <param name="stunDuration">Residual stun duration after removal</param>
    public void ApplyDashRemovalDamage(int extraDamage, float stunDuration)
    {
        if (embeddedEnemy != null)
        {
            // Calculate total damage: normal removal damage + dash bonus damage
            int totalDamage = removalDamage + extraDamage;
            
            // Use OnSwordDashRemoval for IMMEDIATE damage application
            embeddedEnemy.OnSwordDashRemoval(totalDamage, stunDuration);
            
            Debug.Log($"[ThrownSword] Dash removal from enemy - Dealt {totalDamage} damage IMMEDIATELY ({removalDamage} base + {extraDamage} dash bonus), {stunDuration}s residual stun");
            
            embeddedEnemy = null;
        }
    }

    /// <summary>
    /// Force the sword to stick at a position (for instant-hit scenarios).
    /// </summary>
    public void ForceStickAt(Vector3 position, Vector3 normal, Transform parent)
    {
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(-normal);
        
        StickToSurface(parent);
    }

    /// <summary>
    /// Force the sword to return immediately after being blocked by a shield.
    /// Does NOT deal damage to any enemy, does NOT stick to anything.
    /// Called by DefenderShield when sword hits the shield.
    /// </summary>
    public void ForceReturnFromShield()
    {
        // Stop any current flight/stuck state
        isFlying = false;
        isStuck = false;
        
        // Clear any parent (in case it briefly stuck)
        transform.SetParent(null);
        
        // Clear enemy reference (shouldn't be any, but just in case)
        embeddedEnemy = null;
        hitObject = null;

        // Find the player to return to
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            returnTarget = player.transform;
            catchDistance = 1.5f; // Default catch distance
            isReturning = true;

            // Re-enable trail for return flight
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }

            Debug.Log("[ThrownSword] Deflected by shield - returning to player");
        }
        else
        {
            // No player found, just destroy
            Debug.LogWarning("[ThrownSword] No player found for return after shield deflection!");
            Destroy(gameObject);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Flight Logic
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateFlight()
    {
        float frameDistance = flySpeed * Time.deltaTime;
        float segmentLength = frameDistance / maxSegmentsPerFrame;

        // Segmented spherecasting for reliable high-speed collision
        for (int i = 0; i < maxSegmentsPerFrame; i++)
        {
            if (CheckCollisionSegment(segmentLength))
            {
                return; // Hit something, stop processing
            }

            // Move forward one segment
            transform.position += flyDirection * segmentLength;
        }
    }

    private bool CheckCollisionSegment(float distance)
    {
        if (Physics.SphereCast(
            transform.position,
            swordRadius,
            flyDirection,
            out RaycastHit hit,
            distance,
            hitMask,
            QueryTriggerInteraction.Collide))
        {
            // Position at hit point
            transform.position = hit.point + hit.normal * swordRadius;
            transform.rotation = Quaternion.LookRotation(-hit.normal);
            Debug.Log(hit.collider.gameObject.name);
            StickToSurface(hit.transform);
            ProcessHit(hit.collider.gameObject);
            
            return true;
        }

        return false;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Return Logic
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateReturn()
    {
        if (returnTarget == null)
        {
            // Target lost, just destroy
            CompleteReturn();
            return;
        }

        Vector3 toTarget = returnTarget.position - transform.position;
        float distance = toTarget.magnitude;

        // Check if close enough to catch
        if (distance <= catchDistance)
        {
            CompleteReturn();
            return;
        }

        // Move towards target
        Vector3 direction = toTarget.normalized;
        float moveDistance = returnSpeed * Time.deltaTime;

        transform.position += direction * Mathf.Min(moveDistance, distance);
        transform.rotation = Quaternion.LookRotation(direction);
    }

    private void CompleteReturn()
    {
        isReturning = false;

        // Disable trail
        if (trail != null)
        {
            trail.emitting = false;
        }

        OnReturnedToPlayer?.Invoke();
        
        // Destroy self
        Destroy(gameObject);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Hit Processing
    // ════════════════════════════════════════════════════════════════════════

    private void StickToSurface(Transform surface)
    {
        isFlying = false;
        isStuck = true;

        // Parent to the hit object so we move with it
        transform.SetParent(surface);

        // Disable trail while stuck
        if (trail != null)
        {
            trail.emitting = false;
        }
    }

    private void ProcessHit(GameObject target)
    {
        hitObject = target;
        OnHitTarget?.Invoke(target);

        // Check for DefenderShield FIRST (before IEnemy)
        // Shield blocks sword completely - no embed, no stun on defender
        DefenderShield shield = target.GetComponent<DefenderShield>();
        if (shield != null)
        {
            // Shield handles the interaction (reflects sword, exhausts player)
            shield.OnHitByThrownSword(this);
            return; // Don't process as normal hit
        }

        // Check for IEnemy (full sword interaction support)
        // Search up the hierarchy in case we hit a child collider
        IEnemy enemy = target.GetComponentInParent<IEnemy>();

        if (enemy != null)
        {
            // Notify enemy that sword is embedded - this handles the indefinite stun
            enemy.OnSwordEmbedded();
            embeddedEnemy = enemy;
        }
        else
        {
            // Fallback: check for basic IStunnable (for non-enemy stunnables)
            IStunnable stunnable = target.GetComponentInParent<IStunnable>();
            if (stunnable != null)
            {
                // Apply long stun (effectively infinite while stuck)
                stunnable.ApplyStun(999f);
            }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Editor Helpers
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, swordRadius);

        if (isFlying)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, flyDirection * 2f);
        }
    }

    #endregion
}