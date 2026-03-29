using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SPAWNED RAGDOLL - Komponente für Ragdoll-Prefabs
// ════════════════════════════════════════════════════════════════════════════
//
// Wird direkt auf Ragdoll-Prefabs gelegt (nicht mehr per AddComponent).
// Alle Impact- und Freeze-Werte werden am Prefab selbst eingestellt.
//
// Übernimmt:
//   - Layer auf "Dead" setzen (rekursiv)
//   - Ragdoll-Rigidbodies aktivieren (isKinematic = false)
//   - Impact-Kraft anwenden (Richtung kommt vom NpcRagdollSwapper)
//   - Nach einer einstellbaren Zeit die Physics einfrieren
//     (Rigidbodies → kinematisch, Collider bleiben erhalten)
//
// ABLAUF IM SPIEL:
//   1. Ragdoll wird vom NpcRagdollSwapper instanziiert
//   2. Swapper überträgt die Bone-Pose
//   3. Swapper ruft Activate() auf → Ragdoll wird physikalisch aktiv
//   4. Swapper ruft ApplyImpact(direction, impactPoint) auf
//      → Ragdoll berechnet die Kraft selbst aus seinen eigenen Werten
//   5. Nach freezeDelay friert das Ragdoll sich selbst ein
//
// DIRECTION OFFSET:
//   Die Flugrichtung wird über einen Offset-Vektor relativ zur
//   Impact-Richtung gesteuert. Die Z-Achse des Offsets zeigt in
//   Impact-Richtung (forward), X = rechts, Y = oben.
//
//   Beispiele (wenn der Angriff von vorne kommt):
//     (0, 0, 1)     → direkt nach hinten weg (entlang Impact)
//     (0, 0.5, 1)   → nach hinten + leicht nach oben
//     (0.3, 0.2, 1) → nach hinten + leicht rechts + leicht oben
//     (0, 1, 0)     → nur nach oben (kein Rückwärts-Impuls)
//
// STANDALONE-TEST:
//   activateOnStart = true setzen → das Ragdoll aktiviert sich in Start()
//   selbst, ohne dass ein Swapper nötig ist. Praktisch zum Testen in
//   einer Testszene.
//
// PHYSICS FREEZE:
//   Nach dem Freeze-Delay werden alle Rigidbodies auf isKinematic = true
//   gesetzt. Dadurch nimmt Unity sie aus der aktiven Physics-Simulation.
//   Die Collider bleiben bestehen, damit andere Objekte nicht durch
//   die Ragdolls fallen. Das spart Performance und verhindert
//   späte Physics-Glitches (Ragdolls die plötzlich wegfliegen etc.).
//
// ════════════════════════════════════════════════════════════════════════════

