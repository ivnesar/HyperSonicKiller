using UnityEngine;
using UnityEngine.Rendering.Universal;

// ════════════════════════════════════════════════════════════════════════════
// RAGDOLL BLOOD DECALS - Blutflecken beim Bodenkontakt
// ════════════════════════════════════════════════════════════════════════════
//
// Wird direkt auf Ragdoll-Prefabs gelegt (neben SpawnedRagdoll).
// Spawnt URP Decal Projectors wenn bestimmte Bones den Boden berühren.
//
// SETUP:
//   1. Diese Komponente auf das Ragdoll-Prefab legen
//   2. Im Inspector die Bones zuweisen die Blutflecken erzeugen sollen
//      (z.B. Spine, Torso, Head)
//   3. Ein Decal-Material zuweisen (Shader: Shader Graphs/Decal)
//   4. Die Surface-Layer einstellen (auf welchen Layern Decals entstehen)
//   5. Sicherstellen dass ein BloodDecalPool in der Szene existiert
//
// FUNKTIONSWEISE:
//   - Beim Aktivieren werden auf jeden zugewiesenen Bone kleine
//     BloodBoneCollisionReporter-Komponenten gelegt
//   - Diese Reporter melden Kollisionen an dieses Script zurück
//   - Bei Kontakt mit einem passenden Layer wird ein Decal gespawnt
//   - Ein Cooldown pro Bone verhindert Decal-Spam
//   - Im Spur-Modus werden solange Decals gespawnt wie der Bone
//     den Boden berührt (mit Intervall)
//
// DECAL RENDERER FEATURE:
//   Im URP Renderer Asset muss die "Decal" Renderer Feature
//   aktiviert sein, sonst werden keine Decals angezeigt!
//
// ════════════════════════════════════════════════════════════════════════════

