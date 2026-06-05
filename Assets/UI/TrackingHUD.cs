using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>An welcher Ecke der Bounding Box der NPC-Name sitzt.</summary>
public enum LabelCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

// ════════════════════════════════════════════════════════════════════════════
// TRACKING HUD — Bounding-Box-Overlay für alle NPCs im Sichtfeld
// ════════════════════════════════════════════════════════════════════════════
//
// SICHTBARKEIT:
// - Eine Box erscheint nur, wenn der NPC tatsächlich sichtbar ist:
//     a) die Sichtlinie von der Kamera zur Bounds-Mitte ist frei
//        (kein Occluder/keine Wand dazwischen), ODER
//     b) der NPC wird gerade per NpcReveal (X-Ray) sichtbar gemacht.
// - Der Sichtlinien-Check testet NUR gegen 'occluderMask' (deine "Solid"-
//   Layer). Dadurch werden die NPC-eigenen Collider automatisch ignoriert.
//
// RECHNEN vs. ZEICHNEN:
// - OnGUI läuft mehrmals pro Frame (Layout, Repaint, Input-Events). Deshalb
//   wird alles Schwere (NPC-Suche, Frustum, Sichtlinie, Projektion) einmal
//   pro Frame in Update() gemacht und gecacht. OnGUI zeichnet nur noch.
//
// PANINI-KORREKTUR:
// - URP rendert Panini Projection als Post-Processing-Effekt NACH der Welt.
// - OnGUI rendert NACH Post-Processing — die Boxen werden also nicht mehr
//   verzerrt und sitzen versetzt zur (verzerrten) Welt.
// - Der Shader bildet OUTPUT-Pixel -> SOURCE-Pixel ab (er fragt: "welchen
//   Quellpixel sample ich hier?"). Wir haben aber die SOURCE-Position
//   (WorldToScreenPoint) und brauchen die OUTPUT-Position. Deshalb kehren wir
//   die Shader-Abbildung um (Bisektion), damit die Box dort landet, wo der
//   Gegner im verzerrten Bild wirklich erscheint.
// - Die Panini-Funktionen unten sind 1:1 aus PaniniProjection.shader portiert
//   (Pfade _GENERIC und _UNIT_DISTANCE). URPs Panini krümmt nur HORIZONTAL;
//   die Vertikale skaliert mit derselben Funktion von view.x.
//
// PANINI-WERTE:
// - Werden zur Laufzeit aus dem Volume-System gelesen (VolumeManager.stack).
// - Falls kein Panini-Override aktiv ist (oder Distance = 0), wird die
//   Korrektur übersprungen.
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

    [Header("Visibility")]
    [Tooltip("Layer die die Sicht blockieren (Wände, Level-Geometrie). " +
             "Auf deine 'Solid'-Layer setzen. Eine Box erscheint nur, wenn die " +
             "Sichtlinie von der Kamera zum NPC frei ist — oder der NPC gerade " +
             "per NpcReveal sichtbar gemacht wird.")]
    [SerializeField] private LayerMask occluderMask;

    [Header("Box Style")]
    [SerializeField] private Color boxColor = new Color(1f, 0.7f, 0f, 1f);
    [SerializeField] private float boxLineThickness = 2f;

    [Header("Label Style")]
    [Tooltip("An welcher Ecke der Bounding Box der Name sitzt.")]
    [SerializeField] private LabelCorner labelCorner = LabelCorner.TopLeft;
    [SerializeField] private Color labelBackgroundColor = new Color(1f, 0.7f, 0f, 1f);
    [SerializeField] private Color labelTextColor = Color.black;
    [SerializeField] private int labelFontSize = 14;
    [SerializeField] private Vector2 labelPadding = new Vector2(6f, 2f);

    [Header("Icon Style")]
    [Tooltip("Kantenlänge des Icons in Pixeln (feste Größe, unabhängig von der " +
             "Entfernung). Das Icon wird im Zentrum der Box gezeichnet.")]
    [SerializeField] private float iconSize = 32f;

    [Tooltip("Globaler Multiplikator auf die Icon-Größe. 1 = unverändert, " +
             "2 = doppelt so groß. Wirkt auf alle NPC-Icons.")]
    [SerializeField] private float iconSizeMultiplier = 1f;

    [Header("Tracking Jitter")]
    [Tooltip("Intervall in Sekunden (UNSCALED — unabhängig von Slow-Motion), in " +
             "dem der zufällige Positions-Versatz pro Gegner neu gewürfelt wird. " +
             "Die Box folgt dem Gegner weiterhin jeden Frame; nur der Versatz " +
             "springt in diesem Takt.")]
    [SerializeField] private float jitterInterval = 0.2f;

    [Tooltip("Maximaler Versatz als Anteil der Box-Größe (0.1 = bis zu 10% der " +
             "Box-Breite/-Höhe). Proportional, damit nahe und ferne Gegner gleich " +
             "wirken. Die Box-GRÖSSE bleibt unverändert, nur die Position wandert.")]
    [SerializeField, Range(0f, 0.5f)] private float jitterStrength = 0.1f;

    [Header("Panini Correction")]
    [Tooltip("Panini-Verzerrung in Software auf die Box-Punkte anwenden, " +
             "damit sie zum verzerrten Bild passen.")]
    [SerializeField] private bool applyPaniniCorrection = true;

    [Tooltip("Crop-To-Fit kompensieren (die Bild-Skalierung, die URP anwendet, " +
             "damit nach der Verzerrung keine Ränder entstehen). Zum Testen ein-/" +
             "ausschalten: idealerweise gleich wie 'Crop To Fit' am Panini-Override. " +
             "Aus = die Boxen ignorieren die Crop-Skalierung (paniniS = 1).")]
    [SerializeField] private bool compensateCropToFit = true;

    [Tooltip("Feinjustierung der Crop-Skalierung, falls die Boxen minimal zu " +
             "groß/klein wirken. 1.0 = exakte Formel.")]
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

    // Pro Frame in Update() befüllt, in OnGUI() nur gezeichnet
    private struct BoxData
    {
        public Rect rect;
        public string label;
        public Sprite icon;
        public Vector2 iconOffset;
        public float iconScale;
        public Color iconColor;
    }
    private readonly List<BoxData> boxesToDraw = new List<BoxData>(64);

    // Jitter: pro Gegner ein normalisierter Versatz [-1..1] je Achse, der nur
    // alle 'jitterInterval' Sekunden (unscaled) neu gewürfelt wird. Zwischen den
    // Würfen bleibt er konstant -> Box folgt flüssig mit festem prozentualem Versatz.
    private float jitterTimer;
    private readonly Dictionary<NpcBase, Vector2> jitterOffsets = new Dictionary<NpcBase, Vector2>(64);

    // Cached Panini-Werte (jeden Frame in UpdatePaniniSettings aktualisiert)
    private float paniniDistance;   // distance.value (0..1)
    private float paniniS;          // Pre-Scale aus Crop-To-Fit (* Multiplier)
    private float viewExtX;         // tan(fovY/2) * aspect
    private float viewExtY;         // tan(fovY/2)
    private bool paniniActive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (trackingCamera == null)
            trackingCamera = Camera.main;

        if (occluderMask.value == 0)
            Debug.LogWarning($"[TrackingHUD] 'Occluder Mask' ist leer auf {name}. " +
                             "Ohne ausgewählte Layer gilt jeder NPC als sichtbar " +
                             "(keine Verdeckungs-Prüfung). Auf deine 'Solid'-Layer setzen.", this);

        boxLineTexture = CreateSolidTexture(boxColor);
        labelBackgroundTexture = CreateSolidTexture(labelBackgroundColor);
    }

    private void OnDestroy()
    {
        if (boxLineTexture != null) Destroy(boxLineTexture);
        if (labelBackgroundTexture != null) Destroy(labelBackgroundTexture);
    }

    private void Update()
    {
        boxesToDraw.Clear();
        if (trackingCamera == null) return;

        // Jitter-Takt (unscaled, damit Slow-Motion ihn nicht streckt).
        // Beim Neu-Würfeln den Cache leeren -> entfernt zugleich tote Gegner.
        jitterTimer += Time.unscaledDeltaTime;
        bool rerollJitter = jitterTimer >= jitterInterval;
        if (rerollJitter)
        {
            jitterTimer = 0f;
            jitterOffsets.Clear();
        }

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

            // Nur zeichnen wenn der NPC sichtbar (freie Sichtlinie) oder ge-reveal-t ist
            if (!IsNpcVisible(npc, bounds)) continue;

            if (!TryGetScreenRect(bounds, out Rect screenRect)) continue;

            // Zufälligen Versatz holen/würfeln (pro Gegner, konstant zwischen den Ticks).
            if (!jitterOffsets.TryGetValue(npc, out Vector2 norm))
            {
                norm = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f));
                jitterOffsets[npc] = norm;
            }

            // Proportional zur Box-Größe -> nur Position verschieben, Größe bleibt.
            screenRect.x += norm.x * screenRect.width * jitterStrength;
            screenRect.y += norm.y * screenRect.height * jitterStrength;

            // Icon des aktuellen States holen (Komponente ist optional pro NPC).
            var iconComp = npc.GetComponent<NpcHudIcon>();
            Sprite icon = iconComp != null ? iconComp.GetCurrentIcon() : null;
            Vector2 iconOffset = iconComp != null ? iconComp.PositionOffset : Vector2.zero;
            float iconScale = iconComp != null ? iconComp.SizeMultiplier : 1f;
            Color iconColor = iconComp != null ? iconComp.IconColor : Color.white;

            boxesToDraw.Add(new BoxData
            {
                rect = screenRect,
                label = npc.DisplayName,
                icon = icon,
                iconOffset = iconOffset,
                iconScale = iconScale,
                iconColor = iconColor
            });
        }
    }

    private void OnGUI()
    {
        if (trackingCamera == null) return;

        InitStylesIfNeeded();

        foreach (var box in boxesToDraw)
        {
            DrawBox(box.rect);
            DrawIcon(box.rect, box.icon, box.iconOffset, box.iconScale, box.iconColor);
            DrawLabel(box.rect, box.label);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Visibility
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// True wenn der NPC sichtbar ist: entweder per NpcReveal aktiv,
    /// oder die Sichtlinie von der Kamera zur Bounds-Mitte ist frei.
    /// </summary>
    private bool IsNpcVisible(NpcBase npc, Bounds bounds)
    {
        // X-Ray-Reveal überschreibt den Sichtlinien-Check -> immer zeigen.
        var reveal = npc.GetComponent<NpcReveal>();
        if (reveal != null && reveal.IsRevealed) return true;

        // Sichtlinie: ein Strahl von der Kamera zur Bounds-Mitte.
        // Getestet wird NUR gegen die Occluder-Maske (Wände/"Solid"), daher
        // werden die NPC-eigenen Collider automatisch ignoriert (kein Self-Hit).
        Vector3 camPos = trackingCamera.transform.position;
        bool blocked = Physics.Linecast(camPos, bounds.center, occluderMask,
                                        QueryTriggerInteraction.Ignore);
        return !blocked;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Panini Settings
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Liest die aktuellen Panini-Werte aus dem aktiven Volume-Stack und berechnet
    /// den Pre-Scale (paniniS). Wird jeden Frame aufgerufen, damit Änderungen am
    /// Volume sofort wirken.
    /// </summary>
    private void UpdatePaniniSettings()
    {
        paniniActive = false;

        if (!applyPaniniCorrection) return;
        if (VolumeManager.instance == null) return;

        var stack = VolumeManager.instance.stack;
        if (stack == null) return;

        var panini = stack.GetComponent<PaniniProjection>();
        if (panini == null || !panini.IsActive()) return;

        paniniDistance = panini.distance.value;
        if (paniniDistance <= 0.0001f) return; // Effekt aus -> keine Korrektur

        float w = Screen.width;
        float h = Screen.height;
        if (w <= 0f || h <= 0f) return;

        viewExtY = Mathf.Tan(0.5f * trackingCamera.fieldOfView * Mathf.Deg2Rad);
        viewExtX = viewExtY * (w / h);

        float baseS = compensateCropToFit
            ? ComputePaniniS(paniniDistance, viewExtX, viewExtY, panini.cropToFit.value)
            : 1f;

        paniniS = Mathf.Max(0.01f, baseS * paniniStrengthMultiplier);
        paniniActive = true;
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

            // Panini-Verzerrung anwenden (invertierte Shader-Abbildung)
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
    /// Wandelt einen (un-verzerrten) Bildschirmpunkt in die Position um, an der er
    /// NACH der Panini Projection erscheint. Das ist die Umkehrung der Shader-
    /// Abbildung: Der Shader rechnet OUTPUT->SOURCE, wir suchen also per Bisektion
    /// das OUTPUT, dessen Source unserem Eingabepunkt entspricht.
    /// </summary>
    private Vector2 ApplyPaniniDistortion(Vector2 screenPoint)
    {
        float w = Screen.width;
        float h = Screen.height;
        if (w <= 0f || h <= 0f) return screenPoint;

        // Source-Punkt -> NDC (-1..1)
        float srcX = screenPoint.x / w * 2f - 1f;
        float srcY = screenPoint.y / h * 2f - 1f;

        // Output-X suchen: SourceNdcX(outX) ist monoton steigend in outX.
        float lo = -2f, hi = 2f;
        // Suchbereich absichern, falls der Punkt knapp außerhalb des Bildes liegt
        for (int g = 0; g < 6 && SourceNdcX(hi) < srcX; g++) hi *= 2f;
        for (int g = 0; g < 6 && SourceNdcX(lo) > srcX; g++) lo *= 2f;

        for (int i = 0; i < 24; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (SourceNdcX(mid) < srcX) lo = mid; else hi = mid;
        }
        float outX = 0.5f * (lo + hi);

        // Panini skaliert x und y mit demselben Faktor (abhängig von view.x).
        // Den lesen wir aus der bereits gelösten x-Komponente ab.
        const float eps = 1e-4f;
        float scale = (Mathf.Abs(outX) > eps) ? srcX / outX : SourceNdcX(eps) / eps;
        if (Mathf.Abs(scale) < 1e-6f) return screenPoint;
        float outY = srcY / scale;

        return new Vector2((outX * 0.5f + 0.5f) * w, (outY * 0.5f + 0.5f) * h);
    }

    /// <summary>
    /// Vorwärts-Abbildung des Shaders (nur x-Komponente): Output-NDC -> Source-NDC.
    /// </summary>
    private float SourceNdcX(float outX)
    {
        Vector2 v = new Vector2(outX * viewExtX * paniniS, 0f);
        return Panini(v, paniniDistance).x / viewExtX;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Panini Math (Portierung aus PaniniProjection.shader)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Wählt wie der Shader zwischen _GENERIC und _UNIT_DISTANCE.</summary>
    private static Vector2 Panini(Vector2 v, float d)
        => (1f - Mathf.Abs(d) > Mathf.Epsilon) ? PaniniGeneric(v, d) : PaniniUnitDistance(v);

    private static Vector2 PaniniGeneric(Vector2 v, float d)
    {
        float viewDist = 1f + d;
        float viewHypSq = v.x * v.x + viewDist * viewDist;
        float isectD = v.x * d;
        float discrim = Mathf.Max(0f, viewHypSq - isectD * isectD);
        float cylDistMinusD = (-isectD * v.x + viewDist * Mathf.Sqrt(discrim)) / viewHypSq;
        float cylDist = cylDistMinusD + d;
        Vector2 cylPos = v * (cylDist / viewDist);
        return cylPos / (cylDist - d);
    }

    private static Vector2 PaniniUnitDistance(Vector2 v)
    {
        const float d = 1f;
        const float viewDist = 2f;
        const float viewDistSq = 4f;
        float viewHyp = Mathf.Sqrt(v.x * v.x + viewDistSq);
        float frac = (viewHyp - (v.x * v.x) / viewHyp) / viewHyp;
        float cylDist = viewDist * frac;
        return (v * frac) / (cylDist - d);
    }

    /// <summary>
    /// Pre-Scale, der URPs "Crop To Fit" entspricht. Wird numerisch gelöst:
    /// Wir suchen den Skalierungsfaktor, bei dem der Bildrand exakt auf den
    /// Quellrand fällt (pro Achse), und nehmen das Minimum (gleichmäßig, damit
    /// das Bild nicht verzerrt). Bei kleinen Rest-Abweichungen mit
    /// paniniStrengthMultiplier nachjustieren.
    /// </summary>
    private static float ComputePaniniS(float d, float extX, float extY, float cropToFit)
    {
        if (cropToFit <= 0f) return 1f;
        float sx = SolveAxisScale(true, d, extX, extY);
        float sy = SolveAxisScale(false, d, extX, extY);
        float scaleF = Mathf.Min(sx, sy);
        return Mathf.Lerp(1f, Mathf.Clamp01(scaleF), cropToFit);
    }

    private static float SolveAxisScale(bool horizontal, float d, float extX, float extY)
    {
        float lo = 0f, hi = 1f;
        for (int g = 0; g < 8 && EdgeSourceNdc(hi, horizontal, d, extX, extY) < 1f; g++) hi *= 2f;
        for (int i = 0; i < 24; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (EdgeSourceNdc(mid, horizontal, d, extX, extY) < 1f) lo = mid; else hi = mid;
        }
        return 0.5f * (lo + hi);
    }

    private static float EdgeSourceNdc(float s, bool horizontal, float d, float extX, float extY)
    {
        Vector2 v = horizontal ? new Vector2(extX * s, 0f) : new Vector2(0f, extY * s);
        Vector2 p = Panini(v, d);
        return horizontal ? p.x / extX : p.y / extY;
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

    /// <summary>
    /// Zeichnet das Icon mittig über der Box (feste Größe), plus optionalem
    /// per-Prefab Offset (+X rechts, +Y oben).
    /// </summary>
    /// <summary>
    /// Zeichnet das Icon mittig über der Box, plus per-Prefab Offset (+X rechts,
    /// +Y oben), Größen-Multiplikator und Einfärbung.
    /// </summary>
    private void DrawIcon(Rect boxRect, Sprite icon, Vector2 offset, float scale, Color color)
    {
        if (icon == null) return;

        float size = iconSize * iconSizeMultiplier * scale;

        // Icon wird im ZENTRUM der Box platziert (horizontal + vertikal mittig).
        // GUI-Y wächst nach unten -> "+offset.y = nach oben" heißt y verringern.
        float x = boxRect.xMin + boxRect.width * 0.5f - size * 0.5f + offset.x;
        float y = boxRect.yMin + boxRect.height * 0.5f - size * 0.5f - offset.y;

        // GUI.color tintet das Icon (multiplikativ). Vorher sichern, danach
        // zurücksetzen, damit Box und Label nicht eingefärbt werden.
        Color prev = GUI.color;
        GUI.color = color;
        DrawSprite(new Rect(x, y, size, size), icon);
        GUI.color = prev;
    }

    /// <summary>
    /// Zeichnet ein Sprite ins Rechteck. Nutzt die Sprite-eigenen Texturkoordinaten,
    /// damit auch Sprites aus einem Atlas korrekt (nur der eigene Ausschnitt) erscheinen.
    /// Hinweis: Nicht-quadratische Sprites werden auf iconSize x iconSize gestreckt.
    /// </summary>
    private static void DrawSprite(Rect rect, Sprite sprite)
    {
        Texture tex = sprite.texture;
        if (tex == null) return;

        Rect tr = sprite.textureRect; // Pixelbereich des Sprites in der Textur
        Rect texCoords = new Rect(
            tr.x / tex.width,
            tr.y / tex.height,
            tr.width / tex.width,
            tr.height / tex.height);

        GUI.DrawTextureWithTexCoords(rect, tex, texCoords);
    }

    private void DrawLabel(Rect boxRect, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Vector2 textSize = labelStyle.CalcSize(new GUIContent(text));
        float w = textSize.x + labelPadding.x * 2f;
        float h = textSize.y + labelPadding.y * 2f;

        // Ecke wählen: Top -> über der Box, Bottom -> unter der Box;
        // Left -> linksbündig (xMin), Right -> rechtsbündig (xMax - w).
        float x, y;
        switch (labelCorner)
        {
            case LabelCorner.TopRight:
                x = boxRect.xMax - w; y = boxRect.yMin - h; break;
            case LabelCorner.BottomLeft:
                x = boxRect.xMin;     y = boxRect.yMax;     break;
            case LabelCorner.BottomRight:
                x = boxRect.xMax - w; y = boxRect.yMax;     break;
            default: // TopLeft
                x = boxRect.xMin;     y = boxRect.yMin - h; break;
        }

        Rect labelRect = new Rect(x, y, w, h);

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
