using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ════════════════════════════════════════════════════════════════════════════
// ENEMY MARKER UI - Individual marker UI element for one NPC
// ════════════════════════════════════════════════════════════════════════════
//
// Created and managed by EnemyMarkerManager.
// Each frame receives data from an EnemyMarkerTracker and positions itself
// either over the NPC (inside inner radius) or clamped to the radial band
// between inner and outer radius (based on distance to player).
//
// ════════════════════════════════════════════════════════════════════════════

public class EnemyMarkerUI : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region UI References (set by Manager on creation)
    // ════════════════════════════════════════════════════════════════════════

    [Header("UI Elements")]
    public Image stateIcon;
    public TextMeshProUGUI typeLabel;
    public TextMeshProUGUI stateLabel;
    public CanvasGroup canvasGroup;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private RectTransform rectTransform;
    private EnemyMarkerTracker tracker;
    private MarkerState lastState = MarkerState.Idle;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public EnemyMarkerTracker Tracker => tracker;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Initialization
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    /// <summary>
    /// Called by EnemyMarkerManager after instantiation.
    /// </summary>
    public void Initialize(EnemyMarkerTracker target)
    {
        tracker = target;
        lastState = (MarkerState)(-1); // Force icon update on first frame
        UpdateTypeLabel();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Per-Frame Update (called by Manager)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called every frame by the manager. Handles positioning and visual updates.
    /// </summary>
    /// <param name="screenCenter">Screen center in pixels</param>
    /// <param name="innerRadiusPx">Inner radius in pixels (close NPCs)</param>
    /// <param name="outerRadiusPx">Outer radius in pixels (far NPCs)</param>
    /// <param name="minNpcDistance">World distance considered "close"</param>
    /// <param name="maxNpcDistance">World distance considered "far"</param>
    /// <param name="minScale">Scale for markers at outer radius</param>
    /// <param name="maxScale">Scale for markers at inner radius</param>
    /// <param name="stateIcons">Icon sprites indexed by MarkerState</param>
    public void UpdateMarker(
        Camera cam,
        Vector2 screenCenter,
        float innerRadiusPx,
        float outerRadiusPx,
        float minNpcDistance,
        float maxNpcDistance,
        float minScale,
        float maxScale)
    {
        if (tracker == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // ── Update icon if state changed ──
        MarkerState currentState = tracker.CurrentMarkerState;
        if (currentState != lastState)
        {
            lastState = currentState;
            UpdateStateIcon(tracker.StateIcons);
            UpdateStateLabel();
        }

        // ── Calculate screen position ──
        Vector3 worldPos = tracker.MarkerWorldPosition;
        Vector3 viewportPos = cam.WorldToViewportPoint(worldPos);

        // NPC is behind the camera
        bool isBehindCamera = viewportPos.z < 0f;

        // Convert to screen-space pixel position
        Vector2 screenPos;
        if (isBehindCamera)
        {
            // Flip to get correct direction when behind camera
            screenPos = new Vector2(
                (1f - viewportPos.x) * Screen.width,
                (1f - viewportPos.y) * Screen.height
            );
        }
        else
        {
            screenPos = new Vector2(
                viewportPos.x * Screen.width,
                viewportPos.y * Screen.height
            );
        }

        // ── Determine if inside inner radius ──
        Vector2 offsetFromCenter = screenPos - screenCenter;
        float distFromCenter = offsetFromCenter.magnitude;

        // NPC distance from player (world space) — used for radius band interpolation
        float npcDistance = tracker.DistanceToPlayer;
        float distanceT = Mathf.InverseLerp(minNpcDistance, maxNpcDistance, npcDistance);
        distanceT = Mathf.Clamp01(distanceT);

        if (!isBehindCamera && distFromCenter <= innerRadiusPx)
        {
            // ── INSIDE inner radius: marker sits directly over the NPC ──
            rectTransform.position = screenPos;
            transform.localScale = Vector3.one * maxScale;
        }
        else
        {
            // ── OUTSIDE inner radius (or behind camera): clamp to radial band ──
            Vector2 direction = offsetFromCenter.normalized;

            // If NPC is exactly at center (edge case), push in a default direction
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector2.up;

            // Interpolate radius position based on NPC world distance
            float targetRadius = Mathf.Lerp(innerRadiusPx, outerRadiusPx, distanceT);
            Vector2 clampedPos = screenCenter + direction * targetRadius;

            rectTransform.position = clampedPos;

            // Scale down for distant NPCs
            float scale = Mathf.Lerp(maxScale, minScale, distanceT);
            transform.localScale = Vector3.one * scale;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Visual Updates
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateTypeLabel()
    {
        if (typeLabel == null || tracker == null) return;
        typeLabel.text = tracker.Type.ToString();
    }

    private void UpdateStateLabel()
    {
        if (stateLabel == null) return;
        stateLabel.text = lastState.ToString();
    }

    private void UpdateStateIcon(Sprite[] stateIcons)
    {
        if (stateIcon == null || stateIcons == null) return;

        int index = (int)lastState;
        if (index >= 0 && index < stateIcons.Length && stateIcons[index] != null)
        {
            stateIcon.sprite = stateIcons[index];
            stateIcon.enabled = true;
        }
        else
        {
            // No icon assigned for this state — hide the image
            stateIcon.enabled = false;
        }
    }

    #endregion
}
