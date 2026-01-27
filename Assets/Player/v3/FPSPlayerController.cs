using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(scrPlayerInputHandler))]
public class FPSPlayerController : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Enums & State
    // ────────────────────────────────────────────────────────────────────────────────

    public enum PlayerState
    {
        Normal,
        Dashing,
        StuckToSurface,
        Jumping,
        Dead
    }

    [HideInInspector] public PlayerState currentState;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Core & Stats
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Core Settings")]
    public int BlockHP = 130;
    [SerializeField] private int maxBlockHP = 130;

    [Header("Damage System")]
    [SerializeField] private int damageThreshold = 100;
    [SerializeField] private float swordRecoveryTime = 2f;
    [SerializeField] private bool showDamageDebugInfo = true;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Movement
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float sprintInitialBoost = 12f;
    [SerializeField] private AnimationCurve sprintDecayCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private float sprintDecayDuration = 1f;
    [SerializeField] private float sprintResetDelay = 1f;
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float gravity = 20f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Look & Camera
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Look Settings")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float lookSensitivity = 2f;
    [SerializeField] private float maxLookAngle = 80f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Dash & Wall Stick
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Dash Settings")] 
    [SerializeField] private int dashCharges = 3;
    [SerializeField] private float slowDown = 0.1f;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashMaxDistance = 15f;
    [SerializeField] private LayerMask dashSurfaceLayer = -1;
    [SerializeField] private float wallStickDistance = 1f;
    [SerializeField] private float dashCancelUpwardForce = 10f;
    [SerializeField] private float dashCancelDownwardForce = 15f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Dart / Projectile
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Dart Settings")]
    [SerializeField] private scrPlayerProjectile dart;
    [SerializeField] private float throwCooldown = 0.5f;
    [SerializeField] private float throwSpeed = 50f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Death
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Death Settings")]
    [SerializeField] private float deathCameraRotationSpeed = 30f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Components & References
    // ────────────────────────────────────────────────────────────────────────────────

    private CharacterController controller;
    private scrPlayerInputHandler _input;
    public Camera playerCamera;

    private SwordCombatSystem swordCombatSystem;
    private scrLocalGameManager lgm;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Events
    // ────────────────────────────────────────────────────────────────────────────────

    public delegate void PlayerStateChangedHandler(PlayerState oldState, PlayerState newState);
    public event PlayerStateChangedHandler OnPlayerStateChanged;

    public delegate void PlayerDeathHandler();
    public event PlayerDeathHandler OnPlayerDeath;

    public delegate void PlayerDamagedHandler(int damage, int currentHP, int maxHP);
    public event PlayerDamagedHandler OnPlayerDamaged;

    public delegate void SwordDisabledHandler(float recoveryTime);
    public event SwordDisabledHandler OnSwordDisabled;

    public delegate void SwordRecoveredHandler();
    public event SwordRecoveredHandler OnSwordRecovered;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime / State Variables
    // ────────────────────────────────────────────────────────────────────────────────

    private Vector3 moveDirection;
    private float verticalVelocity;
    private float currentLookAngle;
    private float lastThrowTime;

    // Sprint
    private float sprintStartTime;
    private float sprintHoldDuration;
    private bool isSprintingLastFrame;
    private bool sprintDecayActive;

    // Dash
    private Vector3 dashStartPosition;
    private Vector3 dashTargetPosition;
    private Vector3 dashDirection;
    private float dashProgress;

    // Wall stick
    private Vector3 stuckPosition;
    private Vector3 surfaceNormal;

    // Death
    private float deathTime;

    // Flags
    private bool dashDisabled = false;
    private bool movementDisabled = false;

    // Damage System
    private int accumulatedDamage = 0;
    private bool swordDisabled = false;
    private Coroutine swordRecoveryCoroutine;

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    void Start()
    {
        lgm = scrLocalGameManager.Instance;
        playerCamera = GetComponentInChildren<Camera>();

        controller = GetComponent<CharacterController>();
        _input = GetComponent<scrPlayerInputHandler>();

        swordCombatSystem = GetComponent<SwordCombatSystem>();

        if (swordCombatSystem != null)
        {
            swordCombatSystem.OnCombatStateChanged += HandleCombatStateChange;
        }

        if (cameraTransform == null)
        {
            cameraTransform = Camera.main.transform;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SetState(PlayerState.Normal);
        currentDashCharges = dashCharges;
        
        maxBlockHP = BlockHP;
    }

    void Update()
    {
        if (currentState != PlayerState.Dead)
        {
            HandleLook();
            HandleThrowDart();
        }

        switch (currentState)
        {
            case PlayerState.Normal:    UpdateNormalState();    break;
            case PlayerState.Dashing:   UpdateDashingState();   break;
            case PlayerState.StuckToSurface: UpdateStuckState(); break;
            case PlayerState.Jumping:   UpdateJumpingState();   break;
            case PlayerState.Dead:      UpdateDeadState();      break;
        }
    }

    void OnDestroy()
    {
        if (swordCombatSystem != null)
        {
            swordCombatSystem.OnCombatStateChanged -= HandleCombatStateChange;
        }
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────────────

    public void Die()
    {
        if (currentState != PlayerState.Dead)
        {
            SetState(PlayerState.Dead);
            OnPlayerDeath?.Invoke();
            
            if (showDamageDebugInfo)
            {
                Debug.Log("[Player] Died! BlockHP reached 0.");
            }
        }
    }

    public void Revive(int restoreHP = 100)
    {
        if (currentState == PlayerState.Dead)
        {
            BlockHP = restoreHP;
            accumulatedDamage = 0;
            swordDisabled = false;
            
            if (swordRecoveryCoroutine != null)
            {
                StopCoroutine(swordRecoveryCoroutine);
                swordRecoveryCoroutine = null;
            }
            
            SetState(PlayerState.Normal);
            
            if (showDamageDebugInfo)
            {
                Debug.Log($"[Player] Revived! BlockHP restored to {BlockHP}.");
            }
        }
    }

    public bool IsDead() => currentState == PlayerState.Dead;

    public void DisableDash()   => dashDisabled = true;
    public void EnableDash()    => dashDisabled = false;

    public void CancelDash(bool applyFallVelocity = true)
    {
        if (currentState == PlayerState.Dashing || currentState == PlayerState.StuckToSurface)
        {
            verticalVelocity = applyFallVelocity ? -5f : 0f;
            SetState(PlayerState.Jumping);
        }
    }

    public bool CanDash()
    {
        if (dashDisabled) return false;
        if (currentDashCharges <= 0) return false;
        return true;
    }

    public bool CanUseSword() => !swordDisabled && currentState != PlayerState.Dead;
    public int GetCurrentHP() => BlockHP;
    public int GetMaxHP() => maxBlockHP;
    public int GetAccumulatedDamage() => accumulatedDamage;
    public bool IsSwordDisabled() => swordDisabled;
    public PlayerState GetCurrentState() => currentState;

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region State Machine
    // ────────────────────────────────────────────────────────────────────────────────

    private void SetState(PlayerState newState)
    {
        if (currentState == newState) return;

        PlayerState oldState = currentState;
        currentState = newState;

        OnPlayerStateChanged?.Invoke(oldState, newState);

        switch (newState)
        {
            case PlayerState.Dead:
                deathTime = Time.time;
                verticalVelocity = 0f;
                moveDirection = Vector3.zero;
                break;

            case PlayerState.StuckToSurface:
                stuckPosition = transform.position;
                verticalVelocity = 0f;
                moveDirection = Vector3.zero;
                break;

            case PlayerState.Normal:
                if (oldState == PlayerState.Dashing || oldState == PlayerState.StuckToSurface)
                {
                    verticalVelocity = 0f;
                }
                break;
        }
    }

    private void UpdateNormalState()
    {
        HandleMovement();
        HandleJump();
        HandleDashInput();
        ApplyGravity();
    }

    private void UpdateDashingState()
    {
        ProcessDashMovement();
        CheckDashCancels();
    }

    private void UpdateStuckState()
    {
        CheckUnstickInputs();
    }

    private void UpdateJumpingState()
    {
        HandleMovement();
        HandleDashInput();
        ApplyGravity();

        if (controller.isGrounded && verticalVelocity <= 0)
        {
            SetState(PlayerState.Normal);
            currentDashCharges = dashCharges;
        }
    }

    private void UpdateDeadState()
    {
        float rotationAmount = deathCameraRotationSpeed * Time.deltaTime;
        cameraTransform.Rotate(Vector3.forward, rotationAmount);
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Movement & Physics
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        if (movementDisabled) return;

        Vector2 input = _input.GetMoveInput();

        bool isSprinting = input.magnitude > 0.1f && input.y > 0;

        if (isSprinting && !isSprintingLastFrame)
        {
            sprintStartTime = Time.time;
            sprintHoldDuration = 0f;
            sprintDecayActive = false;
        }

        if (isSprinting)
        {
            sprintHoldDuration = Time.time - sprintStartTime;
        }

        if (!isSprinting && isSprintingLastFrame)
        {
            sprintDecayActive = true;
            sprintStartTime = Time.time;
        }

        isSprintingLastFrame = isSprinting;

        float targetSpeed = walkSpeed;
        float speedBoost = 0f;

        if (isSprinting)
        {
            targetSpeed = runSpeed;
            speedBoost = sprintInitialBoost;
        }
        else if (sprintDecayActive)
        {
            float timeSinceRelease = Time.time - sprintStartTime;

            if (timeSinceRelease < sprintResetDelay)
            {
                targetSpeed = runSpeed;
                speedBoost = sprintInitialBoost;
            }
            else
            {
                float decayProgress = (timeSinceRelease - sprintResetDelay) / sprintDecayDuration;
                decayProgress = Mathf.Clamp01(decayProgress);
                speedBoost = sprintInitialBoost * sprintDecayCurve.Evaluate(1f - decayProgress);

                if (decayProgress >= 1f)
                {
                    sprintDecayActive = false;
                }
            }
        }

        float finalSpeed = targetSpeed + speedBoost;

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        moveDirection = (forward * input.y + right * input.x).normalized * finalSpeed;
        Vector3 move = moveDirection * Time.deltaTime * lgm.TimeDialation;

        controller.Move(move);
    }

    private void HandleJump()
    {
        if (_input.GetActionState("Jump") == scrPlayerInputHandler.InputState.Press)
        {
            if (controller.isGrounded)
            {
                verticalVelocity = jumpForce;
                SetState(PlayerState.Jumping);
            }
        }
    }

    private void ApplyGravity()
    {
        if (!controller.isGrounded)
        {
            verticalVelocity -= gravity * Time.deltaTime * lgm.TimeDialation;
        }
        else if (verticalVelocity < 0)
        {
            verticalVelocity = -2f;
        }

        Vector3 verticalMove = new Vector3(0, verticalVelocity, 0) * Time.deltaTime * lgm.TimeDialation;
        controller.Move(verticalMove);
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Look / Camera
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleLook()
    {
        Vector2 lookInput = _input.GetLookInput();

        transform.Rotate(Vector3.up * lookInput.x * lookSensitivity);

        currentLookAngle -= lookInput.y * lookSensitivity;
        currentLookAngle = Mathf.Clamp(currentLookAngle, -maxLookAngle, maxLookAngle);

        cameraTransform.localEulerAngles = new Vector3(currentLookAngle, 0f, 0f);
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Dart / Projectile
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleThrowDart()
    {
        if (_input.GetActionState("ThrowDart") == scrPlayerInputHandler.InputState.Press)
        {
            if (Time.time - lastThrowTime >= throwCooldown)
            {
                PerformThrowDart();
                lastThrowTime = Time.time;
            }
        }
    }

    private void PerformThrowDart()
    {
        Quaternion spawnRotation = playerCamera.transform.rotation;
        Vector3 spawnPosition = cameraTransform.position + new Vector3(0, -0.3f, 0);

        scrPlayerProjectile thisDart = Instantiate(dart, spawnPosition, spawnRotation);
        thisDart.speed = throwSpeed;
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Damage System
    // ────────────────────────────────────────────────────────────────────────────────

    public void TakeDamage(int damage)
    {
        if (currentState == PlayerState.Dead) return;

        // Check if sword can block the damage
        if (swordCombatSystem != null && swordCombatSystem.TryBlockDamage(damage))
        {
            if (showDamageDebugInfo)
            {
                Debug.Log($"[Player] Blocked {damage} damage with sword!");
            }
            return;
        }

        // Apply damage to block HP
        BlockHP -= damage;
        accumulatedDamage += damage;

        if (showDamageDebugInfo)
        {
            Debug.Log($"[Player] Took {damage} damage! BlockHP: {BlockHP}/{maxBlockHP} | Accumulated: {accumulatedDamage}");
        }

        // Trigger damage event
        OnPlayerDamaged?.Invoke(damage, BlockHP, maxBlockHP);

        // Check if damage threshold reached
        if (accumulatedDamage >= damageThreshold && !swordDisabled)
        {
            DisableSword();
        }

        // Check for death
        if (BlockHP <= 0)
        {
            BlockHP = 0;
            Die();
        }
    }

    private void DisableSword()
    {
        swordDisabled = true;

        if (showDamageDebugInfo)
        {
            Debug.Log($"[Player] Sword disabled! Accumulated damage: {accumulatedDamage}. Recovery in {swordRecoveryTime}s.");
        }

        // Notify sword combat system
        if (swordCombatSystem != null)
        {
            swordCombatSystem.DisableSword();
        }

        // Trigger event
        OnSwordDisabled?.Invoke(swordRecoveryTime);

        // Start recovery coroutine
        if (swordRecoveryCoroutine != null)
        {
            StopCoroutine(swordRecoveryCoroutine);
        }
        swordRecoveryCoroutine = StartCoroutine(SwordRecoveryRoutine());
    }

    private IEnumerator SwordRecoveryRoutine()
    {
        yield return new WaitForSeconds(swordRecoveryTime);

        RecoverSword();
    }

    private void RecoverSword()
    {
        swordDisabled = false;
        accumulatedDamage = 0;
        BlockHP = maxBlockHP;

        if (showDamageDebugInfo)
        {
            Debug.Log($"[Player] Sword recovered! BlockHP restored to {BlockHP}.");
        }

        // Notify sword combat system
        if (swordCombatSystem != null)
        {
            swordCombatSystem.EnableSword();
        }

        // Trigger event
        OnSwordRecovered?.Invoke();

        swordRecoveryCoroutine = null;
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Combat Integration
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleCombatStateChange(SwordCombatSystem.CombatState newCombatState)
    {
        switch (newCombatState)
        {
            case SwordCombatSystem.CombatState.Broken:
                CancelDash(true);
                DisableDash();
                break;

            case SwordCombatSystem.CombatState.Blocking:
                DisableDash();
                break;

            case SwordCombatSystem.CombatState.Idle:
            case SwordCombatSystem.CombatState.Thrown:
            case SwordCombatSystem.CombatState.Attacking:
                EnableDash();
                break;
        }
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Dash System
    // ────────────────────────────────────────────────────────────────────────────────

    public int currentDashCharges;
    
    private void HandleDashInput()
    {
        if (dashDisabled) return;
        
        if (_input.GetActionState("Dash") == scrPlayerInputHandler.InputState.Press)
        {
            if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, dashMaxDistance, dashSurfaceLayer))
            {
                if (!IsCurrentSurface(hit))
                {
                    StartDash(hit.point, hit.normal);
                }
            }
        }
    }

    private bool IsCurrentSurface(RaycastHit hit)
    {
        // Already stuck to this surface?
        if (currentState == PlayerState.StuckToSurface)
        {
            if (Physics.Raycast(transform.position, -surfaceNormal, out RaycastHit surfaceHit, wallStickDistance + 0.5f))
            {
                if (surfaceHit.collider == hit.collider) return true;
            }
        }

        // Standing on this surface?
        if (controller.isGrounded && currentState != PlayerState.StuckToSurface)
        {
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit groundHit, controller.height / 2 + 0.2f))
            {
                if (groundHit.collider == hit.collider) return true;
            }
        }

        return false;
    }

    private void StartDash(Vector3 targetPoint, Vector3 normal)
    {
        if (currentDashCharges <= 0) return;
        currentDashCharges--;
        
        dashStartPosition = transform.position;
        dashTargetPosition = targetPoint;
        dashDirection = (dashTargetPosition - dashStartPosition).normalized;
        dashProgress = 0f;
        surfaceNormal = normal;

        SetState(PlayerState.Dashing);
    }

    private void ProcessDashMovement()
    {
        float dashDistance = Vector3.Distance(dashStartPosition, dashTargetPosition);
        float moveDistance = dashSpeed * Time.deltaTime * lgm.TimeDialation;

        dashProgress += moveDistance / dashDistance;

        if (dashProgress >= 1f)
        {
            controller.Move(dashTargetPosition - transform.position);
            CompleteDash();
        }
        else
        {
            controller.Move(dashDirection * moveDistance);
        }
    }

    private void CheckDashCancels()
    {
        if (_input.GetActionState("Dash") == scrPlayerInputHandler.InputState.Press)
        {
            if (!dashDisabled && Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, dashMaxDistance, dashSurfaceLayer))
            {
                if (!IsCurrentSurface(hit))
                {
                    StartDash(hit.point, hit.normal);
                }
            }
        }
        else if (_input.GetActionState("Jump") == scrPlayerInputHandler.InputState.Press)
        {
            verticalVelocity = dashCancelUpwardForce;
            SetState(PlayerState.Jumping);
        }
        else if (_input.GetActionState("DashCancelDown") == scrPlayerInputHandler.InputState.Press)
        {
            verticalVelocity = -dashCancelDownwardForce;
            SetState(PlayerState.Jumping);
        }
    }

    private void CompleteDash()
    {
        SetState(controller.isGrounded ? PlayerState.Normal : PlayerState.StuckToSurface);
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Wall-Stick / Unstick Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private void CheckUnstickInputs()
    {
        if (_input.GetActionState("Jump") == scrPlayerInputHandler.InputState.Press)
        {
            verticalVelocity = jumpForce;
            SetState(PlayerState.Jumping);
        }
        else if (_input.GetActionState("DashCancelDown") == scrPlayerInputHandler.InputState.Press)
        {
            verticalVelocity = -dashCancelDownwardForce;
            SetState(PlayerState.Jumping);
        }
        else if (_input.GetActionState("Dash") == scrPlayerInputHandler.InputState.Press)
        {
            HandleDashInput();
        }
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Gizmos
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (currentState == PlayerState.Dashing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(dashStartPosition, dashTargetPosition);
            Gizmos.DrawWireSphere(dashTargetPosition, 0.5f);
        }

        if (currentState == PlayerState.StuckToSurface)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(stuckPosition, 0.3f);
            Gizmos.DrawLine(stuckPosition, stuckPosition + surfaceNormal * 2f);
        }
    }

    #endregion
}