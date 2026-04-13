using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// NPC LASER POINTER - Visueller Warnstrahl für bevorstehende Angriffe
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

    [Tooltip("Dauer in Sekunden für die Richtungsüberblendung zwischen Forward- und Tracking-Modus")]
    [SerializeField] private float transitionDuration = 0.3f;

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

    [Header("Wiggle Settings")]
    [Tooltip("Maximaler Wiggle-Radius in Grad bei AimProgress = 0 (Beginn des Zielens).")]
    [SerializeField] private float wiggleMaxAngle = 8f;

    [Tooltip("Geschwindigkeit der Wiggle-Bewegung. Höhere Werte = unruhigerer Laser.")]
    [SerializeField] private float wiggleFrequency = 3f;

    [Tooltip("Aktiviert den Wiggle-Effekt. Wenn false, zeigt der Laser direkt auf den Spieler.")]
    [SerializeField] private bool enableWiggle = true;

    [Header("Visuals")]
    [Tooltip("Material für den Laser. Wird zur Laufzeit instanziert — das Original bleibt unverändert.")]
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
    private Transform laserTarget;

    private Vector3 currentDirection;

    private float noiseSeedX;
    private float noiseSeedY;

    private bool isInterceptMode;
    private Vector3 interceptPoint;

    private bool debugInFOV;
    private bool debugHasLOS;
    private float debugAngle;
    private Vector3 debugTargetPoint;
    private float debugWiggleRadius;

    public bool IsTracking { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();
        SetupLineRenderer();

        noiseSeedX = Random.Range(0f, 1000f);
        noiseSeedY = Random.Range(0f, 1000f);
    }

    private void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            var playerCore = player.GetComponent<PlayerCore>();
            if (playerCore != null && playerCore.LaserTarget != null)
            {
                laserTarget = playerCore.LaserTarget;
            }
        }
    }

    private void LateUpdate()
    {
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
        Vector3 targetDirection;

        if (isInterceptMode)
        {
            // ── INTERCEPT-MODUS (GenTwo Charge/Dash) ──
            IsTracking = true;

            if (progress < 1f)
            {
                // CHARGE: Sofort auf Player Aim Target richten
                Vector3 playerPoint = laserTarget != null ? laserTarget.position : playerTransform.position;
                targetDirection = (playerPoint - origin).normalized;

                if (enableWiggle)
                {
                    targetDirection = ApplyWiggle(targetDirection, progress);
                }

                // Smooth Überblendung bei Charge (unscaled, weil Slow-Mo aktiv)
                currentDirection = SmoothDirection(currentDirection, targetDirection, true);
            }
            else
            {
                // DASH: Laser entspricht exakt der Flugbahn des NPCs
                targetDirection = (interceptPoint - npc.transform.position).normalized;
                
                if (targetDirection.sqrMagnitude < 0.01f)
                    targetDirection = npc.transform.forward;

                // Smoothing überspringen: Laser snappt sofort auf Flugbahn!
                currentDirection = targetDirection;
            }
        }
        else
        {
            // ── STANDARD-MODUS (FOV-basiert) ──
            Vector3 targetPoint = laserTarget != null ? laserTarget.position : playerTransform.position;

            bool inFOV = IsPlayerInFOV(origin, targetPoint);
            bool hasLOS = inFOV && HasLineOfSight(origin, targetPoint);

            if (inFOV && hasLOS)
            {
                IsTracking = true;
                targetDirection = (targetPoint - origin).normalized;

                if (enableWiggle)
                {
                    targetDirection = ApplyWiggle(targetDirection, progress);
                }
            }
            else
            {
                IsTracking = false;
                targetDirection = laserOrigin.forward;
            }

            // Smooth Überblendung bei Standard-Tracking
            currentDirection = SmoothDirection(currentDirection, targetDirection);
        }

        UpdateWidthAndColor(progress);

        if (showDebug && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[LaserPointer] {gameObject.name} | " +
                      $"Intercept={isInterceptMode} | Tracking={IsTracking} | " +
                      $"AimProgress={progress:F2} | WiggleRadius={debugWiggleRadius:F1}°");
        }

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
        float easedProgress = progress * progress;

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
    #region Wiggle & Smoothing
    // ════════════════════════════════════════════════════════════════════════

    private Vector3 ApplyWiggle(Vector3 direction, float progress)
    {
        float easedProgress = progress * progress;
        float currentAngle = wiggleMaxAngle * (1f - easedProgress);

        debugWiggleRadius = currentAngle;

        if (currentAngle < 0.01f)
            return direction;

        float time = Time.time * wiggleFrequency;
        float noiseX = (Mathf.PerlinNoise(time, noiseSeedX) - 0.5f) * 2f;
        float noiseY = (Mathf.PerlinNoise(noiseSeedY, time) - 0.5f) * 2f;

        Quaternion baseRotation = Quaternion.LookRotation(direction);
        Vector3 right = baseRotation * Vector3.right;
        Vector3 up = baseRotation * Vector3.up;

        Quaternion wiggleRotation = Quaternion.AngleAxis(noiseX * currentAngle, up)
                                  * Quaternion.AngleAxis(noiseY * currentAngle, right);

        return (wiggleRotation * direction).normalized;
    }

    private Vector3 SmoothDirection(Vector3 current, Vector3 target, bool useUnscaledTime = false)
    {
        if (current == Vector3.zero) return target;
        if (transitionDuration <= 0f) return target;

        float maxDegreesPerSecond = 180f / transitionDuration;
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float maxStep = maxDegreesPerSecond * dt;

        return Vector3.RotateTowards(current, target, maxStep * Mathf.Deg2Rad, 0f);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region FOV & Line of Sight
    // ════════════════════════════════════════════════════════════════════════

    private bool IsPlayerInFOV(Vector3 origin, Vector3 targetPoint)
    {
        Vector3 directionToTarget = targetPoint - origin;
        Vector3 flatDirectionToTarget = new Vector3(directionToTarget.x, 0f, directionToTarget.z).normalized;
        Vector3 flatForward = new Vector3(npc.transform.forward.x, 0f, npc.transform.forward.z).normalized;

        float angle = Vector3.Angle(flatForward, flatDirectionToTarget);
        bool inFOV = angle <= fieldOfView * 0.5f;

        debugAngle = angle;
        debugTargetPoint = targetPoint;
        debugInFOV = inFOV;

        return inFOV;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 targetPoint)
    {
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, losCheckMask))
        {
            debugHasLOS = hit.collider.CompareTag("Player");
            return debugHasLOS;
        }

        debugHasLOS = true;
        return true;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    public void SetOrigin(Transform newOrigin)
    {
        laserOrigin = newOrigin;
    }

    public void SetInterceptMode(Vector3 worldInterceptPoint)
    {
        isInterceptMode = true;
        interceptPoint = worldInterceptPoint;

        // Sofort auf Spieler-Richtung snappen, damit der Laser nicht
        // erst langsam von der alten forward-Richtung überschwenkt.
        if (laserOrigin != null)
        {
            Vector3 playerPoint = laserTarget != null ? laserTarget.position : playerTransform.position;
            currentDirection = (playerPoint - laserOrigin.position).normalized;
        }
    }

    public void UpdateInterceptPoint(Vector3 worldInterceptPoint)
    {
        interceptPoint = worldInterceptPoint;
    }

    public void ClearInterceptMode()
    {
        isInterceptMode = false;
        interceptPoint = Vector3.zero;
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
        Vector3 forward = new Vector3(npc.transform.forward.x, 0f, npc.transform.forward.z).normalized;
        float halfAngle = fieldOfView * 0.5f;
        float coneLength = 5f;

        Vector3 leftDir = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 rightDir = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;

        Gizmos.color = debugInFOV ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 0f, 0f, 0.5f);
        Gizmos.DrawRay(origin, leftDir * coneLength);
        Gizmos.DrawRay(origin, rightDir * coneLength);

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, forward * coneLength);

        if (playerTransform != null)
        {
            Vector3 targetPoint = laserTarget != null ? laserTarget.position : playerTransform.position;
            Gizmos.color = (debugInFOV && debugHasLOS) ? Color.green : Color.red;
            Gizmos.DrawLine(origin, targetPoint);
            Gizmos.DrawWireSphere(targetPoint, 0.2f);
        }

        Gizmos.color = debugInFOV ? Color.green : Color.red;
        Gizmos.DrawWireSphere(origin, 0.1f);

        if (IsTracking && enableWiggle && playerTransform != null)
        {
            Vector3 targetPoint = laserTarget != null ? laserTarget.position : playerTransform.position;
            float distToTarget = Vector3.Distance(origin, targetPoint);
            float wiggleWorldRadius = distToTarget * Mathf.Tan(debugWiggleRadius * Mathf.Deg2Rad);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f); 
            Gizmos.DrawWireSphere(targetPoint, wiggleWorldRadius);
        }
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
        Vector3 previousPoint = Vector3.zero;

        for (int i = 0; i <= 16; i++)
        {
            float t = (float)i / 16;
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 edgeDir = Quaternion.AngleAxis(currentAngle, Vector3.up) * forward;
            Vector3 point = origin + edgeDir.normalized * coneLength;

            if (i > 0) Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }
    #endregion
}