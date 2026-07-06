using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized input handling for the player.
/// Abstracts raw Unity input into action-based queries.
/// Easy to extend for rebinding or input system migration.
/// 
/// UPDATED: Attack (LMB) is now Dash. Hold LMB to aim with zoom + slow-mo, release to dash.
/// UPDATED: Old Q dash removed - dash is now only on LMB.
/// </summary>
public class PlayerInputHandler : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Enums
    // ════════════════════════════════════════════════════════════════════════

    public enum InputState
    {
        None,
        Press,
        Hold,
        Release
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Types
    // ════════════════════════════════════════════════════════════════════════

    private class KeyBinding
    {
        public KeyCode Key;
        public InputState State;
        public bool WasPressedLastFrame;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime Data
    // ════════════════════════════════════════════════════════════════════════

    private Dictionary<string, KeyBinding> bindings;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        InitializeDefaultBindings();
    }

    private void Update()
    {
        // Don't register gameplay inputs while paused
        if (TimeManager.Instance.IsPaused)
        {
            // Clear all states so nothing remains "held" after unpause
            ClearAllStates();
            return;
        }

        UpdateAllBindings();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Initialization
    // ════════════════════════════════════════════════════════════════════════
    
    private void InitializeDefaultBindings()
    {
        bindings = new Dictionary<string, KeyBinding>
        {
            { "Jump",       new KeyBinding { Key = KeyCode.Space } },
            { "Sprint",     new KeyBinding { Key = KeyCode.LeftShift } },
            
            { "Dash",       new KeyBinding { Key = KeyCode.Mouse0 } }, // LMB: hold to aim, release to dash
            { "DashDown",   new KeyBinding { Key = KeyCode.LeftControl } },
            
            { "ThrowSword", new KeyBinding { Key = KeyCode.Mouse1 } }, //KeyCode.Mouse1
        };
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API - Input Queries
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the current state of an action (None, Press, Hold, Release).
    /// </summary>
    public InputState GetActionState(string actionName)
    {
        return bindings.TryGetValue(actionName, out var binding) ? binding.State : InputState.None;
    }

    /// <summary>
    /// Returns true only on the frame the action was pressed.
    /// </summary>
    public bool GetActionDown(string actionName)
    {
        return GetActionState(actionName) == InputState.Press;
    }

    /// <summary>
    /// Returns true while the action is held (including first frame).
    /// </summary>
    public bool GetAction(string actionName)
    {
        var state = GetActionState(actionName);
        return state == InputState.Press || state == InputState.Hold;
    }

    /// <summary>
    /// Returns true only on the frame the action was released.
    /// </summary>
    public bool GetActionUp(string actionName)
    {
        return GetActionState(actionName) == InputState.Release;
    }

    /// <summary>
    /// WASD / Arrow keys as normalized Vector2.
    /// </summary>
    public Vector2 GetMoveInput()
    {
        return new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        ).normalized;
    }

    /// <summary>
    /// Mouse delta for camera rotation.
    /// </summary>
    public Vector2 GetLookInput()
    {
        return new Vector2(
            Input.GetAxis("Mouse X"),
            Input.GetAxis("Mouse Y")
        );
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API - Rebinding
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Change the key for an action at runtime.
    /// </summary>
    public void RebindAction(string actionName, KeyCode newKey)
    {
        if (bindings.TryGetValue(actionName, out var binding))
        {
            binding.Key = newKey;
            binding.State = InputState.None;
            binding.WasPressedLastFrame = false;
        }
    }

    /// <summary>
    /// Get the current key bound to an action.
    /// </summary>
    public KeyCode GetBoundKey(string actionName)
    {
        return bindings.TryGetValue(actionName, out var binding) ? binding.Key : KeyCode.None;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal - Input Processing
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateAllBindings()
    {
        foreach (var binding in bindings.Values)
        {
            bool isPressed = Input.GetKey(binding.Key);
            bool pressedThisFrame = Input.GetKeyDown(binding.Key);
            bool releasedThisFrame = Input.GetKeyUp(binding.Key);

            if (pressedThisFrame)
            {
                binding.State = InputState.Press;
                binding.WasPressedLastFrame = true;
            }
            else if (isPressed && binding.WasPressedLastFrame)
            {
                binding.State = InputState.Hold;
            }
            else if (releasedThisFrame)
            {
                binding.State = InputState.Release;
                binding.WasPressedLastFrame = false;
            }
            else
            {
                binding.State = InputState.None;
                binding.WasPressedLastFrame = false;
            }
        }
    }

    /// <summary>
    /// Resets all bindings to None. Called during pause to prevent
    /// stale Hold/Press states from persisting after unpause.
    /// </summary>
    private void ClearAllStates()
    {
        foreach (var binding in bindings.Values)
        {
            binding.State = InputState.None;
            binding.WasPressedLastFrame = false;
        }
    }

    #endregion
}