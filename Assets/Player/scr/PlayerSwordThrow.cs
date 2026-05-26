using UnityEngine;
using System;

/// <summary>
/// Manages sword throwing mechanics for the player.
/// Separated from PlayerCombat for cleaner code organization.
/// Communicates with PlayerCombat to disable attack/block while sword is thrown.
/// 
/// UPDATED: Added swordRemovalDamage - damage dealt when sword is recalled from an embedded enemy.
/// UPDATED: Throw is now hold-to-aim with zoom + time slow, release-to-throw.
///   - Hold ThrowSword key: camera zooms in over aimZoomInDuration, time slows to aimTimeScale.
///   - Zoom is controlled by aimZoomFactor (e.g. 3 = FOV/3). Mouse sensitivity scales down by the same factor.
///   - Time slow expires after aimSlowMaxDuration (even if still holding).
///   - Time slow managed via TimeManager "AimSlowMo" layer (same priority as DashSlowMo).
///   - Release key: sword is thrown, zoom/time/sensitivity reset immediately.
/// UPDATED: Removed IsSprintBurstActive check — old sprint system removed.
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
    [SerializeField] private float maxThrowDistance = 1000f;
    [SerializeField] private LayerMask throwLayerMask = -1;

    [Header("Auto Pickup (when close to stuck sword)")]
    [Tooltip("Max distance at which a stuck sword is automatically recalled. 0 = disabled.")]
    [SerializeField] private float autoPickupDistance = 3f;

    [Tooltip("Layers that block line of sight between player and sword (usually walls/floors).")]
    [SerializeField] private LayerMask autoPickupLOSMask = ~0;

    [Tooltip("Vertical offset from player transform used as LOS origin (approximates chest/eye height).")]
    [SerializeField] private float autoPickupLOSHeightOffset = 1.0f;

    [Header("Damage Settings")]
    [Tooltip("Damage dealt to enemy when sword is recalled/removed from them")]
    [SerializeField] private int swordRemovalDamage = 30;
    
    [Tooltip("Duration of stun applied after sword is removed (residual stun)")]
    [SerializeField] private float postRemovalStunDuration = 2f;

    [Header("Input")]
    [SerializeField] private string throwActionName = "ThrowSword";

    [Header("Aim Zoom (Hold to Aim)")]
    [Tooltip("Zoom multiplier while aiming (e.g. 3 = 3x zoom, FOV divided by 3)")]
    [SerializeField] private float aimZoomFactor = 3f;

    [Tooltip("Time in unscaled seconds to reach full zoom")]
    [SerializeField] private float aimZoomInDuration = 0.2f;

    [Tooltip("Time slow target while aiming (matches dash timeScale)")]
    [SerializeField] private float aimTimeScale = 0.1f;

    [Tooltip("Max duration of time slow in unscaled seconds (time resets after this even if still holding)")]
    [SerializeField] private float aimSlowMaxDuration = 2f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private ThrownSword activeSword;
    private bool hasSword = true;

    // Aim zoom state
    private bool isAiming;
    private float aimTimer;            // unscaled time since aim started
    private float aimSlowTimer;        // unscaled time since slow started
    private bool aimSlowExpired;       // true after aimSlowMaxDuration elapsed
    private PlayerDashFOV dashFOV;     // cached reference for FOV override
    private PlayerLook look;           // cached reference for sensitivity adjustment
    private float baseSensitivity;     // original sensitivity before aim zoom

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

    /// <summary>True if player is currently holding the throw key to aim.</summary>
    public bool IsAiming => isAiming;

    #endregion

    // Reference to combat for exhaustion check
    private PlayerCombat combat;

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        combat = GetComponent<PlayerCombat>();
        dashFOV = GetComponent<PlayerDashFOV>();
        look = GetComponent<PlayerLook>();
    }


    private void Update()
    {
        if (core.IsDead)
        {
            if (isAiming) StopAim();
            return;
        }

        // Cancel aim if player enters a state where throwing isn't allowed
        if (isAiming && !CanThrow())
        {
            StopAim();
        }

        HandleInput();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Input Handling
    // ════════════════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        if (hasSword)
        {
            // ── Throw flow: Hold to aim, Release to throw ──
            HandleAimInput();
        }
        else
        {
            // ── Recall flow: unchanged (press to recall) ──
            if (!core.Input.GetActionDown(throwActionName)) return;

            if (CanRecall())
            {
                RecallSword();
            }
        }
    }

    private void HandleAimInput()
    {
        bool isHolding = core.Input.GetAction(throwActionName);
        bool justPressed = core.Input.GetActionDown(throwActionName);
        bool justReleased = core.Input.GetActionUp(throwActionName);

        // ── Start aiming ──
        if (justPressed && CanThrow())
        {
            StartAim();
            return;
        }

        // ── While aiming ──
        if (isAiming && isHolding)
        {
            UpdateAim();
            return;
        }

        // ── Release: throw sword + cancel aim ──
        if (isAiming && justReleased)
        {
            StopAim();
            if (CanThrow())
            {
                ThrowSword();
            }
        }
    }

    private bool CanThrow()
    {
        // Can't throw while dead or dashing
        if (core.IsDead) return false;
        if (core.CurrentState == PlayerCore.PlayerState.Dashing) return false;
        if (core.CurrentState == PlayerCore.PlayerState.SprintDashing) return false;

        // Can't throw while exhausted (BlockHP depleted)
        if (combat != null && combat.IsExhausted) return false;

        return true;
    }

    private bool CanRecall()
    {
        // Sword must be stuck somewhere — recall during flight is no longer allowed.
        // (Auto-recall from max distance exceed is handled separately via event.)
        return activeSword != null && activeSword.IsStuck;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Aim Zoom & Time Slow
    // ════════════════════════════════════════════════════════════════════════

    private void StartAim()
    {
        isAiming = true;
        aimTimer = 0f;
        aimSlowTimer = 0f;
        aimSlowExpired = false;

        // Cache base sensitivity for restoration
        if (look != null)
        {
            baseSensitivity = look.GetSensitivity();
        }

        // Start aim slow-mo
        TimeManager.Instance.SetLayer("AimSlowMo", aimTimeScale, TimeManager.PRIORITY_SLOW_MO, blocksGameTime: false);
    }

    private void UpdateAim()
    {
        aimTimer += Time.unscaledDeltaTime;
        aimSlowTimer += Time.unscaledDeltaTime;

        // ── Zoom: ramp up over aimZoomInDuration ──
        float zoomProgress = Mathf.Clamp01(aimTimer / aimZoomInDuration);
        float currentZoom = Mathf.Lerp(1f, aimZoomFactor, zoomProgress);

        // Apply FOV override
        if (dashFOV != null)
        {
            float zoomedFOV = dashFOV.NormalFOV / currentZoom;
            dashFOV.SetFOVOverride(zoomedFOV);
        }

        // Scale sensitivity down proportionally to zoom
        if (look != null)
        {
            look.SetSensitivity(baseSensitivity / currentZoom);
        }

        // ── Time slow expiry ──
        if (!aimSlowExpired && aimSlowTimer >= aimSlowMaxDuration)
        {
            aimSlowExpired = true;
            TimeManager.Instance.RemoveLayer("AimSlowMo");
        }
    }

    private void StopAim()
    {
        if (!isAiming) return;

        isAiming = false;
        aimTimer = 0f;
        aimSlowTimer = 0f;
        aimSlowExpired = false;

        // Remove aim slow-mo layer
        TimeManager.Instance.RemoveLayer("AimSlowMo");

        // Clear FOV override (PlayerDashFOV will SmoothDamp back to normal)
        if (dashFOV != null)
        {
            dashFOV.ClearFOVOverride();
        }

        // Restore original sensitivity
        if (look != null)
        {
            look.SetSensitivity(baseSensitivity);
        }
    }

    /// <summary>
    /// Force-cancels aim state. Called externally (e.g. on death, dash start, etc.)
    /// </summary>
    public void CancelAim()
    {
        StopAim();
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
        activeSword.OnMaxDistanceExceeded += HandleSwordMaxDistanceExceeded;

        // Initialize and launch - pass removal damage, stun duration, and max distance
        activeSword.Initialize(
            throwDir,
            throwSpeed,
            returnSpeed,
            postRemovalStunDuration,
            swordRemovalDamage,
            throwLayerMask,
            maxThrowDistance
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

    /// <summary>
    /// Returns true if the stuck sword is close enough and reachable for instant dash pickup.
    /// This does not perform pickup by itself; it is called from PlayerDash only when dash input is initiated.
    /// </summary>
    private bool CanInstantPickupStuckSword()
    {
        if (autoPickupDistance <= 0f) return false;
        if (activeSword == null || !activeSword.IsStuck) return false;

        Vector3 swordPos = activeSword.transform.position;
        Vector3 playerPos = transform.position;
        float sqrDist = (swordPos - playerPos).sqrMagnitude;
        if (sqrDist > autoPickupDistance * autoPickupDistance) return false;

        return HasLineOfSightToSword(playerPos, swordPos);
    }

    private bool HasLineOfSightToSword(Vector3 playerPos, Vector3 swordPos)
    {
        Vector3 losOrigin = playerPos + Vector3.up * autoPickupLOSHeightOffset;
        Vector3 toSword = swordPos - losOrigin;
        float losDistance = toSword.magnitude;

        if (losDistance <= 0.01f) return true;

        return !Physics.Raycast(
            losOrigin,
            toSword.normalized,
            losDistance,
            autoPickupLOSMask,
            QueryTriggerInteraction.Ignore
        );
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
            activeSword.OnMaxDistanceExceeded -= HandleSwordMaxDistanceExceeded;
        }

        activeSword = null;
        RestoreSword();

        OnSwordCaught?.Invoke();
    }

    private void HandleSwordHit(GameObject target)
    {
        OnSwordHitTarget?.Invoke(target);
    }

    /// <summary>
    /// Fired by ThrownSword when it flies past maxThrowDistance without hitting anything.
    /// Auto-recalls the sword immediately (even mid-flight).
    /// </summary>
    private void HandleSwordMaxDistanceExceeded()
    {
        if (activeSword == null) return;
        if (activeSword.IsReturning) return;

        Debug.Log("[PlayerSwordThrow] Sword exceeded max throw distance - auto-recalling");

        Transform returnTarget = throwOrigin != null ? throwOrigin : transform;
        activeSword.Recall(returnTarget, catchDistance);
        OnSwordRecalled?.Invoke();
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
        CancelAim();

        if (activeSword != null)
        {
            Destroy(activeSword.gameObject);
            activeSword = null;
        }

        RestoreSword();
    }

    /// <summary>
    /// Instantly picks up a nearby stuck sword for the dash input flow.
    /// This is intentionally separate from RecallSword(): RMB recall still uses visible return flight,
    /// while dash pickup restores the held sword immediately before the attack dash starts.
    /// </summary>
    /// <returns>True if a stuck sword was picked up instantly.</returns>
    public bool TryInstantPickupForDash()
    {
        if (!CanInstantPickupStuckSword()) return false;

        if (activeSword != null)
        {
            activeSword.RemoveInstantlyFromTarget();

            activeSword.OnReturnedToPlayer -= HandleSwordReturned;
            activeSword.OnHitTarget -= HandleSwordHit;
            activeSword.OnMaxDistanceExceeded -= HandleSwordMaxDistanceExceeded;

            Destroy(activeSword.gameObject);
            activeSword = null;
        }

        RestoreSword();
        OnSwordCaught?.Invoke();
        return true;
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
        if (debugHasTarget)
        {
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
    }

    private void OnDrawGizmosSelected()
    {
        // Auto-pickup radius around player
        if (autoPickupDistance > 0f)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, autoPickupDistance);

            // LOS origin point
            Vector3 losOrigin = transform.position + Vector3.up * autoPickupLOSHeightOffset;
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(losOrigin, 0.08f);
        }
    }

    #endregion
}
