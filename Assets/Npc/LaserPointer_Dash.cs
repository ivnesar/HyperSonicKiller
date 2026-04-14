using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// LASER POINTER DASH - Visueller Warnstrahl für Dash-basierte NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// Spezialisierter Laser für NPCs die per Dash angreifen (GenTwo etc.).
// Im Gegensatz zum Standard-NpcLaserPointer:
//   - Kein FOV-Check, kein Wiggle, kein Smoothing.
//   - Laser zeigt sofort auf den berechneten Intercept-Punkt.
//   - Farbverlauf und Breite ändern sich über AimProgress (0→1).
//   - Optionale Impact Sphere zur visuellen Darstellung des Einschlagpunkts.
//
// ANIMATION CURVES:
//   - colorWidthCurve: Steuert den Verlauf von Farbe UND Breite über AimProgress.
//     X-Achse = AimProgress (0→1), Y-Achse = Interpolationswert (0→1).
//     Default: Quadratische Kurve (wie vorher progress * progress).
//
// ABLAUF:
//   1. State ruft SetInterceptMode(point) auf → Laser + Sphere snappen sofort auf den Punkt.
//   2. State ruft jeden Frame UpdateInterceptPoint(point) auf → Punkt wird aktualisiert.
//   3. Während Charging (AimProgress < 1): Laser zeigt von laserOrigin zum interceptPoint.
//   4. Während Dashing (AimProgress = 1): Laser zeigt von NPC-Position zum interceptPoint.
//   5. State ruft ClearInterceptMode() auf → Laser + Sphere werden deaktiviert.
//
// STEUERUNG:
//   - Aktivierung/Deaktivierung läuft über NpcBase.IsLaserActive (wie beim Standard-Laser).
//   - AimProgress wird von der Subklasse über SetAimProgress() gesetzt.
//
// SETUP:
//   1. Diese Komponente auf das NPC-Root-GameObject legen (neben GenTwoNpc etc.).
//   2. laserOrigin im Inspector zuweisen (z.B. Hand-Bone oder Muzzle).
//   3. Optional: impactSphere im Inspector zuweisen (Kind-GameObject im Prefab).
//   4. collisionMask konfigurieren (Solid, Wände, Boden).
//   5. Laser-Material zuweisen.
//
// ════════════════════════════════════════════════════════════════════════════

