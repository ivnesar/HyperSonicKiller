// ===== SIMPLIFIED MISSILE BEHAVIOR =====
using UnityEngine;

public class NPCMissileBehavior : MonoBehaviour, INPCBehavior
{
    [Header("Missile Settings")]
    [SerializeField] private GameObject missilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [SerializeField] private float missileSpeed = 15f;
    [SerializeField] private int missileCount = 3;
    [SerializeField] private float missileInterval = 0.5f;
    [SerializeField] private float reloadTime = 3f;
    [SerializeField] private float preparationTime = 1f;

    private NPCEnemyController controller;
    private scrLocalGameManager lgm;
    
    private float attackStartTime;
    private float lastMissileTime;
    private float reloadStartTime;
    private int missilesFired;
    private Vector3 targetPlayerPosition;
    private bool isPreparing;
    private bool needsReload;

    void Awake()
    {
        controller = GetComponent<NPCEnemyController>();
    }

    void Start()
    {
        lgm = scrLocalGameManager.Instance;
    }

    public bool CanAttack(float distanceToPlayer, bool hasLOS)
    {
        // Can attack if: in range, has LOS, and doesn't need reload
        return hasLOS && distanceToPlayer <= controller.Agent.stoppingDistance;
    }

    public void OnStartAttack()
    {
        attackStartTime = Time.time;
        isPreparing = true;
        missilesFired = 0;
        targetPlayerPosition = controller.Player.transform.position;
        Debug.Log($"{gameObject.name} preparing missiles!");
    }

    public void UpdateAttack()
    {
        // Wait for preparation time
        if (isPreparing)
        {
            if (Time.time - attackStartTime >= preparationTime)
            {
                isPreparing = false;
                lastMissileTime = Time.time;
            }
            return;
        }
        Debug.Log("missle");
        // Fire missiles
        if (missilesFired < missileCount)
        {
            float adjustedInterval = missileInterval * lgm.TimeDialation;
            if (Time.time - lastMissileTime >= adjustedInterval)
            {
                Debug.Log("inst missle");
                FireMissile();
                missilesFired++;
                lastMissileTime = Time.time;
            }
        }
    }

    public void OnStopAttack()
    {
        isPreparing = false;
        // If stopped mid-volley, mark as needing reload
        if (missilesFired > 0)
        {
            needsReload = true;
        }
    }

    public bool ShouldReload()
    {
        // Should reload if all missiles fired or if we stopped mid-volley
        return missilesFired >= missileCount || needsReload;
    }

    public void OnStartReload()
    {
        reloadStartTime = Time.time;
        needsReload = true;
        Debug.Log($"{gameObject.name} started reloading missiles...");
    }

    public void UpdateReload()
    {
        // Reload happens automatically over time, no update needed
    }

    public void OnStopReload()
    {
        missilesFired = 0;
        needsReload = false;
        Debug.Log($"{gameObject.name} missiles reloaded!");
    }

    public bool IsReloadComplete()
    {
        float adjustedReloadTime = reloadTime * lgm.TimeDialation;
        return Time.time >= reloadStartTime + adjustedReloadTime;
    }

    private void FireMissile()
    {
        Vector3 spawnPos = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position + Vector3.up;
        
        // Update target to current player position
        targetPlayerPosition = controller.Player.transform.position;
        Vector3 direction = (targetPlayerPosition - spawnPos).normalized;

        GameObject missile = Instantiate(missilePrefab, spawnPos, Quaternion.LookRotation(direction));
        
        // If missile has a rigidbody, set its velocity
        Rigidbody rb = missile.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = direction * missileSpeed;
        }
        
        Debug.DrawLine(spawnPos, spawnPos + direction * 10f, Color.yellow, 1f);
        Debug.Log($"{gameObject.name} fired missile {missilesFired + 1}/{missileCount}");
    }
}