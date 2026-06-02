using UnityEngine;
using System.Collections.Generic;

// ════════════════════════════════════════════════════════════════════════════
// EXPLOSION SPHERE - Wachsende Explosionskugel mit Schaden
// ════════════════════════════════════════════════════════════════════════════
//
// Prefab-Setup:
//   - MeshFilter + MeshRenderer (Sphere-Mesh + Explosions-Material)
//   - Dieses Script
//   - KEIN Collider und KEINE LayerMask mehr nötig (siehe unten)
//
// Schadens-Erkennung (NEU: Distanz statt Physics):
//   - Früher: Physics.OverlapSphere jeden Frame (abhängig von Collider-Layern,
//     Trigger-Flags und vom Physics-Step → bei starker Slowmotion fehleranfällig).
//   - Jetzt: Distanz-Vergleich vom Explosionszentrum zu jedem NPC.
//   - NPCs kommen aus der NpcRegistry (jeder NpcBase trägt sich selbst ein/aus).
//   - Der Spieler steht NICHT in der Registry und wird separat geprüft.
//   - Vorteile: unabhängig von Time.timeScale, deterministisch, kein Collider-Setup,
//     und keine Doppel-/Fehltreffer durch geteilte Eltern-Objekte (transform.root).
//   - Hinweis: Gemessen wird zur Pivot-Position des NPCs (meist die Füße), nicht
//     zur Collider-Hülle. Für den Prototyp ausreichend genau.
//
// Wachsende Welle:
//   - currentRadius wächst über expandDuration von 0 auf maxRadius.
//   - Jeden Frame werden alle noch nicht getroffenen Ziele innerhalb des
//     AKTUELLEN Radius getroffen → die Welle erwischt auch Nachzügler.
//   - Jedes Ziel wird nur einmal getroffen.
//
// Schaden:
//   - Einheitlicher Schadenswert für Spieler und NPCs (explosionDamage).
//   - Kann zur Laufzeit per SetDamage() überschrieben werden (z.B. von ProxyMineNpc).
//   - Spieler: einmalig via PlayerCore.TakeDirectDamage (nicht blockbar).
//   - NPCs/Minen: einmalig via NpcBase.TakeDamage.
//   - NPCs mit NpcImpactTracker erhalten zusätzlich einen Explosions-Impuls
//     (Richtung: Explosion → NPC) für das Ragdoll.
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
    
    private float explosionDamage = 9999;

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

    /// <summary>
    /// Setzt den Explosionsschaden zur Laufzeit (z.B. direkt nach Instantiate).
    /// Ermöglicht, dass Quellen wie ProxyMineNpc ihren eigenen Schadenswert nutzen.
    /// </summary>
    public void SetDamage(float damage)
    {
        explosionDamage = damage;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private float timer;
    private float currentRadius;

    // Festes Explosionszentrum (die Kugel bewegt sich nicht, sie skaliert nur).
    private Vector3 center;

    // Spieler-Referenz (einmalig gesucht, da der Spieler nicht in der Registry steht).
    private PlayerCore player;
    private bool playerDamaged;

    // Bereits getroffene NPCs (jeder wird nur einmal beschädigt).
    private readonly HashSet<NpcBase> damagedNpcs = new();

    // Wiederverwendeter Puffer für die sichere Iteration über die Registry.
    // Wir kopieren die NPC-Liste jeden Frame hier rein, weil TakeDamage einen NPC
    // töten kann → das entfernt ihn aus der Registry → sonst würde die Iteration
    // über die Original-Sammlung crashen ("Collection was modified").
    private readonly List<NpcBase> npcBuffer = new();

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
        center = transform.position;
        player = FindFirstObjectByType<PlayerCore>();

        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        timer += Time.deltaTime;
        UpdateExpansion();
        DealDamageInRadius();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Expansion (rein visuell)
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateExpansion()
    {
        float progress = expandDuration <= 0f
            ? 1f
            : Mathf.Clamp01(timer / expandDuration);

        currentRadius = Mathf.Lerp(0f, maxRadius, progress);

        // Mesh-Visualisierung (Sphere-Mesh hat Durchmesser 1 bei Scale 1)
        float diameter = currentRadius * 2f;
        transform.localScale = Vector3.one * diameter;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Damage (Distanz-basiert)
    // ════════════════════════════════════════════════════════════════════════

    private void DealDamageInRadius()
    {
        if (currentRadius <= 0f) return;

        DamagePlayerIfInRadius();
        DamageNpcsInRadius();
    }

    private void DamagePlayerIfInRadius()
    {
        if (playerDamaged) return;
        if (player == null) return;

        float distance = Vector3.Distance(center, player.transform.position);
        if (distance > currentRadius) return;

        playerDamaged = true;
        player.TakeDirectDamage(explosionDamage, "Explosion");
    }

    private void DamageNpcsInRadius()
    {
        // Kopie ziehen (siehe Kommentar bei npcBuffer oben).
        npcBuffer.Clear();
        npcBuffer.AddRange(NpcRegistry.AliveNpcs);

        foreach (NpcBase npc in npcBuffer)
        {
            if (npc == null) continue;
            if (damagedNpcs.Contains(npc)) continue;

            float distance = Vector3.Distance(center, npc.transform.position);
            if (distance > currentRadius) continue;

            damagedNpcs.Add(npc);
            ApplyDamageToNpc(npc);
        }
    }

    private void ApplyDamageToNpc(NpcBase npc)
    {
        Vector3 hitDirection = (npc.transform.position - center).normalized;
        Vector3 hitPoint = npc.transform.position;

        npc.TakeDamage(explosionDamage, hitPoint, hitDirection);

        // Ragdoll-Impuls registrieren (Richtung: Explosion → NPC), falls vorhanden.
        NpcImpactTracker impactTracker = npc.GetComponent<NpcImpactTracker>();
        if (impactTracker != null)
        {
            impactTracker.RegisterExplosionImpact(center);
        }
    }

    #endregion
}