[RequireComponent(typeof(NpcBase))]
public class LaserPointer_Dash : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Transforms")]
    [Tooltip("Startpunkt des Lasers (z.B. Hand, Muzzle). Wenn leer wird der Laser nicht angezeigt.")]
    [SerializeField] private Transform laserOrigin;

    [Header("Laser Settings")]
    [Tooltip("Maximale Länge des Laserstrahls")]
    [SerializeField] private float laserLength = 50f;

    [Tooltip("Layer-Maske für den Raycast (z.B. Solid, Wände, Boden).")]
    [SerializeField] private LayerMask collisionMask;

    [Header("Laser Width (zeitbasiert)")]
    [Tooltip("Breite des Lasers am Anfang der Aim-Phase (AimProgress = 0).")]
    [SerializeField] private float earlyWidth = 0.01f;

    [Tooltip("Breite des Lasers wenn eingelockt (AimProgress = 1).")]
    [SerializeField] private float lockedWidth = 0.06f;

    [Header("Laser Color (zeitbasiert)")]
    [Tooltip("Farbe des Lasers am Anfang der Aim-Phase (AimProgress = 0).")]
    [SerializeField] private Color earlyColor = new Color(1f, 1f, 0f, 0.5f);

    [Tooltip("Farbe des Lasers wenn eingelockt (AimProgress = 1).")]
    [SerializeField] private Color lockedColor = new Color(1f, 0f, 0f, 1f);

    [Header("Animation Curves")]
    [Tooltip("Steuert den Verlauf von Farbe und Breite über AimProgress (0→1). " +
             "X = AimProgress, Y = Interpolationswert. " +
             "Default: Quadratische Kurve (langsamer Start, schnelleres Ende).")]
    [SerializeField] private AnimationCurve colorWidthCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),      // Start: flach
        new Keyframe(1f, 1f, 2f, 0f)        // Ende: steil (≈ progress²)
    );

    [Header("Impact Sphere")]
    [Tooltip("Optionale Sphere zur visuellen Darstellung des Einschlagpunkts. " +
             "Wird im Intercept-Modus aktiviert und positioniert. " +
             "Wenn nicht zugewiesen, wird sie ignoriert.")]
    [SerializeField] private GameObject impactSphere;

    [Header("Visuals")]
    [Tooltip("Material für den Laser. Wird zur Laufzeit instanziert — das Original bleibt unverändert.")]
    [SerializeField] private Material laserMaterial;

    [Header("Debug")]
    [Tooltip("Aktiviert Debug-Logs.")]
    [SerializeField] private bool showDebug = false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npc;
    private LineRenderer lineRenderer;

    private Vector3 currentDirection;

    private bool isInterceptMode;
    private Vector3 interceptPoint;

    public bool IsTracking { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();
        SetupLineRenderer();
    }

    private void LateUpdate()
    {
        if (npc == null || npc.IsDead || laserOrigin == null)
        {
            lineRenderer.enabled = false;
            IsTracking = false;
            return;
        }

        if (npc.IsLaserActive && isInterceptMode)
        {
            UpdateLaser();
            lineRenderer.enabled = true;
        }
        else
        {
            lineRenderer.enabled = false;
            IsTracking = false;
            currentDirection = Vector3.zero;
            SetImpactSphereActive(false);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Setup
    // ════════════════════════════════════════════════════════════════════════

    private void SetupLineRenderer()
    {
        lineRenderer = gameObject.AddComponent<LineRenderer>();

        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = earlyWidth;
        lineRenderer.endWidth = earlyWidth;
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled = false;

        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

        if (laserMaterial != null)
            lineRenderer.material = new Material(laserMaterial);
        else
            lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

        ApplyColor(earlyColor);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Laser Update
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateLaser()
    {
        Vector3 origin = laserOrigin.position;
        float progress = npc.AimProgress;

        IsTracking = true;

        if (progress < 1f)
        {
            // CHARGE: Laser zeigt von laserOrigin direkt zum Intercept-Punkt
            currentDirection = (interceptPoint - origin).normalized;

            if (currentDirection.sqrMagnitude < 0.01f)
                currentDirection = npc.transform.forward;
        }
        else
        {
            // DASH: Laser entspricht exakt der Flugbahn des NPCs
            currentDirection = (interceptPoint - npc.transform.position).normalized;

            if (currentDirection.sqrMagnitude < 0.01f)
                currentDirection = npc.transform.forward;
        }

        UpdateWidthAndColor(progress);

        // Impact Sphere zur Intercept-Position bewegen
        UpdateImpactSphere(interceptPoint);

        if (showDebug && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[LaserPointer_Dash] {gameObject.name} | " +
                      $"Tracking={IsTracking} | " +
                      $"AimProgress={progress:F2} | " +
                      $"InterceptPoint={interceptPoint}");
        }

        // Endpunkt bestimmen: Raycast für Kollision mit Wänden/Boden
        Vector3 endPoint;
        if (Physics.Raycast(origin, currentDirection, out RaycastHit hit, laserLength, collisionMask))
        {
            endPoint = hit.point;
        }
        else
        {
            endPoint = origin + currentDirection * laserLength;
        }

        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, endPoint);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Width & Color
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateWidthAndColor(float progress)
    {
        // AnimationCurve statt hartkodiertem progress * progress
        float easedProgress = colorWidthCurve.Evaluate(progress);

        float currentWidth = Mathf.Lerp(earlyWidth, lockedWidth, easedProgress);
        lineRenderer.startWidth = currentWidth;
        lineRenderer.endWidth = currentWidth;

        Color currentColor = Color.Lerp(earlyColor, lockedColor, easedProgress);
        ApplyColor(currentColor);
    }

    private void ApplyColor(Color color)
    {
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;

        if (lineRenderer.material != null)
        {
            lineRenderer.material.color = color;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Impact Sphere
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Positioniert die Impact Sphere und aktiviert sie falls nötig.
    /// Wird ignoriert wenn keine Sphere zugewiesen ist.
    /// </summary>
    private void UpdateImpactSphere(Vector3 position)
    {
        if (impactSphere == null) return;

        impactSphere.transform.position = position;

        if (!impactSphere.activeSelf)
            impactSphere.SetActive(true);
    }

    /// <summary>
    /// Aktiviert oder deaktiviert die Impact Sphere.
    /// Wird ignoriert wenn keine Sphere zugewiesen ist.
    /// </summary>
    private void SetImpactSphereActive(bool active)
    {
        if (impactSphere == null) return;

        if (impactSphere.activeSelf != active)
            impactSphere.SetActive(active);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    public void SetOrigin(Transform newOrigin)
    {
        laserOrigin = newOrigin;
    }

    /// <summary>
    /// Aktiviert den Intercept-Modus. Laser und Impact Sphere snappen
    /// sofort auf den angegebenen Punkt.
    /// </summary>
    public void SetInterceptMode(Vector3 worldInterceptPoint)
    {
        isInterceptMode = true;
        interceptPoint = worldInterceptPoint;

        // Sofort auf Intercept-Punkt snappen
        if (laserOrigin != null)
        {
            currentDirection = (interceptPoint - laserOrigin.position).normalized;
        }

        // Impact Sphere sofort positionieren und aktivieren
        UpdateImpactSphere(interceptPoint);
    }

    /// <summary>
    /// Aktualisiert den Intercept-Punkt (z.B. wenn der Spieler seine Dash-Richtung ändert).
    /// Laser und Impact Sphere bewegen sich sofort mit.
    /// </summary>
    public void UpdateInterceptPoint(Vector3 worldInterceptPoint)
    {
        interceptPoint = worldInterceptPoint;
        UpdateImpactSphere(interceptPoint);
    }

    /// <summary>
    /// Beendet den Intercept-Modus. Laser und Impact Sphere werden deaktiviert.
    /// </summary>
    public void ClearInterceptMode()
    {
        isInterceptMode = false;
        interceptPoint = Vector3.zero;
        SetImpactSphereActive(false);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!showDebug || !Application.isPlaying) return;
        if (laserOrigin == null || npc == null) return;
        if (!isInterceptMode) return;

        // Intercept-Punkt visualisieren
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(interceptPoint, 0.5f);

        // Linie von Origin zum Intercept-Punkt
        Gizmos.color = IsTracking ? Color.red : Color.yellow;
        Gizmos.DrawLine(laserOrigin.position, interceptPoint);
    }

    #endregion
}
