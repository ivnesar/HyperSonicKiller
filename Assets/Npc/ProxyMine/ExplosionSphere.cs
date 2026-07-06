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
//   - Spieler-Erkennung ist swept: Es wird die komplette Bewegung von
//     PreviousDetectionPosition → CurrentDetectionPosition geprüft, nicht nur
//     die Endposition des Frames. Dadurch tunnelt der Spieler nicht durch
//     die wachsende Explosionskugel.
//   - Vorteile: unabhängig von Time.timeScale, deterministisch, kein Collider-Setup,
//     und keine Doppel-/Fehltreffer durch geteilte Eltern-Objekte (transform.root).
//   - Hinweis: NPCs werden weiter zur Pivot-Position geprüft. Für den Prototyp
//     ausreichend genau.
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

    [Header("High-Speed Player Detection")]
    [Tooltip("Wenn aktiv, wird der Spieler entlang seines kompletten Bewegungssegments geprüft. Verhindert Tunneling bei extrem hoher Geschwindigkeit.")]
    [SerializeField] private bool useSweptPlayerDetection = true;

    [Tooltip("Fallback-Radius für den Spieler, falls PlayerCore keinen sinnvollen MovementDetectionRadius liefert.")]
    [Min(0f)]
    [SerializeField] private float fallbackPlayerDetectionRadius = 0.35f;

    [Tooltip("Wie alt das Player-Bewegungssegment maximal sein darf. 1 unterstützt übliche Script Execution Orders. Ältere Segmente werden zur aktuellen Position kollabiert.")]
    [Min(0)]
    [SerializeField] private int maxPlayerSegmentAgeFrames = 1;

    [Tooltip("Maximale gültige Länge des Player-Bewegungssegments. Verhindert falsche Treffer nach Teleport/Respawn. 0 = keine Begrenzung.")]
    [Min(0f)]
    [SerializeField] private float maxValidPlayerSegmentLength = 40f;

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

        float playerRadius = GetPlayerDetectionRadius();
        float allowedRadius = currentRadius + playerRadius;

        if (useSweptPlayerDetection && TryGetValidPlayerMovementSegment(out Vector3 playerPrevious, out Vector3 playerCurrent))
        {
            float sqrDistance = PointSegmentSqrDistance(center, playerPrevious, playerCurrent, out _);
            if (sqrDistance > allowedRadius * allowedRadius) return;
        }
        else
        {
            float sqrDistance = (center - GetFallbackPlayerPosition()).sqrMagnitude;
            if (sqrDistance > allowedRadius * allowedRadius) return;
        }

        playerDamaged = true;
        player.TakeDirectDamage(explosionDamage, "Explosion");
    }

    private bool TryGetValidPlayerMovementSegment(out Vector3 previous, out Vector3 current)
    {
        previous = GetFallbackPlayerPosition();
        current = previous;

        if (player == null) return false;

        previous = player.PreviousDetectionPosition;
        current = player.CurrentDetectionPosition;

        // Wenn das Segment zu alt ist, nicht noch einmal den alten Dash-Pfad auswerten.
        int segmentAge = Time.frameCount - player.LastDetectionMoveFrame;
        if (segmentAge > maxPlayerSegmentAgeFrames)
        {
            previous = current;
        }

        // Nach Teleport/Respawn kann das Segment extrem lang sein. Dann nur die
        // aktuelle Position prüfen, damit keine Mine/Explosion quer durch den Level trifft.
        if (maxValidPlayerSegmentLength > 0f)
        {
            float maxSqrLength = maxValidPlayerSegmentLength * maxValidPlayerSegmentLength;
            if ((current - previous).sqrMagnitude > maxSqrLength)
            {
                previous = current;
                return false;
            }
        }

        return true;
    }

    private float GetPlayerDetectionRadius()
    {
        if (player == null) return Mathf.Max(0f, fallbackPlayerDetectionRadius);

        return Mathf.Max(fallbackPlayerDetectionRadius, player.MovementDetectionRadius);
    }

    private Vector3 GetFallbackPlayerPosition()
    {
        return player != null ? player.transform.position : center;
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

    private static float PointSegmentSqrDistance(
        Vector3 point,
        Vector3 segmentStart,
        Vector3 segmentEnd,
        out Vector3 closestPoint)
    {
        Vector3 segment = segmentEnd - segmentStart;
        float sqrLength = segment.sqrMagnitude;

        if (sqrLength <= 0.000001f)
        {
            closestPoint = segmentStart;
            return (point - closestPoint).sqrMagnitude;
        }

        float t = Vector3.Dot(point - segmentStart, segment) / sqrLength;
        t = Mathf.Clamp01(t);

        closestPoint = segmentStart + segment * t;
        return (point - closestPoint).sqrMagnitude;
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
