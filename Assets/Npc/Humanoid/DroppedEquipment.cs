using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// DROPPED EQUIPMENT - Physics + Freeze für gedroptes Equipment
// ════════════════════════════════════════════════════════════════════════════
//
// Wird direkt auf Equipment-Objekte gelegt (z.B. Waffen am NPC).
// Alle Drop- und Freeze-Werte werden am Objekt selbst eingestellt.
//
// Übernimmt beim Aktivieren:
//   - Impact-Kraft in Angriffsrichtung anwenden (mit directionOffset)
//   - Zufälligen Drop-Impuls + Torque (simuliert Fallenlassen)
//   - Nach freezeDelay alle Rigidbodies einfrieren (isKinematic = true)
//
// DIRECTION OFFSET:
//   Wie bei SpawnedRagdoll — der Offset ist relativ zur Impact-Richtung:
//   Z = forward (Impact-Richtung), X = rechts, Y = oben.
//
// ABLAUF:
//   1. NpcRagdollSwapper löst das Equipment aus der NPC-Hierarchie
//   2. Swapper ruft Activate(impactDirection) auf
//   3. Equipment fliegt weg und dreht sich
//   4. Nach freezeDelay wird die Physics eingefroren
//
// ════════════════════════════════════════════════════════════════════════════

public class DroppedEquipment : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────

    [Header("Impact")]
    [Tooltip("Stärke der Impact-Kraft in Angriffsrichtung. " +
             "Simuliert dass das Equipment vom Schlag mitgerissen wird.")]
    [SerializeField] private float impactForce = 3f;

    [Tooltip("Richtungs-Offset relativ zur Impact-Richtung.\n" +
             "Z = forward (Impact-Richtung), X = rechts, Y = oben.\n" +
             "Beispiel: (0, 0.5, 1) = in Impact-Richtung + leicht nach oben.")]
    [SerializeField] private Vector3 directionOffset = new Vector3(0f, 0.3f, 1f);

    [Header("Drop Physics")]
    [Tooltip("Zufälliger Impuls beim Fallenlassen (simuliert Loslassen/Wegschleudern).")]
    [SerializeField] private float dropForce = 2f;

    [Header("Physics Freeze")]
    [Tooltip("Sekunden bis die Physics eingefroren wird. " +
             "Nach dieser Zeit werden alle Rigidbodies kinematisch.")]
    [SerializeField] private float freezeDelay = 5f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────

    private float freezeTimer;
    private bool isActivated;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aktiviert die Drop-Physics: Impact + zufälliger Impuls + Freeze-Timer.
    /// Wird vom NpcRagdollSwapper aufgerufen, nachdem das Equipment
    /// aus der NPC-Hierarchie gelöst wurde.
    /// </summary>
    /// <param name="impactDirection">Richtung des Angriffs der den NPC getötet hat.</param>
    public void Activate(Vector3 impactDirection)
    {
        if (isActivated) return;

        // Laser ausschalten, falls die Waffe einen hat (z.B. AM16).
        foreach (LaserRenderer laser in GetComponentsInChildren<LaserRenderer>())
        {
            laser.IsVisible = false;
        }
        
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;

            // Impact-Kraft mit directionOffset
            if (impactForce > 0f)
            {
                Vector3 finalDirection = CalculateImpactDirection(impactDirection);
                rb.AddForce(finalDirection * impactForce, ForceMode.Impulse);
            }

            // Zufälliger Drop-Impuls (simuliert Fallenlassen)
            if (dropForce > 0f)
            {
                Vector3 randomDir = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(0.5f, 1.5f),
                    Random.Range(-1f, 1f)
                ).normalized;

                rb.AddForce(randomDir * dropForce, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * dropForce, ForceMode.Impulse);
            }
        }

        freezeTimer = 0f;
        isActivated = true;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!isActivated) return;

        freezeTimer += Time.deltaTime;

        if (freezeTimer >= freezeDelay)
        {
            Freeze();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Internal
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Berechnet die finale Impact-Richtung aus der Angriffsrichtung
    /// und dem directionOffset.
    /// 
    /// Der Offset wird in ein Koordinatensystem transformiert, in dem
    /// die Z-Achse der Impact-Richtung entspricht. So dreht sich der
    /// Offset automatisch mit der Angriffsrichtung mit.
    /// </summary>
    private Vector3 CalculateImpactDirection(Vector3 impactDirection)
    {
        Vector3 forward = impactDirection.normalized;

        // Fallback wenn Impact-Richtung zu kurz ist
        if (forward.sqrMagnitude < 0.001f)
            return directionOffset.normalized;

        // Koordinatensystem aufbauen: forward = Impact-Richtung
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

        // Offset in dieses Koordinatensystem transformieren
        Vector3 worldOffset = rotation * directionOffset;

        return worldOffset.normalized;
    }

    private void Freeze()
    {
        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rigidbodies)
        {
            if (rb == null) continue;
            rb.isKinematic = true;
        }

        isActivated = false;
        enabled = false;
    }

    #endregion
}
