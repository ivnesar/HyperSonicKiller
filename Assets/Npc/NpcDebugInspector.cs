using UnityEngine;

/// <summary>
/// Debug-Komponente für NPC-Werte im Inspector.
/// Erweitert mit Forward-Raycast Visualisierung für GenOne.
/// </summary>
public class NpcDebugInspector : MonoBehaviour
{
    private NpcBase npc;
    private GenOneNpc genOne; // Spezifisch für GenOne-Features

    [Header("General")]
    [SerializeField] private string currentState;
    [SerializeField] private BehaviorMode behaviorMode;

    [Header("Health")]
    [SerializeField] private int currentHealth;
    [SerializeField] private bool isDead;
    [SerializeField] private bool isStunned;

    [Header("Target")]
    [SerializeField] private float distanceToTarget;
    [SerializeField] private bool canReachPlayer;

    // ════════════════════════════════════════════════════════════════════════
    #region Forward Raycast Debug
    // ════════════════════════════════════════════════════════════════════════

    [Header("Forward Raycast Debug")]
    [Tooltip("Aktiviert die Visualisierung des Forward-Raycasts")]
    [SerializeField] private bool showForwardRaycast = true;

    [Tooltip("Layer für Kollisionserkennung (z.B. 'Solid')")]
    [SerializeField] private LayerMask solidLayerMask;

    [Tooltip("Maximale Raycast-Distanz")]
    [SerializeField] private float raycastDistance = 999f;

    [Tooltip("Größe der Kollisionspunkt-Sphere")]
    [SerializeField] private float hitPointSphereSize = 0.5f;

    [Header("Debug Colors")]
    [SerializeField] private Color rayColor = Color.cyan;
    [SerializeField] private Color hitPointColor = Color.magenta;
    [SerializeField] private Color noHitRayColor = Color.red;

    // Cached hit info für Gizmos
    private bool hasHit;
    private Vector3 hitPoint;
    private Vector3 rayOrigin;
    private Vector3 rayDirection;

    #endregion

    private void Awake()
    {
        npc = GetComponent<NpcBase>();
        genOne = GetComponent<GenOneNpc>();

        if (npc == null) enabled = false;
    }

    private void Update()
    {
        if (npc == null) return;

        // Standard NPC Debug Info
        currentState = npc.GetCurrentStateName();
        behaviorMode = npc.CurrentBehaviorMode;
        currentHealth = npc.CurrentHealth;
        isDead = npc.IsDead;
        isStunned = npc.IsStunned;
        distanceToTarget = npc.DistanceToTarget;
        canReachPlayer = npc.CanReachPlayer;

        // Forward Raycast für GenOne
        if (showForwardRaycast && genOne != null)
        {
            UpdateForwardRaycast();
        }
    }

    /// <summary>
    /// Führt den Forward-Raycast aus und cached die Ergebnisse für Gizmos.
    /// </summary>
    private void UpdateForwardRaycast()
    {
        rayOrigin = transform.position;

        // Während Dash: dashDirection nutzen, sonst transform.forward
        if (genOne.IsDashing)
        {
            rayDirection = genOne.DashDirection;
        }
        else
        {
            rayDirection = transform.forward;
        }

        // Raycast ausführen
        if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, raycastDistance, solidLayerMask))
        {
            hasHit = true;
            hitPoint = hit.point;
        }
        else
        {
            hasHit = false;
            hitPoint = rayOrigin + rayDirection * raycastDistance;
        }

        // Debug.DrawRay für Scene View (auch ohne Gizmos-Button aktiv)
        if (hasHit)
        {
            Debug.DrawRay(rayOrigin, rayDirection * hit.distance, rayColor);
        }
        else
        {
            Debug.DrawRay(rayOrigin, rayDirection * raycastDistance, noHitRayColor);
        }
    }

    /// <summary>
    /// Zeichnet Gizmos im Scene View.
    /// </summary>
    private void OnDrawGizmos()
    {
        // Nur zeichnen wenn aktiviert und GenOne vorhanden
        if (!showForwardRaycast) return;
        if (!Application.isPlaying) return;
        if (genOne == null) return;

        // Kollisionspunkt als Sphere
        if (hasHit)
        {
            Gizmos.color = hitPointColor;
            Gizmos.DrawSphere(hitPoint, hitPointSphereSize);

            // Optional: Kleine WireSphere für bessere Sichtbarkeit
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(hitPoint, hitPointSphereSize * 1.2f);
        }
    }
}
