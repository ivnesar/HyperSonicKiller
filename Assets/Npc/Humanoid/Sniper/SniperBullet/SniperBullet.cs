using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SNIPER BULLET - Schnelles Hochschaden-Projektil
// ════════════════════════════════════════════════════════════════════════════
//
// Unterschiede zur SoldierBullet:
//   - Deutlich höhere Geschwindigkeit (simuliert Scharfschützengewehr)
//   - Optionaler Trail-Effekt (TrailRenderer oder LineRenderer)
//   - Gleiche Grundmechanik: fliegt in eine Richtung, prüft Kollision
//
// Kollision:
//   Nutzt Raycast pro Frame (Schritt-basiert), um auch bei hoher
//   Geschwindigkeit keine Objekte zu durchfliegen.
//
// SETUP:
//   1. Prefab erstellen: Empty GameObject mit diesem Script
//   2. Optional: TrailRenderer als Child für den Leuchtspureffekt
//   3. Optional: Mesh/Partikel für die Kugel selbst
//   4. Wird von SniperNpc.FireShot() instanziiert
//
// ════════════════════════════════════════════════════════════════════════════

public class SniperBullet : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Bullet Settings")]
    [Tooltip("Geschwindigkeit der Kugel in m/s")]
    [SerializeField] private float speed = 200f;

    [Tooltip("Maximale Lebensdauer in Sekunden (Sicherheitsnetz)")]
    [SerializeField] private float maxLifetime = 3f;

    [Header("Trail")]
    [Tooltip("Optionaler TrailRenderer für den Leuchtspureffekt")]
    [SerializeField] private TrailRenderer trail;

    [Tooltip("Zeit die der Trail nach Einschlag noch sichtbar bleibt")]
    [SerializeField] private float trailLingerTime = 0.3f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private Vector3 direction;
    private int damage;
    private Transform shooter;
    private LayerMask hitMask;
    private bool isInitialized;
    private float spawnTime;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialisiert die Kugel. Wird von SniperNpc.FireShot() aufgerufen.
    /// </summary>
    /// <param name="fireDirection">Normalisierte Schussrichtung</param>
    /// <param name="bulletDamage">Schaden bei Treffer</param>
    /// <param name="shooterTransform">Transform des Schützen (wird bei Kollision ignoriert)</param>
    /// <param name="bulletHitMask">Layer-Maske für Kollisionserkennung</param>
    public void Initialize(Vector3 fireDirection, int bulletDamage, Transform shooterTransform, LayerMask bulletHitMask)
    {
        direction = fireDirection.normalized;
        damage = bulletDamage;
        shooter = shooterTransform;
        hitMask = bulletHitMask;
        isInitialized = true;
        spawnTime = Time.time;

        Debug.Log($"[SniperBullet] Initialisiert — Damage: {damage}, Speed: {speed}, " +
                  $"HitMask: {hitMask.value} (Layers: {LayerMaskToString(hitMask)}), " +
                  $"Richtung: {direction}, Shooter: '{shooter.name}'");

        // Kugel in Flugrichtung drehen
        if (direction.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(direction);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (trail == null)
            trail = GetComponentInChildren<TrailRenderer>();
    }

    private void Update()
    {
        if (!isInitialized) return;

        // Lebensdauer prüfen
        if (Time.time - spawnTime > maxLifetime)
        {
            DestroyBullet();
            return;
        }

        float moveDistance = speed * Time.deltaTime;

        // Raycast für Kollisionserkennung (verhindert Durchfliegen bei hoher Geschwindigkeit)
        if (Physics.Raycast(transform.position, direction, out RaycastHit hit, moveDistance, hitMask))
        {
            // Eigenen Schützen ignorieren
            if (hit.collider.transform.IsChildOf(shooter) || hit.collider.transform == shooter)
            {
                Debug.Log($"[SniperBullet] Eigenen Schützen ignoriert: '{hit.collider.name}'");
                transform.position += direction * moveDistance;
                return;
            }

            // Treffer verarbeiten
            OnHit(hit);
            return;
        }

        // Kein Treffer — weiterfliegen
        transform.position += direction * moveDistance;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Impact
    // ════════════════════════════════════════════════════════════════════════

    private void OnHit(RaycastHit hit)
    {
        // Position auf Trefferpunkt setzen
        transform.position = hit.point;

        Debug.Log($"[SniperBullet] Treffer auf: '{hit.collider.name}' " +
                  $"(Tag: '{hit.collider.tag}', " +
                  $"Layer: '{LayerMask.LayerToName(hit.collider.gameObject.layer)}') " +
                  $"an Position {hit.point}");

        // ── Player-Treffer (gleiche Logik wie SoldierBullet) ──
        if (hit.transform.CompareTag("Player"))
        {
            var playerCore = hit.transform.GetComponent<PlayerCore>();
            if (playerCore != null)
            {
                // Angriffsrichtung: vom Schützen zum Spieler
                Vector3 attackDir = direction.normalized;
                string sourceName = shooter != null ? shooter.gameObject.name : "Sniper";
                playerCore.TakeDamage(damage, attackDir, sourceName);
                Debug.Log($"[SniperBullet] Player getroffen! Damage: {damage}");
            }
            else
            {
                Debug.LogWarning($"[SniperBullet] Player-Tag gefunden aber kein PlayerCore auf '{hit.collider.name}'!");
            }
        }
        // ── NPC/Objekt-Treffer (über IDamageable) ──
        else
        {
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, hit.point, direction);
                Debug.Log($"[SniperBullet] IDamageable getroffen: {damageable.GetType().Name}, Damage: {damage}");
            }
        }

        DestroyBullet();
    }

    /// <summary>
    /// Gibt den vollständigen Hierarchie-Pfad eines Transforms zurück (für Debug).
    /// </summary>
    private string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    /// <summary>
    /// Gibt alle Layer-Namen einer LayerMask als String zurück (für Debug).
    /// </summary>
    private static string LayerMaskToString(LayerMask mask)
    {
        var layers = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 32; i++)
        {
            if ((mask.value & (1 << i)) != 0)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                    layers.Add(name);
                else
                    layers.Add($"Layer{i}");
            }
        }
        return string.Join(", ", layers);
    }

    private void DestroyBullet()
    {
        isInitialized = false;

        // Trail loslösen damit er noch kurz sichtbar bleibt
        if (trail != null)
        {
            trail.transform.SetParent(null);
            trail.autodestruct = true;
            // Trail zerstört sich selbst nach seiner Time-Einstellung
        }

        Destroy(gameObject);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !isInitialized) return;

        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, direction * 2f);
        Gizmos.DrawSphere(transform.position, 0.05f);
    }

    #endregion
}
