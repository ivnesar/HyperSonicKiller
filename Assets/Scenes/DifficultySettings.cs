using UnityEngine;

/// <summary>
/// ScriptableObject that stores the selected difficulty.
/// Lives as an asset in your project – survives scene loads automatically.
/// 
/// SETUP:
/// 1. Right-click in Project window → Create → Game → Difficulty Settings
/// 2. This creates a .asset file. Keep it in e.g. Assets/Settings/
/// 3. Drag it into the MainMenuUI and DifficultyActivator inspector slots.
/// </summary>
[CreateAssetMenu(fileName = "DifficultySettings", menuName = "Game/Difficulty Settings")]
public class DifficultySettings : ScriptableObject
{
    // ════════════════════════════════════════════════════════════════════════
    #region Difficulty Enum
    // ════════════════════════════════════════════════════════════════════════

    public enum Difficulty
    {
        Easy,
        Medium,
        Hard
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Data
    // ════════════════════════════════════════════════════════════════════════

    [Tooltip("The currently selected difficulty. Changed by the Main Menu.")]
    [SerializeField] private Difficulty currentDifficulty = Difficulty.Easy;

    /// <summary>
    /// Read/write access to the current difficulty.
    /// The Main Menu sets this, the DifficultyActivator reads it.
    /// </summary>
    public Difficulty CurrentDifficulty
    {
        get => currentDifficulty;
        set => currentDifficulty = value;
    }

    #endregion
}
