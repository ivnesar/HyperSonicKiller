// ===== SIMPLIFIED BASE CONTROLLER =====
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCEnemyController : MonoBehaviour
{
    public enum NPCState
    {
        Idle,
        Pursuing,
        Attacking,
        Reloading,
        Dead
    }

    [Header("NPC Settings")]
    [SerializeField] private float health = 100f;
    public float attackRange = 10f;
    [SerializeField] private float lineOfSightCheckInterval = 0.2f;
    [SerializeField] private float rotationSpeed = 5f;
    [SerializeField] private LayerMask visionBlockingLayers;

    [Header("Spacing Settings")]
    [SerializeField] private float minDistanceToOtherNPCs = 2f;
    [SerializeField] private float spacingForce = 3f;

    [Header("Movement Settings")]
    [SerializeField] private float speed = 3.5f;
    [SerializeField] private float stoppingDistance = 1.5f;

    // Public properties
    public NPCState CurrentState => currentState;
    public bool HasLineOfSight => hasLineOfSight;
    public Vector3 LastKnownPlayerPosition => lastKnownPlayerPosition;
    public FPSPlayerController Player => player;
    public NavMeshAgent Agent => agent;

    public NPCState currentState;
    private NavMeshAgent agent;
    private scrLocalGameManager lgm;
    private FPSPlayerController player;
    
    private float lastLOSCheckTime;
    private bool hasLineOfSight;
    private Vector3 lastKnownPlayerPosition;
    private bool isDead;

    private INPCBehavior behaviorScript;
    
    public int animatorID;
    
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        lgm = scrLocalGameManager.Instance;
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<FPSPlayerController>();
        
        if (lgm != null)
        {
            lgm.NpcBaseSoldiers.Add(this);
        }

        agent.speed = speed;
        agent.stoppingDistance = stoppingDistance;

        behaviorScript = GetComponent<INPCBehavior>();
        if (behaviorScript == null)
        {
            Debug.LogError($"{gameObject.name} has no behavior script attached!");
        }
        
        SetState(NPCState.Idle);
    }

    void OnDestroy()
    {
        if (lgm != null && lgm.NpcBaseSoldiers != null)
        {
            lgm.NpcBaseSoldiers.Remove(this);
        }
    }

    void Update()
    {
        if (isDead || player == null) return;

        CheckLineOfSight();

        switch (currentState)
        {
            case NPCState.Idle:
                UpdateIdleState();
                break;

            case NPCState.Pursuing:
                UpdatePursuingState();
                break;
            
            case NPCState.Attacking:
                UpdateAttackingState();
                break;

            case NPCState.Reloading:
                UpdateReloadingState();
                break;
            
            case NPCState.Dead:
                Debug.Log("dead");
                break;
        }
    }

    #region State Machine
    public void SetState(NPCState newState)
    {
        if (currentState == newState) return;

        ExitState(currentState);
        currentState = newState;
        EnterState(newState);
    }

    private void EnterState(NPCState state)
    {
        switch (state)
        {
            case NPCState.Idle:
                agent.isStopped = true;
                break;

            case NPCState.Pursuing:
                agent.isStopped = false;
                break;
            
            case NPCState.Attacking:
                agent.isStopped = true;
                behaviorScript?.OnStartAttack();
                break;

            case NPCState.Reloading:
                agent.isStopped = true;
                behaviorScript?.OnStartReload();
                break;
            
            case NPCState.Dead:
                agent.isStopped = true;
                agent.enabled = false;
                break;
        }
    }

    private void ExitState(NPCState state)
    {
        switch (state)
        {
            case NPCState.Attacking:
                behaviorScript?.OnStopAttack();
                break;

            case NPCState.Reloading:
                behaviorScript?.OnStopReload();
                break;
        }
    }
    #endregion

    #region State Updates

    private void UpdateIdleState()
    {
        Vector3 targetPosition = GetTargetPosition();
        float distanceToPlayer = Vector3.Distance(transform.position, targetPosition);

        Debug.Log("Idle");

        // Rotate toward player if not dashing
        if (player.currentState != FPSPlayerController.PlayerState.Dashing)
        {
            RotateTowardsTarget(targetPosition);
        }

        // Check if should start pursuing
        if (distanceToPlayer > attackRange || !hasLineOfSight)
        {
            SetState(NPCState.Pursuing);
        }
        // Check if can attack
        else if (behaviorScript != null && behaviorScript.CanAttack(distanceToPlayer, hasLineOfSight))
        {
            SetState(NPCState.Attacking);
        }
    }
    
    private void UpdatePursuingState()
    {
        // Get target position (use last known if player is dashing)
        Vector3 targetPosition = GetTargetPosition();
        float distanceToPlayer = Vector3.Distance(transform.position, targetPosition);

        Debug.Log("Pursuing LOS:");
        Debug.DrawLine(transform.position, targetPosition);
        
        // Check if should attack
        if (behaviorScript != null && behaviorScript.CanAttack(distanceToPlayer, hasLineOfSight))
        {
            SetState(NPCState.Attacking);
            return;
        }

        // Move toward player with spacing
        Vector3 moveDestination = CalculateMoveDestination(targetPosition);
        agent.SetDestination(moveDestination);
        
        // Rotate toward player if not dashing
        if (player.currentState != FPSPlayerController.PlayerState.Dashing)
        {
            RotateTowardsTarget(targetPosition);
        }
    }

    private void UpdateAttackingState()
    {
        Vector3 targetPosition = GetTargetPosition();
        float distanceToPlayer = Vector3.Distance(transform.position, targetPosition);

        Debug.Log("attacking");
    
        // Always rotate toward player when attacking (unless dashing)
        if (player.currentState != FPSPlayerController.PlayerState.Dashing)
        {
            RotateTowardsTarget(targetPosition);
        }

        // Update attack behavior
        behaviorScript?.UpdateAttack();

        // CHECK LOS/RANGE FIRST - if we can't attack, stop immediately
        if (behaviorScript != null && !behaviorScript.CanAttack(distanceToPlayer, hasLineOfSight))
        {
            SetState(NPCState.Pursuing);
            return; // Exit early
        }

        // Only check reload if we're still in valid attack conditions
        if (behaviorScript != null && behaviorScript.ShouldReload())
        {
            SetState(NPCState.Reloading);
        }
    }

    private void UpdateReloadingState()
    {
        Vector3 targetPosition = GetTargetPosition();
        float distanceToPlayer = Vector3.Distance(transform.position, targetPosition);

        Debug.Log("Reloading");

        // Keep rotating toward player while reloading
        if (player.currentState != FPSPlayerController.PlayerState.Dashing)
        {
            RotateTowardsTarget(targetPosition);
        }

        // Update reload behavior
        behaviorScript?.UpdateReload();

        // Check if reload is complete
        if (behaviorScript != null && behaviorScript.IsReloadComplete())
        {
            // Go back to appropriate state based on distance and LOS
            if (distanceToPlayer <= attackRange && hasLineOfSight)
            {
                SetState(NPCState.Attacking);
            }
            else
            {
                SetState(NPCState.Pursuing);
            }
        }
    }
    #endregion

    #region Movement Helpers
    private Vector3 GetTargetPosition()
    {
        bool playerIsDashing = player.currentState == FPSPlayerController.PlayerState.Dashing;
        
        if (!playerIsDashing)
        {
            lastKnownPlayerPosition = player.transform.position;
            return player.transform.position;
        }
        
        return lastKnownPlayerPosition;
    }

    private Vector3 CalculateMoveDestination(Vector3 playerPosition)
    {
        // Calculate avoidance from nearby NPCs
        Vector3 avoidanceOffset = CalculateSpacingOffset();
        
        // Blend player direction with avoidance
        Vector3 directionToPlayer = (playerPosition - transform.position).normalized;
        Vector3 finalDirection = (directionToPlayer + avoidanceOffset).normalized;
        
        // Calculate destination
        float distanceToPlayer = Vector3.Distance(transform.position, playerPosition);
        float desiredDistance = attackRange - stoppingDistance;
        float moveDistance = Mathf.Max(distanceToPlayer - desiredDistance, 0f);
        
        return transform.position + finalDirection * moveDistance;
    }

    private Vector3 CalculateSpacingOffset()
    {
        if (lgm == null) return Vector3.zero;

        Vector3 avoidanceDirection = Vector3.zero;
        int nearbyCount = 0;

        foreach (var otherNPC in lgm.NpcBaseSoldiers)
        {
            if (otherNPC == null || otherNPC == this) continue;
            
            float distance = Vector3.Distance(transform.position, otherNPC.transform.position);
            
            // Only avoid if too close
            if (distance < minDistanceToOtherNPCs)
            {
                Vector3 directionAway = (transform.position - otherNPC.transform.position).normalized;
                float strength = 1f - (distance / minDistanceToOtherNPCs);
                avoidanceDirection += directionAway * strength;
                nearbyCount++;
            }
        }

        if (nearbyCount == 0) return Vector3.zero;

        return avoidanceDirection.normalized * spacingForce;
    }

    public void RotateTowardsTarget(Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - transform.position).normalized;
        directionToTarget.y = 0;
        
        if (directionToTarget != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            float adjustedSpeed = rotationSpeed / Mathf.Max(lgm.TimeDialation, 0.01f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * adjustedSpeed);
        }
    }
    #endregion

    #region Line of Sight
    private void CheckLineOfSight()
    {
        if (Time.time - lastLOSCheckTime < lineOfSightCheckInterval) return;
        lastLOSCheckTime = Time.time;

        if (player == null) return;

        Vector3 directionToPlayer = (player.transform.position + Vector3.up) - (transform.position + Vector3.up);
        float distanceToPlayer = directionToPlayer.magnitude;

        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up, directionToPlayer.normalized, 
            out hit, distanceToPlayer, visionBlockingLayers))
        {
            hasLineOfSight = false;
        }
        else
        {
            hasLineOfSight = true;
        }
    }
    #endregion

    #region Combat
    public void TakeDamage(float damage)
    {
        if (isDead) return;

        health -= damage;
        Debug.Log($"{gameObject.name} took {damage} damage. Health: {health}");

        if (health <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        SetState(NPCState.Dead);
        
        if (lgm != null && lgm.NpcBaseSoldiers != null)
        {
            lgm.NpcBaseSoldiers.Remove(this);
        }
        
        Debug.Log($"{gameObject.name} has died!");
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, minDistanceToOtherNPCs);

        if (Application.isPlaying && player != null)
        {
            Gizmos.color = hasLineOfSight ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up, player.transform.position);
        }

        if (Application.isPlaying && agent != null && agent.hasPath)
        {
            Gizmos.color = Color.white;
            Vector3[] path = agent.path.corners;
            for (int i = 0; i < path.Length - 1; i++)
            {
                Gizmos.DrawLine(path[i], path[i + 1]);
            }
        }
    }
    #endregion
}