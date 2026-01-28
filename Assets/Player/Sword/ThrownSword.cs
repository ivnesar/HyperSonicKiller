using UnityEngine;
using System;

/// <summary>
/// Thrown sword projectile that flies like an arrow and sticks to surfaces.
/// Uses segmented spherecasting for reliable collision detection at high speeds.
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

    [Header("Stun Settings")]
    [Tooltip("How long the enemy stays stunned AFTER sword is removed")]
    [SerializeField] private float postRemovalStunDuration = 2f;

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
    private Vector3 flyDirection;
    private float flySpeed;
    private float returnSpeed;
    private LayerMask hitMask;

    // Return target
    private Transform returnTarget;
    private float catchDistance = 1f;

    // Enemy tracking
    private IStunnable stunnedTarget;
    private GameObject hitObject;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public bool IsStuck => isStuck;
    public bool IsReturning => isReturning;
    public bool IsFlying => isFlying;
    public GameObject HitObject => hitObject;

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
    public void Initialize(Vector3 direction, float speed, float recallSpeed, LayerMask collisionMask)
    {
        flyDirection = direction.normalized;
        flySpeed = speed;
        returnSpeed = recallSpeed;
        hitMask = collisionMask;

        isFlying = true;
        isStuck = false;
        isReturning = false;

        stunnedTarget = null;
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

        // Apply post-removal stun to enemy
        if (stunnedTarget != null)
        {
            stunnedTarget.ApplyStun(postRemovalStunDuration);
            stunnedTarget = null;
        }

        // Re-enable trail for return flight
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
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
            QueryTriggerInteraction.Ignore))
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

        // Check for stunnable enemy
        // We search up the hierarchy in case we hit a child collider
        IStunnable stunnable = target.GetComponentInParent<IStunnable>();

        if (stunnable != null)
        {
            // Stun for a very long time (effectively infinite while stuck)
            // The actual stun duration resets when recalled (postRemovalStunDuration)
            stunnable.ApplyStun(999f);
            stunnedTarget = stunnable;
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