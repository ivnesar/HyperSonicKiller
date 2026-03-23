using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DROPPABLE EQUIPMENT - Equipment das beim NPC-Tod fallen gelassen wird
// ════════════════════════════════════════════════════════════════════════════
//
// Referenziert ein Child-GameObject am lebenden NPC (z.B. die Waffe in der Hand).
// Beim Tod wird das Objekt aus der NPC-Hierarchie gelöst (unparented) und
// die Physics-Komponenten werden aktiviert, sodass es realistisch zu Boden fällt.
//
// SETUP AM EQUIPMENT-OBJEKT (z.B. AM16):
//   - Mesh/Renderer (sichtbar)
//   - Rigidbody mit Is Kinematic = TRUE (wird beim Drop auf false gesetzt)
//   - Collider mit Enabled = FALSE (wird beim Drop aktiviert)
//
// SETUP IM INSPECTOR (auf NpcRagdollSwapper):
//   - "Equipment Object" = das Child-GameObject in der NPC-Hierarchie zuweisen
//
// ════════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class DroppableEquipment
{
    [Tooltip("Beschreibung (z.B. 'Gewehr', 'Helm'). Nur zur Übersicht im Inspector.")]
    public string label = "Weapon";

    [Tooltip("Das Equipment-GameObject als Kind des NPC (z.B. Waffe am Hand-Bone). " +
             "Muss Rigidbody (isKinematic=true) und Collider (enabled=false) haben.")]
    public GameObject equipmentObject;

    /// <summary>
    /// Prüft ob die nötige Referenz zugewiesen ist.
    /// </summary>
    public bool IsValid => equipmentObject != null;
}
