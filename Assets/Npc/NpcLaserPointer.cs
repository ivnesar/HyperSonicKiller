using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// NPC LASER POINTER - Visueller Warnstrahl für bevorstehende Angriffe
// ════════════════════════════════════════════════════════════════════════════
//
// Zwei Modi:
//
//   TRACKING: Spieler ist im FOV des laserOrigin (Waffenrichtung) UND freie Sichtlinie
//             → Laser zeigt von Origin Richtung Spieler (+ Verlängerung)
//
//   FORWARD:  Spieler nicht im FOV oder Sicht blockiert
//             → Laser zeigt geradeaus (laserOrigin.forward)
//               bis zum nächsten Collider oder bis laserLength
//
// Wird von NpcBase über IsLaserActive gesteuert.
// Jede Subklasse entscheidet selbst, wann der Laser aktiv ist.
//
// Setup im Inspector:
//   1. Auf NPC-Prefab legen (neben NpcBase-Subklasse)
//   2. laserOrigin zuweisen (z.B. Waffe, Hand, Muzzle-Transform)
//   3. laserTarget zuweisen (z.B. Brust-Bone am Spieler) — optional
//   4. collisionMask setzen (Layer für Forward-Raycast, z.B. Solid/Wände)
//   5. losCheckMask setzen (Layer für Sichtlinien-Check, z.B. Solid + Player)
//   6. Material zuweisen (Farbe wird über das Material gesteuert)
//   7. Optional: FOV, Breite, Länge anpassen
//
// ════════════════════════════════════════════════════════════════════════════

