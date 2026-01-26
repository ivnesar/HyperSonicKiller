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
    public int HP = 100;

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
        }
    }

    public void Revive(int restoreHP = 100)
    {
        if (currentState == PlayerState.Dead)
        {
            HP = restoreHP;
            SetState(PlayerState.Normal);
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
        => !dashDisabled && (currentState == PlayerState.Normal || currentState == PlayerState.Jumping);

    public PlayerState GetCurrentState() => currentState;

    public void DisableMovement() => movementDisabled = true;
    public void EnableMovement()  => movementDisabled = false;

    public void ForceState(PlayerState state) => SetState(state);

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region State Machine Core
    // ────────────────────────────────────────────────────────────────────────────────

    private void SetState(PlayerState newState)
    {
        if (currentState == newState) return;

        PlayerState oldState = currentState;
        ExitState(currentState);
        currentState = newState;
        EnterState(newState);

        OnPlayerStateChanged?.Invoke(oldState, newState);
    }

    private void EnterState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Normal:
                verticalVelocity = 0f;
                break;
            case PlayerState.Dashing:
                Time.timeScale = slowDown;
                break;
            case PlayerState.StuckToSurface:
                stuckPosition = transform.position;
                verticalVelocity = 0f;
                break;
            case PlayerState.Jumping:
                break;
            case PlayerState.Dead:
                deathTime = Time.time;
                movementDisabled = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
        }
    }

    private void ExitState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Dashing:
                Time.timeScale = 1;
                dashProgress = 0f;
                break;
            case PlayerState.Dead:
                movementDisabled = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                break;
        }
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region State Update Methods
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateNormalState()
    {
        HandleMovement();
        HandleJump();
        HandleDashInput();
    }

    private void UpdateDashingState()
    {
        ProcessDashMovement();
        CheckDashCancels();
    }

    private void UpdateStuckState()
    {
        transform.position = stuckPosition;
        CheckUnstickInputs();
    }

    private void UpdateJumpingState()
    {
        HandleMovement();
        ApplyGravity();
        HandleDashInput();

        if (controller.isGrounded && verticalVelocity <= 0f)
        {
            SetState(PlayerState.Normal);
        }
    }

    private void UpdateDeadState()
    {
        ApplyGravity();
        float timeSinceDeath = Time.time - deathTime;
        cameraTransform.Rotate(Vector3.forward * deathCameraRotationSpeed * Time.deltaTime);
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Core Movement & Physics
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        if (movementDisabled)
        {
            moveDirection = Vector3.zero;
            return;
        }

        Vector2 moveInput = _input.GetMoveInput();
        Vector3 forward = transform.forward * moveInput.y;
        Vector3 right   = transform.right   * moveInput.x;
        Vector3 movement = (forward + right).normalized;

        UpdateSprintState();

        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? GetCurrentSprintSpeed() : walkSpeed;
        moveDirection = movement * currentSpeed;
    }

    private void HandleJump()
    {
        if (controller.isGrounded)
        {
            verticalVelocity = -1f;

            if (_input.GetActionState("Jump") == scrPlayerInputHandler.InputState.Press)
            {
                verticalVelocity = jumpForce;
                SetState(PlayerState.Jumping);
            }
        }

        moveDirection.y = verticalVelocity;
        controller.Move(moveDirection * Time.deltaTime);
    }

    private void ApplyGravity()
    {
        verticalVelocity -= gravity * Time.deltaTime;
        moveDirection.y = verticalVelocity;
        controller.Move(moveDirection * Time.deltaTime);
    }

    #endregion


    // ────────────────────────────────────────────────────────────────────────────────
    #region Sprint Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateSprintState()
    {
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        if (isSprinting)
        {
            if (!isSprintingLastFrame)
            {
                if (!sprintDecayActive)
                {
                    sprintStartTime = Time.time;
                    sprintHoldDuration = 0f;
                }
                else
                {
                    sprintStartTime = Time.time - sprintHoldDuration;
                }
            }
            else
            {
                sprintHoldDuration = Time.time - sprintStartTime;

                if (sprintHoldDuration >= sprintResetDelay)
                    sprintDecayActive = false;
            }
        }
        else if (isSprintingLastFrame)
        {
            if (sprintHoldDuration < sprintResetDelay)
                sprintDecayActive = true;
        }

        isSprintingLastFrame = isSprinting;
    }

    private float GetCurrentSprintSpeed()
    {
        float decayProgress = Mathf.Clamp01(sprintHoldDuration / sprintDecayDuration);
        float curveValue = sprintDecayCurve.Evaluate(decayProgress);
        return Mathf.Lerp(runSpeed, sprintInitialBoost, curveValue);
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
    #region Damage & Combat Integration
    // ────────────────────────────────────────────────────────────────────────────────

    public void TakeDamage(int damage)
    {
        if (currentState == PlayerState.Dead) return;

        if (swordCombatSystem != null && swordCombatSystem.TryBlockDamage(damage))
            return;

        HP -= damage;

        if (HP <= 0)
        {
            HP = 0;
            Die();
        }
    }

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
            HandleDashInput(); // reuse same logic
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