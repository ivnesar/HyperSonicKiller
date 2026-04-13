using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ════════════════════════════════════════════════════════════════════════════
// ENEMY MARKER UI - Individual marker UI element for one NPC
// ════════════════════════════════════════════════════════════════════════════
//
// DUAL MODE:
// - PRIMARY: Full marker (state icon + type label + state label) positioned
//   directly over the NPC. Active when the NPC is on-screen and inside
//   the inner radius.
// - SECONDARY: Compact icon only, positioned on a fixed outer ring in the
//   direction of the NPC. Active when the NPC is outside the inner radius
//   or behind the camera.
//
// Only one mode is active at a time (hard switch, no fade).
//
// ════════════════════════════════════════════════════════════════════════════

public class EnemyMarkerUI : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region UI References (set by Manager on creation)
    // ════════════════════════════════════════════════════════════════════════

    [Header("Primary Elements")]
    public Image stateIcon;
    public TextMeshProUGUI typeLabel;
    public TextMeshProUGUI stateLabel;

    [Header("Secondary Element")]
    public Image secondaryIcon;

    [Header("Shared")]
    public CanvasGroup canvasGroup;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private RectTransform rectTransform;
    private EnemyMarkerTracker tracker;
    private MarkerState lastState = MarkerState.Idle;

    /// <summary>Whether the primary elements are currently visible.</summary>
    private bool primaryActive = true;

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
    /// Called every frame by the manager. Decides between primary and
    /// secondary mode, then positions the marker accordingly.
    /// </summary>
    public void UpdateMarker(
        Camera cam,
        Vector2 screenCenter,
        float innerRadiusPx,
        float secondaryRingPx)
    {
        if (tracker == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // ── Update icons if state changed ──
        MarkerState currentState = tracker.CurrentMarkerState;
        if (currentState != lastState)
        {
            lastState = currentState;
            UpdateStateIcon(tracker.StateIcons);
            UpdateSecondaryIcon(tracker.SecondaryStateIcons);
            UpdateStateLabel();
        }

        // ── Calculate screen position ──
        Vector3 worldPos = tracker.MarkerWorldPosition;
        Vector3 viewportPos = cam.WorldToViewportPoint(worldPos);
        bool isBehindCamera = viewportPos.z < 0f;

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

        Vector2 offsetFromCenter = screenPos - screenCenter;
        float distFromCenter = offsetFromCenter.magnitude;

        // ── Decide: primary or secondary? ──
        bool usePrimary = !isBehindCamera && distFromCenter <= innerRadiusPx;

        if (usePrimary)
        {
            // PRIMARY: full marker directly over the NPC
            SetPrimaryMode(true);
            rectTransform.position = screenPos;
        }
        else
        {
            // SECONDARY: compact icon on the fixed outer ring
            SetPrimaryMode(false);

            Vector2 direction = offsetFromCenter.normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector2.up;

            Vector2 ringPos = screenCenter + direction * secondaryRingPx;
            rectTransform.position = ringPos;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Mode Switching
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Switches between primary (full) and secondary (compact) display.
    /// Only updates GameObjects when the mode actually changes.
    /// </summary>
    private void SetPrimaryMode(bool showPrimary)
    {
        if (showPrimary == primaryActive) return;

        primaryActive = showPrimary;

        // Primary elements
        if (stateIcon != null) stateIcon.gameObject.SetActive(showPrimary);
        if (typeLabel != null) typeLabel.gameObject.SetActive(showPrimary);
        if (stateLabel != null) stateLabel.gameObject.SetActive(showPrimary);

        // Secondary element
        if (secondaryIcon != null) secondaryIcon.gameObject.SetActive(!showPrimary);
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
            stateIcon.enabled = false;
        }
    }

    private void UpdateSecondaryIcon(Sprite[] stateIcons)
    {
        if (secondaryIcon == null || stateIcons == null) return;

        int index = (int)lastState;
        if (index >= 0 && index < stateIcons.Length && stateIcons[index] != null)
        {
            secondaryIcon.sprite = stateIcons[index];
            secondaryIcon.enabled = true;
        }
        else
        {
            secondaryIcon.enabled = false;
        }
    }

    #endregion
}
