using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(CharacterController))]
public class scrNpc_BaseSoldier : MonoBehaviour
{
    // Enum to define projectile types
    public enum ProjectileType { Bullet, Missile }

    // Player references
    private FPSPlayerController player;
    private Transform playerTransform;

    // Configuration
    [SerializeField] private float rotationSpeed = 5f; // Degrees per second
    [SerializeField] private float fieldOfViewAngle = 210; // 180-degree FOV
    [SerializeField] private LayerMask obstacleMask; // Layers for LOS checks (e.g., walls)
    [SerializeField] private ProjectileType projectileType = ProjectileType.Bullet; // NEW: Select projectile type
    [SerializeField] private GameObject bulletPrefab; // NEW: Bullet prefab
    [SerializeField] private GameObject missilePrefab; // NEW: Missile prefab
    [SerializeField] private Transform projectileSpawnPoint; // Where projectiles spawn
    [SerializeField] private float maxSprayAngle = 12f; // Random spray angle for shots
    [SerializeField] private float burstInterval = 0.03f; // Time between shots in a burst
    [SerializeField] private int burstCount = 31; // Shots per burst
    [SerializeField] private float reloadTime = 2f; // Reload duration

    // State variables
    private enum NpcState { Idle, Shooting, Reloading }
    private NpcState currentState;
    private float stateTimer;
    private int shotsFired;
    private Vector3 targetPlayerPosition;

    // Static queue for shooting order
    private static Queue<scrNpc_BaseSoldier> shootingQueue = new Queue<scrNpc_BaseSoldier>();

    private void Awake()
    {
        GetComponent<CharacterController>(); // Ensure component exists
        player = scrLocalGameManager.Instance.AsmPlayer;
    }

    private void Start()
    {
        playerTransform = player?.transform;
        if (playerTransform == null)
        {
            Debug.LogError("Player not found! Ensure the player has the 'Player' tag.");
            enabled = false;
            return;
        }
        // NEW: Validate projectile prefabs
        if (projectileType == ProjectileType.Bullet && bulletPrefab == null ||
            projectileType == ProjectileType.Missile && missilePrefab == null)
        {
            Debug.LogError($"No {(projectileType == ProjectileType.Bullet ? "bullet" : "missile")} prefab assigned for {gameObject.name}!");
            enabled = false;
            return;
        }
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
        Vector3 rayDirection = player.playerCamera.transform.position - rayStart;
        float distance = rayDirection.magnitude;
        rayDirection.Normalize();

        if (Physics.Raycast(rayStart, rayDirection, out RaycastHit hit, distance, obstacleMask))
        {
            if (hit.collider.CompareTag("Wall"))
            {
                Debug.DrawRay(rayStart, rayDirection * hit.distance, Color.red);
                return false;
            }
        }
        Debug.DrawRay(rayStart, rayDirection * distance, Color.cyan);
        return true;
    }

    // Check if another NPC is in the direct line of fire
    private bool IsClearOfNPCs(Vector3 direction)
    {
        Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
        float distanceToPlayer = Vector3.Distance(spawnPos, player.playerCamera.transform.position);

        // foreach (scrNpc_BaseSoldier npc in scrLocalGameManager.Instance.NpcBaseSoldiers)
        // {
        //     if (npc == this || !npc.enabled) continue;
        //     Vector3 toNPC = npc.transform.position - spawnPos;
        //     float distanceToNPC = toNPC.magnitude;
        //     if (distanceToNPC > distanceToPlayer) continue; // NPC is beyond player
        //
        //     float angleToNPC = Vector3.Angle(direction, toNPC.normalized);
        //     if (angleToNPC < 5f) // Small angle to account for projectile path
        //     {
        //         if (!Physics.Raycast(spawnPos, toNPC.normalized, out RaycastHit hit, distanceToNPC, obstacleMask) || hit.collider == npc.gameObject)
        //         {
        //             Debug.DrawRay(spawnPos, toNPC.normalized * distanceToNPC, Color.yellow, 3f);
        //             return false; // NPC in line of fire
        //         }
        //     }
        // }
        return true;
    }

    // Rotate towards a position (Y-axis only)
    private void RotateTowards(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0; // Lock to Y-axis
        if (direction == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        float timeDilation = Mathf.Max(scrLocalGameManager.Instance.TimeDialation, 0.01f);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime / timeDilation
        );
    }

    // State machine management
    private void TransitionToState(NpcState newState)
    {
        currentState = newState;
        stateTimer = 0f;
        shotsFired = 0;

        switch (newState)
        {
            case NpcState.Idle:
                OnEnterIdle();
                break;
            case NpcState.Shooting:
                OnEnterShooting();
                break;
            case NpcState.Reloading:
                OnEnterReloading();
                break;
        }
    }

