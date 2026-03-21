using UnityEngine;
using System.Collections.Generic;

// ════════════════════════════════════════════════════════════════════════════
// NPC RAGDOLL SWAPPER - Ersetzt NPC beim Tod durch Ragdoll-Prefabs
// ════════════════════════════════════════════════════════════════════════════
//
// KONZEPT:
//   Beim Tod wird der lebende NPC durch vorgefertigte Ragdoll-Prefabs ersetzt.
//   Je nach Todesart wird entweder ein Fullbody-Ragdoll oder ein Paar
//   zerschnittener Ragdoll-Hälften gespawnt.
//
// ABLAUF (alles in einem Frame):
//   1. Todesart bestimmen (Sliced vs WholeBody)
//   2. Bone-Pose vom lebenden NPC kopieren
//   3. Ragdoll-Prefab(s) spawnen an gleicher Position/Rotation
//   4. Kopierte Pose auf neue Ragdolls übertragen (Bone-Name-Matching)
//   5. Impact-Kraft vom NpcImpactTracker übertragen
//   6. Equipment-Prefabs spawnen (Waffen fallen lassen)
//   7. Original-NPC zerstören
//
// SETUP:
//   1. Diese Komponente auf das NPC-Prefab legen (neben NpcBase)
//   2. NpcImpactTracker auf das Prefab legen (für Impact-Registrierung)
//   3. Fullbody-Ragdoll-Prefab zuweisen
//   4. Mindestens ein SlicedRagdollPair zuweisen (Upper + Lower Prefab)
//   5. Optional: DroppableEquipment konfigurieren
//   6. Die Ragdoll-Prefabs müssen das gleiche Bone-Naming wie der NPC haben
//
// ════════════════════════════════════════════════════════════════════════════

