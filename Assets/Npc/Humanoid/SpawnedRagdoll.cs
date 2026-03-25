using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// SPAWNED RAGDOLL - Komponente für gespawnte Ragdoll-Instanzen
// ════════════════════════════════════════════════════════════════════════════
//
// Wird vom NpcRagdollSwapper auf jedes gespawnte Ragdoll-Prefab gelegt.
// Übernimmt:
//   - Layer auf "Dead" setzen (rekursiv)
//   - Ragdoll-Rigidbodies aktivieren (isKinematic = false)
//   - Impact-Kraft anwenden
//   - Nach einer einstellbaren Zeit die Physics einfrieren
//     (Rigidbodies → kinematisch, Collider bleiben erhalten)
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
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────

    private Rigidbody[] rigidbodies;
    private Rigidbody mainRigidbody;

    private float freezeDelay;
    private float freezeTimer;
    private bool isFrozen;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Initialization
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialisiert das gespawnte Ragdoll.
    /// Wird direkt nach dem Instantiate vom NpcRagdollSwapper aufgerufen.
    /// </summary>
    /// <param name="freezeDelay">Sekunden bis die Physics eingefroren wird.</param>
    /// <param name="upwardForceBias">Aufwärts-Anteil der Impact-Kraft (0-1).</param>
    public void Initialize(float freezeDelay, float upwardForceBias = 0.3f)
    {
        // Alle Rigidbodies cachen
        rigidbodies = GetComponentsInChildren<Rigidbody>();

        // Main Rigidbody finden (Hips/Pelvis)
        FindMainRigidbody();

        // Layer auf "Dead" setzen
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Dead"));

        // Ragdoll aktivieren — Rigidbodies auf nicht-kinematisch
        ActivateRagdoll();

        // Freeze-Timer starten
        this.freezeDelay = freezeDelay;
        freezeTimer = 0f;
        isFrozen = false;
    }

    /// <summary>
    /// Wendet eine Impact-Kraft auf das Ragdoll an.
    /// Sollte NACH Initialize() aufgerufen werden.
    /// </summary>
    public void ApplyImpact(Vector3 forceDirection, float forceMagnitude, float upwardBias, Vector3? impactPoint = null)
    {
        if (rigidbodies == null || rigidbodies.Length == 0) return;

        Vector3 adjustedDirection = (forceDirection + Vector3.up * upwardBias).normalized;
        float finalForce = forceMagnitude;

        if (impactPoint.HasValue && mainRigidbody != null)
        {
            mainRigidbody.AddForceAtPosition(
                adjustedDirection * finalForce,
                impactPoint.Value,
                ForceMode.Impulse
            );
        }
        else if (mainRigidbody != null)
        {
            mainRigidbody.AddForce(adjustedDirection * finalForce, ForceMode.Impulse);
        }

        // Verteilte Kraft auf alle anderen Bones
        ApplyDistributedForce(adjustedDirection, finalForce * 0.3f, impactPoint);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (isFrozen) return;

        freezeTimer += Time.deltaTime;

        if (freezeTimer >= freezeDelay)
        {
            FreezePhysics();
        }
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
