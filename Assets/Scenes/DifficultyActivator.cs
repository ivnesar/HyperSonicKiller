using UnityEngine;

/// <summary>
/// Reads the current difficulty from the DifficultySettings asset
/// and activates the matching enemy composition group.
/// 
/// SETUP:
/// 1. Add this script to any GameObject in your level scene 
///    (e.g. an empty "DifficultyManager" object).
/// 2. Drag the same DifficultySettings asset from your project into the inspector.
/// 3. Assign your 3 enemy group GameObjects (Easy, Medium, Hard).
/// 4. Make sure all 3 groups are INACTIVE by default in the scene.
///    (Uncheck the checkbox next to their name in the inspector.)
/// </summary>
public class DifficultyActivator : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Settings")]
    [Tooltip("Drag the same DifficultySettings asset here")]
    [SerializeField] private DifficultySettings difficultySettings;

    [Header("Enemy Groups (set all to INACTIVE by default)")]
    [Tooltip("Parent GameObject containing all enemies for Easy difficulty")]
    [SerializeField] private GameObject easyGroup;

    [Tooltip("Parent GameObject containing all enemies for Medium difficulty")]
    [SerializeField] private GameObject mediumGroup;

    [Tooltip("Parent GameObject containing all enemies for Hard difficulty")]
    [SerializeField] private GameObject hardGroup;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (difficultySettings == null)
        {
            Debug.LogError("[DifficultyActivator] No DifficultySettings assigned! " +
                           "Defaulting to Medium.");
            ActivateGroup(mediumGroup);
            return;
        }

        // Read the difficulty that was selected in the main menu
        DifficultySettings.Difficulty current = difficultySettings.CurrentDifficulty;

        Debug.Log($"[DifficultyActivator] Activating group for difficulty: {current}");

        switch (current)
        {
            case DifficultySettings.Difficulty.Easy:
                ActivateGroup(easyGroup);
                break;

            case DifficultySettings.Difficulty.Medium:
                ActivateGroup(mediumGroup);
                break;

            case DifficultySettings.Difficulty.Hard:
                ActivateGroup(hardGroup);
                break;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deactivates all groups, then activates only the chosen one.
    /// </summary>
    private void ActivateGroup(GameObject groupToActivate)
    {
        // Safety: deactivate all first
        if (easyGroup != null) easyGroup.SetActive(false);
        if (mediumGroup != null) mediumGroup.SetActive(false);
        if (hardGroup != null) hardGroup.SetActive(false);

        // Activate the correct one
        if (groupToActivate != null)
        {
            groupToActivate.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[DifficultyActivator] The group for the selected " +
                             "difficulty is not assigned in the inspector!");
        }
    }

    #endregion
}
