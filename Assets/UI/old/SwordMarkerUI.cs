using UnityEngine;
using UnityEngine.UI;

// ════════════════════════════════════════════════════════════════════════════
// SWORD MARKER UI - HUD marker for the thrown sword
// ════════════════════════════════════════════════════════════════════════════
//
// Shows a single icon over the sword's position when it is stuck in a
// surface (enemy or environment). When the sword is off-screen or behind
// the camera, the icon is placed on a fixed outer ring pointing in the
// sword's direction — identical behaviour to the NPC secondary markers.
//
// SETUP:
// 1. Create an empty GameObject as child of your HUD Canvas
// 2. Attach this script to it
// 3. Assign the icon texture in the Inspector
// 4. The sword registers itself via SwordMarkerUI.Instance.SetSword()
//
// ════════════════════════════════════════════════════════════════════════════

public class SwordMarkerUI : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Singleton
    // ════════════════════════════════════════════════════════════════════════

    public static SwordMarkerUI Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SwordMarkerUI] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CreateIconElement();
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

    [Header("Icon")]
    [Tooltip("The icon texture displayed for the sword marker.")]
    [SerializeField] private Texture2D iconTexture;

    [Tooltip("Size of the icon in pixels.")]
    [SerializeField] private Vector2 iconSize = new Vector2(32f, 32f);

    [Header("Position")]
    [Tooltip("World-space offset above the sword's position.")]
    [SerializeField] private Vector3 anchorOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Ring (off-screen)")]
    [Tooltip("Ring radius as percentage of screen height (0-1). " +
             "The icon is placed on this ring when the sword is off-screen.")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float ringRadiusPercent = 0.45f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private ThrownSword trackedSword;
    private Camera mainCam;

    // UI elements (created at runtime)
    private GameObject iconGO;
    private RectTransform iconRect;
    private Image iconImage;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by the sword when it spawns. Pass null to clear.
    /// </summary>
    public void SetSword(ThrownSword sword)
    {
        trackedSword = sword;
    }

    /// <summary>
    /// Clears the tracked sword reference. Called when the sword is
    /// destroyed or returned to the player.
    /// </summary>
    public void ClearSword()
    {
        trackedSword = null;
        SetIconVisible(false);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        mainCam = Camera.main;
        SetIconVisible(false);
    }

    private void LateUpdate()
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
            if (mainCam == null) return;
        }

        // ── Should the marker be visible? ──
        // Only show when a sword exists and is stuck somewhere.
        if (trackedSword == null || !trackedSword.IsStuck)
        {
            SetIconVisible(false);
            return;
        }

        SetIconVisible(true);
        UpdatePosition();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Icon Creation (Runtime — no prefab needed)
    // ════════════════════════════════════════════════════════════════════════

    private void CreateIconElement()
    {
        iconGO = new GameObject("SwordIcon");
        iconGO.transform.SetParent(transform, false);

        iconRect = iconGO.AddComponent<RectTransform>();
        iconRect.sizeDelta = iconSize;
        iconRect.pivot = new Vector2(0.5f, 0.5f);

        iconImage = iconGO.AddComponent<Image>();
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;

        if (iconTexture != null)
        {
            iconImage.sprite = Sprite.Create(
                iconTexture,
                new Rect(0, 0, iconTexture.width, iconTexture.height),
                new Vector2(0.5f, 0.5f)
            );
        }

        iconGO.SetActive(false);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Positioning
    // ════════════════════════════════════════════════════════════════════════

    private void UpdatePosition()
    {
        Vector3 worldPos = trackedSword.transform.position + anchorOffset;
        Vector3 viewportPos = mainCam.WorldToViewportPoint(worldPos);
        bool isBehindCamera = viewportPos.z < 0f;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

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

        // ── Check if on-screen ──
        bool isOnScreen = !isBehindCamera
            && viewportPos.x > 0f && viewportPos.x < 1f
            && viewportPos.y > 0f && viewportPos.y < 1f;

        if (isOnScreen)
        {
            // Directly over the sword
            iconRect.position = screenPos;
        }
        else
        {
            // Place on the outer ring, pointing towards the sword
            Vector2 direction = (screenPos - screenCenter).normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector2.up;

            float ringPx = Screen.height * ringRadiusPercent;
            iconRect.position = screenCenter + direction * ringPx;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    private void SetIconVisible(bool visible)
    {
        if (iconGO != null && iconGO.activeSelf != visible)
            iconGO.SetActive(visible);
    }

    #endregion
}
