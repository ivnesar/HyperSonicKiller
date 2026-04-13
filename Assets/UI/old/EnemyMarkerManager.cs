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
// 3. Make sure each NPC has an EnemyMarkerTracker component
//
// DUAL MARKER SYSTEM:
// - Primary marker: Full marker (icon + labels), shown when NPC is
//   on-screen and within the inner radius.
// - Secondary marker: Compact icon only, shown on a fixed outer ring
//   when the NPC is outside the inner radius or behind the camera.
//   Points in the direction of the NPC.
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

    [Header("Primary Marker — Radius")]
    [Tooltip("Inner radius as percentage of screen height (0-1). " +
             "NPCs inside this circle get the full primary marker over them.")]
    [Range(0.05f, 0.8f)]
    [SerializeField] private float innerRadiusPercent = 0.30f;

    [Header("Secondary Marker — Ring")]
    [Tooltip("Fixed ring radius as percentage of screen height (0-1). " +
             "Compact markers are placed on this ring when the NPC is outside the inner radius.")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float secondaryRingPercent = 0.45f;

    [Tooltip("Size of the secondary (compact) icon in pixels.")]
    [SerializeField] private Vector2 secondaryIconSize = new Vector2(20f, 20f);

    [Header("Primary Marker — Appearance")]
    [Tooltip("Size of the primary state icon in pixels.")]
    [SerializeField] private Vector2 iconSize = new Vector2(40f, 40f);

    [Tooltip("Font size for the type label.")]
    [SerializeField] private float typeFontSize = 14f;

    [Tooltip("Font size for the state label.")]
    [SerializeField] private float stateFontSize = 11f;

    [Tooltip("Color of the type label text.")]
    [SerializeField] private Color typeLabelColor = Color.white;

    [Tooltip("Color of the state label text.")]
    [SerializeField] private Color stateLabelColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private readonly List<EnemyMarkerTracker> trackers = new List<EnemyMarkerTracker>();
    private readonly Dictionary<EnemyMarkerTracker, EnemyMarkerUI> markerMap
        = new Dictionary<EnemyMarkerTracker, EnemyMarkerUI>();

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
        float secondaryRingPx = Screen.height * secondaryRingPercent;

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
                secondaryRingPx
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
        markerRect.sizeDelta = new Vector2(iconSize.x, iconSize.y + 30f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);

        CanvasGroup cg = markerGO.AddComponent<CanvasGroup>();

        // ────────────────────────────────────────────────────────────────
        // PRIMARY elements (icon + two labels)
        // ────────────────────────────────────────────────────────────────

        GameObject iconGO = new GameObject("StateIcon");
        iconGO.transform.SetParent(markerGO.transform, false);

        RectTransform iconRect = iconGO.AddComponent<RectTransform>();
        iconRect.sizeDelta = iconSize;
        iconRect.anchoredPosition = new Vector2(0f, 10f);

        Image iconImage = iconGO.AddComponent<Image>();
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;

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

        // ────────────────────────────────────────────────────────────────
        // SECONDARY element (compact icon only)
        // ────────────────────────────────────────────────────────────────

        GameObject secondaryIconGO = new GameObject("SecondaryIcon");
        secondaryIconGO.transform.SetParent(markerGO.transform, false);

        RectTransform secIconRect = secondaryIconGO.AddComponent<RectTransform>();
        secIconRect.sizeDelta = secondaryIconSize;
        secIconRect.anchoredPosition = Vector2.zero;

        Image secondaryImage = secondaryIconGO.AddComponent<Image>();
        secondaryImage.raycastTarget = false;
        secondaryImage.preserveAspect = true;

        // Starts hidden — only shown when outside inner radius
        secondaryIconGO.SetActive(false);

        // ────────────────────────────────────────────────────────────────
        // Assemble EnemyMarkerUI component
        // ────────────────────────────────────────────────────────────────

        EnemyMarkerUI markerUI = markerGO.AddComponent<EnemyMarkerUI>();
        markerUI.stateIcon = iconImage;
        markerUI.typeLabel = typeText;
        markerUI.stateLabel = stateText;
        markerUI.canvasGroup = cg;
        markerUI.secondaryIcon = secondaryImage;

        markerUI.Initialize(tracker);

        return markerUI;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cleanup
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Remove entries where the tracker was destroyed without proper unregister.
    /// </summary>
    private void CleanupNullTrackers()
    {
        // Clean up the list
        for (int i = trackers.Count - 1; i >= 0; i--)
        {
            if (trackers[i] == null)
                trackers.RemoveAt(i);
        }

        // Clean up dictionary entries whose key (tracker) was destroyed.
        // A destroyed UnityEngine.Object becomes a "fake null" — the reference
        // still exists as a dictionary key but compares equal to null.
        List<EnemyMarkerTracker> deadKeys = null;

        foreach (var kvp in markerMap)
        {
            if (kvp.Key == null)
            {
                deadKeys ??= new List<EnemyMarkerTracker>();
                deadKeys.Add(kvp.Key);
            }
        }

        if (deadKeys == null) return;

        foreach (var key in deadKeys)
        {
            if (markerMap.TryGetValue(key, out var markerUI) && markerUI != null)
                Destroy(markerUI.gameObject);

            markerMap.Remove(key);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        UnityEditor.Handles.color = new Color(0f, 1f, 0f, 0.3f);
        UnityEditor.Handles.Label(
            transform.position,
            $"Markers: {trackers.Count} | Inner: {innerRadiusPercent:P0} | Ring: {secondaryRingPercent:P0}"
        );
    }
#endif

    #endregion
}