public class RagdollBloodDecals : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────

    [Header("Bone Setup")]
    [Tooltip("Die Bones die bei Bodenkontakt Blutflecken erzeugen sollen. " +
             "Wenn leer, werden automatisch Bones mit 'spine' oder 'torso' im Namen gesucht.")]
    [SerializeField] private Transform[] bloodBones;

    [Header("Decal Settings")]
    [Tooltip("Das Decal-Material (muss Shader Graphs/Decal verwenden). " +
             "Die Blutfleck-Textur wird über dieses Material gesteuert.")]
    [SerializeField] private Material decalMaterial;

    [Tooltip("Minimale Größe eines Blutfleck-Decals (Breite/Höhe in Metern).")]
    [SerializeField] private float decalSizeMin = 0.3f;

    [Tooltip("Maximale Größe eines Blutfleck-Decals (Breite/Höhe in Metern).")]
    [SerializeField] private float decalSizeMax = 0.6f;

    [Tooltip("Tiefe des Decal Projectors (wie tief die Projektion reicht). " +
             "Muss groß genug sein damit der Decal die Oberfläche erreicht.")]
    [SerializeField] private float decalDepth = 0.5f;

    [Header("Surface Detection")]
    [Tooltip("Auf welchen Layern sollen Blutflecken entstehen? " +
             "Typischerweise der Ground/Environment Layer.")]
    [SerializeField] private LayerMask surfaceLayers;

    [Header("Timing")]
    [Tooltip("Minimale Zeit in Sekunden zwischen zwei Decals vom gleichen Bone. " +
             "Verhindert Decal-Spam bei dauerhaftem Bodenkontakt.")]
    [SerializeField] private float spawnInterval = 0.3f;

    [Tooltip("Maximale Anzahl Decals die dieses Ragdoll insgesamt erzeugen kann. " +
             "Verhindert endlose Decal-Erzeugung bei lang liegenden Ragdolls.")]
    [SerializeField] private int maxDecalsPerRagdoll = 10;

    [Tooltip("Nach dieser Zeit (in Sekunden) werden keine neuen Decals mehr erzeugt. " +
             "Verhindert dass ruhig liegende Ragdolls endlos Blutflecken produzieren.")]
    [SerializeField] private float spawnDuration = 5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────

    private float[] boneCooldowns;
    private int totalDecalsSpawned;
    private float spawnTimer;
    private bool isInitialized;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialisiert das Blood-Decal-System.
    /// Sollte NACH SpawnedRagdoll.Activate() aufgerufen werden,
    /// damit die Bones bereits aktiv (nicht-kinematisch) sind.
    ///
    /// Kann auch automatisch von SpawnedRagdoll aufgerufen werden,
    /// oder manuell vom NpcRagdollSwapper.
    /// </summary>
    public void Initialize()
    {
        if (isInitialized) return;
        if (decalMaterial == null)
        {
            Debug.LogWarning($"[RagdollBloodDecals] {name}: Kein Decal-Material zugewiesen! " +
                             "Blutflecken werden nicht erzeugt.", this);
            return;
        }

        // Falls keine Bones manuell zugewiesen: automatisch suchen
        if (bloodBones == null || bloodBones.Length == 0)
        {
            FindDefaultBones();
        }

        if (bloodBones == null || bloodBones.Length == 0)
        {
            Debug.LogWarning($"[RagdollBloodDecals] {name}: Keine Bones gefunden! " +
                             "Blutflecken werden nicht erzeugt.", this);
            return;
        }

        // Cooldowns initialisieren
        boneCooldowns = new float[bloodBones.Length];

        // Reporter auf jeden Bone legen
        for (int i = 0; i < bloodBones.Length; i++)
        {
            if (bloodBones[i] == null) continue;

            var reporter = bloodBones[i].gameObject.AddComponent<BloodBoneCollisionReporter>();
            reporter.Initialize(this, i);

            if (showDebugInfo)
                Debug.Log($"[RagdollBloodDecals] Reporter auf Bone: {bloodBones[i].name}");
        }

        totalDecalsSpawned = 0;
        spawnTimer = 0f;
        isInitialized = true;

        if (showDebugInfo)
            Debug.Log($"[RagdollBloodDecals] {name}: Initialisiert mit {bloodBones.Length} Bones.");
    }

    /// <summary>
    /// Wird von BloodBoneCollisionReporter aufgerufen wenn ein Bone
    /// eine Oberfläche berührt (OnCollisionStay).
    /// </summary>
    public void OnBoneCollision(int boneIndex, Collision collision)
    {
        if (!isInitialized) return;
        if (totalDecalsSpawned >= maxDecalsPerRagdoll) return;
        if (spawnTimer >= spawnDuration) return;

        // Cooldown prüfen
        if (boneCooldowns[boneIndex] > 0f) return;

        // Layer-Check: Ist die Oberfläche im richtigen Layer?
        if (!IsInLayerMask(collision.gameObject.layer, surfaceLayers)) return;

        // Kontaktpunkt holen
        if (collision.contactCount == 0) return;
        ContactPoint contact = collision.GetContact(0);

        // Decal spawnen
        SpawnDecal(contact.point, contact.normal);

        // Cooldown setzen
        boneCooldowns[boneIndex] = spawnInterval;
        totalDecalsSpawned++;

        if (showDebugInfo)
            Debug.Log($"[RagdollBloodDecals] Decal #{totalDecalsSpawned} bei {contact.point} " +
                      $"(Bone: {bloodBones[boneIndex].name})");
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Auto-Initialize falls noch nicht passiert
        // (z.B. wenn das Ragdoll direkt in der Szene liegt zum Testen)
        if (!isInitialized)
        {
            Initialize();
        }
    }

    private void Update()
    {
        if (!isInitialized) return;

        // Spawn-Timer hochzählen
        spawnTimer += Time.deltaTime;

        // Cooldowns runterzählen
        for (int i = 0; i < boneCooldowns.Length; i++)
        {
            if (boneCooldowns[i] > 0f)
            {
                boneCooldowns[i] -= Time.deltaTime;
            }
        }

        // Komponente deaktivieren wenn Maximum oder Zeitlimit erreicht
        if (totalDecalsSpawned >= maxDecalsPerRagdoll || spawnTimer >= spawnDuration)
        {
            enabled = false;

            if (showDebugInfo)
            {
                string reason = totalDecalsSpawned >= maxDecalsPerRagdoll
                    ? $"Maximum ({maxDecalsPerRagdoll}) erreicht"
                    : $"Zeitlimit ({spawnDuration}s) erreicht";
                Debug.Log($"[RagdollBloodDecals] {name}: {reason}. " +
                          "Keine weiteren Decals.");
            }
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Decal Spawning
    // ────────────────────────────────────────────────────────────────────────

    private void SpawnDecal(Vector3 position, Vector3 surfaceNormal)
    {
        // Pool fragen — falls kein Pool vorhanden, direkt erzeugen
        GameObject decalObj = BloodDecalPool.Instance != null
            ? BloodDecalPool.Instance.GetDecal()
            : CreateDecalObject();

        // Position: leicht über der Oberfläche platzieren
        // Der Projector projiziert entlang seiner lokalen Z-Achse nach unten
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

        decalObj.SetActive(true);
    }

    /// <summary>
    /// Erzeugt ein neues Decal-GameObject (Fallback wenn kein Pool vorhanden).
    /// Im Normalfall nutzt der Pool eigene Objekte.
    /// </summary>
    private GameObject CreateDecalObject()
    {
        GameObject obj = new GameObject("BloodDecal");
        obj.AddComponent<DecalProjector>();
        return obj;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Bone Finding
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sucht automatisch nach Spine/Torso-Bones wenn keine manuell zugewiesen sind.
    /// </summary>
    private void FindDefaultBones()
    {
        string[] defaultBoneNames = { "spine", "torso", "chest" };
        var allTransforms = GetComponentsInChildren<Transform>();
        var found = new System.Collections.Generic.List<Transform>();

        foreach (var t in allTransforms)
        {
            string boneName = t.name.ToLower();
            foreach (string searchName in defaultBoneNames)
            {
                if (boneName.Contains(searchName))
                {
                    found.Add(t);
                    break;
                }
            }
        }

        bloodBones = found.ToArray();

        if (showDebugInfo)
            Debug.Log($"[RagdollBloodDecals] Auto-gefunden: {found.Count} Bones " +
                      $"({string.Join(", ", found.ConvertAll(t => t.name))})");
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Utility
    // ────────────────────────────────────────────────────────────────────────

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    #endregion
}
