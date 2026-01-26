using UnityEngine;

[RequireComponent(typeof(scrPlayerInputHandler))]
public class SwordCombatSystem : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Enums & State
    // ────────────────────────────────────────────────────────────────────────────────

    public enum CombatState
    {
        Idle,
        Blocking,
        Broken,
        Thrown,
        Attacking
    }

    [HideInInspector] public CombatState currentCombatState;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – References
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private GameObject heldSwordVisual;
    [SerializeField] private scrThrownSword thrownSwordPrefab;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform throwOrigin;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Melee Attack
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Melee Attack Settings")]
    [SerializeField] private float attackRange = 3f;
    [SerializeField] private float attackAngle = 30f;
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private float attackCooldown = 0.5f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Block
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Block Settings")]
    [SerializeField] private float maxBlockHP = 100f;
    [SerializeField] private float blockRechargeRate = 10f;
    [SerializeField] private float brokenStunDuration = 3.0f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Throw
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Throw Settings")]
    [SerializeField] private float throwForce = 40f;
    [SerializeField] private float maxThrowDistance = 100f;
    [SerializeField] private LayerMask throwableLayerMask = ~0;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug / Status
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Debug/Status")]
    [SerializeField] private float currentBlockHP;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Components & References
    // ────────────────────────────────────────────────────────────────────────────────

    private scrPlayerInputHandler input;
    private scrThrownSword currentThrownInstance;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Events
    // ────────────────────────────────────────────────────────────────────────────────

    public delegate void CombatStateChangedHandler(CombatState newState);
    public event CombatStateChangedHandler OnCombatStateChanged;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime Variables
    // ────────────────────────────────────────────────────────────────────────────────

    private float stunTimer;
    private float lastAttackTime;
    private float lastThrowActionTime;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        input = GetComponent<scrPlayerInputHandler>();
        currentBlockHP = maxBlockHP;
        currentCombatState = CombatState.Idle;

        if (heldSwordVisual == null)
            Debug.LogError("Assign the Held Sword Visual object in inspector!");
    }

    private void Update()
    {
        HandleCombatState();
        RegenerateBlockHP();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────────────

    public CombatState GetCurrentState() => currentCombatState;

    public void RecallSword()
    {
        if (currentCombatState == CombatState.Thrown && currentThrownInstance != null)
        {
            currentThrownInstance.Recall(throwOrigin);
        }
    }

    public bool CanAttack()
        => currentCombatState == CombatState.Idle && Time.time - lastAttackTime >= attackCooldown;

    public bool CanBlock()
        => currentCombatState == CombatState.Idle || currentCombatState == CombatState.Blocking;

    public void ForceGuardBreak()
    {
        if (currentCombatState == CombatState.Blocking)
        {
            currentBlockHP = 0;
            stunTimer = brokenStunDuration;
            EnterState(CombatState.Broken);
        }
    }

    public float GetBlockHPPercent() => currentBlockHP / maxBlockHP;

    public bool TryBlockDamage(float damageAmount)
    {
        if (currentCombatState != CombatState.Blocking) return false;

        currentBlockHP -= damageAmount;

        if (currentBlockHP <= 0)
        {
            currentBlockHP = 0;
            stunTimer = brokenStunDuration;
            EnterState(CombatState.Broken);
        }

        return true;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Machine Core
    // ────────────────────────────────────────────────────────────────────────────────

    private void EnterState(CombatState newState)
    {
        CombatState oldState = currentCombatState;
        currentCombatState = newState;

        UpdateSwordVisual(newState);
        OnCombatStateChanged?.Invoke(newState);
    }

    private void UpdateSwordVisual(CombatState state)
    {
        bool shouldShow = state != CombatState.Thrown;
        if (heldSwordVisual != null)
            heldSwordVisual.SetActive(shouldShow);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Main Combat Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleCombatState()
    {
        switch (currentCombatState)
        {
            case CombatState.Idle:
                HandleIdleState();
                break;

            case CombatState.Blocking:
                if (input.GetActionState("Block") != scrPlayerInputHandler.InputState.Hold)
                    EnterState(CombatState.Idle);
                break;

            case CombatState.Broken:
                stunTimer -= Time.deltaTime;
                if (stunTimer <= 0)
                {
                    currentBlockHP = maxBlockHP;
                    EnterState(CombatState.Idle);
                }
                break;

            case CombatState.Thrown:
                HandleThrownState();
                break;

            case CombatState.Attacking:
                // Usually transient – handled inside PerformMeleeAttack
                break;
        }
    }

    private void HandleIdleState()
    {
        if (input.GetActionState("Attack") == scrPlayerInputHandler.InputState.Press)
        {
            if (Time.time - lastAttackTime >= attackCooldown)
                PerformMeleeAttack();
        }
        else if (input.GetActionState("Block") == scrPlayerInputHandler.InputState.Hold)
        {
            EnterState(CombatState.Blocking);
        }
        else if (input.GetActionState("ThrowSword") == scrPlayerInputHandler.InputState.Press)
        {
            ThrowSword();
        }
    }

    private void HandleThrownState()
    {
        if (currentThrownInstance == null)
        {
            EnterState(CombatState.Idle);
            return;
        }

        if (input.GetActionState("ThrowSword") == scrPlayerInputHandler.InputState.Press)
        {
            if (Time.unscaledTime > lastThrowActionTime + 0.2f)
            {
                currentThrownInstance.Recall(throwOrigin);
                lastThrowActionTime = Time.unscaledTime;
            }
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Actions – Attack / Throw
    // ────────────────────────────────────────────────────────────────────────────────

    private void PerformMeleeAttack()
    {
        lastAttackTime = Time.time;
        EnterState(CombatState.Attacking);

        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, enemyLayer);

        foreach (var col in hits)
        {
            Vector3 toEnemy = (col.transform.position - cameraTransform.position).normalized;
            float angle = Vector3.Angle(cameraTransform.forward, toEnemy);

            if (angle <= attackAngle)
            {
                Debug.Log("col "+col.transform.name);
                if (col.TryGetComponent<INpcInteraction>(out var target))
                {
                    target.OnMeeleDamage(500);
                }
            }
        }

        EnterState(CombatState.Idle);
    }

    private void ThrowSword()
    {
        EnterState(CombatState.Thrown);
        lastThrowActionTime = Time.unscaledTime;

        bool isSlowTime = Time.timeScale < 1f;
        Quaternion lookRot = cameraTransform.rotation;

        if (isSlowTime)
        {
            var proj = Instantiate(thrownSwordPrefab, throwOrigin.position, lookRot);
            proj.InitializeProjectile(cameraTransform.forward, throwForce, true, throwableLayerMask);
            currentThrownInstance = proj;
        }
        else
        {
            if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out RaycastHit hit, maxThrowDistance, throwableLayerMask))
            {
                var stuck = Instantiate(thrownSwordPrefab);
                stuck.InitializeStuck(hit.point, -hit.normal, hit.transform);
                currentThrownInstance = stuck;
            }
            else
            {
                var proj = Instantiate(thrownSwordPrefab, throwOrigin.position, lookRot);
                proj.InitializeProjectile(cameraTransform.forward, throwForce, false, throwableLayerMask);
                currentThrownInstance = proj;
            }
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Block Regeneration
    // ────────────────────────────────────────────────────────────────────────────────

    private void RegenerateBlockHP()
    {
        if (currentCombatState != CombatState.Broken && currentCombatState != CombatState.Blocking)
        {
            currentBlockHP = Mathf.MoveTowards(currentBlockHP, maxBlockHP, blockRechargeRate * Time.deltaTime);
        }
    }

    #endregion
}