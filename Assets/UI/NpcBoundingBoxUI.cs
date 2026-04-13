using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ════════════════════════════════════════════════════════════════════════════
// NPC BOUNDING BOX UI - Zeigt Rahmen + Info-Text über einem NPC
// ════════════════════════════════════════════════════════════════════════════
//
// KONZEPT:
// - Projiziert die 8 Ecken der Welt-Bounding-Box auf den Screen.
// - Rechnet Screen-Pixel → Canvas-lokale Koordinaten um via
//   RectTransformUtility.ScreenPointToLocalPointInRectangle().
// - Positioniert 4 Border-Images als Rahmen + TMPro-Text darüber.
//
// KOORDINATEN:
// - Alle UI-Elemente sind direkte Kinder des Canvas (über das
//   Overlay-GameObject). Anchor (0.5, 0.5) = Canvas-Mitte, passend
//   zum Koordinatensystem von ScreenPointToLocalPointInRectangle.
// - Pivot (0, 0) = anchoredPosition bezieht sich auf die untere
//   linke Ecke des Elements.
//
// USAGE:
// - Wird von NpcOverlayManager automatisch erstellt und zugewiesen.
// - Nicht manuell auf GameObjects legen.
//
// ════════════════════════════════════════════════════════════════════════════

public class NpcBoundingBoxUI : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region References
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase targetNpc;
    private Camera mainCamera;
    private RectTransform canvasRect;

    // Alle UI-Elemente sind direkte Kinder von diesem GameObject.
    // Keine verschachtelte Hierarchie — damit anchoredPosition
    // direkt in Canvas-lokalen Koordinaten arbeitet.
    private Image topBorder;
    private Image bottomBorder;
    private Image leftBorder;
    private Image rightBorder;
    private TextMeshProUGUI infoText;

    // Cached RectTransforms (vermeidet GetComponent pro Frame)
    private RectTransform topRT, bottomRT, leftRT, rightRT;
    private RectTransform textRT;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Settings
    // ════════════════════════════════════════════════════════════════════════

    private Color frameColor = Color.red;
    private float borderThickness = 2f;
    private float fontSize = 14f;
    private float textOffset = 4f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Initialization
    // ════════════════════════════════════════════════════════════════════════

    public void Initialize(NpcBase npc, Camera camera, RectTransform canvasRectTransform,
                           Color color, float thickness, float textSize)
    {
        targetNpc = npc;
        mainCamera = camera;
        canvasRect = canvasRectTransform;
        frameColor = color;
        borderThickness = thickness;
        fontSize = textSize;

        BuildUI();
    }

    private void BuildUI()
    {
        // Alle Elemente direkt als Kinder dieses GameObjects.
        // Dieses GameObject selbst ist ein Kind des Canvas.
        // → anchoredPosition = Canvas-lokale Koordinaten.
        topBorder = CreateBorder("Top", out topRT);
        bottomBorder = CreateBorder("Bottom", out bottomRT);
        leftBorder = CreateBorder("Left", out leftRT);
        rightBorder = CreateBorder("Right", out rightRT);

        // Info-Text
        GameObject textObj = new GameObject("InfoText");
        textObj.transform.SetParent(transform, false);
        infoText = textObj.AddComponent<TextMeshProUGUI>();
        infoText.fontSize = fontSize;
        infoText.color = frameColor;
        infoText.alignment = TextAlignmentOptions.BottomLeft;
        infoText.enableWordWrapping = false;
        infoText.overflowMode = TextOverflowModes.Overflow;
        infoText.raycastTarget = false;

        textRT = infoText.GetComponent<RectTransform>();
        SetAnchorCenter(textRT);
    }

    private Image CreateBorder(string name, out RectTransform rt)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(transform, false);
        Image img = obj.AddComponent<Image>();
        img.color = frameColor;
        img.raycastTarget = false;

        rt = img.GetComponent<RectTransform>();
        SetAnchorCenter(rt);

        return img;
    }

    /// <summary>
    /// Anchor = Canvas-Mitte (0.5, 0.5), Pivot = unten-links (0, 0).
    /// So stimmt anchoredPosition mit den Werten von
    /// ScreenPointToLocalPointInRectangle überein.
    /// </summary>
    private void SetAnchorCenter(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0f);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Update
    // ════════════════════════════════════════════════════════════════════════

    private void LateUpdate()
    {
        if (targetNpc == null || targetNpc.IsDead)
        {
            gameObject.SetActive(false);
            return;
        }

        Renderer boundsRenderer = targetNpc.BoundsRenderer;
        if (boundsRenderer == null)
        {
            gameObject.SetActive(false);
            return;
        }

        Bounds worldBounds = boundsRenderer.bounds;

        if (!WorldBoundsToCanvasRect(worldBounds, out Rect rect))
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        UpdateFrame(rect);
        UpdateInfoText(rect);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Bounding Box Berechnung
    // ════════════════════════════════════════════════════════════════════════

    private bool WorldBoundsToCanvasRect(Bounds bounds, out Rect result)
    {
        result = Rect.zero;

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3[] corners = new Vector3[8];
        corners[0] = center + new Vector3(-extents.x, -extents.y, -extents.z);
        corners[1] = center + new Vector3(-extents.x, -extents.y,  extents.z);
        corners[2] = center + new Vector3(-extents.x,  extents.y, -extents.z);
        corners[3] = center + new Vector3(-extents.x,  extents.y,  extents.z);
        corners[4] = center + new Vector3( extents.x, -extents.y, -extents.z);
        corners[5] = center + new Vector3( extents.x, -extents.y,  extents.z);
        corners[6] = center + new Vector3( extents.x,  extents.y, -extents.z);
        corners[7] = center + new Vector3( extents.x,  extents.y,  extents.z);

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        bool anyVisible = false;

        for (int i = 0; i < 8; i++)
        {
            Vector3 screenPoint = mainCamera.WorldToScreenPoint(corners[i]);

            if (screenPoint.z < 0) continue;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                new Vector2(screenPoint.x, screenPoint.y),
                mainCamera,
                out Vector2 localPoint
            );

            anyVisible = true;
            if (localPoint.x < minX) minX = localPoint.x;
            if (localPoint.x > maxX) maxX = localPoint.x;
            if (localPoint.y < minY) minY = localPoint.y;
            if (localPoint.y > maxY) maxY = localPoint.y;
        }

        if (!anyVisible) return false;

        result = new Rect(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Frame & Text Positionierung
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateFrame(Rect rect)
    {
        float x = rect.x;
        float y = rect.y;
        float w = rect.width;
        float h = rect.height;
        float t = borderThickness;

        // Top
        topRT.anchoredPosition = new Vector2(x, y + h - t);
        topRT.sizeDelta = new Vector2(w, t);

        // Bottom
        bottomRT.anchoredPosition = new Vector2(x, y);
        bottomRT.sizeDelta = new Vector2(w, t);

        // Left
        leftRT.anchoredPosition = new Vector2(x, y);
        leftRT.sizeDelta = new Vector2(t, h);

        // Right
        rightRT.anchoredPosition = new Vector2(x + w - t, y);
        rightRT.sizeDelta = new Vector2(t, h);
    }

    private void UpdateInfoText(Rect rect)
    {
        string npcName = targetNpc.DisplayName;
        string npcType = targetNpc.GetNpcType().ToString();
        int hp = targetNpc.CurrentHealth;
        int maxHp = targetNpc.MaxHealth;
        string state = targetNpc.GetCurrentStateName();

        infoText.text = $"{npcName}  [{npcType}]\nHP: {hp}/{maxHp}  |  {state}";
        textRT.anchoredPosition = new Vector2(rect.x, rect.yMax + textOffset);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    private void SetVisible(bool visible)
    {
        // Statt einzelne Elemente zu togglen, togglen wir das ganze GameObject.
        // LateUpdate prüft ohnehin ob der NPC noch lebt.
        if (topBorder != null) topBorder.enabled = visible;
        if (bottomBorder != null) bottomBorder.enabled = visible;
        if (leftBorder != null) leftBorder.enabled = visible;
        if (rightBorder != null) rightBorder.enabled = visible;
        if (infoText != null) infoText.enabled = visible;
    }

    public void SetColor(Color color)
    {
        frameColor = color;
        if (topBorder != null) topBorder.color = color;
        if (bottomBorder != null) bottomBorder.color = color;
        if (leftBorder != null) leftBorder.color = color;
        if (rightBorder != null) rightBorder.color = color;
        if (infoText != null) infoText.color = color;
    }

    public void SetFontSize(float size)
    {
        fontSize = size;
        if (infoText != null) infoText.fontSize = size;
    }

    #endregion
}
