using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SLICED RAGDOLL PAIR - Zusammengehörige Ragdoll-Hälften
// ════════════════════════════════════════════════════════════════════════════
//
// Ein Paar besteht aus zwei Prefabs die zusammen den kompletten NPC ergeben.
// Z.B. obere Hälfte + untere Hälfte bei einem diagonalen Schnitt.
//
// Mehrere Paare pro NPC möglich (z.B. diagonal, horizontal, vertikal).
// Später kann die Angriffsrichtung bestimmen welches Paar verwendet wird.
// Für die erste Iteration wird zufällig ein Paar gewählt.
//
// ════════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class SlicedRagdollPair
{
    [Tooltip("Beschreibung des Schnitt-Typs (z.B. 'Diagonal', 'Horizontal'). Nur zur Übersicht im Inspector.")]
    public string label = "Diagonal";

    [Tooltip("Obere Hälfte des zerschnittenen NPC (Ragdoll-Prefab mit Rigidbodies).")]
    public GameObject upperHalfPrefab;

    [Tooltip("Untere Hälfte des zerschnittenen NPC (Ragdoll-Prefab mit Rigidbodies).")]
    public GameObject lowerHalfPrefab;

    /// <summary>
    /// Prüft ob beide Prefabs zugewiesen sind.
    /// </summary>
    public bool IsValid => upperHalfPrefab != null && lowerHalfPrefab != null;
}
