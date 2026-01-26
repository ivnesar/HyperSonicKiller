using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(CharacterController))]
public class scrNpc_GenOne : MonoBehaviour
{
    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 0.4f;
    
    [Header("Dashing")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashEndThreshold = 0.5f;
    // Player references
    private FPSPlayerController asmPlayer;
    private Transform playerTransform;

    // Configuration
    [SerializeField] private float rotationSpeed = 30f; // Degrees per second
    [SerializeField] private float fieldOfViewAngle = 210; // 180-degree FOV
    [SerializeField] private LayerMask obstacleMask; // Layers for LOS checks (e.g., walls)
    
    [SerializeField] private float reloadTime = 2f; // Reload duration

    // State variables
    public enum NpcState { Idle, WallCling, Dashing, Recharging, Evade, StundFall, StundIdle, DeadFall, DeadIdle }
    public NpcState currentState;
    private float stateTimer;
    
    private CharacterController characterController;
    
    // Static queue for shooting order
    private static Queue<scrNpc_GenOne> DashQueue = new Queue<scrNpc_GenOne>();
    private scrLocalGameManager lgm;
    private void Awake()
    {
        
        characterController = GetComponent<CharacterController>(); // Ensure component exists
        lgm = scrLocalGameManager.Instance;
        asmPlayer = lgm.AsmPlayer;
    }

    private void Start()
    {
        playerTransform = asmPlayer?.transform;

        
        TransitionToState(NpcState.Idle);
    }

    private void Update()
    {
        UpdateState();
    }

    // Check if player is in 180° FOV and not blocked by walls
    private bool CanSeePlayer()
    {
        Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, toPlayer);
        if (angle > fieldOfViewAngle / 2) return false;

        Vector3 rayStart = transform.position + Vector3.up; // Eye-level
        Vector3 rayDirection = asmPlayer.playerCamera.transform.position - rayStart;
        float distance = rayDirection.magnitude;
        rayDirection.Normalize();

        if (Physics.Raycast(rayStart, rayDirection, out RaycastHit hit, distance, obstacleMask))
        {
            if (hit.collider.CompareTag("Wall"))
            {
                Debug.DrawRay(rayStart, rayDirection * hit.distance, Color.yellow);
                return false;
            }
        }
        Debug.DrawRay(rayStart, rayDirection * distance, Color.cyan);
        return true;
    }

    public LayerMask dashMask = -1;
    private Vector3 dashPoint;
    private Vector3 direction;
    private Vector3 movement;
    private bool didCollideInLastMove;
    private Vector3 lastCollisionNormal;
    
    private void RotateTowards(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0; // Lock to Y-axis
        if (direction == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        float timeDilation = Mathf.Max(lgm.TimeDialation, 0.01f);
        float rotVel = rotationSpeed * Time.deltaTime / timeDilation;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotVel);
    }
    
