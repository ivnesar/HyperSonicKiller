using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ════════════════════════════════════════════════════════════════════════════
// TRACKING HUD — Bounding-Box-Overlay für alle NPCs im Sichtfeld
// ════════════════════════════════════════════════════════════════════════════
//
// PANINI-KORREKTUR:
// - URP rendert Panini Projection als Post-Processing-Effekt NACH der Welt.
// - OnGUI rendert NACH Post-Processing — die Boxen werden also nicht mehr
//   verzerrt und sitzen versetzt zur (verzerrten) Welt.
// - Lösung: Wir wenden die gleiche Panini-Verzerrung in Software auf jeden
//   projizierten 2D-Punkt an, BEVOR wir ihn zeichnen. Damit "wandert" die
//   Box mit dem Pixel mit, den sie eigentlich umschließen soll.
//
// PANINI-WERTE:
// - Werden zur Laufzeit aus dem Volume-System gelesen (VolumeManager.stack).
// - Falls kein Panini-Override aktiv ist, wird die Korrektur übersprungen.
//
// ════════════════════════════════════════════════════════════════════════════

public class TrackingHUD : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Camera")]
    [Tooltip("Kamera, durch die der Spieler sieht. Leer = Camera.main")]
    [SerializeField] private Camera trackingCamera;

    [Header("Filter")]
    [Tooltip("Tote NPCs ausblenden")]
    [SerializeField] private bool hideDeadNpcs = true;

    [Header("Box Style")]
    [SerializeField] private Color boxColor = new Color(1f, 0.7f, 0f, 1f);
    [SerializeField] private float boxLineThickness = 2f;

    [Header("Label Style")]
    [SerializeField] private Color labelBackgroundColor = new Color(1f, 0.7f, 0f, 1f);
    [SerializeField] private Color labelTextColor = Color.black;
    [SerializeField] private int labelFontSize = 14;
    [SerializeField] private Vector2 labelPadding = new Vector2(6f, 2f);

    [Header("Panini Correction")]
    [Tooltip("Panini-Verzerrung in Software auf die Box-Punkte anwenden, " +
             "damit sie zum verzerrten Bild passen.")]
    [SerializeField] private bool applyPaniniCorrection = true;
    [Tooltip("Feinjustierung falls die Korrektur leicht zu stark/schwach wirkt. " +
             "1.0 = exakte URP-Formel.")]
    [SerializeField, Range(0f, 2f)] private float paniniStrengthMultiplier = 1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime
    // ════════════════════════════════════════════════════════════════════════

    private Texture2D boxLineTexture;
    private Texture2D labelBackgroundTexture;
    private GUIStyle labelStyle;
    private bool stylesInitialized;

    private readonly Vector3[] boundsCorners = new Vector3[8];
    private readonly List<NpcBase> npcBuffer = new List<NpcBase>(64);

    // Cached Panini-Werte (jeden Frame aktualisiert)
    private float paniniDistance;
    private float paniniCropToFit;
    private bool paniniActive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (trackingCamera == null)
            trackingCamera = Camera.main;

        boxLineTexture = CreateSolidTexture(boxColor);
        labelBackgroundTexture = CreateSolidTexture(labelBackgroundColor);
    }

    private void OnDestroy()
    {
        if (boxLineTexture != null) Destroy(boxLineTexture);
        if (labelBackgroundTexture != null) Destroy(labelBackgroundTexture);
    }

    private void OnGUI()
    {
        if (trackingCamera == null) return;

        InitStylesIfNeeded();
        UpdatePaniniSettings();
        CollectNpcs();

        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(trackingCamera);

        foreach (var npc in npcBuffer)
        {
            if (npc == null) continue;
            if (hideDeadNpcs && npc.IsDead) continue;

            Renderer renderer = npc.BoundsRenderer;
            if (renderer == null) continue;

            Bounds bounds = renderer.bounds;
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds)) continue;

            if (!TryGetScreenRect(bounds, out Rect screenRect)) continue;

            DrawBox(screenRect);
            DrawLabel(screenRect, npc.DisplayName);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Panini Settings
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Liest die aktuellen Panini-Werte aus dem aktiven Volume-Stack.
    /// Wird jeden Frame aufgerufen, damit Änderungen am Volume sofort wirken.
    /// </summary>
    private void UpdatePaniniSettings()
    {
        paniniActive = false;

        if (!applyPaniniCorrection) return;

        var stack = VolumeManager.instance.stack;
        if (stack == null) return;

        var panini = stack.GetComponent<PaniniProjection>();
        if (panini == null || !panini.IsActive()) return;

        paniniDistance = panini.distance.value * paniniStrengthMultiplier;
        paniniCropToFit = panini.cropToFit.value;
        paniniActive = paniniDistance > 0.0001f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NPC Collection
    // ════════════════════════════════════════════════════════════════════════

    private void CollectNpcs()
    {
        npcBuffer.Clear();
        var found = FindObjectsByType<NpcBase>(FindObjectsSortMode.None);
        npcBuffer.AddRange(found);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Screen-Space Math
    // ════════════════════════════════════════════════════════════════════════

    private bool TryGetScreenRect(Bounds bounds, out Rect rect)
    {
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;

        boundsCorners[0] = c + new Vector3(-e.x, -e.y, -e.z);
        boundsCorners[1] = c + new Vector3( e.x, -e.y, -e.z);
        boundsCorners[2] = c + new Vector3(-e.x,  e.y, -e.z);
        boundsCorners[3] = c + new Vector3( e.x,  e.y, -e.z);
        boundsCorners[4] = c + new Vector3(-e.x, -e.y,  e.z);
        boundsCorners[5] = c + new Vector3( e.x, -e.y,  e.z);
        boundsCorners[6] = c + new Vector3(-e.x,  e.y,  e.z);
        boundsCorners[7] = c + new Vector3( e.x,  e.y,  e.z);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool anyInFront = false;

        for (int i = 0; i < 8; i++)
        {
            Vector3 sp = trackingCamera.WorldToScreenPoint(boundsCorners[i]);
            if (sp.z < 0f) continue;
            anyInFront = true;

            // Panini-Verzerrung anwenden
            Vector2 distorted = paniniActive
                ? ApplyPaniniDistortion(new Vector2(sp.x, sp.y))
                : new Vector2(sp.x, sp.y);

            if (distorted.x < minX) minX = distorted.x;
            if (distorted.y < minY) minY = distorted.y;
            if (distorted.x > maxX) maxX = distorted.x;
            if (distorted.y > maxY) maxY = distorted.y;
        }

        if (!anyInFront)
        {
            rect = default;
            return false;
        }

        // Screen-Y → GUI-Y umrechnen
        float guiMinY = Screen.height - maxY;
        float guiMaxY = Screen.height - minY;

        rect = new Rect(minX, guiMinY, maxX - minX, guiMaxY - guiMinY);
        return true;
    }

    /// <summary>
    /// Wendet die Panini-Projection-Verzerrung auf einen Bildschirmpunkt an.
    /// Portierung der URP-Shader-Formel (Generalized Panini, Sharpless 2010).
    /// </summary>
    private Vector2 ApplyPaniniDistortion(Vector2 screenPoint)
    {
        float w = Screen.width;
        float h = Screen.height;
        if (w <= 0f || h <= 0f) return screenPoint;

        float aspect = w / h;

        // 1) Pixel → View-Plane-Koordinaten (was die Kamera bei Distanz=1 sehen würde)
        float halfFovY = trackingCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float viewExtentY = Mathf.Tan(halfFovY);
        float viewExtentX = viewExtentY * aspect;

        Vector2 view;
        view.x = (screenPoint.x / w * 2f - 1f) * viewExtentX;
        view.y = (screenPoint.y / h * 2f - 1f) * viewExtentY;

        // 2) Generalized-Panini-Verzerrung (Sharpless 2010, wie im URP-Shader)
        float d = paniniDistance;
        float xy2 = view.x * view.x + view.y * view.y;
        float S = (d + 1f) / (d + Mathf.Sqrt(1f + xy2));

        Vector2 distortedView;
        distortedView.x = view.x * S;
        distortedView.y = view.y * S;

        // 3) CropToFit: kompensiert die Bild-Schrumpfung an den Ecken
        float cornerXY = viewExtentX * viewExtentX + viewExtentY * viewExtentY;
        float cornerScale = (d + 1f) / (d + Mathf.Sqrt(1f + cornerXY));
        float crop = Mathf.Lerp(1f, 1f / cornerScale, paniniCropToFit);
        distortedView *= crop;

        // 4) Zurück in Pixel-Koordinaten
        Vector2 result;
        result.x = (distortedView.x / viewExtentX * 0.5f + 0.5f) * w;
        result.y = (distortedView.y / viewExtentY * 0.5f + 0.5f) * h;
        return result;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Drawing
    // ════════════════════════════════════════════════════════════════════════

    private void DrawBox(Rect r)
    {
        float t = boxLineThickness;
        GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, t), boxLineTexture);
        GUI.DrawTexture(new Rect(r.xMin, r.yMax - t, r.width, t), boxLineTexture);
        GUI.DrawTexture(new Rect(r.xMin, r.yMin, t, r.height), boxLineTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.yMin, t, r.height), boxLineTexture);
    }

    private void DrawLabel(Rect boxRect, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Vector2 textSize = labelStyle.CalcSize(new GUIContent(text));
        float w = textSize.x + labelPadding.x * 2f;
        float h = textSize.y + labelPadding.y * 2f;
        Rect labelRect = new Rect(boxRect.xMin, boxRect.yMin - h, w, h);

        GUI.DrawTexture(labelRect, labelBackgroundTexture);
        GUI.Label(labelRect, text, labelStyle);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    private void InitStylesIfNeeded()
    {
        if (stylesInitialized) return;

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = labelFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = labelTextColor }
        };

        stylesInitialized = true;
    }

    private static Texture2D CreateSolidTexture(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    #endregion
}
