using UnityEngine;

public class scrThrownSword : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Collision Settings")]
    [SerializeField] private float swordRadius = 0.15f;
    [SerializeField] private float skinWidth = 0.05f;

    [Header("Throw Damage")]
    [Tooltip("Damage applied to enemy after stun duration ends")]
    [SerializeField] private int throwDamage = 80;
    
    [Tooltip("Duration of stun after sword is removed")]
    [SerializeField] private float stunDuration = 2f;

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

    private float returnSpeed = 40f;
    private float recallUnlockTime = 0f;

    // Projectile flight data
    private bool useUnscaledTime;
    private Vector3 flyDirection;
    private float flySpeed;
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

        flyDirection = direction.normalized;
        flySpeed = speed;
        useUnscaledTime = unscaledTime;
        hitMask = collisionLayerMask;

        // Orient sword along flight path
        transform.forward = flyDirection;
    }

    public void InitializeStuck(Vector3 position, Vector3 normal, Transform targetParent)
    {
        transform.position = position;
        transform.forward = -normal;

        StickToSurface(targetParent);
        CheckEnemyHit(targetParent.gameObject, position);
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

        transform.SetParent(null);

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
            transform.position = hit.point + hit.normal * swordRadius;
            transform.forward = -hit.normal;

            StickToSurface(hit.transform);
            CheckEnemyHit(hit.collider.gameObject, hit.point);
        }
        else
        {
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

        if (col != null) col.enabled = true;
    }

    private void CheckEnemyHit(GameObject hitObject, Vector3 hitPoint)
    {
        if (hitObject.TryGetComponent<INpcInteraction>(out var target))
        {
            recallUnlockTime = Time.unscaledTime + 1f;
            embeddedEnemy = target;
            
            // Pass stun duration, damage amount, sword direction, and hit point
            target.OnThrowStun(stunDuration, throwDamage, flyDirection, hitPoint);

            Debug.Log($"Sword embedded in enemy: {hitObject.name} (pending damage: {throwDamage})");
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