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
//   2. Bone-Pose vom lebenden NPC kopieren (WORLD-SPACE)
//   3. Ragdoll-Prefab(s) spawnen an gleicher Position/Rotation
//   4. Kopierte Pose auf neue Ragdolls übertragen (World-Space, Parent-First)
//   5. SpawnedRagdoll.Activate() aufrufen → Physics starten
//   6. SpawnedRagdoll.ApplyImpact(direction) → Ragdoll nutzt eigene Kraftwerte
//   7. Equipment aus NPC-Hierarchie lösen → DroppedEquipment.Activate(direction)
//   8. Original-NPC zerstören
//
// VERANTWORTUNG:
//   Der Swapper ist NUR für den Swap-Vorgang zuständig:
//   - Pose kopieren + übertragen
//   - Prefabs instanziieren
//   - Impact-Richtung + Trefferpunkt weitergeben
//   - Equipment lösen
//
//   Alle Kraft-, Physik- und Freeze-Werte liegen auf den Prefabs selbst
//   (SpawnedRagdoll, DroppedEquipment). So kann jedes Prefab individuell
//   konfiguriert werden und eigenständig getestet werden.
//
// WORLD-SPACE POSE TRANSFER:
//   Kopiert position/rotation (World-Space) und setzt sie in
//   Parent-First-Reihenfolge auf die Ragdolls. Dadurch wird die exakte
//   Todes-Pose übertragen — inklusive aller IK-Modifikationen.
//
// SETUP:
//   1. Diese Komponente auf das NPC-Prefab legen (neben NpcBase)
//   2. NpcImpactTracker auf das Prefab legen (für Impact-Registrierung)
//   3. Fullbody-Ragdoll-Prefab zuweisen (muss SpawnedRagdoll-Komponente haben)
//   4. Mindestens ein SlicedRagdollPair zuweisen (Upper + Lower Prefab,
//      beide müssen SpawnedRagdoll-Komponente haben)
//   5. Optional: DroppableEquipment konfigurieren
//      (Equipment-Objekte müssen DroppedEquipment-Komponente haben)
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
             "Muss gleiche Bone-Namen wie der NPC haben. " +
             "MUSS eine SpawnedRagdoll-Komponente haben!")]
    [SerializeField] private GameObject fullBodyRagdollPrefab;

    [Header("Sliced Ragdolls")]
    [Tooltip("Verfügbare Schnitt-Paare. Jedes Paar besteht aus zwei Prefabs (obere + untere Hälfte). " +
             "Bei Melee-Tod wird zufällig ein Paar gewählt. " +
             "Beide Prefabs MÜSSEN eine SpawnedRagdoll-Komponente haben!")]
    [SerializeField] private SlicedRagdollPair[] slicedPairs;

    [Header("Equipment Drop")]
    [Tooltip("Equipment das beim Tod fallen gelassen wird (z.B. Waffen). " +
             "Equipment-Objekte MÜSSEN eine DroppedEquipment-Komponente haben!")]
    [SerializeField] private DroppableEquipment[] droppableEquipment;

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
        Vector3? impactPoint = null)
    {
        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] {gameObject.name}: Swap starten — Typ: {deathType}");

        // ── 1. Bone-Pose vom lebenden NPC kopieren (World-Space) ──
        Dictionary<string, BoneSnapshot> boneSnapshots = CaptureBonePose();

        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] {boneSnapshots.Count} Bones kopiert (World-Space).");

        // ── 2. Ragdoll(s) spawnen je nach Todesart ──
        if (deathType == NpcDeathType.Sliced && TryGetRandomSlicedPair(out SlicedRagdollPair pair))
        {
            SpawnSlicedRagdolls(pair, boneSnapshots, impactDirection, impactPoint);
        }
        else
        {
            // Fallback auf WholeBody wenn kein Sliced-Paar verfügbar
            SpawnFullBodyRagdoll(boneSnapshots, impactDirection, impactPoint);
        }

        // ── 3. Equipment lösen (vor Destroy!) ──
        ReleaseEquipment(impactDirection);

        // ── 4. Original zerstören ──
        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] {gameObject.name}: Original wird zerstört.");

        Destroy(gameObject);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Bone Pose Capture (World-Space)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Snapshot einer einzelnen Bone-Transformation in World-Space.
    /// </summary>
    private struct BoneSnapshot
    {
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public Vector3 localScale;
    }

    /// <summary>
    /// Kopiert die aktuelle Pose aller Bones im NPC-Skelett in World-Space.
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
                worldPosition = bone.position,
                worldRotation = bone.rotation,
                localScale = bone.localScale
            };
        }

        return snapshots;
    }

    /// <summary>
    /// Überträgt die gespeicherte World-Space-Pose auf ein Ragdoll-Prefab.
    /// Matcht Bones per Name.
    /// 
    /// WICHTIG: GetComponentsInChildren liefert Transforms in Hierarchie-Reihenfolge
    /// (Parent vor Children, Depth-First). Dadurch wird jeder Parent-Bone zuerst
    /// positioniert, bevor seine Children gesetzt werden.
    /// </summary>
    private void ApplyBonePose(GameObject ragdollInstance, Dictionary<string, BoneSnapshot> snapshots)
    {
        Transform[] ragdollBones = ragdollInstance.GetComponentsInChildren<Transform>();
        int matchedCount = 0;

        foreach (Transform bone in ragdollBones)
        {
            if (snapshots.TryGetValue(bone.name, out BoneSnapshot snapshot))
            {
                bone.position = snapshot.worldPosition;
                bone.rotation = snapshot.worldRotation;
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
        Vector3 impactDir, Vector3? impactPoint)
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

        // Pose übertragen BEVOR Activate() — sonst fällt der Ragdoll in Default-Pose
        ApplyBonePose(ragdoll, snapshots);

        SpawnedRagdoll spawnedRagdoll = ragdoll.GetComponent<SpawnedRagdoll>();

        if (spawnedRagdoll == null)
        {
            Debug.LogError($"[RagdollSwapper] {fullBodyRagdollPrefab.name} hat keine SpawnedRagdoll-Komponente! " +
                           "Bitte SpawnedRagdoll auf das Prefab legen.");
            return;
        }

        spawnedRagdoll.Activate();
        spawnedRagdoll.ApplyImpact(impactDir, impactPoint);

        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] Fullbody-Ragdoll gespawnt: {ragdoll.name}");
    }

    /// <summary>
    /// Spawnt ein Paar zerschnittener Ragdolls (obere + untere Hälfte).
    /// </summary>
    private void SpawnSlicedRagdolls(
        SlicedRagdollPair pair,
        Dictionary<string, BoneSnapshot> snapshots,
        Vector3 impactDir, Vector3? impactPoint)
    {
        if (pair.upperHalfPrefab != null)
        {
            SpawnSlicedHalf(pair.upperHalfPrefab, snapshots, impactDir, impactPoint, "Obere", pair.label);
        }

        if (pair.lowerHalfPrefab != null)
        {
            SpawnSlicedHalf(pair.lowerHalfPrefab, snapshots, impactDir, impactPoint, "Untere", pair.label);
        }
    }

    /// <summary>
    /// Spawnt eine einzelne Hälfte eines Sliced-Ragdolls.
    /// </summary>
    private void SpawnSlicedHalf(
        GameObject prefab,
        Dictionary<string, BoneSnapshot> snapshots,
        Vector3 impactDir, Vector3? impactPoint,
        string halfLabel, string pairLabel)
    {
        GameObject half = Instantiate(prefab, transform.position, transform.rotation);

        ApplyBonePose(half, snapshots);

        SpawnedRagdoll spawnedRagdoll = half.GetComponent<SpawnedRagdoll>();

        if (spawnedRagdoll == null)
        {
            Debug.LogError($"[RagdollSwapper] {prefab.name} hat keine SpawnedRagdoll-Komponente! " +
                           "Bitte SpawnedRagdoll auf das Prefab legen.");
            return;
        }

        spawnedRagdoll.Activate();
        spawnedRagdoll.ApplyImpact(impactDir, impactPoint);

        if (showDebugInfo)
            Debug.Log($"[RagdollSwapper] {halfLabel} Hälfte gespawnt: {half.name} (Paar: {pairLabel})");
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
    /// Löst Equipment aus der NPC-Hierarchie und aktiviert Physics.
    /// Das Equipment-Objekt überlebt die Zerstörung des NPC,
    /// weil es vorher unparented wird.
    /// </summary>
    private void ReleaseEquipment(Vector3 impactDirection)
    {
        if (droppableEquipment == null || droppableEquipment.Length == 0) return;

        foreach (var equip in droppableEquipment)
        {
            if (!equip.IsValid) continue;

            GameObject obj = equip.equipmentObject;

            // Aus NPC-Hierarchie lösen — überlebt dadurch Destroy(gameObject)
            obj.transform.SetParent(null);

            // Layer auf "Dead" setzen
            SetLayerRecursively(obj, LayerMask.NameToLayer("Dead"));

            // Collider aktivieren
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = true;
            }

            // DroppedEquipment aktivieren — übernimmt Impact + Drop-Physik + Freeze
            DroppedEquipment dropped = obj.GetComponent<DroppedEquipment>();
            if (dropped != null)
            {
                dropped.Activate(impactDirection);
            }
            else
            {
                Debug.LogWarning($"[RagdollSwapper] {obj.name} hat keine DroppedEquipment-Komponente! " +
                                 "Equipment wird nicht physikalisch simuliert.");
            }

            if (showDebugInfo)
                Debug.Log($"[RagdollSwapper] Equipment released: {equip.label}");
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
