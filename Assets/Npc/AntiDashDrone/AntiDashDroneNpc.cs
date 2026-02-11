using UnityEngine;

/// <summary>
/// Anti-Dash Drone - Stationary floating NPC that creates a no-dash zone.
/// 
/// Behavior:
/// 1. Hovers at a fixed position in the air (no movement, no rotation).
/// 2. Maintains a spherical area of effect around itself.
/// 3. While the player is inside this area:
///    - New dashes cannot be initiated
///    - Active dashes are cancelled after a short delay (unscaled time)
/// 4. A billboard quad displays the effect radius to the player (always faces camera).
/// 5. The drone can be destroyed by melee, thrown sword, or bullets.
/// 
/// States: Idle (active, anti-dash zone on) → Stunned → back to Idle
///         Any state → Dead (explosion + destroy)
/// 
/// NOTE: Does NOT use NavMesh, does NOT rotate toward player.
/// Inherits unused fields from NpcBase (behaviorMode, moveSpeed, etc.) — just ignore them.
/// </summary>
public class AntiDashDroneNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Anti-Dash Zone")]
    [Tooltip("Radius of the dash-blocking sphere around the drone")]
    [SerializeField] private float effectRadius = 8f;

    [Tooltip("Delay (unscaled seconds) before an active dash is cancelled")]
    [SerializeField] private float dashCancelDelay = 0.1f;

    [Header("Billboard")]
    [Tooltip("The billboard Transform (child quad/sprite that shows the effect area)")]
    [SerializeField] private Transform billboardTransform;

    [Header("Death Effect")]
    [Tooltip("Optional particle effect prefab spawned on death")]
    [SerializeField] private GameObject explosionEffectPrefab;

    [Header("Audio")]
    [SerializeField] private AudioClip playerEnterSound;
    [SerializeField] private AudioClip playerExitSound;
    [SerializeField] private AudioClip explosionSound;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors
    // ════════════════════════════════════════════════════════════════════════

    public float EffectRadius => effectRadius;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<AntiDashDroneNpc> currentState;

    // Player references
    private PlayerCore playerCore;
    private PlayerDash playerDash;

    // Zone tracking
    private bool playerInZone;
    private bool isDashDisabled;
    private float dashCancelTimer;
    private bool dashCancelActive;

    // Camera reference for billboard
    private Transform cameraTransform;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();

        // Anti-Dash Drone does NOT use NavMesh
        if (navAgent != null)
        {
            navAgent.enabled = false;
        }
    }

    protected override void Start()
    {
        base.Start();

        // Cache player components
        if (playerTransform != null)
        {
            playerCore = playerTransform.GetComponent<PlayerCore>();
            playerDash = playerTransform.GetComponent<PlayerDash>();
        }

        if (playerCore == null)
        {
            Debug.LogError($"[AntiDashDrone] {name}: PlayerCore not found! Drone will not function.");
        }

        // Cache camera
        cameraTransform = Camera.main != null ? Camera.main.transform : null;

        // Setup billboard scale to match effect radius
        SetupBillboard();
    }

    protected override void OnStart()
    {
        ChangeState(new AntiDashDroneStates.Idle());
    }

    protected override void Update()
    {
        if (isDead) return;

        // Stun handling (from NpcBase)
        if (isStunned)
        {
            // While stunned, disable the anti-dash zone
            DisableZone();
            HandleStunnedInternal();
            return;
        }

        // State machine
        if (currentState != null)
        {
            var nextState = currentState.Update(this);
            if (nextState != null)
                ChangeState(nextState);
        }

        // Billboard always faces camera (only in Idle)
        UpdateBillboard();
    }

    protected override void UpdateBehavior()
    {
        // Handled by state machine in Update()
    }

    protected override void OnStunStart()
    {
        ChangeState(new AntiDashDroneStates.Stunned());
    }

    protected override void OnStunEnd()
    {
        ChangeState(new AntiDashDroneStates.Idle());
    }

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.AntiDashDrone;
    public override int GetStateID() => currentState?.StateID ?? -1;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Death Override
    // ════════════════════════════════════════════════════════════════════════

    protected override void Die()
    {
        if (isDead) return;

        // Cleanup zone before dying
        DisableZone();

        isDead = true;
        isStunned = false;
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        // Explosion effect
        PlaySound(explosionSound);
        if (explosionEffectPrefab != null)
        {
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        }

        // Hide billboard
        SetBillboardVisible(false);

        // Change to dead state
        ChangeState(new AntiDashDroneStates.Dead());

        // Disable visuals (mesh renderer etc.)
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
            r.enabled = false;

        // Destroy after delay
        if (destroyDelay >= 0)
            Destroy(gameObject, destroyDelay);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Anti-Dash Zone Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called every frame by the Idle state. Checks player distance
    /// and manages dash blocking.
    /// </summary>
    public void UpdateZoneCheck()
    {
        if (playerCore == null || playerDash == null) return;

        float distance = DistanceToTarget;
        bool wasInZone = playerInZone;
        playerInZone = distance <= effectRadius;

        // Player entered zone
        if (playerInZone && !wasInZone)
        {
            OnPlayerEnterZone();
        }
        // Player exited zone
        else if (!playerInZone && wasInZone)
        {
            OnPlayerExitZone();
        }

        // If player is in zone and currently dashing, run cancel timer
        if (playerInZone)
        {
            CheckDashCancellation();
        }
    }

    private void OnPlayerEnterZone()
    {
        PlaySound(playerEnterSound);

        // Block new dashes
        if (!isDashDisabled)
        {
            playerDash.SetDashEnabled(false);
            isDashDisabled = true;
        }

        // If player is already dashing, start cancel countdown
        if (playerCore.CurrentState == PlayerCore.PlayerState.Dashing)
        {
            StartDashCancelTimer();
        }
    }

    private void OnPlayerExitZone()
    {
        PlaySound(playerExitSound);

        // Re-enable dashes
        if (isDashDisabled)
        {
            playerDash.SetDashEnabled(true);
            isDashDisabled = false;
        }

        // Stop cancel timer
        dashCancelActive = false;
        dashCancelTimer = 0f;
    }

    private void CheckDashCancellation()
    {
        bool isPlayerDashing = playerCore.CurrentState == PlayerCore.PlayerState.Dashing;

        if (isPlayerDashing)
        {
            if (!dashCancelActive)
            {
                StartDashCancelTimer();
            }
            else
            {
                // Tick timer (unscaled so it works during time-slow)
                dashCancelTimer -= Time.unscaledDeltaTime;

                if (dashCancelTimer <= 0f)
                {
                    // Cancel the dash!
                    playerDash.ForceCancelDash();
                    dashCancelActive = false;

                    Debug.Log($"[AntiDashDrone] {name}: Cancelled player dash!");
                }
            }
        }
        else
        {
            // Player stopped dashing, reset timer
            dashCancelActive = false;
        }
    }

    private void StartDashCancelTimer()
    {
        dashCancelActive = true;
        dashCancelTimer = dashCancelDelay;
    }

    /// <summary>
    /// Disables the zone effect and re-enables player dash.
    /// Called when drone is stunned or dies.
    /// </summary>
    public void DisableZone()
    {
        if (isDashDisabled && playerDash != null)
        {
            playerDash.SetDashEnabled(true);
            isDashDisabled = false;
        }

        playerInZone = false;
        dashCancelActive = false;
        dashCancelTimer = 0f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Billboard
    // ════════════════════════════════════════════════════════════════════════

    private void SetupBillboard()
    {
        if (billboardTransform == null) return;

        // Scale billboard to match effect radius diameter
        float diameter = effectRadius * 2f;
        billboardTransform.localScale = new Vector3(diameter, diameter, 1f);
    }

    /// <summary>
    /// Rotates the billboard to always face the camera.
    /// </summary>
    private void UpdateBillboard()
    {
        if (billboardTransform == null || cameraTransform == null) return;
        if (!billboardTransform.gameObject.activeSelf) return;

        // Look at camera (billboard faces toward camera)
        billboardTransform.LookAt(
            billboardTransform.position + cameraTransform.forward
        );
    }

    public void SetBillboardVisible(bool visible)
    {
        if (billboardTransform != null)
            billboardTransform.gameObject.SetActive(visible);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Machine
    // ════════════════════════════════════════════════════════════════════════

    private void ChangeState(INpcState<AntiDashDroneNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Stun Handling
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mirrors NpcBase.HandleStunned() but without NavMesh calls.
    /// </summary>
    private void HandleStunnedInternal()
    {
        if (hasSwordEmbedded) return;

        if (Time.time >= stunEndTime)
        {
            isStunned = false;

            if (animator != null)
                animator.SetBool("IsStunned", false);

            if (hasPendingSwordDamage)
            {
                // Let NpcBase handle pending damage through normal flow
                // This triggers via the base EndStun → ApplyPendingSwordDamage
            }

            if (!isDead)
                OnStunEnd();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    private void OnDestroy()
    {
        // Safety: always re-enable dash when drone is destroyed
        if (isDashDisabled && playerDash != null)
        {
            playerDash.SetDashEnabled(true);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        // Effect radius (always visible in editor when selected)
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.25f);
        Gizmos.DrawSphere(transform.position, effectRadius);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, effectRadius);

        // At runtime: show player connection
        if (Application.isPlaying && playerTransform != null)
        {
            Gizmos.color = playerInZone ? Color.red : Color.green;
            Gizmos.DrawLine(transform.position, playerTransform.position);
        }
    }

    // Also show radius when not selected (dimmer)
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, effectRadius);
    }

    #endregion
}
