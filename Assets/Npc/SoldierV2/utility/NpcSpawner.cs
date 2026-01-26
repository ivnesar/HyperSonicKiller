using UnityEngine;

/// <summary>
/// Simple utility for spawning NPCs in the scene.
/// Useful for testing and wave-based gameplay.
/// </summary>
public class NpcSpawner : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Prefabs")]
    [SerializeField] private GameObject soldierPrefab;
    [SerializeField] private GameObject defenderPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float spawnRadius = 2f;

    [Header("Debug Spawning")]
    [SerializeField] private bool enableDebugSpawning = true;
    [SerializeField] private KeyCode spawnSoldierKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode spawnDefenderKey = KeyCode.Alpha2;
    [SerializeField] private KeyCode spawnPairKey = KeyCode.Alpha3;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!enableDebugSpawning) return;

        if (Input.GetKeyDown(spawnSoldierKey))
        {
            SpawnSoldier();
        }

        if (Input.GetKeyDown(spawnDefenderKey))
        {
            SpawnDefender();
        }

        if (Input.GetKeyDown(spawnPairKey))
        {
            SpawnSoldierDefenderPair();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public Spawn Methods
    // ────────────────────────────────────────────────────────────────────────────────

    public SoldierNpc SpawnSoldier()
    {
        return SpawnSoldier(GetRandomSpawnPosition());
    }

    public SoldierNpc SpawnSoldier(Vector3 position)
    {
        if (soldierPrefab == null)
        {
            Debug.LogError("[NpcSpawner] Soldier prefab not assigned!");
            return null;
        }

        GameObject go = Instantiate(soldierPrefab, position, Quaternion.identity);
        go.name = $"Soldier_{System.Guid.NewGuid().ToString().Substring(0, 4)}";

        return go.GetComponent<SoldierNpc>();
    }

    public DefenderNpc SpawnDefender()
    {
        return SpawnDefender(GetRandomSpawnPosition());
    }

    public DefenderNpc SpawnDefender(Vector3 position)
    {
        if (defenderPrefab == null)
        {
            Debug.LogError("[NpcSpawner] Defender prefab not assigned!");
            return null;
        }

        GameObject go = Instantiate(defenderPrefab, position, Quaternion.identity);
        go.name = $"Defender_{System.Guid.NewGuid().ToString().Substring(0, 4)}";

        return go.GetComponent<DefenderNpc>();
    }

    /// <summary>
    /// Spawns a soldier with a defender positioned to protect them.
    /// </summary>
    public void SpawnSoldierDefenderPair()
    {
        Vector3 soldierPos = GetRandomSpawnPosition();
        SpawnSoldier(soldierPos);

        // Spawn defender slightly in front of the soldier (toward player)
        Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player != null)
        {
            Vector3 dirToPlayer = (player.position - soldierPos).normalized;
            Vector3 defenderPos = soldierPos + dirToPlayer * 2f;
            SpawnDefender(defenderPos);
        }
        else
        {
            // No player, spawn defender nearby
            Vector3 defenderPos = soldierPos + Vector3.forward * 2f;
            SpawnDefender(defenderPos);
        }
    }

    /// <summary>
    /// Spawns a wave of enemies.
    /// </summary>
    public void SpawnWave(int soldierCount, int defenderCount)
    {
        for (int i = 0; i < soldierCount; i++)
        {
            SpawnSoldier();
        }

        for (int i = 0; i < defenderCount; i++)
        {
            SpawnDefender();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Position Helpers
    // ────────────────────────────────────────────────────────────────────────────────

    private Vector3 GetRandomSpawnPosition()
    {
        // Use spawn points if available
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            return point.position + Random.insideUnitSphere * spawnRadius;
        }

        // Otherwise spawn around this object
        Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
        return transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug Visualization
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Draw spawn radius around this object
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        // Draw spawn points
        if (spawnPoints != null)
        {
            Gizmos.color = Color.cyan;
            foreach (var point in spawnPoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 0.5f);
                    Gizmos.DrawWireSphere(point.position, spawnRadius);
                }
            }
        }
    }

    #endregion
}
