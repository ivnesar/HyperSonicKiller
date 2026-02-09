using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ════════════════════════════════════════════════════════════════════════════
// ENEMY MARKER MANAGER - Central HUD controller for all enemy markers
// ════════════════════════════════════════════════════════════════════════════
//
// SETUP:
// 1. Create an empty GameObject as child of your HUD Canvas
// 2. Attach this script to it
// 3. Assign the state icon sprites in the Inspector
// 4. Make sure each NPC has an EnemyMarkerTracker component
//
// The manager creates marker UI elements at runtime (no prefab needed).
// Markers are children of this GameObject's RectTransform.
//
// ════════════════════════════════════════════════════════════════════════════

public class EnemyMarkerManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Singleton
    // ════════════════════════════════════════════════════════════════════════

    public static EnemyMarkerManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[EnemyMarkerManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Radial Layout")]
    [Tooltip("Inner radius as percentage of screen height (0-1). Markers inside this circle sit over the NPC.")]
    [Range(0.05f, 0.8f)]
    [SerializeField] private float innerRadiusPercent = 0.30f;

    [Tooltip("Outer radius as percentage of screen height (0-1). Farthest markers are placed here.")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float outerRadiusPercent = 0.45f;

    [Header("Distance Mapping")]
    [Tooltip("NPCs closer than this distance are placed at the inner radius")]
    [SerializeField] private float minNpcDistance = 5f;

    [Tooltip("NPCs farther than this distance are placed at the outer radius")]
    [SerializeField] private float maxNpcDistance = 50f;

    [Header("Marker Scaling")]
    [Tooltip("Scale of markers at the inner radius (close NPCs)")]
    [SerializeField] private float markerScaleClose = 1.0f;

    [Tooltip("Scale of markers at the outer radius (far NPCs)")]
    [SerializeField] private float markerScaleFar = 0.5f;

    [Header("Marker Appearance")]
    [Tooltip("Size of the state icon in pixels")]
    [SerializeField] private Vector2 iconSize = new Vector2(40f, 40f);

    [Tooltip("Font size for the type label")]
    [SerializeField] private float typeFontSize = 14f;

    [Tooltip("Font size for the state label")]
    [SerializeField] private float stateFontSize = 11f;

    [Tooltip("Color of the type label text")]
    [SerializeField] private Color typeLabelColor = Color.white;

    [Tooltip("Color of the state label text")]
    [SerializeField] private Color stateLabelColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private readonly List<EnemyMarkerTracker> trackers = new List<EnemyMarkerTracker>();
    private readonly Dictionary<EnemyMarkerTracker, EnemyMarkerUI> markerMap = new Dictionary<EnemyMarkerTracker, EnemyMarkerUI>();

    private Camera mainCam;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        mainCam = Camera.main;
    }

    private void LateUpdate()
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
            if (mainCam == null) return;
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float innerRadiusPx = Screen.height * innerRadiusPercent;
        float outerRadiusPx = Screen.height * outerRadiusPercent;

        // Update all active markers
        foreach (var kvp in markerMap)
        {
            var tracker = kvp.Key;
            var markerUI = kvp.Value;

            if (tracker == null || markerUI == null)
                continue;

            markerUI.UpdateMarker(
                mainCam,
                screenCenter,
                innerRadiusPx,
                outerRadiusPx,
                minNpcDistance,
                maxNpcDistance,
                markerScaleFar,
                markerScaleClose
            );
        }

        CleanupNullTrackers();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Registration API
    // ════════════════════════════════════════════════════════════════════════

    public void RegisterTracker(EnemyMarkerTracker tracker)
    {
        if (tracker == null || markerMap.ContainsKey(tracker)) return;

        trackers.Add(tracker);

        // Create marker UI element
        var markerUI = CreateMarkerUI(tracker);
        markerMap[tracker] = markerUI;
    }

    public void UnregisterTracker(EnemyMarkerTracker tracker)
    {
        if (tracker == null) return;

        trackers.Remove(tracker);

        if (markerMap.TryGetValue(tracker, out var markerUI))
        {
            if (markerUI != null)
                Destroy(markerUI.gameObject);

            markerMap.Remove(tracker);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Marker Creation (Runtime — no prefab needed)
    // ════════════════════════════════════════════════════════════════════════

    private EnemyMarkerUI CreateMarkerUI(EnemyMarkerTracker tracker)
    {
        // ── Root GameObject ──
        GameObject markerGO = new GameObject($"Marker_{tracker.gameObject.name}");
        markerGO.transform.SetParent(transform, false);

        RectTransform markerRect = markerGO.AddComponent<RectTransform>();
        markerRect.sizeDelta = new Vector2(iconSize.x, iconSize.y + 30f); // icon + labels
        markerRect.pivot = new Vector2(0.5f, 0.5f);

        CanvasGroup cg = markerGO.AddComponent<CanvasGroup>();

        // ── State Icon ──
        GameObject iconGO = new GameObject("StateIcon");
        iconGO.transform.SetParent(markerGO.transform, false);

        RectTransform iconRect = iconGO.AddComponent<RectTransform>();
        iconRect.sizeDelta = iconSize;
        iconRect.anchoredPosition = new Vector2(0f, 10f); // slightly above center

        Image iconImage = iconGO.AddComponent<Image>();
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;

        // ── Type Label (above icon) ──
        GameObject typeLabelGO = new GameObject("TypeLabel");
        typeLabelGO.transform.SetParent(markerGO.transform, false);

        RectTransform typeLabelRect = typeLabelGO.AddComponent<RectTransform>();
        typeLabelRect.sizeDelta = new Vector2(100f, 20f);
        typeLabelRect.anchoredPosition = new Vector2(0f, iconSize.y * 0.5f + 18f);

        TextMeshProUGUI typeText = typeLabelGO.AddComponent<TextMeshProUGUI>();
        typeText.fontSize = typeFontSize;
        typeText.color = typeLabelColor;
        typeText.alignment = TextAlignmentOptions.Center;
        typeText.raycastTarget = false;
        typeText.enableWordWrapping = false;
        typeText.overflowMode = TextOverflowModes.Overflow;

        // ── State Label (below icon) ──
        GameObject stateLabelGO = new GameObject("StateLabel");
        stateLabelGO.transform.SetParent(markerGO.transform, false);

        RectTransform stateLabelRect = stateLabelGO.AddComponent<RectTransform>();
        stateLabelRect.sizeDelta = new Vector2(100f, 18f);
        stateLabelRect.anchoredPosition = new Vector2(0f, -(iconSize.y * 0.5f + 2f));

        TextMeshProUGUI stateText = stateLabelGO.AddComponent<TextMeshProUGUI>();
        stateText.fontSize = stateFontSize;
        stateText.color = stateLabelColor;
        stateText.alignment = TextAlignmentOptions.Center;
        stateText.raycastTarget = false;
        stateText.enableWordWrapping = false;
        stateText.overflowMode = TextOverflowModes.Overflow;

        // ── Assemble EnemyMarkerUI component ──
        EnemyMarkerUI markerUI = markerGO.AddComponent<EnemyMarkerUI>();
        markerUI.stateIcon = iconImage;
        markerUI.typeLabel = typeText;
        markerUI.stateLabel = stateText;
        markerUI.canvasGroup = cg;

        markerUI.Initialize(tracker);

        return markerUI;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Remove entries where the tracker was destroyed without proper unregister
    /// (e.g. scene reload, forced Destroy).
    /// </summary>
    private void CleanupNullTrackers()
    {
        for (int i = trackers.Count - 1; i >= 0; i--)
        {
            if (trackers[i] == null)
            {
                var tracker = trackers[i];
                trackers.RemoveAt(i);

                if (markerMap.TryGetValue(tracker, out var markerUI))
                {
                    if (markerUI != null)
                        Destroy(markerUI.gameObject);
                    markerMap.Remove(tracker);
                }
            }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw inner/outer radius circles in Scene view (approximation)
        if (!Application.isPlaying) return;

        // This is just a Scene view hint — the actual circles are screen-space
        UnityEditor.Handles.color = new Color(0f, 1f, 0f, 0.3f);
        UnityEditor.Handles.Label(
            transform.position,
            $"Markers: {trackers.Count} | Inner: {innerRadiusPercent:P0} | Outer: {outerRadiusPercent:P0}"
        );
    }
#endif

    #endregion
}