    private void UpdateState()
    {
        switch (currentState)
        {
            case NpcState.Idle:
                UpdateIdle();
                break;
            case NpcState.Shooting:
                UpdateShooting();
                break;
            case NpcState.Reloading:
                UpdateReloading();
                break;
        }
    }

    // Idle state: Wait until player is visible
    private void OnEnterIdle()
    {
        // No initialization needed
    }

    private void UpdateIdle()
    {
        if (CanSeePlayer())
        {
            // Add self to queue if not already present
            if (!shootingQueue.Contains(this))
            {
                List<scrNpc_BaseSoldier> eligibleNPCs = new List<scrNpc_BaseSoldier> { this };
                // Collect other NPCs that can see the player
                // foreach (scrNpc_BaseSoldier npc in scrLocalGameManager.Instance.NpcBaseSoldiers)
                // {
                //     if (npc != this && npc.enabled && npc.CanSeePlayer() && !shootingQueue.Contains(npc))
                //     {
                //         eligibleNPCs.Add(npc);
                //     }
                // }
                // Shuffle and add to queue
                ShuffleList(eligibleNPCs);
                foreach (scrNpc_BaseSoldier npc in eligibleNPCs)
                {
                    shootingQueue.Enqueue(npc);
                }
            }

            RotateTowards(playerTransform.position);
            stateTimer += Time.deltaTime;
            if (stateTimer >= 1f && shootingQueue.Count > 0 && shootingQueue.Peek() == this)
            {
                TransitionToState(NpcState.Shooting);
            }
        }
        else if (shootingQueue.Contains(this))
        {
            // Remove from queue if no longer eligible
            shootingQueue = new Queue<scrNpc_BaseSoldier>(shootingQueue.Where(npc => npc != this));
        }
    }

    // Fisher-Yates shuffle for randomizing NPC order
    private void ShuffleList(List<scrNpc_BaseSoldier> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            scrNpc_BaseSoldier temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    // Shooting state: Fire a burst of projectiles
    private void OnEnterShooting()
    {
        targetPlayerPosition = player.playerCamera.transform.position;
    }

    private void UpdateShooting()
    {
        // Continue rotating towards the stored player position
        RotateTowards(targetPlayerPosition);

        // Fire projectiles until burst is complete
        stateTimer += Time.deltaTime;
        float interval = burstInterval * scrLocalGameManager.Instance.TimeDialation;
        if (stateTimer >= interval)
        {
            FireProjectile();
            shotsFired++;
            stateTimer = 0f;
            if (shotsFired >= burstCount)
            {
                TransitionToState(NpcState.Reloading);
            }
        }
    }

    private void FireProjectile()
    {
        Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
        Vector3 direction = (targetPlayerPosition - spawnPos).normalized;

        // Check for NPCs in the line of fire
        if (!IsClearOfNPCs(direction))
        {
            return; // Skip firing to avoid friendly fire
        }

        // NEW: Select the appropriate prefab based on projectile type
        GameObject selectedPrefab = projectileType == ProjectileType.Bullet ? bulletPrefab : missilePrefab;

        // NEW: Adjust spray angle for missiles (optional, missiles might need less spray)
        float appliedSprayAngle = projectileType == ProjectileType.Bullet ? maxSprayAngle : maxSprayAngle = 0f; // Example: Missiles have half the spray angle

        Quaternion spray = Quaternion.Euler(
            Random.Range(-appliedSprayAngle, appliedSprayAngle),
            Random.Range(-appliedSprayAngle, appliedSprayAngle),
            Random.Range(-appliedSprayAngle, appliedSprayAngle)
        );
        Vector3 sprayDirection = spray * direction;

        GameObject projectile = Instantiate(selectedPrefab, spawnPos, Quaternion.identity);
        projectile.transform.forward = sprayDirection;

        Debug.DrawLine(spawnPos, spawnPos + sprayDirection * 5f, Color.red, 3f);
    }

    // Reloading state: Wait for reload duration
    private void OnEnterReloading()
    {
        stateTimer = 0f;
        // Remove self from queue to allow next NPC to shoot
        if (shootingQueue.Count > 0 && shootingQueue.Peek() == this)
        {
            shootingQueue.Dequeue();
        }
    }

    private void UpdateReloading()
    {
        if (CanSeePlayer())
        {
            RotateTowards(playerTransform.position);
        }
        
        stateTimer += Time.deltaTime;
        float adjustedReloadTime = reloadTime * scrLocalGameManager.Instance.TimeDialation;
        if (stateTimer >= adjustedReloadTime)
        {
            TransitionToState(NpcState.Idle);
        }
    }
}