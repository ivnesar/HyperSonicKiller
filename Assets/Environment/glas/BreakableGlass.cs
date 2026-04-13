using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// BREAKABLE GLASS - Zerstörbare Glasscheibe mit Splitter-Physik
// ════════════════════════════════════════════════════════════════════════════
//
// Funktionsweise:
//   Beim Treffer wird das ganze Scheiben-Mesh deaktiviert und die
//   vorgefertigten Splitter-Objekte aktiviert. Jeder Splitter bekommt
//   per AddExplosionForce einen Impuls, der mit der Nähe zum Einschlagspunkt
//   skaliert. Nach einer einstellbaren Zeit werden die Rigidbodies
//   auf kinematic gesetzt und die Collider deaktiviert.
//
// Auslöser:
//   - SoldierBullet / SniperBullet  → über IDamageable
//   - ThrownSword                   → über IDamageable
//   - Spieler-Dash                  → über OnTriggerEnter (Player-Tag + Dash-State)
//   - GenTwo-NPC-Dash               → über OnTriggerEnter (NPC mit GenTwoNpc)
//
// Prefab-Struktur (erwartet):
//   GlassPane              ← dieses Script + Collider (Trigger)
//     ├── WholeMesh        ← das intakte Scheiben-Mesh
//     └── Splinters        ← deaktiviertes Parent-Objekt
//          ├── Splinter_01 ← Mesh + Rigidbody (kinematic) + Collider
//          ├── Splinter_02
//          └── ...
//
// WICHTIG:
//   - Die Splitter-Rigidbodies müssen im Editor auf "Is Kinematic = true" stehen.
//   - Der Haupt-Collider auf dem GlassPane sollte ein Trigger sein, damit
//     Dash-Erkennung funktioniert. Alternativ einen zweiten Trigger-Collider
//     hinzufügen.
//   - Bullets treffen über ihren eigenen Raycast/SphereCast und rufen
//     TakeDamage() direkt auf. Dafür braucht die Glasscheibe AUCH einen
//     normalen (nicht-Trigger) Collider auf dem richtigen Layer.
//
// ════════════════════════════════════════════════════════════════════════════

