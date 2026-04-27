using UnityEngine;

/// <summary>
/// Universelle Komponente, die zerbrechliches Glas auf dem eigenen Bewegungspfad
/// erkennt und zerbrechen lässt. Verhindert Tunneling bei schnellen Bewegungen.
///
/// Funktionsweise:
/// - Misst jeden Frame die Distanz zwischen alter und neuer Position
/// - Macht einen SphereCast über diese Strecke
/// - Trifft sie ein BreakableGlass-Objekt, wird Shatter() aufgerufen
///
/// Anwendung: Auf jedes Objekt setzen, das durch Glas brechen soll.
/// Spieler, Geschosse, Granaten, Gegner etc.
///
/// Hinweis: Beeinflusst die Bewegung NICHT. Das Objekt bewegt sich
/// völlig unabhängig (CharacterController, Rigidbody, transform direkt – egal).
/// </summary>
public class GlassBreaker : MonoBehaviour
{
    [Header("Sweep-Einstellungen")]
    [Tooltip("Layer, auf der zerbrechliches Glas liegt.")]
    [SerializeField] private LayerMask glassLayer;

    [Tooltip("Radius des Sweep-Tests. Größer = trifft auch seitlich vorbeiziehendes Glas. " +
             "Spieler ~0.5, Geschosse ~0.05.")]
    [SerializeField] private float sweepRadius = 0.5f;

    [Tooltip("Minimale Distanz pro Frame, ab der ein Sweep gemacht wird. " +
             "Verhindert unnötige Casts bei stillstehenden Objekten.")]
    [SerializeField] private float minSweepDistance = 0.01f;

    [Header("Velocity-Übergabe")]
    [Tooltip("Wird an BreakableGlass übergeben, falls dieses Objekt KEINEN " +
             "CharacterController/Rigidbody hat. Wird dann aus Position/Frame-Zeit berechnet.")]
    [SerializeField] private bool calculateVelocityFromMovement = true;

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private Vector3 lastPosition;
    private Vector3 currentVelocity;

    // Optionale Komponenten, falls vorhanden
    private CharacterController characterController;
    private Rigidbody rb;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Optionale Komponenten holen – falls vorhanden, nutzen wir deren Velocity
        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        // Bei Aktivierung Position zurücksetzen, damit kein Sweep über
        // eine alte Position aus dem letzten Leben gemacht wird
        lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        // LateUpdate, weil zu diesem Zeitpunkt alle Bewegungen des Frames
        // (Movement, Sprint, Dash) bereits ausgeführt sind.

        Vector3 currentPosition = transform.position;
        Vector3 movement = currentPosition - lastPosition;
        float distance = movement.magnitude;

        if (distance >= minSweepDistance)
        {
            // Velocity bestimmen – entweder aus Komponente oder berechnet
            currentVelocity = GetVelocity(movement);

            // Sweep über die zurückgelegte Strecke
            CheckForGlassOnPath(lastPosition, movement.normalized, distance);
        }

        lastPosition = currentPosition;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sweep Logic
    // ════════════════════════════════════════════════════════════════════════

    private void CheckForGlassOnPath(Vector3 origin, Vector3 direction, float distance)
    {
        // SphereCastAll, damit auch mehrere Glasscheiben hintereinander zerbrechen
        // (z.B. Spieler dasht durch zwei Fenster auf einmal)
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            sweepRadius,
            direction,
            distance,
            glassLayer,
            QueryTriggerInteraction.Collide  // wichtig: auch Trigger-Collider treffen!
        );

        foreach (RaycastHit hit in hits)
        {
            // BreakableGlass kann auf dem getroffenen Objekt oder einem Parent liegen
            BreakableGlass glass = hit.collider.GetComponentInParent<BreakableGlass>();
            if (glass != null)
            {
                glass.Shatter(currentVelocity);
            }
        }
    }

    private Vector3 GetVelocity(Vector3 frameMovement)
    {
        // Bevorzugt: Velocity von vorhandenen Physik-Komponenten
        if (characterController != null && characterController.enabled)
        {
            return characterController.velocity;
        }

        if (rb != null)
        {
            return rb.linearVelocity;
        }

        // Fallback: aus Frame-Bewegung berechnen
        if (calculateVelocityFromMovement && Time.deltaTime > 0f)
        {
            return frameMovement / Time.deltaTime;
        }

        return Vector3.zero;
    }

    #endregion
}