    private void ShuffleList(List<scrNpc_GenOne> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            scrNpc_GenOne temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
        
    private bool TryDash()
    {
        Vector3 startPos = transform.position + Vector3.up;
        Vector3 dirPos = asmPlayer.playerCamera.transform.position - startPos;
        Ray ray = new Ray(startPos, dirPos);
        
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, dashMask))
        {
            dashPoint = hit.point;
            Debug.DrawLine(startPos, dashPoint, Color.magenta, 3);
            return true;
        }
        return false;
    }
    
    
    
    
    

    #region STATES

    // State machine management
    private void TransitionToState(NpcState newState)
    {
        currentState = newState;
        stateTimer = 0f;

        switch (newState)
        {
            case NpcState.Idle: OnEnterIdle(); break;
            case NpcState.Recharging: OnEnterRecharging(); break;
            case NpcState.Dashing: OnEnterDashing(); break;
            case NpcState.WallCling: OnEnterWallCling(); break;
            case NpcState.Evade:  OnEnterEvade(); break;
            case NpcState.StundFall: OnEnterStundFall(); break;
            case NpcState.StundIdle: OnEnterStunIdle(); break;
            case NpcState.DeadFall: OnEnterDeadFall(); break;
            case NpcState.DeadIdle: OnEnterDeadIdle(); break;
        }
    }

    private void UpdateState()
    {
        switch (currentState)
        {
            case NpcState.Idle: UpdateIdle(); break;
            case NpcState.Recharging: UpdateRecharging(); break;
            case NpcState.Dashing: UpdateDashing(); break;
            case NpcState.WallCling: UpdateWallCling(); break;
            case NpcState.Evade: UpdateEvade(); break;
            case NpcState.StundFall: UpdateStunFall(); break;
            case NpcState.StundIdle: UpdateStunIdle(); break;
            case NpcState.DeadFall: UpdateDeadFall(); break;
            case NpcState.DeadIdle: UpdateDeadIdle(); break;
        }
    }

    #region IDLE

    private void OnEnterIdle()
    {
    }

    private void UpdateIdle()
    {
        if (CanSeePlayer())
        {
            if (!DashQueue.Contains(this))
            {
                List<scrNpc_GenOne> eligibleNPCs = new List<scrNpc_GenOne> { this };
                foreach (scrNpc_GenOne npcGenOne in lgm.NpcGenOnes)
                {
                    if (npcGenOne != this && npcGenOne.enabled && npcGenOne.CanSeePlayer() && !DashQueue.Contains(npcGenOne))
                    {
                        eligibleNPCs.Add(npcGenOne);
                    }
                }
                ShuffleList(eligibleNPCs);
                foreach (scrNpc_GenOne npc in eligibleNPCs)
                {
                    DashQueue.Enqueue(npc);
                }
            }
            
            RotateTowards(asmPlayer.transform.position);
            stateTimer += Time.deltaTime;
            if (stateTimer >= 1f && DashQueue.Count > 0 && DashQueue.Peek() == this)
            {
                if (!TryDash()) return;
                TransitionToState(NpcState.Dashing);
            }
        }
        else if (DashQueue.Contains(this))
        {
            DashQueue = new Queue<scrNpc_GenOne>(DashQueue.Where(npc => npc != this));
        }

    }
    
    #endregion

    #region WALLCLING

    private void OnEnterWallCling()
    {
        // Face the direction of the wall normal
        if (lastCollisionNormal != Vector3.zero)
        {
            Vector3 wallNormalDirection = lastCollisionNormal;
            wallNormalDirection.y = 0; // Lock to Y-axis to prevent tilting
            if (wallNormalDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(wallNormalDirection);
                transform.rotation = targetRotation; // Instant rotation to face wall normal
            }
        }
    }

    private void UpdateWallCling()
    {
        if (CanSeePlayer())
        {
            if (!DashQueue.Contains(this))
            {
                List<scrNpc_GenOne> eligibleNPCs = new List<scrNpc_GenOne> { this };
                foreach (scrNpc_GenOne npcGenOne in lgm.NpcGenOnes)
                {
                    if (npcGenOne != this && npcGenOne.enabled && npcGenOne.CanSeePlayer() && !DashQueue.Contains(npcGenOne))
                    {
                        eligibleNPCs.Add(npcGenOne);
                    }
                }

                ShuffleList(eligibleNPCs);
                foreach (scrNpc_GenOne npc in eligibleNPCs)
                {
                    DashQueue.Enqueue(npc);
                }
            }
            
            stateTimer += Time.deltaTime;
            if (stateTimer >= 1f && DashQueue.Count > 0 && DashQueue.Peek() == this)
            {
                if (!TryDash()) return;
                TransitionToState(NpcState.Dashing);
            }
        }
        else if (DashQueue.Contains(this))
        {
            DashQueue = new Queue<scrNpc_GenOne>(DashQueue.Where(npc => npc != this));
        }
    }

    #endregion
    
    #region DASHING

    private void OnEnterDashing()
    {
        
    }

    private void UpdateDashing()
    {
        HandleDash();
        CheckForEvade();
        
        CheckMeleeInteractionWithPlayer();
        
        if (Vector3.Distance(transform.position, dashPoint) < dashEndThreshold || didCollideInLastMove)
        {
            if (IsGrounded())
            {
                TransitionToState(NpcState.Idle);
            }
            else if (didCollideInLastMove && lastCollisionNormal != Vector3.zero)
            {
                float angleToUp = Vector3.Angle(lastCollisionNormal, Vector3.up);
                if (angleToUp < 45f)
                {
                    TransitionToState(NpcState.Idle);
                }
                else
                {
                    TransitionToState(NpcState.WallCling);
                }
            }
            else if (CheckForNearbyWall())
            {
                TransitionToState(NpcState.WallCling);
            }
            else
            {
                TransitionToState(NpcState.Idle);
            }
        }
        
        
        return;
        void CheckMeleeInteractionWithPlayer()
        {
            float distanceToPlayer = Vector3.Distance(transform.position, asmPlayer.transform.position);
            if (distanceToPlayer > lgm.meleeRange) return;

            // Optional: LOS check (reuse CanSeePlayer or simple ray)
            if (!CanSeePlayer()) return; // No interaction if no LOS

            Debug.Log("Enemy in melee range during dash!");

            // Scenario checks
            // if (asmPlayer.IsInAttackWindow()) // Simultaneous attack
            // {
            //     // Scenario 3: Bounce back
            //     BounceBack();
            // }
            // else if (asmPlayer.IsBlocking()) // Player blocking
            // {
            //     // Scenario 2: Pass through (do nothing extra)
            //     Debug.Log("Enemy attack blocked - passing through.");
            // }
            // else // Player does nothing
            // {
            //     // Scenario 1: Kill player
            //     PlayerDeath();
            // }
            
            return;
            void BounceBack()
            {
                // Reverse direction
                direction = -direction;
                // Update dashPoint to a point behind (e.g., current pos + reversed direction * some distance)
                dashPoint = transform.position + direction * dashSpeed * 2; // Adjust distance as needed
                didCollideInLastMove = false; // Reset to continue dashing
                Debug.Log("Enemy bounced back!");

                // Optional: Add force/velocity tweak if needed
                // movement = direction * dashSpeed * Time.deltaTime; // Immediate push

                // Dequeue to prevent immediate re-attack
                if (DashQueue.Contains(this))
                {
                    DashQueue = new Queue<scrNpc_GenOne>(DashQueue.Where(npc => npc != this));
                }

                // Transition to Recharging or add stun if desired
                // TransitionToState(NpcState.Recharging);
            }

                // NEW: Melee Addition - Player death handler
            void PlayerDeath()
            {
                // Assume a global handler; customize as needed
                //scrLocalGameManager.Instance.PlayerDeath(); // Implement this to trigger game over
                Debug.Log("Player killed by enemy dash!");
                // Optional: Destroy(asmPlayer.gameObject);
            }
        }
        
        bool IsGrounded()
        {
            if (characterController.isGrounded) return true;

            Vector3 rayOrigin = transform.position - new Vector3(0, characterController.height / 2, 0);
            Ray groundRay = new Ray(rayOrigin, Vector3.down);
            return Physics.Raycast(groundRay, out _, groundCheckDistance);
        }
        
        void HandleDash()
        {
            direction = (dashPoint - transform.position).normalized;
            
            Vector3 oldPos = transform.position;
            movement = direction * dashSpeed;
            movement *= Time.deltaTime;
            
            characterController.Move(movement);
            
            Vector3 delta = transform.position - oldPos;
            didCollideInLastMove = delta.magnitude < movement.magnitude - 0.001f;

            lastCollisionNormal = Vector3.zero;
            if (didCollideInLastMove)
            {
                lastCollisionNormal = GetCollisionNormal(oldPos, direction);
            }
            
            
            
            return;
            Vector3 GetCollisionNormal(Vector3 oldPos, Vector3 direction)
            {
                Vector3 rayOrigin = oldPos + direction * (characterController.radius * 0.5f);
                Ray collisionRay = new Ray(rayOrigin, direction);
                if (Physics.Raycast(collisionRay, out RaycastHit hit, movement.magnitude + characterController.radius + 0.2f))
                {
                    return hit.normal;
                }
                return Vector3.zero;
            }
        }
        
        bool CheckForNearbyWall()
        {
            float checkRadius = characterController.radius + 0.2f;
            if (Physics.SphereCast(transform.position, checkRadius, direction, out RaycastHit hit, checkRadius))
            {
                float angleToUp = Vector3.Angle(hit.normal, Vector3.up);
                return angleToUp >= 45f;
            }
            return false;
        }

        void CheckForEvade()
        {
            for (int i = 0; i < lgm.PlayerProjectiles.Count; i++)
            {
                scrPlayerProjectile iProj = lgm.PlayerProjectiles[i];
                float iDist = Vector3.Distance(iProj.transform.position, transform.position);
                if (iDist < 3)
                {
                    TransitionToState(NpcState.Evade);
                    return;
                }
            }
        }
    }

    #endregion

    #region RECHARGE

    private void OnEnterRecharging()
    {
        stateTimer = 0f;
        if (DashQueue.Count > 0 && DashQueue.Peek() == this)
        {
            DashQueue.Dequeue();
        }
    }

    private void UpdateRecharging()
    {
        if (CanSeePlayer())
        {
            RotateTowards(playerTransform.position);
        }
        
        stateTimer += Time.deltaTime;
        float adjustedReloadTime = reloadTime * lgm.TimeDialation;
        if (stateTimer >= adjustedReloadTime)
        {
            TransitionToState(NpcState.Idle);
        }
    }

    #endregion

    #region EVADE

    private void OnEnterEvade()
    {
        
    }

    private void UpdateEvade()
    {
        // Vector3 moveDirection = transform.right + transform.forward;
        // Vector3 movement = moveDirection * speed * Time.deltaTime;
        //
        // verticalVelocity += gravity * Time.deltaTime;
        // movement.y += verticalVelocity * Time.deltaTime;
        //
        // characterController.Move(movement);
    }

    #endregion
    
    
    
    #region STUN FALL

    private void OnEnterStundFall()
    {
        
    }

    private void UpdateStunFall()
    {
        
    }

    #endregion
    
    
    
    #region STUN IDLE

    private void OnEnterStunIdle()
    {
        
    }

    private void UpdateStunIdle()
    {
        
    }

    #endregion
    
    
    
    #region DEAD IDLE

    private void OnEnterDeadIdle()
    {
        
    }

    private void UpdateDeadIdle()
    {
        
    }

    #endregion
    
    #region DEAD FALL

    private void OnEnterDeadFall()
    {
        
    }

    private void UpdateDeadFall()
    {
        
    }

    #endregion
    
    #endregion
}