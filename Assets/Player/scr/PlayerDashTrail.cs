using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawns guide planes along the dash trajectory when the player enters
/// the Dashing state. Planes are placed every <see cref="spawnInterval"/> meters
/// along the straight line from dashStart to dashTarget.
///
/// Uses a simple object pool so no Instantiate/Destroy happens at runtime
/// after the initial setup.
///
/// Orientation:  +Y = forward (flight direction),  +Z = up (world up).
/// Attach to the Player GameObject (same as other subsystems).
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerDashTrail : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Prefab")]
    [Tooltip("Plane prefab with your transparent circle material, size, etc. " +
             "Will be pooled at Start — never instantiated during gameplay.")]
    [SerializeField] private GameObject planePrefab;

    [Header("Spawn Settings")]
    [Tooltip("Distance (in meters) between each guide plane along the dash path.")]
    [SerializeField] private float spawnInterval = 2f;

    [Tooltip("Maximum number of planes that can exist at once. " +
             "Also defines the pool size created at Start.")]
    [SerializeField] private int maxPlanes = 100;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private PlayerDash dash;

    // Object pool
    private Transform poolContainer; // independent root object so planes don't follow the player
    private List<GameObject> pool;
    private int activePlaneCount;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        dash = GetComponent<PlayerDash>();
    }

    private void Start()
    {
        // Validate
        if (planePrefab == null)
        {
            Debug.LogError("[PlayerDashTrail] planePrefab is not assigned!");
            enabled = false;
            return;
        }

        // Build pool
        CreatePool();

        // Subscribe to state changes
        core.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        if (core != null)
        {
            core.OnStateChanged -= HandleStateChanged;
        }

        // Clean up the independent pool container
        if (poolContainer != null)
        {
            Destroy(poolContainer.gameObject);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Pool Management
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates all pool objects once at Start. They stay deactivated until needed.
    /// </summary>
    private void CreatePool()
    {
        // Create an independent root object — not parented to the player,
        // so the planes stay in world space and don't move with the player.
        poolContainer = new GameObject("DashTrailPool").transform;

        pool = new List<GameObject>(maxPlanes);

        for (int i = 0; i < maxPlanes; i++)
        {
            GameObject plane = Instantiate(planePrefab, Vector3.zero, Quaternion.identity, poolContainer);
            plane.SetActive(false);
            pool.Add(plane);
        }
    }

    /// <summary>
    /// Deactivates all currently active planes and resets the counter.
    /// </summary>
    private void DeactivateAll()
    {
        for (int i = 0; i < activePlaneCount; i++)
        {
            pool[i].SetActive(false);
        }
        activePlaneCount = 0;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Change Handling
    // ════════════════════════════════════════════════════════════════════════

    private void HandleStateChanged(PlayerCore.PlayerState oldState, PlayerCore.PlayerState newState)
    {
        // Spawn planes when entering attack dash
        if (newState == PlayerCore.PlayerState.Dashing)
        {
            SpawnTrail();
        }

        // Remove planes when leaving attack dash (any exit reason)
        if (oldState == PlayerCore.PlayerState.Dashing)
        {
            DeactivateAll();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Trail Spawning
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Places planes along the dash path from start to target.
    /// Each plane faces the flight direction with +Y forward and +Z up.
    /// </summary>
    private void SpawnTrail()
    {
        // Make sure the previous trail is gone (safety check)
        DeactivateAll();

        if (dash == null) return;

        // Use the dash's recorded start point (= camera position at dash start).
        // This guarantees the trail is anchored exactly to the dash axis and
        // doesn't shift if the camera moves a tiny bit between dash trigger
        // and the next frame.
        Vector3 start = dash.DashStartPosition;
        Vector3 direction = dash.DashDirection;

        // Distance from the dash's own data — guaranteed identical to the
        // length the player actually traverses. No extra raycast needed.
        float dashDistance = Vector3.Distance(start, dash.DashTargetPosition);

        // How many planes to spawn
        // First plane at spawnInterval, then every spawnInterval until end
        int planeCount = Mathf.FloorToInt(dashDistance / spawnInterval);
        planeCount = Mathf.Min(planeCount, maxPlanes);

        if (planeCount <= 0) return;

        // Build rotation: we want +Y pointing along flight direction, +Z pointing up.
        // Unity's default Plane mesh has +Y as its normal (up).
        // LookRotation(forward, up) gives us: +Z = forward, +Y = up.
        // We need +Y = forward (flight dir), +Z = up (world up).
        // So we build the rotation in two steps:
        //   1. LookRotation makes +Z = dashDirection, +Y = up
        //   2. Then rotate -90° around local X so +Y becomes what was +Z (= dashDirection)
        Quaternion baseRotation = Quaternion.LookRotation(direction, Vector3.up);
        Quaternion correction = Quaternion.Euler(-90f, 0f, 0f);
        Quaternion finalRotation = baseRotation * correction;

        for (int i = 0; i < planeCount; i++)
        {
            float distance = (i + 1) * spawnInterval;
            Vector3 position = start + direction * distance;

            GameObject plane = pool[i];
            plane.transform.SetPositionAndRotation(position, finalRotation);
            plane.SetActive(true);
        }

        activePlaneCount = planeCount;
    }

    #endregion
}
