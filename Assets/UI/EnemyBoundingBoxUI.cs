using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// ════════════════════════════════════════════════════════════════════════════
// ENEMY BOUNDING BOX UI - Screen-Space Bounding Box um jeden lebenden NPC
// ════════════════════════════════════════════════════════════════════════════
//
// Unterstützt Panini Projection Post-Processing:
//   Die Bounding-Box-Koordinaten werden durch dieselbe Panini-Formel
//   verzerrt, die auch Unity's URP-Shader nutzt, sodass die Boxen
//   korrekt auf den verzerrten Gegnern sitzen.
//
// SETUP:
//   1. Auf ein GameObject unter einem ScreenSpace-Overlay Canvas legen
//   2. NpcBase muss NpcRegistry.Register/Unregister aufrufen
//   3. Panini Projection im Volume → wird automatisch erkannt
//
// ════════════════════════════════════════════════════════════════════════════

public class EnemyBoundingBoxUI : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Appearance")]
    [Tooltip("Farbe der Bounding Box")]
    [SerializeField] private Color boxColor = new Color(1f, 0.2f, 0.2f, 0.9f);

    [Tooltip("Dicke der Rahmenlinien in Pixeln")]
    [SerializeField] private float borderThickness = 2f;

    [Tooltip("Padding um die Box herum in Pixeln")]
    [SerializeField] private float padding = 4f;

    [Header("Camera")]
    [Tooltip("Wird automatisch auf Camera.main gesetzt wenn leer")]
    [SerializeField] private Camera targetCamera;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime
    // ════════════════════════════════════════════════════════════════════════

    private readonly Dictionary<NpcBase, RectTransform> activeBoxes = new();
    private readonly Stack<RectTransform> pool = new();
    private readonly Vector3[] boundsCorners = new Vector3[8];

    // Panini-Parameter (pro Frame aus Volume Stack gelesen)
    private bool paniniActive;
    private float paniniDistance;
    private float paniniScale;
    private float viewExtX;
    private float viewExtY;

    // Temporäre Listen um GC-Allocs zu vermeiden
    private readonly HashSet<NpcBase> tempActiveNpcs = new();
    private readonly List<NpcBase> toRemove = new();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
        {
            Debug.LogError("[EnemyBoundingBoxUI] No camera found!");
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (targetCamera == null) return;

        UpdatePaniniParams();

        tempActiveNpcs.Clear();

        foreach (NpcBase npc in NpcRegistry.AliveNpcs)
        {
            if (npc == null || npc.IsDead) continue;

            tempActiveNpcs.Add(npc);

            if (!activeBoxes.TryGetValue(npc, out RectTransform box))
            {
                box = GetBoxFromPool();
                activeBoxes[npc] = box;
            }

            UpdateBox(npc, box);
        }

        // Boxen für entfernte/tote NPCs recyceln
        toRemove.Clear();
        foreach (var kvp in activeBoxes)
        {
            if (!tempActiveNpcs.Contains(kvp.Key))
                toRemove.Add(kvp.Key);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            ReturnBoxToPool(activeBoxes[toRemove[i]]);
            activeBoxes.Remove(toRemove[i]);
        }
    }

    private void OnDisable()
    {
        foreach (var kvp in activeBoxes)
            ReturnBoxToPool(kvp.Value);

        activeBoxes.Clear();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Panini Projection
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Liest Panini-Parameter aus dem URP Volume Stack und berechnet
    /// einmalig pro Frame alle benötigten Werte.
    /// </summary>
    private void UpdatePaniniParams()
    {
        paniniActive = false;

        if (VolumeManager.instance == null) return;

        var volumeStack = VolumeManager.instance.stack;
        if (volumeStack == null) return;

        PaniniProjection panini;
        try
        {
            panini = volumeStack.GetComponent<PaniniProjection>();
        }
        catch
        {
            return;
        }

        if (panini == null || !panini.active || panini.distance.value <= 0.001f) return;

        paniniActive = true;
        paniniDistance = panini.distance.value;

        float fovY = targetCamera.fieldOfView * Mathf.Deg2Rad;
        float aspect = (float)Screen.width / Screen.height;

        viewExtY = Mathf.Tan(0.5f * fovY);
        viewExtX = aspect * viewExtY;

        // CalcCropExtents: Panini-verzerrt den Rand des Bildes
        float cropExtX = PaniniForward(viewExtX, 0f, paniniDistance).x;
        float scaleF = Mathf.Min(cropExtX / viewExtX, 1f);

        // Wenn cropToFit nicht per Override aktiviert ist, nutzt Unity den Default (1.0)
        float cropToFit = panini.cropToFit.overrideState ? panini.cropToFit.value : 1f;
        paniniScale = Mathf.Lerp(1f, Mathf.Clamp01(scaleF), cropToFit);
    }

    /// <summary>
    /// Wendet die Panini-Verzerrung auf einen Screen-Space Punkt an.
    /// 
    /// Der Shader sampelt für jede Output-UV die Quell-Textur an Panini(output_uv).
    /// Das bedeutet: ein Objekt an Quell-Position Q erscheint im Output bei P,
    /// wobei Panini(P) = Q → P = Panini⁻¹(Q).
    /// 
    /// Wir berechnen die Inverse iterativ per Newton-Raphson.
    /// </summary>
    private Vector2 ApplyPaniniToScreenPoint(Vector2 screenPoint)
    {
        // Screen-Pixel → UV [0,1]
        float u = screenPoint.x / Screen.width;
        float v = screenPoint.y / Screen.height;

        // UV → View-Space (wie im Shader: view_pos = (2*uv - 1) * ext * scale)
        float targetVX = (2f * u - 1f) * viewExtX * paniniScale;
        float targetVY = (2f * v - 1f) * viewExtY * paniniScale;

        // Bessere Startschätzung: Forward-Panini skaliert Punkte nach außen,
        // also muss die Inverse sie nach innen bringen.
        // Grobe Annäherung: invers ≈ original * Verhältnis(zentrum/rand)
        Vector2 fwdCenter = PaniniForward(targetVX, targetVY, paniniDistance);
        float ratio = 1f;
        float fwdLen = Mathf.Sqrt(fwdCenter.x * fwdCenter.x + fwdCenter.y * fwdCenter.y);
        float targetLen = Mathf.Sqrt(targetVX * targetVX + targetVY * targetVY);
        if (fwdLen > 1e-6f)
            ratio = targetLen / fwdLen;

        float startVX = targetVX * ratio;
        float startVY = targetVY * ratio;

        float gu = (startVX / (viewExtX * paniniScale) + 1f) * 0.5f;
        float gv = (startVY / (viewExtY * paniniScale) + 1f) * 0.5f;

        // Newton-Iteration
        float scU = 2f * viewExtX * paniniScale;
        float scV = 2f * viewExtY * paniniScale;

        for (int i = 0; i < 12; i++)
        {
            float gx = (2f * gu - 1f) * viewExtX * paniniScale;
            float gy = (2f * gv - 1f) * viewExtY * paniniScale;

            Vector2 p = PaniniForward(gx, gy, paniniDistance);

            float errX = p.x - targetVX;
            float errY = p.y - targetVY;

            if (errX * errX + errY * errY < 1e-12f) break;

            // Numerische partielle Ableitungen
            const float h = 0.0001f;
            Vector2 dpX = PaniniForward(gx + h, gy, paniniDistance);
            Vector2 dpY = PaniniForward(gx, gy + h, paniniDistance);

            float j00 = (dpX.x - p.x) / h * scU;
            float j01 = (dpY.x - p.x) / h * scV;
            float j10 = (dpX.y - p.y) / h * scU;
            float j11 = (dpY.y - p.y) / h * scV;

            float det = j00 * j11 - j01 * j10;
            if (Mathf.Abs(det) < 1e-14f) break;

            float inv = 1f / det;
            gu -= ( j11 * errX - j01 * errY) * inv;
            gv -= (-j10 * errX + j00 * errY) * inv;
        }

        return new Vector2(gu * Screen.width, gv * Screen.height);
    }

    /// <summary>
    /// C#-Port von Unity's Panini_Generic Shader-Funktion.
    /// Identische Mathematik wie im URP PaniniProjection.shader.
    /// </summary>
    private static Vector2 PaniniForward(float viewX, float viewY, float d)
    {
        float viewDist = 1f + d;
        float viewHypSq = viewX * viewX + viewDist * viewDist;

        float isectD = viewX * d;
        float isectDiscrim = viewHypSq - isectD * isectD;
        if (isectDiscrim < 0f) isectDiscrim = 0f;

        float cylDistMinusD = (-isectD * viewX + viewDist * Mathf.Sqrt(isectDiscrim)) / viewHypSq;
        float cylDist = cylDistMinusD + d;

        float ratio = cylDist / viewDist;
        float denom = cylDist - d;
        if (Mathf.Abs(denom) < 1e-10f) denom = 1e-10f;

        return new Vector2(viewX * ratio / denom, viewY * ratio / denom);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Box Update
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateBox(NpcBase npc, RectTransform box)
    {
        Renderer[] renderers = npc.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            box.gameObject.SetActive(false);
            return;
        }

        // Kombinierte Bounds
        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combinedBounds.Encapsulate(renderers[i].bounds);

        // 8 Ecken der 3D-Bounds
        Vector3 center = combinedBounds.center;
        Vector3 extents = combinedBounds.extents;

        boundsCorners[0] = center + new Vector3(-extents.x, -extents.y, -extents.z);
        boundsCorners[1] = center + new Vector3(-extents.x, -extents.y,  extents.z);
        boundsCorners[2] = center + new Vector3(-extents.x,  extents.y, -extents.z);
        boundsCorners[3] = center + new Vector3(-extents.x,  extents.y,  extents.z);
        boundsCorners[4] = center + new Vector3( extents.x, -extents.y, -extents.z);
        boundsCorners[5] = center + new Vector3( extents.x, -extents.y,  extents.z);
        boundsCorners[6] = center + new Vector3( extents.x,  extents.y, -extents.z);
        boundsCorners[7] = center + new Vector3( extents.x,  extents.y,  extents.z);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        bool anyBehindCamera = false;

        for (int i = 0; i < 8; i++)
        {
            Vector3 screenPoint = targetCamera.WorldToScreenPoint(boundsCorners[i]);

            if (screenPoint.z < 0f)
            {
                anyBehindCamera = true;
                break;
            }

            Vector2 finalPoint;
            if (paniniActive)
                finalPoint = ApplyPaniniToScreenPoint(new Vector2(screenPoint.x, screenPoint.y));
            else
                finalPoint = new Vector2(screenPoint.x, screenPoint.y);

            if (finalPoint.x < minX) minX = finalPoint.x;
            if (finalPoint.x > maxX) maxX = finalPoint.x;
            if (finalPoint.y < minY) minY = finalPoint.y;
            if (finalPoint.y > maxY) maxY = finalPoint.y;
        }

        if (anyBehindCamera)
        {
            box.gameObject.SetActive(false);
            return;
        }

        minX -= padding;
        minY -= padding;
        maxX += padding;
        maxY += padding;

        box.gameObject.SetActive(true);
        box.position = new Vector3(minX, minY, 0f);
        box.sizeDelta = new Vector2(maxX - minX, maxY - minY);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Object Pool
    // ════════════════════════════════════════════════════════════════════════

    private RectTransform GetBoxFromPool()
    {
        if (pool.Count > 0)
        {
            RectTransform recycled = pool.Pop();
            recycled.gameObject.SetActive(true);
            return recycled;
        }

        return CreateBoxElement();
    }

    private void ReturnBoxToPool(RectTransform box)
    {
        box.gameObject.SetActive(false);
        pool.Push(box);
    }

    private RectTransform CreateBoxElement()
    {
        GameObject container = new GameObject("EnemyBox", typeof(RectTransform));
        container.transform.SetParent(transform, false);

        RectTransform rect = container.GetComponent<RectTransform>();
        rect.pivot = Vector2.zero;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;

        CreateBorderEdge(rect, "Top",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0),               new Vector2(0, borderThickness));
        CreateBorderEdge(rect, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -borderThickness), new Vector2(0, borderThickness));
        CreateBorderEdge(rect, "Left",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(-borderThickness, 0), new Vector2(borderThickness, 0));
        CreateBorderEdge(rect, "Right",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 0),               new Vector2(borderThickness, 0));

        return rect;
    }

    private void CreateBorderEdge(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(parent, false);

        RectTransform edgeRect = edge.GetComponent<RectTransform>();
        edgeRect.anchorMin = anchorMin;
        edgeRect.anchorMax = anchorMax;
        edgeRect.offsetMin = offsetMin;
        edgeRect.offsetMax = offsetMax;

        Image img = edge.GetComponent<Image>();
        img.color = boxColor;
        img.raycastTarget = false;
    }

    #endregion
}
