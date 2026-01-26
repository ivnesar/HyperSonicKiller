using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Unified animator controller for NPC enemies (Soldier and Defender).
/// Automatically detects NPC type and maps states to animator parameters.
/// 
/// ANIMATOR PARAMETERS REQUIRED:
/// - Float: "MoveSpeed" (0-1, normalized movement speed)
/// - Integer: "StateID" (state identifier for animation layers)
/// - Bool: "IsStunned" (true when stunned)
/// - Bool: "IsDead" (true when dead)
/// - Trigger: "Hit" (damage reaction)
/// - Trigger: "Fire" (for Soldier shooting)
/// - Trigger: "Block" (for Defender blocking)
/// - Trigger: "Counter" (for Defender counter-attack)
/// </summary>
public class NpcAnimatorController : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Animation Settings")]
    [SerializeField] private bool autoDetectNpcType = true;
    [SerializeField] private NpcType manualNpcType = NpcType.Soldier;

    [Header("Smoothing")]
    [SerializeField] private float speedSmoothTime = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Components & References
    // ────────────────────────────────────────────────────────────────────────────────

    private Animator animator;
    private NavMeshAgent navAgent;
    private NpcBase npcBase;
    
    // Type-specific references
    private SoldierNpc soldierNpc;
    private DefenderNpc defenderNpc;

    private NpcType detectedType;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime State
    // ────────────────────────────────────────────────────────────────────────────────

    private int lastStateID = -1;
    private float currentSpeedVelocity;

    // Debug display
    private string currentStateName;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        animator = GetComponent<Animator>();
        navAgent = GetComponent<NavMeshAgent>();
        npcBase = GetComponent<NpcBase>();

        // Validate components
        if (animator == null)
        {
            Debug.LogError($"[{gameObject.name}] NpcAnimatorController requires an Animator component!");
            enabled = false;
            return;
        }

        if (npcBase == null)
        {
            Debug.LogError($"[{gameObject.name}] NpcAnimatorController requires an NpcBase-derived component!");
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        // Detect or set NPC type
        if (autoDetectNpcType)
        {
            soldierNpc = GetComponent<SoldierNpc>();
            defenderNpc = GetComponent<DefenderNpc>();

            if (soldierNpc != null)
            {
                detectedType = NpcType.Soldier;
            }
            else if (defenderNpc != null)
            {
                detectedType = NpcType.Defender;
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] Could not auto-detect NPC type! Using manual type.");
                detectedType = manualNpcType;
            }
        }
        else
        {
            detectedType = manualNpcType;
            soldierNpc = GetComponent<SoldierNpc>();
            defenderNpc = GetComponent<DefenderNpc>();
        }

        //Debug.Log($"[{gameObject.name}] Animator Controller initialized for {detectedType}");
    }

    private void Update()
    {
        if (animator == null) return;

        UpdateMovementSpeed();
        UpdateStateAnimations();
        UpdateStatusFlags();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Movement Animation
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateMovementSpeed()
    {
        float targetSpeed = 0f;

        if (navAgent != null && navAgent.enabled)
        {
            // Calculate normalized speed (0-1)
            float currentSpeed = navAgent.velocity.magnitude;
            float maxSpeed = navAgent.speed;
            targetSpeed = maxSpeed > 0 ? currentSpeed / maxSpeed : 0f;
        }

        // Smooth the speed change
        float smoothedSpeed = Mathf.SmoothDamp(
            animator.GetFloat("MoveSpeed"),
            targetSpeed,
            ref currentSpeedVelocity,
            speedSmoothTime
        );

        animator.SetFloat("MoveSpeed", smoothedSpeed);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Animation Mapping
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateStateAnimations()
    {
        int newStateID = -1;
        string stateName = "Unknown";

        switch (detectedType)
        {
            case NpcType.Soldier:
                if (soldierNpc != null)
                {
                    newStateID = MapSoldierState(soldierNpc.currentState);
                    stateName = soldierNpc.currentState.ToString();
                }
                break;

            case NpcType.Defender:
                if (defenderNpc != null)
                {
                    newStateID = MapDefenderState(defenderNpc.currentState);
                    stateName = defenderNpc.currentState.ToString();
                }
                break;
        }

        // Only update if state changed
        if (newStateID != lastStateID)
        {
            animator.SetInteger("StateID", newStateID);
            lastStateID = newStateID;
            currentStateName = stateName;

            if (showDebugInfo)
            {
                Debug.Log($"[{gameObject.name}] Animation State Changed: {stateName} (ID: {newStateID})");
            }
        }
    }

    /// <summary>
    /// Maps Soldier states to animation state IDs.
    /// </summary>
    private int MapSoldierState(SoldierNpc.SoldierState state)
    {
        switch (state)
        {
            case SoldierNpc.SoldierState.Idle:
                return 0;

            case SoldierNpc.SoldierState.MovingToRange:
                return 1;

            case SoldierNpc.SoldierState.Aiming:
                return 2;

            case SoldierNpc.SoldierState.Firing:
                return 3;

            case SoldierNpc.SoldierState.Reloading:
                return 4;

            case SoldierNpc.SoldierState.Stunned:
                return 5;

            default:
                return 0;
        }
    }

    /// <summary>
    /// Maps Defender states to animation state IDs.
    /// </summary>
    private int MapDefenderState(DefenderNpc.DefenderState state)
    {
        switch (state)
        {
            case DefenderNpc.DefenderState.Idle:
                return 0;

            case DefenderNpc.DefenderState.MovingToProtect:
                return 1;

            case DefenderNpc.DefenderState.Guarding:
                return 2;

            case DefenderNpc.DefenderState.Blocking:
                return 3;

            case DefenderNpc.DefenderState.Countering:
                return 4;

            case DefenderNpc.DefenderState.Stunned:
                return 5;

            default:
                return 0;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Status Flags
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateStatusFlags()
    {
        if (npcBase == null) return;

        // Update stun status
        animator.SetBool("IsStunned", npcBase.IsStunned);

        // Update death status
        animator.SetBool("IsDead", npcBase.IsDead);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public API - Manual Trigger Control
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manually trigger the hit reaction animation.
    /// (Usually called by NpcBase when taking damage)
    /// </summary>
    public void TriggerHitReaction()
    {
        animator?.SetTrigger("Hit");
    }

    /// <summary>
    /// Manually trigger the fire animation.
    /// (For soldiers when shooting)
    /// </summary>
    public void TriggerFire()
    {
        animator?.SetTrigger("Fire");
    }

    /// <summary>
    /// Manually trigger the block animation.
    /// (For defenders when blocking)
    /// </summary>
    public void TriggerBlock()
    {
        animator?.SetTrigger("Block");
    }

    /// <summary>
    /// Manually trigger the counter animation.
    /// (For defenders when counter-attacking)
    /// </summary>
    public void TriggerCounter()
    {
        animator?.SetTrigger("Counter");
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug Info
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!showDebugInfo || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3f);
        if (screenPos.z > 0)
        {
            float moveSpeed = animator.GetFloat("MoveSpeed");
            int stateID = animator.GetInteger("StateID");

            string debugText = $"Type: {detectedType}\n" +
                             $"State: {currentStateName}\n" +
                             $"ID: {stateID}\n" +
                             $"Speed: {moveSpeed:F2}";

            GUI.Label(
                new Rect(screenPos.x - 60, Screen.height - screenPos.y, 120, 80),
                debugText
            );
        }
    }

    #endregion
}