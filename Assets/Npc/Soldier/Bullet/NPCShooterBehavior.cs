// ===== SIMPLIFIED SHOOTER BEHAVIOR =====
using UnityEngine;

public class NPCShooterBehavior : MonoBehaviour, INPCBehavior
{
    [Header("Shooter Settings")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [SerializeField] private float maxSprayAngle = 12f;
    [SerializeField] private float burstInterval = 0.03f;
    [SerializeField] private int burstCount = 31;
    [SerializeField] private float reloadTime = 4f;
    [SerializeField] private float preparationTime = 0.5f;

    [SerializeField] private Transform target;
    
    private NPCEnemyController controller;
    private scrLocalGameManager lgm;
    private FPSPlayerController asmPlayer;

    private float lastShotTime;
    private float reloadStartTime;
    private int shotsFired;
    private bool isPreparing;
    private bool needsReload;

    void Awake()
    {
        controller = GetComponent<NPCEnemyController>();
    }

    void Start()
    {
        lgm = scrLocalGameManager.Instance;
        asmPlayer = GameObject.FindGameObjectWithTag("Player").GetComponent<FPSPlayerController>();
        target = asmPlayer.playerCamera.transform;
    }

    public bool CanAttack(float distanceToPlayer, bool hasLOS)
    {
        // Can attack if: in range, has LOS, and doesn't need reload
        return hasLOS && distanceToPlayer <= controller.attackRange;
    }

    public void OnStartAttack()
    {
        isPreparing = true;
        shotsFired = 0;
        Debug.Log($"{gameObject.name} started attack!");
    }

    public void UpdateAttack()
    {
        // Fire burst
        if (shotsFired < burstCount)
        {
            float adjustedInterval = burstInterval * lgm.TimeDialation;
            if (Time.time - lastShotTime >= adjustedInterval)
            {
                FireBullet();
                shotsFired++;
                lastShotTime = Time.time;
            }
        }
    }

    public void OnStopAttack()
    {
        isPreparing = false;
        // If stopped mid-burst, mark as needing reload
        if (shotsFired > 0)
        {
            needsReload = true;
        }
    }

    public bool ShouldReload()
    {
        // Should reload if burst is complete or if we stopped mid-burst
        return shotsFired >= burstCount || needsReload;
    }

    public void OnStartReload()
    {
        reloadStartTime = Time.time;
        needsReload = true;
        Debug.Log($"{gameObject.name} started reloading...");
    }

    public void UpdateReload()
    {
        // Reload happens automatically over time, no update needed
    }

    public void OnStopReload()
    {
        shotsFired = 0;
        needsReload = false;
        Debug.Log($"{gameObject.name} reload complete!");
    }

    public bool IsReloadComplete()
    {
        float adjustedReloadTime = reloadTime * lgm.TimeDialation;
        return Time.time >= reloadStartTime + adjustedReloadTime;
    }

    private void FireBullet()
    {
        Vector3 spawnPos = projectileSpawnPoint.position;
        Vector3 targetPos = target.transform.position; // Aim at player
        // Use the NPC's current facing direction
        
        Vector3 add = (targetPos - spawnPos).normalized;
        Vector3 direction = transform.forward + new Vector3(0, add.y, 0);
  
        // Apply spray
        Quaternion spray = Quaternion.Euler(
            Random.Range(-maxSprayAngle, maxSprayAngle),
            Random.Range(-maxSprayAngle, maxSprayAngle),
            0
        );
        Vector3 sprayDirection = spray * direction;

        GameObject projectile = Instantiate(bulletPrefab, spawnPos, Quaternion.LookRotation(sprayDirection));
    
        Debug.DrawLine(spawnPos, spawnPos + sprayDirection * 5f, Color.red, 0.5f);
    }
}