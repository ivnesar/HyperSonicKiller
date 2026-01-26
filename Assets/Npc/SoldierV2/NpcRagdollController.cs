using UnityEngine;

/// <summary>
/// Controls ragdoll physics for NPCs.
/// Assumes the ragdoll is already set up via Unity's Ragdoll Builder tool.
/// Manages the transition between animated and ragdoll states.
/// 
/// SETUP:
/// 1. Use Unity's Ragdoll Builder to create ragdoll components on the NPC
/// 2. Add this script to the root NPC GameObject (same as NpcBase)
/// 3. Ragdoll will automatically be disabled on start and enabled on death
/// </summary>
public class NpcRagdollController : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields - Ragdoll Settings
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Ragdoll Settings")]
    [Tooltip("Total mass distributed across all ragdoll rigidbodies")]
    [SerializeField] private float totalMass = 70f;
    
    [Tooltip("How much drag the ragdoll has (higher = falls slower)")]
    [SerializeField] private float ragdollDrag = 0.5f;
    
    [Tooltip("Angular drag for rotation (higher = less spinning)")]
    [SerializeField] private float ragdollAngularDrag = 0.5f;

    [Header("Impact Force Settings")]
    [Tooltip("Multiplier for incoming impact forces")]
    [SerializeField] private float impactForceMultiplier = 1f;
    
    [Tooltip("Maximum force that can be applied on death")]
    [SerializeField] private float maxImpactForce = 500f;
    
    [Tooltip("Upward bias added to impact direction (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float upwardForceBias = 0.3f;

    [Header("Melee Impact")]
    [Tooltip("Base force applied when killed by melee attack")]
    [SerializeField] private float meleeImpactForce = 300f;

    [Header("Thrown Sword Impact")]
    [Tooltip("Base force applied when killed by thrown sword")]
    [SerializeField] private float thrownSwordImpactForce = 400f;

    [Header("Bullet Impact")]
    [Tooltip("Base force applied per bullet hit")]
    [SerializeField] private float bulletImpactForce = 50f;
    
    [Tooltip("Accumulated bullet impacts are applied on death")]
    [SerializeField] private bool accumulateBulletImpacts = true;

    [Header("References")]
    [Tooltip("Leave empty to auto-find. The main rigidbody to apply central forces to (usually hips/pelvis)")]
    [SerializeField] private Rigidbody mainRigidbody;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────────────

    private Rigidbody[] ragdollRigidbodies;
    private Collider[] ragdollColliders;
    private Animator animator;
    
    private bool isRagdollActive = false;
    
    // Impact tracking
    private Vector3 accumulatedImpactForce;
    private Vector3 lastImpactPoint;
    private bool hasAccumulatedImpact;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Cache components
        animator = GetComponentInChildren<Animator>();
        
        // Find all ragdoll rigidbodies in children (exclude root if it has one)
        ragdollRigidbodies = GetComponentsInChildren<Rigidbody>();
        ragdollColliders = GetComponentsInChildren<Collider>();
        
        // Find main rigidbody (usually the hips/pelvis)
        if (mainRigidbody == null)
        {
            FindMainRigidbody();
        }
        
        // Configure ragdoll mass and physics
        ConfigureRagdollPhysics();
        
        // Start with ragdoll disabled
        SetRagdollActive(false);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Ragdoll Configuration
    // ────────────────────────────────────────────────────────────────────────────────

    private void FindMainRigidbody()
    {
        // Try to find by common bone names
        string[] commonHipNames = { "hips", "pelvis", "spine", "root" };
        
        foreach (var rb in ragdollRigidbodies)
        {
            string boneName = rb.gameObject.name.ToLower();
            foreach (string hipName in commonHipNames)
            {
                if (boneName.Contains(hipName))
                {
                    mainRigidbody = rb;
                    if (showDebugInfo)
                    {
                        Debug.Log($"[{gameObject.name}] Found main rigidbody: {rb.gameObject.name}");
                    }
                    return;
                }
            }
        }
        
        // Fallback: use the first rigidbody that's not on the root object
        foreach (var rb in ragdollRigidbodies)
        {
            if (rb.gameObject != gameObject)
            {
                mainRigidbody = rb;
                if (showDebugInfo)
                {
                    Debug.Log($"[{gameObject.name}] Using fallback main rigidbody: {rb.gameObject.name}");
                }
                return;
            }
        }
        
        // Last resort: just use the first one
        if (ragdollRigidbodies.Length > 0)
        {
            mainRigidbody = ragdollRigidbodies[0];
        }
    }

    private void ConfigureRagdollPhysics()
    {
        if (ragdollRigidbodies.Length == 0) return;
        
        // Distribute mass across all rigidbodies
        float massPerBody = totalMass / ragdollRigidbodies.Length;
        
        foreach (var rb in ragdollRigidbodies)
        {
            rb.mass = massPerBody;
            rb.linearDamping = ragdollDrag;
            rb.angularDamping = ragdollAngularDrag;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        
        // Give the main rigidbody slightly more mass (center of mass)
        if (mainRigidbody != null)
        {
            mainRigidbody.mass = massPerBody * 1.5f;
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Configured {ragdollRigidbodies.Length} ragdoll rigidbodies with total mass {totalMass}kg");
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Ragdoll State Control
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enables or disables the ragdoll state.
    /// </summary>
    public void SetRagdollActive(bool active)
    {
        isRagdollActive = active;
        
        // Toggle animator
        if (animator != null)
        {
            animator.enabled = !active;
        }
        
        // Toggle rigidbodies
        foreach (var rb in ragdollRigidbodies)
        {
            if (rb == null) continue;
            
            rb.isKinematic = !active;
            rb.useGravity = active;
            
            if (!active)
            {
                // Reset velocities when disabling ragdoll
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        
        // Toggle colliders - set to trigger when not ragdolling
        foreach (var col in ragdollColliders)
        {
            if (col == null) continue;
            if (col is CharacterController) continue;
            
            col.isTrigger = !active;
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Ragdoll {(active ? "ACTIVATED" : "DEACTIVATED")}");
        }
    }

    /// <summary>
    /// Activates ragdoll with an impact force applied.
    /// Call this when the NPC dies.
    /// </summary>
    public void ActivateRagdollWithImpact(Vector3 impactDirection, float forceMagnitude, Vector3? impactPoint = null)
    {
        // First activate the ragdoll
        SetRagdollActive(true);
        
        // Add upward bias to the impact direction
        Vector3 adjustedDirection = (impactDirection + Vector3.up * upwardForceBias).normalized;
        
        // Clamp and scale the force
        float finalForce = Mathf.Min(forceMagnitude * impactForceMultiplier, maxImpactForce);
        
        // Apply force
        if (impactPoint.HasValue && mainRigidbody != null)
        {
            mainRigidbody.AddForceAtPosition(
                adjustedDirection * finalForce,
                impactPoint.Value,
                ForceMode.Impulse
            );
        }
        else if (mainRigidbody != null)
        {
            mainRigidbody.AddForce(adjustedDirection * finalForce, ForceMode.Impulse);
        }
        
        // Also apply some force to nearby body parts
        ApplyDistributedForce(adjustedDirection, finalForce * 0.3f, impactPoint);
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Ragdoll impact: Dir={adjustedDirection}, Force={finalForce}");
        }
    }

    /// <summary>
    /// Activates ragdoll using any accumulated impact forces.
    /// </summary>
    public void ActivateRagdollWithAccumulatedImpact()
    {
        if (hasAccumulatedImpact)
        {
            ActivateRagdollWithImpact(
                accumulatedImpactForce.normalized,
                accumulatedImpactForce.magnitude,
                lastImpactPoint
            );
        }
        else
        {
            // No accumulated impact, just activate with a small random force
            Vector3 randomDir = new Vector3(
                Random.Range(-0.3f, 0.3f),
                0.2f,
                Random.Range(-0.3f, 0.3f)
            ).normalized;
            
            ActivateRagdollWithImpact(randomDir, 50f, null);
        }
        
        ClearAccumulatedImpact();
    }

    private void ApplyDistributedForce(Vector3 direction, float force, Vector3? center)
    {
        Vector3 centerPoint = center ?? (mainRigidbody != null ? mainRigidbody.position : transform.position);
        
        foreach (var rb in ragdollRigidbodies)
        {
            if (rb == null || rb == mainRigidbody) continue;
            
            float distance = Vector3.Distance(rb.position, centerPoint);
            float falloff = 1f / (1f + distance);
            
            rb.AddForce(direction * force * falloff, ForceMode.Impulse);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Impact Registration
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a melee attack impact.
    /// </summary>
    public void RegisterMeleeImpact(Vector3 attackerPosition)
    {
        Vector3 impactDir = (transform.position - attackerPosition).normalized;
        impactDir.y = 0;
        
        accumulatedImpactForce = impactDir * meleeImpactForce;
        lastImpactPoint = transform.position + Vector3.up;
        hasAccumulatedImpact = true;
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Registered melee impact from {attackerPosition}");
        }
    }

    /// <summary>
    /// Register a thrown sword impact.
    /// </summary>
    public void RegisterThrownSwordImpact(Vector3 swordDirection, Vector3 hitPoint)
    {
        accumulatedImpactForce = swordDirection.normalized * thrownSwordImpactForce;
        lastImpactPoint = hitPoint;
        hasAccumulatedImpact = true;
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Registered thrown sword impact at {hitPoint}");
        }
    }

    /// <summary>
    /// Register a bullet impact. Multiple bullets accumulate.
    /// </summary>
    public void RegisterBulletImpact(Vector3 bulletDirection, Vector3 hitPoint)
    {
        Vector3 bulletForce = bulletDirection.normalized * bulletImpactForce;
        
        if (accumulateBulletImpacts && hasAccumulatedImpact)
        {
            accumulatedImpactForce += bulletForce;
        }
        else
        {
            accumulatedImpactForce = bulletForce;
        }
        
        lastImpactPoint = hitPoint;
        hasAccumulatedImpact = true;
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Registered bullet impact. Total force: {accumulatedImpactForce.magnitude}");
        }
    }

    /// <summary>
    /// Register a generic impact force.
    /// </summary>
    public void RegisterImpact(Vector3 force, Vector3 hitPoint)
    {
        accumulatedImpactForce = force;
        lastImpactPoint = hitPoint;
        hasAccumulatedImpact = true;
    }

    /// <summary>
    /// Clear any accumulated impact forces.
    /// </summary>
    public void ClearAccumulatedImpact()
    {
        accumulatedImpactForce = Vector3.zero;
        lastImpactPoint = Vector3.zero;
        hasAccumulatedImpact = false;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────────────

    public bool IsRagdollActive => isRagdollActive;
    
    /// <summary>
    /// Gets the current center of mass position of the ragdoll.
    /// </summary>
    public Vector3 GetCenterOfMass()
    {
        if (mainRigidbody != null)
        {
            return mainRigidbody.worldCenterOfMass;
        }
        return transform.position;
    }

    /// <summary>
    /// Applies an explosion force to the ragdoll.
    /// </summary>
    public void ApplyExplosionForce(float force, Vector3 explosionPosition, float radius)
    {
        if (!isRagdollActive) return;
        
        foreach (var rb in ragdollRigidbodies)
        {
            if (rb == null) continue;
            rb.AddExplosionForce(force, explosionPosition, radius, upwardForceBias, ForceMode.Impulse);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug Visualization
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!showDebugInfo) return;
        
        // Show main rigidbody
        if (mainRigidbody != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(mainRigidbody.position, 0.2f);
        }
        
        // Show accumulated impact direction
        if (hasAccumulatedImpact)
        {
            Gizmos.color = Color.red;
            Vector3 startPoint = lastImpactPoint != Vector3.zero ? lastImpactPoint : transform.position;
            Gizmos.DrawRay(startPoint, accumulatedImpactForce.normalized * 2f);
            Gizmos.DrawWireSphere(startPoint, 0.1f);
        }
        
        // Show all ragdoll rigidbodies
        if (ragdollRigidbodies != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            foreach (var rb in ragdollRigidbodies)
            {
                if (rb != null && rb != mainRigidbody)
                {
                    Gizmos.DrawWireSphere(rb.position, 0.1f);
                }
            }
        }
    }

    #endregion
}