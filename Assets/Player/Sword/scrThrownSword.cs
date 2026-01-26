using UnityEngine;

public class scrThrownSword : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Collision Settings")]
    [SerializeField] private float swordRadius = 0.15f;     // Effective thickness of sword for collision
    [SerializeField] private float skinWidth   = 0.05f;     // Buffer to prevent clipping through surfaces

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Components
    // ────────────────────────────────────────────────────────────────────────────────

    private Collider col;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime State
    // ────────────────────────────────────────────────────────────────────────────────

    private bool isReturning;
    private bool stuck;

    private Transform returnTarget;

    private float returnSpeed       = 40f;
    private float recallUnlockTime  = 0f;

    // Projectile flight data
    private bool    useUnscaledTime;
    private Vector3 flyDirection;
    private float   flySpeed;
    private LayerMask hitMask;

    // Track embedded enemy for notification on recall
    private INpcInteraction embeddedEnemy;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        col = GetComponent<Collider>();
    }

    private void Update()
    {
        if (isReturning && returnTarget != null)
        {
            HandleReturn();
            return;
        }

        if (!stuck && !isReturning)
        {
            Debug.Log("(!stuck && !isReturning)");
            HandleProjectileFlight();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public Initialization API
    // ────────────────────────────────────────────────────────────────────────────────

    public void InitializeProjectile(Vector3 direction, float speed, bool unscaledTime, LayerMask collisionLayerMask)
    {
        stuck = false;
        isReturning = false;
        embeddedEnemy = null;

        flyDirection   = direction.normalized;
        flySpeed       = speed;
        useUnscaledTime = unscaledTime;
        hitMask        = collisionLayerMask;

        // Orient sword along flight path
        transform.forward = flyDirection;
    }

    public void InitializeStuck(Vector3 position, Vector3 normal, Transform targetParent)
    {
        transform.position = position;
        transform.forward = -normal; // Point into the surface

        StickToSurface(targetParent);
        CheckEnemyHit(targetParent.gameObject);
    }

    public void Recall(Transform target)
    {
        if (Time.unscaledTime < recallUnlockTime) return;

        // Notify embedded enemy that sword is being removed
        if (embeddedEnemy != null)
        {
            embeddedEnemy.OnSwordRemoved();
            embeddedEnemy = null;
        }

        isReturning = true;
        returnTarget = target;
        stuck = false;

        // Detach from any surface
        transform.SetParent(null);

        // Disable collider during return to prevent new hits
        if (col != null) col.enabled = false;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Movement Logic – Projectile Flight
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleProjectileFlight()
    {
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float stepDistance = flySpeed * delta;

        if (Physics.SphereCast(
            transform.position,
            swordRadius,
            flyDirection,
            out RaycastHit hit,
            stepDistance + skinWidth,
            hitMask))
        {
            // Position sword center correctly relative to hit surface
            transform.position = hit.point + hit.normal * swordRadius;

            // Face into the surface
            transform.forward = -hit.normal;

            StickToSurface(hit.transform);
            CheckEnemyHit(hit.collider.gameObject);
        }
        else
        {
            // Free flight – no hit this frame
            transform.position += flyDirection * stepDistance;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Movement Logic – Recall / Return
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleReturn()
    {
        transform.position = Vector3.MoveTowards(
            transform.position,
            returnTarget.position,
            returnSpeed * Time.unscaledDeltaTime);

        transform.LookAt(returnTarget);

        // Destroy when close enough to player/hand
        if (Vector3.Distance(transform.position, returnTarget.position) < 1.2f)
        {
            Destroy(gameObject);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Sticking & Collision Handling
    // ────────────────────────────────────────────────────────────────────────────────

    private void StickToSurface(Transform surface)
    {
        stuck = true;
        transform.SetParent(surface);

        // Re-enable collider if needed (visual/collision purposes)
        if (col != null) col.enabled = true;
    }

    private void CheckEnemyHit(GameObject hitObject)
    {
        // Special case: DashTrackingTurret or any enemy with INpcInteraction
        if (hitObject.TryGetComponent<INpcInteraction>(out var target))
        {
            recallUnlockTime = Time.unscaledTime + 1f;
            embeddedEnemy = target; // Store reference for recall notification
            target.OnThrowStun(3);
            
            Debug.Log($"Sword embedded in enemy: {hitObject.name}");
        }
        
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Editor Visualization
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, swordRadius);
    }

    #endregion
}