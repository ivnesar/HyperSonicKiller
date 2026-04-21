using UnityEngine;

/// <summary>
/// Visualisiert den Scan-Puls als expandierende Sphere am Spieler.
/// Liest Radius und Zustand vom PlayerScanPulse, skaliert die Sphere entsprechend,
/// und steuert die Alpha über eine Animation Curve.
///
/// ARCHITEKTUR:
/// Diese Komponente ist reine Visualisierung — sie enthält KEINE Logik,
/// sondern spiegelt nur den Zustand von PlayerScanPulse.
///
/// PREFAB SETUP:
/// 1. Child-GameObject unter dem Player erstellen, z.B. "ScanPulseSphere"
/// 2. GameObject > 3D Object > Sphere, Collider löschen
/// 3. Child-GameObject auf SetActive(false)
/// 4. Diese Komponente (ScanPulseVisual) auf das gleiche GameObject wie
///    PlayerScanPulse (meist Root) — Sphere-GameObject ins 'sphereObject'-Feld,
///    Material ins 'scanMaterial'-Feld ziehen.
///
/// MATERIAL ANFORDERUNGEN:
/// - Shader: URP/Unlit (oder kompatibel)
/// - Surface Type: Transparent
/// - Property "_BaseColor" mit Alpha-Kanal (Standard bei URP Unlit)
/// - Für Sichtbarkeit von innen: Render Face = Both (sonst sieht der Spieler
///   die Sphere nicht mehr sobald er drin steht) — siehe Anmerkung im Chat.
/// </summary>
[RequireComponent(typeof(PlayerScanPulse))]
public class ScanPulseVisual : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector
    // ════════════════════════════════════════════════════════════════════════

    [Header("Sphere Setup")]
    [Tooltip("Child-GameObject mit Sphere-Mesh. " +
             "Wird automatisch aktiviert/deaktiviert basierend auf Puls-Status.")]
    [SerializeField] private GameObject sphereObject;

    [Tooltip("Material für die Sphere. Wird beim Start als Instanz kopiert, " +
             "damit die Alpha-Animation das Asset nicht permanent verändert. " +
             "Shader muss '_BaseColor' mit Alpha unterstützen (URP Unlit passt).")]
    [SerializeField] private Material scanMaterial;

    [Header("Alpha Curve")]
    [Tooltip("X-Achse: 0 = Puls gestartet, 1 = maxRadius erreicht. " +
             "Y-Achse: Alpha-Multiplikator (0 = unsichtbar, 1 = voll sichtbar). " +
             "Standard: 1 am Anfang, 0 am Ende (fadet während Wachstum aus).")]
    [SerializeField]
    private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerScanPulse scanPulse;
    private Renderer sphereRenderer;
    private Material materialInstance;
    private Color baseColor;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private int colorPropertyId;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        scanPulse = GetComponent<PlayerScanPulse>();

        if (sphereObject == null)
        {
            Debug.LogError($"[ScanPulseVisual] sphereObject nicht zugewiesen auf {name}!", this);
            enabled = false;
            return;
        }

        if (scanMaterial == null)
        {
            Debug.LogError($"[ScanPulseVisual] scanMaterial nicht zugewiesen auf {name}!", this);
            enabled = false;
            return;
        }

        sphereRenderer = sphereObject.GetComponent<Renderer>();
        if (sphereRenderer == null)
        {
            Debug.LogError($"[ScanPulseVisual] sphereObject hat keinen Renderer!", this);
            enabled = false;
            return;
        }

        // Material-Instanz erstellen (verhindert, dass Änderungen das Asset beeinflussen)
        materialInstance = new Material(scanMaterial);
        sphereRenderer.material = materialInstance;

        // Color-Property detektieren (URP nutzt _BaseColor, Legacy nutzt _Color)
        if (materialInstance.HasProperty(BaseColorId))
        {
            colorPropertyId = BaseColorId;
        }
        else if (materialInstance.HasProperty(ColorId))
        {
            colorPropertyId = ColorId;
        }
        else
        {
            Debug.LogWarning($"[ScanPulseVisual] Material hat weder '_BaseColor' noch '_Color' — " +
                             "Fade wird nicht funktionieren!", this);
            colorPropertyId = BaseColorId;
        }

        // Basis-Farbe cachen, damit wir beim Fade nur den Alpha-Kanal ändern
        baseColor = materialInstance.GetColor(colorPropertyId);

        // Zu Beginn versteckt
        sphereObject.SetActive(false);
    }

    private void OnDestroy()
    {
        // Instanz aufräumen
        if (materialInstance != null)
            Destroy(materialInstance);
    }

    private void Update()
    {
        if (scanPulse.IsPulsing)
        {
            UpdateActiveSphere();
        }
        else if (sphereObject.activeSelf)
        {
            sphereObject.SetActive(false);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Sphere Update
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateActiveSphere()
    {
        if (!sphereObject.activeSelf)
            sphereObject.SetActive(true);

        // Normalized Progress (0-1) basierend auf Radius / maxRadius
        float maxRadius = scanPulse.MaxRadius;
        float progress = maxRadius > 0f
            ? Mathf.Clamp01(scanPulse.CurrentRadius / maxRadius)
            : 0f;

        // Scale: Unity Default-Sphere hat Radius 0.5, Scale 1 = Durchmesser 1.
        // Wir wollen Durchmesser = 2 * currentRadius → Scale = 2 * currentRadius.
        float diameter = scanPulse.CurrentRadius * 2f;
        sphereObject.transform.localScale = new Vector3(diameter, diameter, diameter);

        // Alpha aus Curve ableiten und in _BaseColor.a schreiben
        float alpha = alphaCurve.Evaluate(progress);
        Color c = baseColor;
        c.a = alpha;
        materialInstance.SetColor(colorPropertyId, c);

        if (logDebug)
            Debug.Log($"[ScanPulseVisual] progress={progress:F2}, " +
                      $"alpha={alpha:F2}, diameter={diameter:F1}");
    }

    #endregion
}
