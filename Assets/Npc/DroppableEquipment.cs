using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DROPPABLE EQUIPMENT - Equipment das beim NPC-Tod fallen gelassen wird
// ════════════════════════════════════════════════════════════════════════════
//
// Verknüpft ein Bone-Transform am lebenden NPC (wo die Waffe sitzt)
// mit einem "losen" Prefab, das beim Tod instanziiert wird.
//
// Das Prefab sollte haben:
//   - Mesh/Renderer (sichtbar)
//   - Rigidbody (damit es fällt)
//   - Collider (damit es auf dem Boden liegen bleibt)
//
// ════════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class DroppableEquipment
{
    [Tooltip("Beschreibung (z.B. 'Gewehr', 'Helm'). Nur zur Übersicht im Inspector.")]
    public string label = "Weapon";

    [Tooltip("Transform am lebenden NPC, wo das Equipment sitzt (z.B. Hand-Bone). " +
             "Position und Rotation werden beim Tod kopiert.")]
    public Transform attachPoint;

    [Tooltip("Prefab das beim Tod instanziiert wird. Sollte Mesh + Rigidbody + Collider haben.")]
    public GameObject droppedPrefab;

    /// <summary>
    /// Prüft ob die nötigen Referenzen zugewiesen sind.
    /// </summary>
    public bool IsValid => attachPoint != null && droppedPrefab != null;
}
