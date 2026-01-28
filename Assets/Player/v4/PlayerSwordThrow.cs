using UnityEngine;
using System;

/// <summary>
/// Manages sword throwing mechanics for the player.
/// Separated from PlayerCombat for cleaner code organization.
/// Communicates with PlayerCombat to disable attack/block while sword is thrown.
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
    [SerializeField] private LayerMask throwLayerMask = -1;

    [Header("Input")]
    [SerializeField] private string throwActionName = "ThrowSword";

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private ThrownSword activeSword;
    private bool hasSword = true;

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
            // Player doesn't have sword -> recall it
            RecallSword();
        }
    }

    private bool CanThrow()
    {
        // Can't throw while dead or dashing
        if (core.IsDead) return false;
        if (core.CurrentState == PlayerCore.PlayerState.Dashing) return false;

        return true;
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
        Vector3 throwDir = core.CameraTransform.forward;

        // Spawn thrown sword
        activeSword = Instantiate(thrownSwordPrefab, spawnPos, Quaternion.LookRotation(throwDir));

        // Subscribe to events
        activeSword.OnReturnedToPlayer += HandleSwordReturned;
        activeSword.OnHitTarget += HandleSwordHit;

        // Initialize and launch
        activeSword.Initialize(throwDir, throwSpeed, returnSpeed, throwLayerMask);

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
    /// Reset to initial state (e.g., on respawn).
    /// </summary>
    public void ResetState()
    {
        ForceRecall();
    }

    #endregion
}