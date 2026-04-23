using UnityEngine;

/// <summary>
/// Individual bullet GameObject that simulates travel using segmented raycasting.
/// Works correctly at any time scale - bullets appear fast in normal time and 
/// slow/visible during player's dash slow-motion.
/// 
/// SIMPLIFIED VERSION: No Rigidbody, no bullet system dependency.
/// Just instantiate and initialize, bullet handles everything itself.
/// 
/// UPDATED: Compatible with new PlayerCore system.
/// UPDATED: Added IDamageable fallback for destructible objects (BreakableGlass etc.)
/// </summary>
public class SoldierBullet : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Configuration
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Bullet Physics")]
    [SerializeField] private float bulletSpeed = 400f;
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



    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────
    

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
        float timeScale = 1f;
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
        // UPDATED: Try to damage player using PlayerCore
        if (hit.transform.CompareTag("Player"))
        {
            // Try PlayerCore first (new system)
            var playerCore = hit.transform.GetComponent<PlayerCore>();
            if (playerCore != null)
            {
                // Pass bullet travel direction so player camera nudges toward attacker
                string sourceName = shooter != null ? shooter.gameObject.name : "Soldier";
                playerCore.TakeDamage(damage, direction, sourceName,shooter.transform);
                //Debug.Log($"[SoldierBullet] Hit player for {damage} damage!");
            }
        }
        else
        {
            // ── NEW: IDamageable fallback für zerstörbare Objekte (Glas etc.) ──
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, hit.point, direction);
            }
        }

        // Spawn impact effect
        if (impactEffectPrefab != null)
        {
            GameObject impact = Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            Destroy(impact, 2f);
        }

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
