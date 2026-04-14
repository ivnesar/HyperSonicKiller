using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main Menu controller.
/// Handles level selection buttons, difficulty toggle buttons,
/// and level preview images on hover.
/// 
/// SETUP:
/// 1. Create a Canvas in your MainMenu scene.
/// 2. Add this script to an empty GameObject (e.g. "MenuController").
/// 3. Assign the DifficultySettings asset in the inspector.
/// 4. Set up your Level Entries (see LevelEntry below).
/// 5. Create 3 Buttons for difficulty and assign them in the inspector.
/// 6. Assign your existing Image component as the Preview Image.
/// 7. For each LevelEntry, assign a preview sprite (optional – can be added later).
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Level Entry
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pairs a UI Button with a scene name and an optional preview image.
    /// </summary>
    [System.Serializable]
    public class LevelEntry
    {
        [Tooltip("Name shown on the button")]
        public string displayName = "Level 1";

        [Tooltip("Exact scene name as it appears in Build Settings")]
        public string sceneName = "";

        [Tooltip("The UI Button for this level (assign in inspector)")]
        public Button button;

        [Tooltip("Preview image for this level (can be left empty for now)")]
        public Sprite previewSprite;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Difficulty")]
    [Tooltip("Drag your DifficultySettings asset here")]
    [SerializeField] private DifficultySettings difficultySettings;

    [Header("Difficulty Buttons")]
    [Tooltip("Button for Easy difficulty")]
    [SerializeField] private Button easyButton;

    [Tooltip("Button for Medium difficulty")]
    [SerializeField] private Button mediumButton;

    [Tooltip("Button for Hard difficulty")]
    [SerializeField] private Button hardButton;

    [Header("Difficulty Button Colors")]
    [Tooltip("Color for the active difficulty button")]
    [SerializeField] private Color activeColor = Color.white;

    [Tooltip("Color for inactive (greyed out) difficulty buttons")]
    [SerializeField] private Color inactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Level Buttons")]
    [Tooltip("Add one entry per level (including Tutorial). " +
             "The FIRST entry is used as the default preview image on scene start.")]
    [SerializeField] private LevelEntry[] levels;

    [Header("Level Preview")]
    [Tooltip("The UI Image component that displays the level preview. " +
             "Assign your existing Image component here.")]
    [SerializeField] private Image previewImage;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        // Make sure cursor is visible in the menu
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Make sure time is running (in case we came back from a paused level)
        Time.timeScale = 1f;

        SetupDifficultyButtons();
        SetupLevelButtons();

        // Apply the current difficulty from the asset (default or last selected)
        UpdateDifficultyVisuals(difficultySettings.CurrentDifficulty);

        // Show the default preview (first entry = Tutorial)
        ShowDefaultPreview();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Difficulty
    // ════════════════════════════════════════════════════════════════════════

    private void SetupDifficultyButtons()
    {
        easyButton.onClick.AddListener(() => SelectDifficulty(DifficultySettings.Difficulty.Easy));
        mediumButton.onClick.AddListener(() => SelectDifficulty(DifficultySettings.Difficulty.Medium));
        hardButton.onClick.AddListener(() => SelectDifficulty(DifficultySettings.Difficulty.Hard));
    }

    /// <summary>
    /// Called when a difficulty button is clicked.
    /// Stores the choice in the ScriptableObject and updates button visuals.
    /// </summary>
    private void SelectDifficulty(DifficultySettings.Difficulty difficulty)
    {
        difficultySettings.CurrentDifficulty = difficulty;
        UpdateDifficultyVisuals(difficulty);
    }

    /// <summary>
    /// Highlights the active button and greys out the others.
    /// </summary>
    private void UpdateDifficultyVisuals(DifficultySettings.Difficulty activeDifficulty)
    {
        SetButtonColor(easyButton, activeDifficulty == DifficultySettings.Difficulty.Easy);
        SetButtonColor(mediumButton, activeDifficulty == DifficultySettings.Difficulty.Medium);
        SetButtonColor(hardButton, activeDifficulty == DifficultySettings.Difficulty.Hard);
    }

    /// <summary>
    /// Sets a button's color to active or inactive.
    /// Works by tinting the button's ColorBlock.
    /// </summary>
    private void SetButtonColor(Button button, bool isActive)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = isActive ? activeColor : inactiveColor;
        colors.highlightedColor = isActive ? activeColor : inactiveColor;
        colors.selectedColor = isActive ? activeColor : inactiveColor;
        button.colors = colors;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Level Loading
    // ════════════════════════════════════════════════════════════════════════

    private void SetupLevelButtons()
    {
        foreach (LevelEntry level in levels)
        {
            if (level.button == null)
            {
                Debug.LogWarning($"[MainMenuUI] Level '{level.displayName}' has no button assigned!");
                continue;
            }

            // Set the button label text
            Text buttonText = level.button.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = level.displayName;
            }

            // Register click listener (capture sceneName for the lambda)
            string sceneToLoad = level.sceneName;
            level.button.onClick.AddListener(() => LoadLevel(sceneToLoad));

            // Register hover events for preview image
            SetupHoverEvents(level);
        }
    }

    private void LoadLevel(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[MainMenuUI] Scene name is empty! Check your Level Entries.");
            return;
        }

        Debug.Log($"[MainMenuUI] Loading level: {sceneName} " +
                  $"(Difficulty: {difficultySettings.CurrentDifficulty})");

        SceneManager.LoadScene(sceneName);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Preview Image
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shows the first level entry's preview as default (Tutorial).
    /// </summary>
    private void ShowDefaultPreview()
    {
        if (previewImage == null) return;

        if (levels != null && levels.Length > 0 && levels[0].previewSprite != null)
        {
            previewImage.sprite = levels[0].previewSprite;
            previewImage.color = Color.white;
        }
        else
        {
            // No sprite assigned yet – make the image transparent
            previewImage.sprite = null;
            previewImage.color = Color.clear;
        }
    }

    /// <summary>
    /// Adds a PointerEnter event to a button so hovering shows its preview.
    /// No PointerExit handler – the last hovered image stays visible
    /// until another button is hovered.
    /// </summary>
    private void SetupHoverEvents(LevelEntry level)
    {
        if (previewImage == null) return;

        // Add an EventTrigger component if the button doesn't have one
        EventTrigger trigger = level.button.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = level.button.gameObject.AddComponent<EventTrigger>();
        }

        // PointerEnter → show this level's preview
        EventTrigger.Entry enterEntry = new EventTrigger.Entry();
        enterEntry.eventID = EventTriggerType.PointerEnter;

        // Capture the sprite for the lambda
        Sprite sprite = level.previewSprite;
        enterEntry.callback.AddListener((_) => ShowPreview(sprite));

        trigger.triggers.Add(enterEntry);
    }

    /// <summary>
    /// Updates the preview image to the given sprite.
    /// If sprite is null (not yet assigned), shows nothing.
    /// </summary>
    private void ShowPreview(Sprite sprite)
    {
        if (previewImage == null) return;

        if (sprite != null)
        {
            previewImage.sprite = sprite;
            previewImage.color = Color.white;
        }
        else
        {
            previewImage.sprite = null;
            previewImage.color = Color.clear;
        }
    }

    #endregion
}
