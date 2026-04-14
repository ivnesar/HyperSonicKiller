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
    #region Hidden Base Fields
    // ════════════════════════════════════════════════════════════════════════

    public override string[] HiddenBaseFields => new[]
    {
        "behaviorMode",      // Drone ist immer stationär
        "moveSpeed",         // Drone bewegt sich nicht
        "stoppingDistance",  // Drone bewegt sich nicht
        "maxRotationSpeed",  // Drone rotiert nicht
    };

    #endregion

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

    [Header("Zone Sphere")]
    [Tooltip("The sphere Transform (child sphere that visualizes the effect area in 3D)")]
    [SerializeField] private Transform zoneSphereTransform;

    [Header("Pulse Sphere")]
    [Tooltip("The sphere Transform (child sphere that pulses outward to show the drone is active)")]
    [SerializeField] private Transform pulseSphereTransform;

    [Tooltip("Duration of one pulse cycle in seconds")]
    [SerializeField] private float pulseDuration = 2f;

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

    // Player references (playerCore is inherited from NpcBase)
    private PlayerDash playerDash;

    // Zone tracking
    private bool playerInZone;
    private bool isDashDisabled;
    private float dashCancelTimer;
    private bool dashCancelActive;

    // Camera reference for billboard
    private Transform cameraTransform;

    // Pulse sphere
    private float pulseTimer;

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
        // Cache camera
        cameraTransform = Camera.main != null ? Camera.main.transform : null;

        // Setup billboard and sphere BEFORE base.Start(),
        // because base.Start() → OnStart() → Idle.Enter() activates them
        SetupBillboard();
        SetupZoneSphere();
        SetupPulseSphere();

        base.Start();

        // Cache PlayerDash (playerCore is already cached by NpcBase.Start())
        if (playerTransform != null)
        {
            playerDash = playerTransform.GetComponent<PlayerDash>();
        }

        if (playerCore == null)
        {
            Debug.LogError($"[AntiDashDrone] {name}: PlayerCore not found! Drone will not function.");
        }
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

        // Pulse sphere animation
        UpdatePulseSphere();
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

        // Hide billboard and zone spheres
        SetBillboardVisible(false);
        SetZoneSphereVisible(false);
        SetPulseSphereVisible(false);

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

    private void SetupZoneSphere()
    {
        if (zoneSphereTransform == null) return;

        // Ensure sphere stays centered on the drone
        zoneSphereTransform.localPosition = Vector3.zero;

        // Scale sphere to match effect radius diameter
        // Unity's default sphere has a radius of 0.5, so diameter = correct scale
        float diameter = effectRadius * 2f;
        zoneSphereTransform.localScale = new Vector3(diameter, diameter, diameter);

        // Disable the collider — sphere is visual only
        var collider = zoneSphereTransform.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
    }

    /// <summary>
    /// Rotates the billboard to always face the camera.
    /// </summary>
    private void UpdateBillboard()
    {
        if (billboardTransform == null || cameraTransform == null) return;
        if (!billboardTransform.gameObject.activeSelf) return;

        // Billboard faces toward camera position
        billboardTransform.LookAt(cameraTransform.position);
    }

    public void SetBillboardVisible(bool visible)
    {
        if (billboardTransform != null)
            billboardTransform.gameObject.SetActive(visible);
    }

    public void SetZoneSphereVisible(bool visible)
    {
        if (zoneSphereTransform != null)
            zoneSphereTransform.gameObject.SetActive(visible);
    }

    private void SetupPulseSphere()
    {
        if (pulseSphereTransform == null) return;

        // Ensure sphere stays centered on the drone
        pulseSphereTransform.localPosition = Vector3.zero;

        // Start at scale 0
        pulseSphereTransform.localScale = Vector3.zero;

        // Disable the collider — sphere is visual only
        var collider = pulseSphereTransform.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
    }

    private void UpdatePulseSphere()
    {
        if (pulseSphereTransform == null) return;
        if (!pulseSphereTransform.gameObject.activeSelf) return;

        // Advance timer
        pulseTimer += Time.deltaTime;

        // Loop: hard reset when cycle ends
        if (pulseTimer >= pulseDuration)
            pulseTimer -= pulseDuration;

        // Normalized progress 0→1
        float t = pulseTimer / pulseDuration;

        // Ease-out: starts fast, slows down (1 - (1-t)^2)
        float eased = 1f - (1f - t) * (1f - t);

        // Scale from 0 to effect radius diameter
        float diameter = effectRadius * 2f;
        float scale = eased * diameter;
        pulseSphereTransform.localScale = new Vector3(scale, scale, scale);
    }

    public void SetPulseSphereVisible(bool visible)
    {
        if (pulseSphereTransform == null) return;

        pulseSphereTransform.gameObject.SetActive(visible);

        // Reset pulse on activation so it always starts from 0
        if (visible)
        {
            pulseTimer = 0f;
            pulseSphereTransform.localScale = Vector3.zero;
        }
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
                ApplyPendingSwordDamage();
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
