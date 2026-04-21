using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Simple GameManager for prototype.
/// Handles level restart and basic game state.
/// 
/// USAGE:
/// - Place in scene (will persist across reloads)
/// - Call GameManager.Instance.RestartLevel() to restart
/// - Press B key (or your configured key) to restart when dead
/// </summary>
public class GameManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Singleton
    // ════════════════════════════════════════════════════════════════════════

    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        // Singleton pattern with scene reload support
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Subscribe to scene loaded event for cleanup
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Subscribe to pause input action (works even at timeScale = 0)
        if (pauseAction != null && pauseAction.action != null)
        {
            pauseAction.action.performed += OnPauseInput;
            pauseAction.action.Enable();
        }
        else
        {
            Debug.LogWarning("[GameManager] No Pause InputAction assigned! Pause will not work.");
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        // Unsubscribe from pause action
        if (pauseAction != null && pauseAction.action != null)
        {
            pauseAction.action.performed -= OnPauseInput;
        }

        // Clear singleton reference if this is the instance being destroyed
        if (Instance == this)
        {
            Instance = null;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Restart Settings")]
    [Tooltip("Key to restart level (only works when player is dead)")]
    [SerializeField] private KeyCode restartKey = KeyCode.B;

    [Tooltip("Allow restart at any time (for debugging)")]
    [SerializeField] private bool allowRestartAnytime = false;

    [Tooltip("Small delay before reload to let UI update")]
    [SerializeField] private float restartDelay = 0.1f;

    [Header("Main Menu")]
    [Tooltip("Name of the Main Menu scene (must be in Build Settings)")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Tooltip("Key to return to Main Menu")]
    [SerializeField] private KeyCode returnToMenuKey = KeyCode.M;

    [Header("Pause Settings")]
    [Tooltip("Input Action for toggling pause (from InputSystem_Actions)")]
    [SerializeField] private InputActionReference pauseAction;

    [Header("Debug")]
    [SerializeField] private bool showDebugUI = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private bool isRestarting = false;
    private PlayerCore player;

    public bool IsPlayerDead => player != null && player.IsDead;
    public bool IsPaused => TimeManager.Instance.IsPaused;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        FindPlayer();
    }

    private void Update()
    {
        // Don't process other input while paused
        if (IsPaused) return;

        // Check for return to menu input (skip if already in menu)
        bool isInMenu = SceneManager.GetActiveScene().name == mainMenuSceneName;
        if (Input.GetKeyDown(returnToMenuKey) && !isRestarting && !isInMenu)
        {
            ReturnToMainMenu();
        }

        // Check for restart input
        if (Input.GetKeyDown(restartKey) && !isRestarting)
        {
            if (allowRestartAnytime || IsPlayerDead)
            {
                RestartLevel();
            }
        }
    }

    /// <summary>
    /// Called by Input System when Pause action is performed.
    /// Works at any timeScale because Input System callbacks are frame-based.
    /// </summary>
    private void OnPauseInput(InputAction.CallbackContext context)
    {
        TogglePause();
    }

    private void OnGUI()
    {
        if (!showDebugUI) return;

        // Don't show gameplay hints in the main menu
        bool isInMenu = SceneManager.GetActiveScene().name == mainMenuSceneName;
        if (isInMenu) return;

        // Show restart hint when dead
        if (IsPlayerDead && !isRestarting)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            float width = 400;
            float height = 50;
            Rect rect = new Rect(
                (Screen.width - width) / 2,
                Screen.height * 0.6f,
                width,
                height
            );

            // Draw shadow
            GUI.color = Color.black;
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height),
                $"Press [{restartKey}] to Restart", style);

            // Draw text
            GUI.color = Color.white;
            GUI.Label(rect, $"Press [{restartKey}] to Restart", style);
        }

        // Show menu hint (bottom-left corner)
        {
            GUIStyle menuHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.LowerLeft,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            GUI.color = Color.white;
            GUI.Label(
                new Rect(10, Screen.height - 30, 300, 25),
                $"[{returnToMenuKey}] Main Menu", menuHintStyle
            );
        }
    

        // Show pause overlay
        if (IsPaused)
        {
            // Dim background
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            GUIStyle pauseStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            GUI.color = Color.white;
            GUI.Label(
                new Rect(0, Screen.height * 0.35f, Screen.width, 60),
                "PAUSED", pauseStyle
            );

            GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };

            string pauseKeyName = GetPauseKeyDisplayName();
            GUI.Label(
                new Rect(0, Screen.height * 0.45f, Screen.width, 40),
                $"Press [{pauseKeyName}] to Resume", hintStyle
            );
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Pause
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Toggle pause on/off.
    /// </summary>
    public void TogglePause()
    {
        if (IsPaused)
            Unpause();
        else
            Pause();
    }

    /// <summary>
    /// Pause the game. Cursor is unlocked so player can interact with menus.
    /// </summary>
    public void Pause()
    {
        if (IsPaused) return;

        TimeManager.Instance.Pause();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
    }

    /// <summary>
    /// Unpause the game. Cursor is locked again for gameplay.
    /// </summary>
    public void Unpause()
    {
        if (!IsPaused) return;

        TimeManager.Instance.Unpause();

        // Only lock cursor if player is alive
        if (player == null || !player.IsDead)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log("[GameManager] Game resumed.");
    }

    /// <summary>
    /// Returns the display name of the currently bound pause key.
    /// </summary>
    private string GetPauseKeyDisplayName()
    {
        if (pauseAction != null && pauseAction.action != null && pauseAction.action.bindings.Count > 0)
        {
            return pauseAction.action.GetBindingDisplayString(0);
        }
        return "Esc";
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Scene Management
    // ════════════════════════════════════════════════════════════════════════

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        //Debug.Log($"[GameManager] Scene loaded: {scene.name}");

        // Reset state
        isRestarting = false;

        // Find player in new scene
        FindPlayer();

        // Ensure cursor is properly locked for gameplay
        ResetCursorState();

        // Reset all time layers (in case scene loaded while paused/slow-mo)
        TimeManager.Instance.ClearAllLayers();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Restart the current level.
    /// This is the main method to call for a clean restart.
    /// </summary>
    public void RestartLevel()
    {
        if (isRestarting) return;

        Debug.Log("[GameManager] Restarting level...");
        StartCoroutine(RestartLevelCoroutine());
    }

    /// <summary>
    /// Return to the Main Menu scene.
    /// Can be called from anywhere (pause menu, death screen, etc.)
    /// </summary>
    public void ReturnToMainMenu()
    {
        if (isRestarting) return;

        if (string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.LogError("[GameManager] Main Menu scene name is not set!");
            return;
        }

        Debug.Log("[GameManager] Returning to Main Menu...");
        LoadScene(mainMenuSceneName);
    }

    /// <summary>
    /// Load a specific scene by name.
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (isRestarting) return;

        Debug.Log($"[GameManager] Loading scene: {sceneName}");
        StartCoroutine(LoadSceneCoroutine(sceneName));
    }

    /// <summary>
    /// Load a specific scene by build index.
    /// </summary>
    public void LoadScene(int buildIndex)
    {
        if (isRestarting) return;

        Debug.Log($"[GameManager] Loading scene index: {buildIndex}");
        StartCoroutine(LoadSceneCoroutine(buildIndex));
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Methods
    // ════════════════════════════════════════════════════════════════════════

    private IEnumerator RestartLevelCoroutine()
    {
        isRestarting = true;

        // Pre-reload cleanup
        CleanupBeforeReload();

        // Small delay for UI feedback
        yield return new WaitForSecondsRealtime(restartDelay);

        // Reload current scene
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    private IEnumerator LoadSceneCoroutine(string sceneName)
    {
        isRestarting = true;
        CleanupBeforeReload();
        yield return new WaitForSecondsRealtime(restartDelay);
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator LoadSceneCoroutine(int buildIndex)
    {
        isRestarting = true;
        CleanupBeforeReload();
        yield return new WaitForSecondsRealtime(restartDelay);
        SceneManager.LoadScene(buildIndex);
    }

    /// <summary>
    /// Cleanup that must happen BEFORE scene reload.
    /// Add any static variable resets here.
    /// </summary>
    private void CleanupBeforeReload()
    {
        Debug.Log("[GameManager] Running pre-reload cleanup...");

        // Reset all time layers
        TimeManager.Instance.ClearAllLayers();

        // Reset cursor (will be set properly on scene load)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Stop all audio (optional - prevents audio bleeding through)
        AudioListener.pause = true;

        // ══════════════════════════════════════════════════════════════════
        // ADD YOUR STATIC RESETS HERE
        // ══════════════════════════════════════════════════════════════════
        // Example:
        // SomeStaticClass.Reset();
        // YourManager.ClearStaticData();
    }

    private void ResetCursorState()
    {
        // Resume audio
        AudioListener.pause = false;

        // Only lock cursor if player exists and is alive
        if (player != null && !player.IsDead)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void FindPlayer()
    {
        player = FindFirstObjectByType<PlayerCore>();

        if (player == null)
        {
            Debug.LogWarning("[GameManager] No PlayerCore found in scene!");
        }
    }

    #endregion
}
