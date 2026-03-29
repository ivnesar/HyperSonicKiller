using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

// ════════════════════════════════════════════════════════════════════════════
// PARTICLE COLLISION DECAL SPAWNER - Blutflecken bei Partikel-Kollisionen
// ════════════════════════════════════════════════════════════════════════════
//
// Spawnt URP Decal Projectors an den Stellen, wo Partikel kollidieren.
// Nutzt den bestehenden BloodDecalPool (Singleton) für das Recycling.
//
// SETUP:
//   1. Dieses Script auf das gleiche GameObject wie das ParticleSystem legen
//   2. Im ParticleSystem → Collision-Modul aktivieren:
//      - Type: World
//      - Send Collision Messages: AN (wichtig!)
//   3. Decal-Material zuweisen (Shader: Shader Graphs/Decal mit Blut-Textur)
//   4. Sicherstellen dass ein BloodDecalPool in der Szene existiert
//
// FUNKTIONSWEISE:
//   - Unity ruft OnParticleCollision auf wenn Partikel einen Collider treffen
//   - Für jede Kollision wird Position + Normale ausgelesen
//   - Ein Decal-Objekt wird aus dem BloodDecalPool geholt
//   - Der DecalProjector wird konfiguriert (Material, Größe, Rotation)
//   - Das Decal wird aktiviert und projiziert auf die getroffene Oberfläche
//
// DECAL RENDERER FEATURE:
//   Im URP Renderer Asset muss die "Decal" Renderer Feature
//   aktiviert sein, sonst werden keine Decals angezeigt!
//
// ════════════════════════════════════════════════════════════════════════════

public class ParticleCollisionDecalSpawner : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────

    [Header("Decal Settings")]
    [Tooltip("Das Decal-Material (muss Shader Graphs/Decal verwenden). " +
             "Die Blutfleck-Textur wird über dieses Material gesteuert.")]
    [SerializeField] private Material decalMaterial;

    [Tooltip("Minimale Größe eines Blutfleck-Decals (Breite/Höhe in Metern).")]
    [SerializeField] private float decalSizeMin = 0.2f;

    [Tooltip("Maximale Größe eines Blutfleck-Decals (Breite/Höhe in Metern).")]
    [SerializeField] private float decalSizeMax = 0.5f;

    [Tooltip("Tiefe des Decal Projectors (wie tief die Projektion reicht). " +
             "Muss groß genug sein damit der Decal die Oberfläche erreicht.")]
    [SerializeField] private float decalDepth = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────

    // Wiederverwendbare Liste für Collision Events (vermeidet Allokationen pro Frame)
    private List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>();

    // Referenz auf unser ParticleSystem
    private ParticleSystem particleSys;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        particleSys = GetComponent<ParticleSystem>();

        if (particleSys == null)
        {
            Debug.LogError($"[ParticleCollisionDecalSpawner] {name}: Kein ParticleSystem gefunden! " +
                           "Dieses Script muss auf dem gleichen GameObject wie das ParticleSystem liegen.");
        }

        if (decalMaterial == null)
        {
            Debug.LogWarning($"[ParticleCollisionDecalSpawner] {name}: Kein Decal-Material zugewiesen! " +
                             "Blutflecken werden nicht erzeugt.", this);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Particle Collision Callback
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unity ruft diese Methode automatisch auf, wenn Partikel mit einem Collider kollidieren.
    /// Voraussetzung: Im Collision-Modul des ParticleSystems muss "Send Collision Messages" aktiviert sein!
    /// </summary>
    private void OnParticleCollision(GameObject other)
    {
        if (particleSys == null) return;
        if (decalMaterial == null) return;

        // Alle Kollisionen mit diesem GameObject in diesem Frame abfragen
        int eventCount = particleSys.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < eventCount; i++)
        {
            SpawnDecal(collisionEvents[i].intersection, collisionEvents[i].normal);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Decal Spawning
    // ────────────────────────────────────────────────────────────────────────

    private void SpawnDecal(Vector3 position, Vector3 surfaceNormal)
    {
        // Pool fragen — falls kein Pool vorhanden, direkt erzeugen (Fallback)
        GameObject decalObj = BloodDecalPool.Instance != null
            ? BloodDecalPool.Instance.GetDecal()
            : CreateDecalObject();

        // Position: leicht über der Oberfläche platzieren
        // Der Projector projiziert entlang seiner lokalen Z-Achse
        decalObj.transform.position = position + surfaceNormal * (decalDepth * 0.5f);

        // Rotation: Projector schaut entlang der Surface-Normal (nach innen)
        decalObj.transform.rotation = Quaternion.LookRotation(-surfaceNormal);

        // Zufällige Rotation um die Projektionsachse (damit nicht alle gleich aussehen)
        float randomAngle = Random.Range(0f, 360f);
        decalObj.transform.Rotate(Vector3.forward, randomAngle, Space.Self);

        // Zufällige Größe
        float size = Random.Range(decalSizeMin, decalSizeMax);

        // Decal Projector konfigurieren
        DecalProjector projector = decalObj.GetComponent<DecalProjector>();
        if (projector != null)
        {
            projector.material = decalMaterial;
            projector.size = new Vector3(size, size, decalDepth);
            projector.fadeFactor = 1f;
        }

        // Aktivieren — Pool gibt deaktivierte Objekte zurück
        decalObj.SetActive(true);

        if (showDebugInfo)
            Debug.Log($"[ParticleCollisionDecalSpawner] Decal bei {position} " +
                      $"(Normale: {surfaceNormal}, Größe: {size:F2})");
    }

    /// <summary>
    /// Erzeugt ein neues Decal-GameObject (Fallback wenn kein Pool vorhanden).
    /// Im Normalfall nutzt der Pool eigene Objekte.
    /// </summary>
    private GameObject CreateDecalObject()
    {
        Debug.LogWarning("[ParticleCollisionDecalSpawner] Kein BloodDecalPool in der Szene! " +
                         "Fallback: Decal wird direkt erzeugt (kein Recycling).");

        GameObject obj = new GameObject("BloodDecal_Fallback");
        obj.AddComponent<DecalProjector>();
        return obj;
    }

    #endregion
}