[RequireComponent(typeof(NpcBase))]
public class NpcLaserPointer : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Transforms")]
    [Tooltip("Startpunkt des Lasers (z.B. Waffe, Hand, Muzzle). Wenn leer wird der Laser nicht angezeigt.")]
    [SerializeField] private Transform laserOrigin;

    [Tooltip("Zielpunkt am Spieler (z.B. Brust-Bone, Kopf-Bone). Wenn leer wird playerTransform.position als Fallback genutzt.")]
    [SerializeField] private Transform laserTarget;

    [Header("FOV & Line of Sight")]
    [Tooltip("Sichtfeld-Winkel (voll, nicht halb). Spieler muss innerhalb dieses Winkels ab laserOrigin.forward sein, damit der Laser auf ihn zeigt.")]
    [SerializeField] private float fieldOfView = 60f;

    [Tooltip("Layer-Maske für den Forward-Raycast im Forward-Modus (z.B. Solid, Wände, Boden).")]
    [SerializeField] private LayerMask collisionMask;

    [Tooltip("Layer-Maske für den Sichtlinien-Check zum Spieler. Sollte Solid + Player enthalten.")]
    [SerializeField] private LayerMask losCheckMask;

    [Header("Laser Settings")]
    [Tooltip("Maximale Länge des Laserstrahls")]
    [SerializeField] private float laserLength = 50f;

    [Tooltip("Breite des Lasers am Startpunkt")]
    [SerializeField] private float startWidth = 0.03f;

    [Tooltip("Breite des Lasers am Endpunkt")]
    [SerializeField] private float endWidth = 0.01f;

    [Tooltip("Dauer in Sekunden für die Richtungsüberblendung zwischen Forward- und Tracking-Modus")]
    [SerializeField] private float transitionDuration = 0.3f;

    [Header("Visuals")]
    [Tooltip("Material für den Laser. Farbe wird über das Material gesteuert.")]
    [SerializeField] private Material laserMaterial;

    [Header("Debug")]
    [Tooltip("Aktiviert Debug-Visualisierung: FOV-Cone, LOS-Ray und Konsolen-Logs")]
    [SerializeField] private bool showDebug = false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npc;
    private LineRenderer lineRenderer;
    private Transform playerTransform;

    // Aktuelle Richtung des Lasers — wird smooth interpoliert
    private Vector3 currentDirection;

    // Debug: letzte Werte für Gizmos-Zeichnung
    private bool debugInFOV;
    private bool debugHasLOS;
    private float debugAngle;
    private Vector3 debugTargetPoint;

    /// <summary>
    /// True wenn der Laser aktuell im Tracking-Modus ist (Spieler im FOV + LOS frei).
    /// Kann von außen gelesen werden, z.B. für UI oder Gameplay-Logik.
    /// </summary>
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

    private void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            playerTransform = player.transform;
    }

    private void LateUpdate()
    {
        // LateUpdate damit der Laser NACH Animationen und Bone-Rotation aktualisiert wird

        if (npc == null || npc.IsDead || laserOrigin == null || playerTransform == null)
        {
            lineRenderer.enabled = false;
            IsTracking = false;
            return;
        }

        if (npc.IsLaserActive)
        {
            UpdateLaser();
            lineRenderer.enabled = true;
        }
        else
        {
            lineRenderer.enabled = false;
            IsTracking = false;
            currentDirection = Vector3.zero;
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
        lineRenderer.startWidth = startWidth;
        lineRenderer.endWidth = endWidth;
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled = false;

        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

        if (laserMaterial != null)
        {
            lineRenderer.material = laserMaterial;
        }
        else
        {
            lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Laser Update
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateLaser()
    {
        Vector3 origin = laserOrigin.position;

        // Zielpunkt: laserTarget wenn zugewiesen, sonst Spieler-Position
        Vector3 targetPoint = laserTarget != null ? laserTarget.position : playerTransform.position;

        // Zielrichtung bestimmen (Tracking oder Forward)
        bool inFOV = IsPlayerInFOV(origin, targetPoint);
        bool hasLOS = inFOV && HasLineOfSight(origin, targetPoint);
        Vector3 targetDirection;

        if (inFOV && hasLOS)
        {
            IsTracking = true;
            targetDirection = (targetPoint - origin).normalized;
        }
        else
        {
            IsTracking = false;
            targetDirection = laserOrigin.forward;
        }

        // Debug-Log (alle 0.5 Sekunden, damit Konsole nicht geflutet wird)
        if (showDebug && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[LaserPointer] {gameObject.name} | " +
                      $"HorizontalAngle: {debugAngle:F1}° / {fieldOfView * 0.5f:F1}° (inFOV={inFOV}) | " +
                      $"LOS={hasLOS} | Tracking={IsTracking} | " +
                      $"NPC.forward={npc.transform.forward}");
        }

        // Smooth Überblendung zur Zielrichtung
        currentDirection = SmoothDirection(currentDirection, targetDirection);

        // Endpunkt berechnen: Raycast für Collider-Stopp, sonst volle Länge
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

    /// <summary>
    /// Interpoliert die aktuelle Richtung smooth zur Zielrichtung.
    /// Nutzt eine maximale Winkelgeschwindigkeit basierend auf transitionDuration.
    /// </summary>
    private Vector3 SmoothDirection(Vector3 current, Vector3 target)
    {
        // Beim ersten Frame oder wenn currentDirection noch leer ist → sofort setzen
        if (current == Vector3.zero)
            return target;

        // Kein Überblendung nötig wenn transitionDuration 0 oder negativ
        if (transitionDuration <= 0f)
            return target;

        // 180° in transitionDuration Sekunden → maximale Grad pro Sekunde
        float maxDegreesPerSecond = 180f / transitionDuration;
        float maxStep = maxDegreesPerSecond * Time.deltaTime;

        return Vector3.RotateTowards(current, target, maxStep * Mathf.Deg2Rad, 0f);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region FOV & Line of Sight
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prüft ob der Zielpunkt innerhalb des FOV-Winkels liegt.
    /// 
    /// Nutzt den HORIZONTALEN Winkel zwischen NPC-Body-Forward und Spielerrichtung.
    /// Grund: laserOrigin.forward hängt von der Aim-Bone-Rotation ab, die erst
    /// in SoldierNpc.LateUpdate() angewendet wird. Wenn NpcLaserPointer.LateUpdate()
    /// zuerst läuft, zeigt laserOrigin.forward noch horizontal → der vertikale
    /// Winkel zum Spieler übersteigt das FOV → tracking = false.
    /// 
    /// Durch die horizontale Prüfung ist der Check unabhängig von der LateUpdate-
    /// Reihenfolge und der vertikalen Bone-Rotation.
    /// </summary>
    private bool IsPlayerInFOV(Vector3 origin, Vector3 targetPoint)
    {
        // Horizontale Richtung zum Spieler (Y ignoriert)
        Vector3 directionToTarget = targetPoint - origin;
        Vector3 flatDirectionToTarget = new Vector3(directionToTarget.x, 0f, directionToTarget.z).normalized;

        // Horizontale NPC-Blickrichtung (Body-Forward, wird in Update() gesetzt)
        Vector3 flatForward = new Vector3(npc.transform.forward.x, 0f, npc.transform.forward.z).normalized;

        float angle = Vector3.Angle(flatForward, flatDirectionToTarget);
        bool inFOV = angle <= fieldOfView * 0.5f;

        // Debug-Daten cachen
        debugAngle = angle;
        debugTargetPoint = targetPoint;
        debugInFOV = inFOV;

        if (showDebug)
        {
            // NPC-Body-Forward als cyan Ray (horizontal)
            Debug.DrawRay(origin, flatForward * 5f, Color.cyan);
            // Richtung zum Spieler als grün (im FOV) oder rot (außerhalb)
            Debug.DrawRay(origin, directionToTarget.normalized * 5f, inFOV ? Color.green : Color.red);
        }

        return inFOV;
    }

    /// <summary>
    /// Prüft ob eine freie Sichtlinie zwischen Origin und Zielpunkt besteht.
    /// Freie Sicht = nichts getroffen, oder erstes getroffenes Objekt ist der Spieler.
    /// </summary>
    private bool HasLineOfSight(Vector3 origin, Vector3 targetPoint)
    {
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, losCheckMask))
        {
            debugHasLOS = hit.collider.CompareTag("Player");

            if (showDebug && !debugHasLOS)
            {
                Debug.DrawLine(origin, hit.point, Color.yellow);
                Debug.Log($"[LaserPointer] LOS blockiert von: {hit.collider.name} (Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)})");
            }

            return debugHasLOS;
        }

        debugHasLOS = true;
        return true;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Erlaubt das Ändern des Startpunkts zur Laufzeit.
    /// </summary>
    public void SetOrigin(Transform newOrigin)
    {
        laserOrigin = newOrigin;
    }

    /// <summary>
    /// Erlaubt das Ändern des Zielpunkts zur Laufzeit.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        laserTarget = newTarget;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!showDebug || !Application.isPlaying) return;
        if (laserOrigin == null || npc == null) return;

        Vector3 origin = laserOrigin.position;
        // FOV-Cone basiert auf dem horizontalen Body-Forward (wie IsPlayerInFOV)
        Vector3 forward = new Vector3(npc.transform.forward.x, 0f, npc.transform.forward.z).normalized;
        float halfAngle = fieldOfView * 0.5f;
        float coneLength = 5f;

        // ── FOV Cone Ränder zeichnen ──

        // Horizontale Ränder (links/rechts)
        Vector3 leftDir = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 rightDir = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;

        // Cone Farbe: grün wenn Spieler im FOV, rot wenn nicht
        Gizmos.color = debugInFOV ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 0f, 0f, 0.5f);

        Gizmos.DrawRay(origin, leftDir * coneLength);
        Gizmos.DrawRay(origin, rightDir * coneLength);

        // Forward-Richtung (cyan)
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, forward * coneLength);

        // Richtung zum Spieler-Target
        if (playerTransform != null)
        {
            Vector3 targetPoint = laserTarget != null ? laserTarget.position : playerTransform.position;

            Gizmos.color = (debugInFOV && debugHasLOS) ? Color.green : Color.red;
            Gizmos.DrawLine(origin, targetPoint);
            Gizmos.DrawWireSphere(targetPoint, 0.2f);
        }

        // Winkel-Info als kleiner Sphere am Origin
        Gizmos.color = debugInFOV ? Color.green : Color.red;
        Gizmos.DrawWireSphere(origin, 0.1f);
    }

    private void OnDrawGizmosSelected()
    {
        if (!showDebug || !Application.isPlaying) return;
        if (laserOrigin == null || npc == null) return;

        Vector3 origin = laserOrigin.position;
        Vector3 forward = new Vector3(npc.transform.forward.x, 0f, npc.transform.forward.z).normalized;
        float halfAngle = fieldOfView * 0.5f;
        float coneLength = 5f;

        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);

        // Horizontaler FOV-Bogen (16 Segmente)
        int segments = 16;
        Vector3 previousPoint = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            // Interpoliere von -halfAngle bis +halfAngle um Vector3.up
            float t = (float)i / segments;
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 edgeDir = Quaternion.AngleAxis(currentAngle, Vector3.up) * forward;

            Vector3 point = origin + edgeDir.normalized * coneLength;

            if (i > 0)
                Gizmos.DrawLine(previousPoint, point);

            previousPoint = point;
        }
    }

    #endregion
}
