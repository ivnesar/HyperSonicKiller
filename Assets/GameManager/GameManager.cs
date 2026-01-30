using UnityEngine;
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
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

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

    [Header("Debug")]
    [SerializeField] private bool showDebugUI = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private bool isRestarting = false;
    private PlayerCore player;

    public bool IsPlayerDead => player != null && player.IsDead;

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
        // Check for restart input
        if (Input.GetKeyDown(restartKey) && !isRestarting)
        {
            if (allowRestartAnytime || IsPlayerDead)
            {
                RestartLevel();
            }
        }
    }

    private void OnGUI()
    {
        if (!showDebugUI) return;

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
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Scene Management
    // ════════════════════════════════════════════════════════════════════════

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[GameManager] Scene loaded: {scene.name}");

        // Reset state
        isRestarting = false;

        // Find player in new scene
        FindPlayer();

        // Ensure cursor is properly locked for gameplay
        ResetCursorState();

        // Reset time scale (in case it was modified)
        Time.timeScale = 1f;
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

        // Reset time scale
        Time.timeScale = 1f;

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