[RequireComponent(typeof(NpcBase))]
public class NpcRagdollSwapper : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Fullbody Ragdoll")]
    [Tooltip("Ragdoll-Prefab für nicht-Melee-Tode (Kugeln, Explosionen, etc.). " +
             "Muss gleiche Bone-Namen wie der NPC haben.")]
    [SerializeField] private GameObject fullBodyRagdollPrefab;

    [Header("Sliced Ragdolls")]
    [Tooltip("Verfügbare Schnitt-Paare. Jedes Paar besteht aus zwei Prefabs (obere + untere Hälfte). " +
             "Bei Melee-Tod wird zufällig ein Paar gewählt.")]
    [SerializeField] private SlicedRagdollPair[] slicedPairs;

    [Header("Equipment Drop")]
    [Tooltip("Equipment das beim Tod fallen gelassen wird (z.B. Waffen).")]
    [SerializeField] private DroppableEquipment[] droppableEquipment;

    [Header("Ragdoll Settings")]
    [Tooltip("Sekunden bis die gespawnten Ragdolls zerstört werden.")]
    [SerializeField] private float ragdollDestroyDelay = 10f;

    [Tooltip("Aufwärts-Anteil bei der Impact-Kraft (0 = kein Aufwärts, 1 = stark nach oben).")]
    [Range(0f, 1f)]
    [SerializeField] private float upwardForceBias = 0.3f;

    [Header("Sliced Impact")]
    [Tooltip("Impact-Multiplikator für beide Hälften bei Sliced-Tod. " +
             "Die Original-Impact-Kraft ist für einen ganzen Körper kalibriert — " +
             "halbe Körper sind leichter und fliegen sonst zu stark weg. " +
             "0.5 = halbe Kraft, 1.0 = volle Kraft.")]
    [Range(0f, 2f)]
    [SerializeField] private float slicedImpactMultiplier = 0.5f;

    [Header("Equipment Drop Settings")]
    [Tooltip("Zufälliger Impuls auf gedropte Ausrüstung (simuliert Fallenlassen).")]
    [SerializeField] private float equipmentDropForce = 2f;

    [Tooltip("Sekunden bis gedropptes Equipment zerstört wird.")]
    [SerializeField] private float equipmentDestroyDelay = 15f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime Data
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npc;
    private NpcImpactTracker impactTracker;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();
        impactTracker = GetComponent<NpcImpactTracker>();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prüft ob der Swapper korrekt konfiguriert ist und mindestens
    /// ein Ragdoll-Prefab zugewiesen hat.
    /// </summary>
    public bool IsConfigured
    {
        get
        {
            if (fullBodyRagdollPrefab != null) return true;
            if (slicedPairs != null && slicedPairs.Length > 0)
            {
                foreach (var pair in slicedPairs)
                {
                    if (pair.IsValid) return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Führt den Ragdoll-Swap durch. Wird von NpcBase.Die() aufgerufen.
    /// 
    /// WICHTIG: Nach diesem Aufruf wird das Original-GameObject zerstört.
    /// Der Aufrufer darf danach nichts mehr am Original machen.
    /// </summary>
    public void PerformSwap(
        NpcDeathType deathType,
        Vector3 impactDirection,
        float impactMagnitude,
        Vector3? impactPoint = null)
    {
        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] {gameObject.name}: Swap starten — Typ: {deathType}");

        // ── 1. Bone-Pose vom lebenden NPC kopieren ──
        Dictionary<string, BoneSnapshot> boneSnapshots = CaptureBonePose();

        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] {boneSnapshots.Count} Bones kopiert.");

        // ── 2. Ragdoll(s) spawnen je nach Todesart ──
        if (deathType == NpcDeathType.Sliced && TryGetRandomSlicedPair(out SlicedRagdollPair pair))
        {
            SpawnSlicedRagdolls(pair, boneSnapshots, impactDirection, impactMagnitude, impactPoint);
        }
        else
        {
            // Fallback auf WholeBody wenn kein Sliced-Paar verfügbar
            SpawnFullBodyRagdoll(boneSnapshots, impactDirection, impactMagnitude, impactPoint);
        }

        // ── 3. Equipment droppen ──
        SpawnDroppedEquipment();

        // ── 4. Original zerstören ──
        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] {gameObject.name}: Original wird zerstört.");

        Destroy(gameObject);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Bone Pose Capture
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Snapshot einer einzelnen Bone-Transformation.
    /// </summary>
    private struct BoneSnapshot
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    /// <summary>
    /// Kopiert die aktuelle Pose aller Bones im NPC-Skelett.
    /// Nutzt den Bone-Namen als Key — darum müssen die Namen
    /// zwischen NPC und Ragdoll-Prefabs identisch sein.
    /// </summary>
    private Dictionary<string, BoneSnapshot> CaptureBonePose()
    {
        var snapshots = new Dictionary<string, BoneSnapshot>();

        Transform[] allTransforms = GetComponentsInChildren<Transform>();

        foreach (Transform bone in allTransforms)
        {
            if (snapshots.ContainsKey(bone.name)) continue;

            snapshots[bone.name] = new BoneSnapshot
            {
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale
            };
        }

        return snapshots;
    }

    /// <summary>
    /// Überträgt die gespeicherte Pose auf ein Ragdoll-Prefab.
    /// Matcht Bones per Name.
    /// </summary>
    private void ApplyBonePose(GameObject ragdollInstance, Dictionary<string, BoneSnapshot> snapshots)
    {
        Transform[] ragdollBones = ragdollInstance.GetComponentsInChildren<Transform>();
        int matchedCount = 0;

        foreach (Transform bone in ragdollBones)
        {
            if (snapshots.TryGetValue(bone.name, out BoneSnapshot snapshot))
            {
                bone.localPosition = snapshot.localPosition;
                bone.localRotation = snapshot.localRotation;
                bone.localScale = snapshot.localScale;
                matchedCount++;
            }
        }

        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] Pose übertragen: {matchedCount}/{ragdollBones.Length} Bones gematcht.");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Ragdoll Spawning
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spawnt ein einzelnes Fullbody-Ragdoll.
    /// </summary>
    private void SpawnFullBodyRagdoll(
        Dictionary<string, BoneSnapshot> snapshots,
        Vector3 impactDir, float impactMag, Vector3? impactPoint)
    {
        if (fullBodyRagdollPrefab == null)
        {
            Debug.LogWarning($"[RagdollSwapper] {gameObject.name}: Kein Fullbody-Ragdoll-Prefab zugewiesen!");
            return;
        }

        GameObject ragdoll = Instantiate(
            fullBodyRagdollPrefab,
            transform.position,
            transform.rotation
        );

        ApplyBonePose(ragdoll, snapshots);

        SpawnedRagdoll spawnedRagdoll = ragdoll.AddComponent<SpawnedRagdoll>();
        spawnedRagdoll.Initialize(ragdollDestroyDelay, upwardForceBias);

        if (impactMag > 0f)
        {
            spawnedRagdoll.ApplyImpact(impactDir, impactMag, upwardForceBias, impactPoint);
        }

        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] Fullbody-Ragdoll gespawnt: {ragdoll.name}");
    }

    /// <summary>
    /// Spawnt ein Paar zerschnittener Ragdolls (obere + untere Hälfte).
    /// Beide Hälften bekommen die gleiche Impact-Stärke (skaliert mit slicedImpactMultiplier).
    /// </summary>
    private void SpawnSlicedRagdolls(
        SlicedRagdollPair pair,
        Dictionary<string, BoneSnapshot> snapshots,
        Vector3 impactDir, float impactMag, Vector3? impactPoint)
    {
        float slicedForce = impactMag * slicedImpactMultiplier;

        // ── Obere Hälfte ──
        if (pair.upperHalfPrefab != null)
        {
            GameObject upper = Instantiate(
                pair.upperHalfPrefab,
                transform.position,
                transform.rotation
            );

            ApplyBonePose(upper, snapshots);

            SpawnedRagdoll upperRagdoll = upper.AddComponent<SpawnedRagdoll>();
            upperRagdoll.Initialize(ragdollDestroyDelay, upwardForceBias);

            if (slicedForce > 0f)
            {
                upperRagdoll.ApplyImpact(impactDir, slicedForce, upwardForceBias, impactPoint);
            }

            if (showDebugInfo)
                Debug.Log($"[RagdollSwapper] Obere Hälfte gespawnt: {upper.name} " +
                          $"(Paar: {pair.label}, Impact: {slicedForce:F1})");
        }

        // ── Untere Hälfte ──
        if (pair.lowerHalfPrefab != null)
        {
            GameObject lower = Instantiate(
                pair.lowerHalfPrefab,
                transform.position,
                transform.rotation
            );

            ApplyBonePose(lower, snapshots);

            SpawnedRagdoll lowerRagdoll = lower.AddComponent<SpawnedRagdoll>();
            lowerRagdoll.Initialize(ragdollDestroyDelay, upwardForceBias);

            if (slicedForce > 0f)
            {
                lowerRagdoll.ApplyImpact(impactDir, slicedForce, upwardForceBias, impactPoint);
            }

            if (showDebugInfo)
                Debug.Log($"[RagdollSwapper] Untere Hälfte gespawnt: {lower.name} " +
                          $"(Paar: {pair.label}, Impact: {slicedForce:F1})");
        }
    }

    /// <summary>
    /// Wählt zufällig ein gültiges SlicedRagdollPair aus.
    /// </summary>
    private bool TryGetRandomSlicedPair(out SlicedRagdollPair pair)
    {
        pair = null;

        if (slicedPairs == null || slicedPairs.Length == 0)
            return false;

        var validPairs = new List<SlicedRagdollPair>();
        foreach (var p in slicedPairs)
        {
            if (p.IsValid)
                validPairs.Add(p);
        }

        if (validPairs.Count == 0)
            return false;

        pair = validPairs[Random.Range(0, validPairs.Count)];
        return true;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Equipment Drop
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spawnt gedropte Ausrüstung an den letzten Knochen-Positionen.
    /// </summary>
    private void SpawnDroppedEquipment()
    {
        if (droppableEquipment == null || droppableEquipment.Length == 0) return;

        foreach (var equip in droppableEquipment)
        {
            if (!equip.IsValid) continue;

            GameObject dropped = Instantiate(
                equip.droppedPrefab,
                equip.attachPoint.position,
                equip.attachPoint.rotation
            );

            SetLayerRecursively(dropped, LayerMask.NameToLayer("Dead"));

            Rigidbody rb = dropped.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 randomDir = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0.5f, 1.5f),
                    Random.Range(-1f, 1f)
                ).normalized;

                rb.AddForce(randomDir * equipmentDropForce, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * equipmentDropForce, ForceMode.Impulse);
            }

            if (equipmentDestroyDelay >= 0f)
                Destroy(dropped, equipmentDestroyDelay);

            if (showDebugInfo)
                Debug.Log($"[RagdollSwapper] Equipment gedroppt: {equip.label} an {equip.attachPoint.name}");
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Utility
    // ════════════════════════════════════════════════════════════════════════

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    #endregion
}
