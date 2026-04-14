using UnityEngine;
using System.Collections.Generic;

// ════════════════════════════════════════════════════════════════════════════
// EXPLOSION SPHERE - Wachsende Explosionskugel mit Schaden
// ════════════════════════════════════════════════════════════════════════════
//
// Prefab-Setup:
//   - MeshFilter + MeshRenderer (Sphere-Mesh + Explosions-Material)
//   - Dieses Script
//   - Kein SphereCollider nötig (nutzt Physics.OverlapSphere)
//
// Schaden:
//   - Spieler: einmalig via PlayerCore.TakeDirectDamage (nicht blockbar)
//   - NPCs/Minen: einmalig via IDamageable.TakeDamage
//   - Ragdoll-NPCs erhalten Impact Force (Richtung: Explosion → NPC)
//   - Jedes Ziel wird nur einmal getroffen
//
// Detection:
//   - Physics.OverlapSphere jeden Frame (funktioniert auch mit Trigger-Collidern)
//
// ════════════════════════════════════════════════════════════════════════════

public class ExplosionSphere : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Expansion")]
    [Tooltip("Maximaler Radius der Explosion")]
    [SerializeField] private float maxRadius = 5f;

    [Tooltip("Zeit bis die Explosion ihre volle Größe erreicht")]
    [SerializeField] private float expandDuration = 0.3f;

    [Header("Damage")]
    [Tooltip("Schaden am Spieler bei Berührung (einmalig)")]
    [SerializeField] private float playerDamage = 80f;

    [Tooltip("Schaden an NPCs bei Berührung (einmalig)")]
    [SerializeField] private float npcDamage = 50f;

    [Header("Detection")]
    [Tooltip("Welche Layer von der Explosion getroffen werden")]
    [SerializeField] private LayerMask damageableLayers = ~0;

    [Header("Lifetime")]
    [Tooltip("Gesamte Lebensdauer des ExplosionGO (nach Spawn)")]
    [SerializeField] private float lifetime = 1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maximaler Explosionsradius. Wird von ProxyMineNpc gelesen,
    /// damit die Warn-Sphere synchron bleibt.
    /// </summary>
    public float MaxRadius => maxRadius;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private float timer;
    private float currentRadius;

    // Jedes Ziel wird nur einmal getroffen (Root-GameObject als Key)
    private HashSet<GameObject> damagedTargets = new HashSet<GameObject>();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        transform.localScale = Vector3.zero;
    }

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        timer += Time.deltaTime;
        UpdateExpansion();
        CheckOverlap();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Expansion
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateExpansion()
    {
        if (timer >= expandDuration) return;

        float progress = timer / expandDuration;
        currentRadius = Mathf.Lerp(0f, maxRadius, progress);

        // Mesh-Visualisierung (Sphere-Mesh hat Durchmesser 1 bei Scale 1)
        float diameter = currentRadius * 2f;
        transform.localScale = Vector3.one * diameter;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Overlap Detection & Damage
    // ════════════════════════════════════════════════════════════════════════

    private void CheckOverlap()
    {
        if (currentRadius <= 0f) return;

        // QueryTriggerInteraction.Collide → erkennt auch Trigger-Collider
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            currentRadius,
            damageableLayers,
            QueryTriggerInteraction.Collide
        );

        foreach (Collider hit in hits)
        {
            ProcessHit(hit);
        }
    }

    private void ProcessHit(Collider hit)
    {
        // Root-GameObject bestimmen (verhindert Mehrfach-Treffer durch Child-Collider)
        GameObject rootObject = hit.transform.root.gameObject;

        if (damagedTargets.Contains(rootObject)) return;

        // ── Spieler ─────────────────────────────────────────────────────
        PlayerCore player = hit.GetComponent<PlayerCore>();
        if (player == null) player = hit.GetComponentInParent<PlayerCore>();

        if (player != null)
        {
            damagedTargets.Add(rootObject);
            player.TakeDirectDamage(playerDamage, "Explosion");
            return;
        }

        // ── IDamageable (NPCs, Minen, zerstörbare Objekte) ─────────────
        IDamageable damageable = hit.GetComponent<IDamageable>();
        if (damageable == null) damageable = hit.GetComponentInParent<IDamageable>();

        if (damageable != null)
        {
            damagedTargets.Add(rootObject);

            Vector3 hitDirection = (hit.transform.position - transform.position).normalized;
            Vector3 hitPoint = hit.ClosestPoint(transform.position);
            damageable.TakeDamage(npcDamage, hitPoint, hitDirection);

            // Impact registrieren (falls vorhanden)
            NpcImpactTracker impactTracker = hit.GetComponent<NpcImpactTracker>();
            if (impactTracker == null) impactTracker = hit.GetComponentInParent<NpcImpactTracker>();

            if (impactTracker != null)
            {
                impactTracker.RegisterExplosionImpact(transform.position);
            }
        }
    }

    #endregion
}
