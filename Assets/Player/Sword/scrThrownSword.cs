using UnityEngine;

public class scrThrownSword : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Collision Settings")]
    [SerializeField] private float swordRadius = 0.15f;     // Effective thickness of sword for collision
    [SerializeField] private float skinWidth   = 0.05f;     // Buffer to prevent clipping through surfaces

    [Header("Damage Settings")]
    [Tooltip("Damage dealt AFTER stun duration ends")]
    [SerializeField] private int throwDamage = 50;

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
    
    // Damage tracking data - stored for when stun ends
    private NpcBase embeddedNpc;              // Direct reference to NPC (so we can apply damage later)
    private Vector3 storedImpactDirection;    // Direction sword was flying
    private Vector3 storedHitPoint;           // Where sword hit the NPC

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
        embeddedNpc = null;

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
            
            // Schedule damage calculation for after stun ends
            if (embeddedNpc != null)
            {
                ScheduleDamageAfterStun();
            }
            
            embeddedEnemy = null;
            embeddedNpc = null;
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

            // Store impact data for later damage calculation
            storedImpactDirection = flyDirection;
            storedHitPoint = hit.point;

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
            embeddedEnemy = target; // Store interface reference
            
            // Also store NpcBase reference for damage calculation
            embeddedNpc = hitObject.GetComponent<NpcBase>();
            
            // Apply stun (NO damage yet!)
            target.OnThrowStun(3);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Delayed Damage System
    // ────────────────────────────────────────────────────────────────────────────────


    private void ScheduleDamageAfterStun()
    {
        if (embeddedNpc == null) return;
        float stunDuration = embeddedNpc.GetResidualStunDuration();
    
        embeddedNpc.StartCoroutine(ApplyDamageAfterDelay(
            embeddedNpc, 
            storedImpactDirection, 
            storedHitPoint, 
            throwDamage, 
            stunDuration
            )
        );
    }


    private System.Collections.IEnumerator ApplyDamageAfterDelay(
        NpcBase targetNpc, 
        Vector3 impactDir, 
        Vector3 hitPoint, 
        int damage,
        float delay
        )
    {
        yield return new WaitForSeconds(delay);
        if (targetNpc != null && !targetNpc.IsDead)
        {
            targetNpc.OnThrowDamage(damage, impactDir, hitPoint);
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