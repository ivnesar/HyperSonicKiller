using UnityEngine;

/// <summary>
/// Einfaches Test-Script zum Spawnen von SoldierBullets in regelmäßigen Abständen.
/// Schießt Bullets in die Forward-Richtung des GameObjects.
/// </summary>
public class BulletSpawnerTest : MonoBehaviour
{
    [Header("Bullet Settings")]
    [SerializeField] private GameObject bulletPrefab; // Das SoldierBullet Prefab
    [SerializeField] private Transform spawnPoint; // Optional: Spawn-Position (falls null, wird transform.position benutzt)
    
    [Header("Spawn Timing")]
    [SerializeField] private float shootInterval = 1f; // Zeit zwischen Schüssen in Sekunden
    [SerializeField] private bool autoStart = true; // Automatisch beim Start schießen?
    
    [Header("Bullet Parameters")]
    [SerializeField] private int bulletDamage = 10;
    [SerializeField] private LayerMask hitMask = -1; // Welche Layer getroffen werden können
    
    
    private float nextShootTime;
    private bool isShooting;

    private void Start()
    {
        if (autoStart)
        {
            StartShooting();
        }
    }

    private void Update()
    {
        if (!isShooting) return;
        
        // Prüfen ob es Zeit ist zu schießen
        if (Time.time >= nextShootTime)
        {
            ShootBullet();
            nextShootTime = Time.time + shootInterval;
        }
    }

    /// <summary>
    /// Schießt eine einzelne Bullet.
    /// </summary>
    public void ShootBullet()
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning("[BulletSpawnerTest] Kein Bullet Prefab zugewiesen!");
            return;
        }

        // Spawn-Position bestimmen
        Vector3 spawnPos = (spawnPoint != null) ? spawnPoint.position : transform.position;
        
        // Bullet spawnen
        GameObject bulletObj = Instantiate(bulletPrefab, spawnPos, Quaternion.identity);
        
        // SoldierBullet Component holen und initialisieren
        SoldierBullet bullet = bulletObj.GetComponent<SoldierBullet>();
        if (bullet != null)
        {
            // Bullet in Forward-Richtung schießen
            bullet.Initialize(transform.forward, bulletDamage, transform, hitMask);
            
        }
        else
        {
            Debug.LogError("[BulletSpawnerTest] SoldierBullet Component nicht auf dem Prefab gefunden!");
            Destroy(bulletObj);
        }
    }

    /// <summary>
    /// Startet das automatische Schießen.
    /// </summary>
    public void StartShooting()
    {
        isShooting = true;
        nextShootTime = Time.time; // Sofort beim Start schießen
        
    }

    /// <summary>
    /// Stoppt das automatische Schießen.
    /// </summary>
    public void StopShooting()
    {
        isShooting = false;
        
    }

    // Visualisierung im Editor
    private void OnDrawGizmos()
    {
        Vector3 origin = (spawnPoint != null) ? spawnPoint.position : transform.position;
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, 0.1f);
        
        Gizmos.color = Color.red;
        Gizmos.DrawRay(origin, transform.forward * 2f);
    }
}