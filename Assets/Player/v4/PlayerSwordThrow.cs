using UnityEngine;
using System;

/// <summary>
/// Manages sword throwing mechanics for the player.
/// Separated from PlayerCombat for cleaner code organization.
/// Communicates with PlayerCombat to disable attack/block while sword is thrown.
/// 
/// UPDATED: Added swordRemovalDamage - damage dealt when sword is recalled from an embedded enemy.
/// UPDATED: Added ForceRecallWithDashDamage for sword dash mechanic.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerSwordThrow : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Fired when sword is thrown.</summary>
    public event Action OnSwordThrown;

    /// <summary>Fired when sword is recalled (starts returning).</summary>
    public event Action OnSwordRecalled;

    /// <summary>Fired when sword returns to player's hand.</summary>
    public event Action OnSwordCaught;

    /// <summary>Fired when sword hits a target.</summary>
    public event Action<GameObject> OnSwordHitTarget;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Sword References")]
    [SerializeField] private GameObject heldSwordVisual;
    [SerializeField] private ThrownSword thrownSwordPrefab;
    [SerializeField] private Transform throwOrigin;

    [Header("Throw Settings")]
    [SerializeField] private float throwSpeed = 300f;
    [SerializeField] private float returnSpeed = 900f;
    [SerializeField] private float catchDistance = 1.2f;
    [SerializeField] private float recallDelay = 1f;
    [SerializeField] private float maxThrowDistance = 1000f;
    [SerializeField] private LayerMask throwLayerMask = -1;

    [Header("Damage Settings")]
    [Tooltip("Damage dealt to enemy when sword is recalled/removed from them")]
    [SerializeField] private int swordRemovalDamage = 30;
    
    [Tooltip("Duration of stun applied after sword is removed (residual stun)")]
    [SerializeField] private float postRemovalStunDuration = 2f;

    [Header("Input")]
    [SerializeField] private string throwActionName = "ThrowSword";

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private ThrownSword activeSword;
    private bool hasSword = true;

    // Recall delay after hit
    private float lastHitTime = -999f;
    

    // Debug visualization
    private Vector3 debugTargetPoint;
    private bool debugHasTarget;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>True if player currently has the sword (can attack/block).</summary>
    public bool HasSword => hasSword;

    /// <summary>True if sword is currently flying or stuck somewhere.</summary>
    public bool IsSwordOut => activeSword != null;

    /// <summary>True if sword is stuck to a surface.</summary>
    public bool IsSwordStuck => activeSword != null && activeSword.IsStuck;

    /// <summary>True if sword is returning to player.</summary>
    public bool IsSwordReturning => activeSword != null && activeSword.IsReturning;

    /// <summary>Reference to the active thrown sword (null if none).</summary>
    public ThrownSword ActiveSword => activeSword;
    
    /// <summary>Damage dealt when sword is removed from an enemy.</summary>
    public int SwordRemovalDamage => swordRemovalDamage;
    
    /// <summary>True if sword is currently embedded in an enemy.</summary>
    public bool IsSwordInEnemy => activeSword != null && activeSword.IsStuck && activeSword.EmbeddedEnemy != null;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
    }

    private void Update()
    {
        if (core.IsDead) return;

        HandleInput();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Input Handling
    // ════════════════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        if (!core.Input.GetActionDown(throwActionName)) return;

        if (hasSword)
        {
            // Player has sword -> throw it
            if (CanThrow())
            {
                ThrowSword();
            }
        }
        else
        {
            // Player doesn't have sword -> recall it (only if delay has passed)
            if (CanRecall())
            {
                RecallSword();
            }
        }
    }

    private bool CanThrow()
    {
        // Can't throw while dead or dashing
        if (core.IsDead) return false;
        if (core.CurrentState == PlayerCore.PlayerState.Dashing) return false;
        if (core.CurrentState == PlayerCore.PlayerState.DashingToSword) return false;

        return true;
    }

    private bool CanRecall()
    {
        // Must wait 1 second after sword hits something before recall
        float timeSinceHit = Time.time - lastHitTime;
        return timeSinceHit >= recallDelay;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Throw Logic
    // ════════════════════════════════════════════════════════════════════════

    private void ThrowSword()
    {
        hasSword = false;

        // Hide held sword visual
        if (heldSwordVisual != null)
        {
            heldSwordVisual.SetActive(false);
        }

        // Determine spawn position and direction
        Vector3 spawnPos = GetThrowOrigin();
        Vector3 throwDir = GetThrowDirection();

        // Spawn thrown sword
        activeSword = Instantiate(thrownSwordPrefab, spawnPos, Quaternion.LookRotation(throwDir));

        // Subscribe to events
        activeSword.OnReturnedToPlayer += HandleSwordReturned;
        activeSword.OnHitTarget += HandleSwordHit;

        // Initialize and launch - pass removal damage and stun duration
        activeSword.Initialize(
            throwDir,
            throwSpeed,
            returnSpeed,
            postRemovalStunDuration,
            swordRemovalDamage,
            throwLayerMask
        );

        OnSwordThrown?.Invoke();
    }

    private Vector3 GetThrowOrigin()
    {
        if (throwOrigin != null)
        {
            return throwOrigin.position;
        }

        // Fallback: slightly in front of camera
        return core.CameraTransform.position + core.CameraTransform.forward * 0.5f;
    }

    private Vector3 GetThrowDirection()
    {
        // Cast ray from camera center (crosshair position) forward
        Ray ray = new Ray(core.CameraTransform.position, core.CameraTransform.forward);
        
        Vector3 targetPoint;

        // Check if ray hits something
        if (Physics.Raycast(ray, out RaycastHit hit, maxThrowDistance, throwLayerMask))
        {
            // Ray hit something - aim at that point
            targetPoint = hit.point;
        }
        else
        {
            // Ray didn't hit anything - aim at a far point in that direction
            targetPoint = ray.GetPoint(maxThrowDistance);
        }

        // Store for gizmo visualization
        debugTargetPoint = targetPoint;
        debugHasTarget = true;

        // Calculate direction from throw origin to target point
        Vector3 throwOriginPos = GetThrowOrigin();
        Vector3 direction = (targetPoint - throwOriginPos).normalized;

        return direction;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Recall Logic
    // ════════════════════════════════════════════════════════════════════════

    private void RecallSword()
    {
        if (activeSword == null)
        {
            // Sword was somehow destroyed, just restore state
            RestoreSword();
            return;
        }

        // Get return target
        Transform returnTarget = throwOrigin != null ? throwOrigin : transform;

        activeSword.Recall(returnTarget, catchDistance);
        OnSwordRecalled?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleSwordReturned()
    {
        // Unsubscribe
        if (activeSword != null)
        {
            activeSword.OnReturnedToPlayer -= HandleSwordReturned;
            activeSword.OnHitTarget -= HandleSwordHit;
        }

        activeSword = null;
        RestoreSword();

        OnSwordCaught?.Invoke();
    }

    private void HandleSwordHit(GameObject target)
    {
        // Record hit time for recall delay
        lastHitTime = Time.time;
        
        OnSwordHitTarget?.Invoke(target);
    }

    private void RestoreSword()
    {
        hasSword = true;

        // Show held sword visual
        if (heldSwordVisual != null)
        {
            heldSwordVisual.SetActive(true);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Force recall the sword immediately (e.g., on death or scene change).
    /// Does NOT deal extra damage to embedded enemy.
    /// </summary>
    public void ForceRecall()
    {
        if (activeSword != null)
        {
            Destroy(activeSword.gameObject);
            activeSword = null;
        }

        RestoreSword();
    }

    /// <summary>
    /// Force recall the sword with additional damage dealt to embedded enemy.
    /// Used by sword dash mechanic - player dashes to sword and retrieves it violently.
    /// </summary>
    /// <param name="extraDamage">Additional damage on top of normal removal damage</param>
    public void ForceRecallWithDashDamage(int extraDamage)
    {
        if (activeSword != null)
        {
            // Deal damage to embedded enemy before destroying sword
            activeSword.ApplyDashRemovalDamage(extraDamage, postRemovalStunDuration);
            
            Destroy(activeSword.gameObject);
            activeSword = null;
        }

        RestoreSword();
        OnSwordCaught?.Invoke();
    }

    /// <summary>
    /// Reset to initial state (e.g., on respawn).
    /// </summary>
    public void ResetState()
    {
        ForceRecall();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Visualization
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!debugHasTarget) return;

        // Draw sphere at target point
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(debugTargetPoint, 0.2f);

        // Draw line from throw origin to target point
        if (throwOrigin != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(throwOrigin.position, debugTargetPoint);
        }
    }

    #endregion
}