public class SpawnedRagdoll : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────

    [Header("Impact")]
    [Tooltip("Stärke der Impact-Kraft beim Tod. " +
             "Fullbody-Ragdolls brauchen typischerweise mehr als halbe Ragdolls.")]
    [SerializeField] private float impactForce = 10f;

    [Tooltip("Richtungs-Offset relativ zur Impact-Richtung.\n" +
             "Z = forward (Impact-Richtung), X = rechts, Y = oben.\n" +
             "Beispiel: (0, 0.3, 1) = nach hinten + leicht nach oben.")]
    [SerializeField] private Vector3 directionOffset = new Vector3(0f, 0.3f, 1f);

    [Header("Physics Freeze")]
    [Tooltip("Sekunden bis die Physics eingefroren wird. " +
             "Nach dieser Zeit werden alle Rigidbodies kinematisch.")]
    [SerializeField] private float freezeDelay = 5f;

    [Header("Standalone Test")]
    [Tooltip("Wenn true, aktiviert sich das Ragdoll in Start() selbst " +
             "(ohne NpcRagdollSwapper). Nur zum Testen in einer Testszene gedacht.")]
    [SerializeField] private bool activateOnStart = false;

    [Tooltip("Impact-Richtung für den Standalone-Test. " +
             "Wird nur verwendet wenn activateOnStart = true.")]
    [SerializeField] private Vector3 testImpactDirection = Vector3.forward;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────

    private Rigidbody[] rigidbodies;
    private Rigidbody mainRigidbody;

    private float freezeTimer;
    private bool isActivated;
    private bool isFrozen;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (activateOnStart)
        {
            Activate();
            ApplyImpact(testImpactDirection);
        }
    }

    private void Update()
    {
        if (!isActivated || isFrozen) return;

        freezeTimer += Time.deltaTime;

        if (freezeTimer >= freezeDelay)
        {
            FreezePhysics();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aktiviert das Ragdoll: Layer setzen, Rigidbodies wecken, Freeze-Timer starten.
    /// 
    /// Im Spiel wird das vom NpcRagdollSwapper NACH dem Pose-Transfer aufgerufen.
    /// Im Standalone-Test passiert das automatisch in Start().
    /// 
    /// WICHTIG: Erst aufrufen wenn die Bone-Pose bereits übertragen wurde,
    /// sonst fallen die Bones in der Default-Pose runter.
    /// </summary>
    public void Activate()
    {
        if (isActivated) return;

        // Alle Rigidbodies cachen
        rigidbodies = GetComponentsInChildren<Rigidbody>();

        // Main Rigidbody finden (Hips/Pelvis)
        FindMainRigidbody();

        // Layer auf "Dead" setzen
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Dead"));

        // Ragdoll aktivieren — Rigidbodies auf nicht-kinematisch
        ActivateRagdoll();

        // Freeze-Timer starten
        freezeTimer = 0f;
        isFrozen = false;
        isActivated = true;
    }

    /// <summary>
    /// Wendet die Impact-Kraft auf das Ragdoll an.
    /// 
    /// Die finale Flugrichtung wird aus der Impact-Richtung + directionOffset
    /// berechnet. Der Offset ist relativ zur Impact-Richtung:
    /// Z = forward (Impact-Richtung), X = rechts, Y = oben.
    /// 
    /// Sollte NACH Activate() aufgerufen werden.
    /// </summary>
    /// <param name="direction">Richtung des Impacts (vom Angriff).</param>
    /// <param name="impactPoint">Optionaler Trefferpunkt für gerichtete Kraft.</param>
    public void ApplyImpact(Vector3 direction, Vector3? impactPoint = null)
    {
        if (rigidbodies == null || rigidbodies.Length == 0) return;
        if (impactForce <= 0f) return;

        Vector3 finalDirection = CalculateImpactDirection(direction);

        if (impactPoint.HasValue && mainRigidbody != null)
        {
            mainRigidbody.AddForceAtPosition(
                finalDirection * impactForce,
                impactPoint.Value,
                ForceMode.Impulse
            );
        }
        else if (mainRigidbody != null)
        {
            mainRigidbody.AddForce(finalDirection * impactForce, ForceMode.Impulse);
        }

        // Verteilte Kraft auf alle anderen Bones
        ApplyDistributedForce(finalDirection, impactForce * 0.3f, impactPoint);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Physics Freeze
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Friert alle Rigidbodies ein (isKinematic = true).
    /// Danach wird auch die Update-Schleife gestoppt,
    /// weil nichts mehr zu tun ist.
    /// </summary>
    private void FreezePhysics()
    {
        if (rigidbodies == null) return;

        foreach (var rb in rigidbodies)
        {
            if (rb == null) continue;
            rb.isKinematic = true;
        }

        isFrozen = true;

        // Update wird nicht mehr gebraucht — Komponente deaktivieren
        // spart den Update-Call-Overhead
        enabled = false;
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

    private void ActivateRagdoll()
    {
        foreach (var rb in rigidbodies)
        {
            if (rb == null) continue;

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        // Collider aktivieren (nicht mehr Trigger)
        var colliders = GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            if (col == null) continue;
            if (col is CharacterController) continue;
            col.isTrigger = false;
        }
    }

    private void FindMainRigidbody()
    {
        string[] commonHipNames = { "hips", "pelvis", "spine", "root" };

        foreach (var rb in rigidbodies)
        {
            string boneName = rb.gameObject.name.ToLower();
            foreach (string hipName in commonHipNames)
            {
                if (boneName.Contains(hipName))
                {
                    mainRigidbody = rb;
                    return;
                }
            }
        }

        // Fallback: erstes Rigidbody das nicht am Root hängt
        foreach (var rb in rigidbodies)
        {
            if (rb.gameObject != gameObject)
            {
                mainRigidbody = rb;
                return;
            }
        }

        if (rigidbodies.Length > 0)
            mainRigidbody = rigidbodies[0];
    }

    private void ApplyDistributedForce(Vector3 direction, float force, Vector3? center)
    {
        Vector3 centerPoint = center ?? (mainRigidbody != null ? mainRigidbody.position : transform.position);

        foreach (var rb in rigidbodies)
        {
            if (rb == null || rb == mainRigidbody) continue;

            float distance = Vector3.Distance(rb.position, centerPoint);
            float falloff = 1f / (1f + distance);

            rb.AddForce(direction * force * falloff, ForceMode.Impulse);
        }
    }

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
