using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// ════════════════════════════════════════════════════════════════════════════
// NPC OVERLAY MANAGER - Verwaltet Bounding-Box-Overlays für alle NPCs
// ════════════════════════════════════════════════════════════════════════════
//
// KONZEPT:
// - Erstellt automatisch einen Screen-Space-Camera Canvas.
// - Fragt jeden Frame das NpcRegistry nach allen lebenden NPCs.
// - Erstellt/entfernt UI-Elemente (NpcBoundingBoxUI) automatisch.
//
// SETUP:
// 1. Leeres GameObject in der Szene erstellen.
// 2. NpcOverlayManager drauflegen.
// 3. Play drücken → Canvas wird automatisch erstellt.
//
// HINWEIS: Screen Space - Camera wird verwendet, damit Post-Processing
// (z.B. Glitch-Effekte) das UI mit beeinflusst. Wenn du das nicht willst,
// ändere renderMode auf ScreenSpaceOverlay in CreateCanvas().
//
// ════════════════════════════════════════════════════════════════════════════

public class NpcOverlayManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Overlay Einstellungen")]
    [Tooltip("Farbe des Bounding-Box-Rahmens")]
    [SerializeField] private Color frameColor = new Color(1f, 0.2f, 0.2f, 0.8f);

    [Tooltip("Dicke des Rahmens in Pixeln")]
    [SerializeField] private float borderThickness = 2f;

    [Tooltip("Schriftgröße der NPC-Informationen")]
    [SerializeField] private float fontSize = 14f;

    [Header("Canvas Einstellungen")]
    [Tooltip("Sortier-Reihenfolge des Overlay-Canvas")]
    [SerializeField] private int canvasSortOrder = 100;

    [Tooltip("Plane Distance für Screen Space - Camera Modus")]
    [SerializeField] private float planeDistance = 1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime
    // ════════════════════════════════════════════════════════════════════════

    private Canvas overlayCanvas;
    private RectTransform canvasRect;
    private Camera mainCamera;

    // Mapping: NpcBase → zugehöriges UI-Element
    private Dictionary<NpcBase, NpcBoundingBoxUI> activeOverlays = new Dictionary<NpcBase, NpcBoundingBoxUI>();

    // Pool für wiederverwendbare UI-Elemente
    private List<NpcBoundingBoxUI> pool = new List<NpcBoundingBoxUI>();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("[NpcOverlayManager] Keine Main Camera gefunden! " +
                           "Stelle sicher, dass eine Kamera den Tag 'MainCamera' hat.");
            enabled = false;
            return;
        }

        CreateCanvas();
    }

    private void LateUpdate()
    {
        SyncOverlays();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Canvas Setup
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Erstellt den Canvas für die Overlays zur Laufzeit.
    /// Screen Space - Camera damit Post-Processing das UI beeinflusst.
    /// </summary>
    private void CreateCanvas()
    {
        GameObject canvasObj = new GameObject("NpcOverlayCanvas");
        canvasObj.transform.SetParent(transform);

        overlayCanvas = canvasObj.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        overlayCanvas.worldCamera = mainCamera;
        overlayCanvas.planeDistance = planeDistance;
        overlayCanvas.sortingOrder = canvasSortOrder;

        canvasRect = canvasObj.GetComponent<RectTransform>();

        // Canvas Scaler (damit Pixel-Werte konsistent bleiben)
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // Graphic Raycaster ist nicht nötig da wir keine Klicks brauchen,
        // aber Unity meckert manchmal ohne. Kann entfernt werden.
        // canvasObj.AddComponent<GraphicRaycaster>();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Overlay Synchronisierung
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Synchronisiert die UI-Elemente mit den aktuell registrierten NPCs.
    /// Neue NPCs bekommen ein Overlay, tote/entfernte werden zurückgepoolt.
    /// </summary>
    private void SyncOverlays()
    {
        var allNpcs = NpcRegistry.AliveNpcs;

        if (allNpcs == null) return;

        // Schritt 1: Overlays für tote oder entfernte NPCs zurückpoolen
        // (Wir sammeln zuerst die Keys, da wir das Dictionary nicht
        //  während der Iteration ändern dürfen)
        List<NpcBase> toRemove = null;

        foreach (var kvp in activeOverlays)
        {
            NpcBase npc = kvp.Key;
            bool shouldRemove = npc == null || npc.IsDead || !allNpcs.Contains(npc);

            if (shouldRemove)
            {
                if (toRemove == null) toRemove = new List<NpcBase>();
                toRemove.Add(npc);
            }
        }

        if (toRemove != null)
        {
            foreach (var npc in toRemove)
                ReturnToPool(npc);
        }

        // Schritt 2: Neue Overlays für NPCs ohne UI erstellen
        foreach (var npc in allNpcs)
        {
            if (npc == null || npc.IsDead) continue;
            if (activeOverlays.ContainsKey(npc)) continue;

            NpcBoundingBoxUI ui = GetFromPool();
            ui.Initialize(npc, mainCamera, canvasRect, frameColor, borderThickness, fontSize);
            ui.gameObject.SetActive(true);
            activeOverlays[npc] = ui;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Object Pool
    // ════════════════════════════════════════════════════════════════════════

    private NpcBoundingBoxUI GetFromPool()
    {
        // Versuche aus dem Pool zu nehmen
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            NpcBoundingBoxUI ui = pool[i];
            if (ui != null)
            {
                pool.RemoveAt(i);
                return ui;
            }
            // Null-Eintrag entfernen (wurde extern zerstört)
            pool.RemoveAt(i);
        }

        // Neues UI-Element erstellen
        GameObject obj = new GameObject("NpcOverlay");
        obj.transform.SetParent(overlayCanvas.transform, false);

        // RectTransform stretcht über den ganzen Canvas (Standard bei Canvas-Kindern).
        // Wir setzen es explizit, damit die Kinder-Anchors korrekt relativ
        // zur Canvas-Mitte arbeiten.
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt == null) rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        NpcBoundingBoxUI newUI = obj.AddComponent<NpcBoundingBoxUI>();
        return newUI;
    }

    private void ReturnToPool(NpcBase npc)
    {
        if (!activeOverlays.TryGetValue(npc, out NpcBoundingBoxUI ui))
            return;

        activeOverlays.Remove(npc);

        if (ui != null)
        {
            ui.gameObject.SetActive(false);
            pool.Add(ui);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime Settings (Inspector-Änderungen anwenden)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ermöglicht das Ändern der Farbe zur Laufzeit im Inspector.
    /// Wird bei Änderungen im Inspector automatisch aufgerufen.
    /// </summary>
    private void OnValidate()
    {
        if (!Application.isPlaying) return;

        foreach (var kvp in activeOverlays)
        {
            if (kvp.Value != null)
            {
                kvp.Value.SetColor(frameColor);
                kvp.Value.SetFontSize(fontSize);
            }
        }
    }

    #endregion
}
