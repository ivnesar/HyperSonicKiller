using UnityEngine;

/// <summary>
/// Laserpointer: Zeichnet eine Linie vom Transform in eine einstellbare lokale Richtung.
/// Stoppt bei Collidern, die der LayerMask entsprechen, sonst bei maxDistance.
///
/// Public API (von außen steuerbar):
///   - Color           → Farbe des Lasers
///   - LineWidth       → Breite des Lasers
///   - IsVisible       → sichtbar an/aus
///
/// Alle anderen Einstellungen (Richtung, Reichweite, HitMask) werden im
/// Inspector konfiguriert und nicht zur Laufzeit von außen verändert.
/// </summary>
public class LaserRenderer : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields (configuration — not part of public runtime API)
    // ════════════════════════════════════════════════════════════════════════

    [Header("Richtung & Reichweite")]
    [Tooltip("Lokale Richtung des Lasers (z.B. (0,0,1) für forward).")]
    [SerializeField] private Vector3 direction = new Vector3(0f, 0f, 1f);

    [Tooltip("Maximale Reichweite, falls kein Collider getroffen wird.")]
    [SerializeField] private float maxDistance = 100f;

    [Tooltip("Welche Layer blockieren den Laser?")]
    [SerializeField] private LayerMask hitMask = ~0; // ~0 = alle Layer

    [Header("Initiale Darstellung")]
    [Tooltip("Startfarbe des Lasers. Wird zur Laufzeit über die Color-Property überschrieben.")]
    [SerializeField] private Color initialColor = Color.red;

    [Tooltip("Startbreite des Lasers. Wird zur Laufzeit über die LineWidth-Property überschrieben.")]
    [SerializeField] private float initialWidth = 0.01f;

    [Tooltip("Ist der Laser beim Start sichtbar?")]
    [SerializeField] private bool initiallyVisible = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private LineRenderer lineRenderer;

    // Backing-Fields für die Public-Properties
    private Color currentColor;
    private float currentWidth;
    private bool isVisible;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Runtime API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Farbe des Lasers. Wird im nächsten Update() auf den LineRenderer übertragen.
    /// </summary>
    public Color Color
    {
        get => currentColor;
        set => currentColor = value;
    }

    /// <summary>
    /// Breite des Lasers. Wird im nächsten Update() auf den LineRenderer übertragen.
    /// </summary>
    public float LineWidth
    {
        get => currentWidth;
        set => currentWidth = value;
    }

    /// <summary>
    /// Sichtbarkeit des Lasers. Schaltet den internen LineRenderer direkt an/aus.
    /// </summary>
    public bool IsVisible
    {
        get => isVisible;
        set
        {
            isVisible = value;
            if (lineRenderer != null)
                lineRenderer.enabled = value;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    void Start()
    {
        GameObject go = new GameObject("LaserLine");
        go.transform.SetParent(transform, false);

        lineRenderer = go.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        // Initialwerte aus dem Inspector in den Runtime-State übernehmen
        currentColor = initialColor;
        currentWidth = initialWidth;
        IsVisible = initiallyVisible;
    }

    void Update()
    {
        // Richtung in Weltkoordinaten umrechnen (lokal -> global, basierend auf Rotation)
        Vector3 worldDirection = transform.TransformDirection(direction.normalized);

        Vector3 start = transform.position;
        Vector3 end;

        // Raycast: trifft der Laser einen Collider auf der Maske?
        if (Physics.Raycast(start, worldDirection, out RaycastHit hit, maxDistance, hitMask))
        {
            end = hit.point;
        }
        else
        {
            end = start + worldDirection * maxDistance;
        }

        // LineRenderer updaten
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
        lineRenderer.startWidth = currentWidth;
        lineRenderer.endWidth = currentWidth;
        lineRenderer.startColor = currentColor;
        lineRenderer.endColor = currentColor;
    }

    #endregion
}