public class BreakableGlass : MonoBehaviour, IDamageable
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Referenzen")]
    [Tooltip("Das intakte Scheiben-Mesh (wird beim Bruch deaktiviert)")]
    [SerializeField] private GameObject wholeMesh;

    [Tooltip("Parent-Objekt das alle Splitter enthält (wird beim Bruch aktiviert)")]
    [SerializeField] private GameObject splintersParent;

    [Header("Explosionskraft")]
    [Tooltip("Maximale Kraft auf Splitter direkt am Einschlagspunkt")]
    [SerializeField] private float explosionForce = 300f;

    [Tooltip("Radius der Explosion. Splitter außerhalb bekommen keine Kraft.")]
    [SerializeField] private float explosionRadius = 3f;

    [Tooltip("Vertikaler Offset der Explosion (hebt Splitter leicht an)")]
    [SerializeField] private float upwardsModifier = 0.5f;

    [Header("Physik-Lifecycle")]
    [Tooltip("Sekunden bis Rigidbodies auf kinematic gesetzt werden")]
    [SerializeField] private float physicsActiveTime = 2.5f;

    [Tooltip("Sekunden bis die Splitter komplett entfernt werden (0 = nie)")]
    [SerializeField] private float destroySplinterAfter = 0f;

    [Header("Splitter-Kollision")]
    [Tooltip("Layer auf den Splitter nach dem Bruch gesetzt werden (z.B. 'Debris').\n" +
             "Auf 'Nothing' lassen, um den Layer nicht zu ändern.")]
    [SerializeField] private LayerMask debrisLayer;

    [Header("Audio (Optional)")]
    [SerializeField] private AudioClip breakSound;
    [SerializeField] private float breakSoundVolume = 1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private bool isBroken;

    // Gecachte Splitter-Daten
    private Rigidbody[] splinterBodies;
    private Collider[] splinterColliders;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Splitter cachen, solange sie noch existieren
        if (splintersParent != null)
        {
            splinterBodies = splintersParent.GetComponentsInChildren<Rigidbody>(true);
            splinterColliders = splintersParent.GetComponentsInChildren<Collider>(true);
        }

        // Sicherstellen, dass der Ausgangszustand korrekt ist
        if (wholeMesh != null) wholeMesh.SetActive(true);
        if (splintersParent != null) splintersParent.SetActive(false);
    }

    /// <summary>
    /// Erkennt Spieler-Dash und GenTwo-NPC-Dash.
    /// Voraussetzung: Der Haupt-Collider ist ein Trigger.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (isBroken) return;

        // ── Spieler-Dash ──
        if (other.CompareTag("Player"))
        {
            PlayerCore playerCore = other.GetComponent<PlayerCore>();
            if (playerCore == null) playerCore = other.GetComponentInParent<PlayerCore>();

            if (playerCore != null && playerCore.CurrentState == PlayerCore.PlayerState.Dashing)
            {
                // Einschlagspunkt: nächster Punkt auf dem Glas-Collider zum Spieler
                Vector3 impactPoint = GetComponent<Collider>().ClosestPoint(other.transform.position);
                Vector3 impactDirection = other.transform.forward;

                Break(impactPoint, impactDirection);
                return;
            }
        }

        // ── GenTwo-NPC-Dash ──
        GenTwoNpc genTwo = other.GetComponent<GenTwoNpc>();
        if (genTwo == null) genTwo = other.GetComponentInParent<GenTwoNpc>();

        if (genTwo != null && genTwo.IsDashing)
        {
            // GenTwo dasht → Glas bricht
            Vector3 impactPoint = GetComponent<Collider>().ClosestPoint(other.transform.position);
            Vector3 impactDirection = genTwo.DashDirection;

            Break(impactPoint, impactDirection);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region IDamageable Implementation
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Einfacher Schaden ohne Positionsinfo.
    /// Nutzt die Mitte der Scheibe als Einschlagspunkt.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (isBroken) return;

        Break(transform.position, Vector3.forward);
    }

    /// <summary>
    /// Schaden mit Einschlagspunkt und Richtung.
    /// Wird von SoldierBullet, SniperBullet und ThrownSword aufgerufen.
    /// </summary>
    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (isBroken) return;

        Break(hitPoint, hitDirection);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Break Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Zerstört die Glasscheibe. Kann auch von externen Scripts aufgerufen werden.
    /// </summary>
    /// <param name="impactPoint">Punkt an dem der Einschlag stattfand</param>
    /// <param name="impactDirection">Richtung des Einschlags (für zusätzlichen Impuls)</param>
    public void Break(Vector3 impactPoint, Vector3 impactDirection)
    {
        if (isBroken) return;
        isBroken = true;

        // Visueller Swap: ganzes Mesh aus, Splitter an
        if (wholeMesh != null) wholeMesh.SetActive(false);
        if (splintersParent != null) splintersParent.SetActive(true);

        // Sound abspielen
        if (breakSound != null)
        {
            AudioSource.PlayClipAtPoint(breakSound, impactPoint, breakSoundVolume);
        }

        // Splitter aktivieren und Kraft anwenden
        ActivateSplinters(impactPoint, impactDirection);

        // Nach einer Weile Physik deaktivieren
        Invoke(nameof(FreezeAllSplinters), physicsActiveTime);

        // Optional: Splitter nach Zeit komplett entfernen
        if (destroySplinterAfter > 0f)
        {
            Invoke(nameof(DestroyAllSplinters), destroySplinterAfter);
        }
    }

    /// <summary>
    /// Aktiviert alle Splitter-Rigidbodies und wendet AddExplosionForce an.
    /// </summary>
    private void ActivateSplinters(Vector3 impactPoint, Vector3 impactDirection)
    {
        if (splinterBodies == null) return;

        // Optionalen Debris-Layer bestimmen
        int layerIndex = GetLayerFromMask(debrisLayer);

        for (int i = 0; i < splinterBodies.Length; i++)
        {
            Rigidbody rb = splinterBodies[i];
            if (rb == null) continue;

            // Kinematic ausschalten damit Physik wirkt
            rb.isKinematic = false;

            // Optional Layer ändern
            if (layerIndex >= 0)
            {
                rb.gameObject.layer = layerIndex;
            }

            // Explosionskraft anwenden
            // AddExplosionForce skaliert automatisch mit Distanz:
            // Je näher am impactPoint, desto stärker die Kraft
            rb.AddExplosionForce(
                explosionForce,
                impactPoint,
                explosionRadius,
                upwardsModifier,
                ForceMode.Impulse
            );

            // Kleinen Richtungsimpuls in Einschlagrichtung geben
            // (damit Splitter nicht nur radial wegfliegen, sondern
            //  auch in Schussrichtung gedrückt werden)
            float distanceToImpact = Vector3.Distance(rb.position, impactPoint);
            float distanceFactor = 1f - Mathf.Clamp01(distanceToImpact / explosionRadius);
            rb.AddForce(impactDirection.normalized * explosionForce * 0.3f * distanceFactor, ForceMode.Impulse);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Setzt alle Splinter-Rigidbodies auf kinematic und deaktiviert ihre Collider.
    /// Wird per Invoke nach physicsActiveTime aufgerufen.
    /// </summary>
    private void FreezeAllSplinters()
    {
        if (splinterBodies != null)
        {
            for (int i = 0; i < splinterBodies.Length; i++)
            {
                if (splinterBodies[i] != null)
                {
                    splinterBodies[i].isKinematic = true;
                }
            }
        }

        if (splinterColliders != null)
        {
            for (int i = 0; i < splinterColliders.Length; i++)
            {
                if (splinterColliders[i] != null)
                {
                    splinterColliders[i].enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// Entfernt alle Splitter komplett aus der Scene.
    /// Wird per Invoke nach destroySplinterAfter aufgerufen (wenn > 0).
    /// </summary>
    private void DestroyAllSplinters()
    {
        if (splintersParent != null)
        {
            Destroy(splintersParent);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Utility
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extrahiert den ersten aktiven Layer-Index aus einer LayerMask.
    /// Gibt -1 zurück wenn die Maske leer ist ("Nothing").
    /// </summary>
    private int GetLayerFromMask(LayerMask mask)
    {
        int value = mask.value;
        if (value == 0) return -1;

        for (int i = 0; i < 32; i++)
        {
            if ((value & (1 << i)) != 0)
                return i;
        }
        return -1;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        // Explosionsradius visualisieren
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }

    #endregion
}
