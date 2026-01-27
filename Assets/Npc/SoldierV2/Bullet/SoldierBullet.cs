using UnityEngine;

/// <summary>
/// Individual bullet GameObject that simulates travel using segmented raycasting.
/// Works correctly at any time scale - bullets appear fast in normal time and 
/// slow/visible during player's dash slow-motion.
/// 
/// SIMPLIFIED VERSION: No Rigidbody, no bullet system dependency.
/// Just instantiate and initialize, bullet handles everything itself.
/// </summary>
public class SoldierBullet : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Configuration
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Bullet Physics")]
    [SerializeField] private float bulletSpeed = 50f;
    [SerializeField] private float maxLifetime = 5f;
    [SerializeField] private float bulletRadius = 0.05f;
    
    [Header("Simulation")]
    [SerializeField] private int maxSegmentsPerFrame = 10;
    [SerializeField] private float segmentLength = 1f;
    
    [Header("Visual")]
    [SerializeField] private GameObject impactEffectPrefab;
    [SerializeField] private TrailRenderer trailRenderer;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugRays = true;
    [SerializeField] private Color bulletDebugColor = Color.red;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────────────

    private Vector3 direction;
    private int damage;
    private Transform shooter;
    private LayerMask hitMask;
    private float lifetime;
    private bool isInitialized;
    
    private scrLocalGameManager lgm;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        lgm = scrLocalGameManager.Instance;
    }

    private void Update()
    {
        if (!isInitialized) return;

        // Age the bullet
        lifetime += Time.deltaTime;
        
        // Destroy if too old
        if (lifetime >= maxLifetime)
        {
            DestroyBullet();
            return;
        }

        // Simulate bullet movement this frame
        SimulateBullet();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the bullet with firing parameters.
    /// Call this immediately after instantiation.
    /// </summary>
    public void Initialize(Vector3 fireDirection, int bulletDamage, Transform bulletShooter, LayerMask bulletHitMask)
    {
        direction = fireDirection.normalized;
        damage = bulletDamage;
        shooter = bulletShooter;
        hitMask = bulletHitMask;
        lifetime = 0f;
        isInitialized = true;

        // Orient the bullet to face the direction it's traveling
        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Bullet Simulation
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates bullet trajectory using segmented raycasting.
    /// Respects time dilation for slow-motion effects.
    /// </summary>
    private void SimulateBullet()
    {
        // Calculate how far the bullet should travel this frame based on time dilation
        float timeScale = (lgm != null) ? lgm.TimeDialation : 1f;
        float travelDistance = bulletSpeed * Time.deltaTime * timeScale;
        
        // Break travel distance into segments for accurate collision detection
        int segmentCount = Mathf.CeilToInt(travelDistance / segmentLength);
        segmentCount = Mathf.Min(segmentCount, maxSegmentsPerFrame);
        float segmentDist = travelDistance / segmentCount;

        Vector3 currentPosition = transform.position;
        
        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 nextPosition = currentPosition + direction * segmentDist;
            
            // Use SphereCast for more reliable hits
            if (Physics.SphereCast(
                currentPosition,
                bulletRadius,
                direction,
                out RaycastHit hit,
                segmentDist,
                hitMask))
            {
                // Hit something!
                HandleBulletImpact(hit);
                
                // Debug visualization
                if (showDebugRays)
                {
                    Debug.DrawLine(transform.position, hit.point, bulletDebugColor, 1f);
                }
                
                return; // Bullet is destroyed after impact
            }
            
            currentPosition = nextPosition;
        }

        // Bullet didn't hit anything this frame - update position
        transform.position = currentPosition;
        
        // Debug visualization
        if (showDebugRays)
        {
            Debug.DrawLine(transform.position, currentPosition, bulletDebugColor, 0.1f);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Impact Handling
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleBulletImpact(RaycastHit hit)
    {
        // Try to damage player
        if (hit.transform.CompareTag("Player"))
        {
            var playerController = hit.transform.GetComponent<FPSPlayerController>();
            if (playerController != null)
            {
                playerController.TakeDamage(damage);
                Debug.Log($"[SoldierBullet] Hit player for {damage} damage!");
            }
        }

        // Spawn impact effect
        if (impactEffectPrefab != null)
        {
            GameObject impact = Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            Destroy(impact, 2f);
        }

        Debug.Log("hit: " +  hit.transform.name);
        // Destroy the bullet
        DestroyBullet();
    }

    private void DestroyBullet()
    {
        // Disable trail renderer to prevent visual artifacts
        if (trailRenderer != null)
        {
            trailRenderer.enabled = false;
        }

        Destroy(gameObject);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showDebugRays || !isInitialized) return;

        Gizmos.color = bulletDebugColor;
        Gizmos.DrawWireSphere(transform.position, bulletRadius);
        Gizmos.DrawRay(transform.position, direction * 0.5f);
    }

    #endregion
}