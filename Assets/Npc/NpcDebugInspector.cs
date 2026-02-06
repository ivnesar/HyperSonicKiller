using UnityEngine;

/// <summary>
/// Debug-Komponente für NPC-Werte im Inspector.
/// Unterstützt GenOne (Forward-Raycast) und GenTwo (Intercept-Visualisierung).
/// </summary>
public class NpcDebugInspector : MonoBehaviour
{
    private NpcBase npc;
    private GenOneNpc genOne;
    private GenTwoNpc genTwo;

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
    #region Forward Raycast Debug (GenOne)
    // ════════════════════════════════════════════════════════════════════════

    [Header("Forward Raycast Debug (GenOne)")]
    [Tooltip("Aktiviert die Visualisierung des Forward-Raycasts")]
    [SerializeField] private bool showForwardRaycast = true;

    [Tooltip("Layer für Kollisionserkennung (z.B. 'Solid')")]
    [SerializeField] private LayerMask solidLayerMask;

    [Tooltip("Maximale Raycast-Distanz")]
    [SerializeField] private float raycastDistance = 999f;

    [Tooltip("Größe der Kollisionspunkt-Sphere")]
    [SerializeField] private float hitPointSphereSize = 0.5f;

    [Header("Debug Colors (GenOne)")]
    [SerializeField] private Color rayColor = Color.cyan;
    [SerializeField] private Color hitPointColor = Color.magenta;
    [SerializeField] private Color noHitRayColor = Color.red;

    // Cached hit info für Gizmos (GenOne)
    private bool hasHit;
    private Vector3 hitPoint;
    private Vector3 rayOrigin;
    private Vector3 rayDirection;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Intercept Debug (GenTwo)
    // ════════════════════════════════════════════════════════════════════════

    [Header("Intercept Debug (GenTwo)")]
    [Tooltip("Aktiviert die Visualisierung der Intercept-Berechnung")]
    [SerializeField] private bool showInterceptDebug = true;

    [Tooltip("Farbe des Intercept-Rays (GenTwo → Abfangpunkt)")]
    [SerializeField] private Color interceptRayColor = Color.yellow;

    [Tooltip("Farbe des Dash-Richtungs-Rays während des Dashes")]
    [SerializeField] private Color genTwoDashRayColor = Color.red;

    [Tooltip("Farbe des Abfangpunkt-Gizmos")]
    [SerializeField] private Color interceptPointColor = new Color(1f, 0.5f, 0f); // Orange

    [Tooltip("Größe der Abfangpunkt-Sphere")]
    [SerializeField] private float interceptPointSize = 0.6f;

    [Header("GenTwo State (Read Only)")]
    [SerializeField] private bool genTwoIsDashing;
    [SerializeField] private bool genTwoHasValidIntercept;
    [SerializeField] private bool genTwoPlayerInRange;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();
        genOne = GetComponent<GenOneNpc>();
        genTwo = GetComponent<GenTwoNpc>();

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

        // GenOne Forward Raycast
        if (showForwardRaycast && genOne != null)
        {
            UpdateForwardRaycast();
        }

        // GenTwo Intercept Debug
        if (showInterceptDebug && genTwo != null)
        {
            UpdateGenTwoDebug();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region GenOne - Forward Raycast
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateForwardRaycast()
    {
        rayOrigin = transform.position;

        if (genOne.IsDashing)
        {
            rayDirection = genOne.DashDirection;
        }
        else
        {
            rayDirection = transform.forward;
        }

        if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, raycastDistance, solidLayerMask))
        {
            hasHit = true;
            hitPoint = hit.point;
            Debug.DrawRay(rayOrigin, rayDirection * hit.distance, rayColor);
        }
        else
        {
            hasHit = false;
            hitPoint = rayOrigin + rayDirection * raycastDistance;
            Debug.DrawRay(rayOrigin, rayDirection * raycastDistance, noHitRayColor);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region GenTwo - Intercept Visualization
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateGenTwoDebug()
    {
        genTwoIsDashing = genTwo.IsDashing;
        genTwoHasValidIntercept = genTwo.HasValidIntercept;
        genTwoPlayerInRange = genTwo.IsPlayerInRange;

        Vector3 origin = transform.position + Vector3.up;

        if (genTwo.IsDashing)
        {
            // Während Dash: zeige Dash-Richtung als Ray
            Debug.DrawRay(origin, genTwo.DashDirection * 20f, genTwoDashRayColor);
        }

        if (genTwo.HasValidIntercept)
        {
            // Zeige Linie zum Abfangpunkt
            Vector3 interceptTarget = genTwo.LastInterceptPoint + Vector3.up;
            Debug.DrawLine(origin, interceptTarget, interceptRayColor);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // GenOne: Kollisionspunkt
        if (showForwardRaycast && genOne != null && hasHit)
        {
            Gizmos.color = hitPointColor;
            Gizmos.DrawSphere(hitPoint, hitPointSphereSize);

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(hitPoint, hitPointSphereSize * 1.2f);
        }

        // GenTwo: Abfangpunkt
        if (showInterceptDebug && genTwo != null && genTwo.HasValidIntercept)
        {
            Vector3 interceptPos = genTwo.LastInterceptPoint;

            // Solider Punkt am Abfangort
            Gizmos.color = interceptPointColor;
            Gizmos.DrawSphere(interceptPos, interceptPointSize);

            // WireSphere für bessere Sichtbarkeit
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(interceptPos, interceptPointSize * 1.3f);

            // Linie von GenTwo zum Abfangpunkt
            Gizmos.color = interceptRayColor;
            Gizmos.DrawLine(transform.position + Vector3.up, interceptPos + Vector3.up);

            // Vertikale Markierung am Abfangpunkt (Säule)
            Gizmos.color = new Color(interceptPointColor.r, interceptPointColor.g, interceptPointColor.b, 0.4f);
            Gizmos.DrawLine(interceptPos, interceptPos + Vector3.up * 3f);
        }
    }

    #endregion
}
