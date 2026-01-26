using System.Collections.Generic;
using UnityEngine;

public class scrPlayerInputHandler : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Enums & Input Types
    // ────────────────────────────────────────────────────────────────────────────────

    public enum InputState
    {
        None,
        Press,
        Hold,
        Release
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Internal Binding Class
    // ────────────────────────────────────────────────────────────────────────────────

    private class KeyBinding
    {
        public KeyCode Key { get; set; }
        public InputState State { get; set; }
        public bool WasPressedLastFrame { get; set; }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────────────

    private Dictionary<string, KeyBinding> keyBindings;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        InitializeDefaultBindings();
    }

    private void Update()
    {
        UpdateAllBindings();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Initialization
    // ────────────────────────────────────────────────────────────────────────────────

    private void InitializeDefaultBindings()
    {
        keyBindings = new Dictionary<string, KeyBinding>
        {
            { "Jump",           new KeyBinding { Key = KeyCode.Space } },
            { "Dash",           new KeyBinding { Key = KeyCode.Q } },
            { "DashCancelDown", new KeyBinding { Key = KeyCode.LeftControl } },
            { "Attack",         new KeyBinding { Key = KeyCode.Mouse0 } },
            { "Block",          new KeyBinding { Key = KeyCode.Mouse1 } },
            { "ThrowSword",     new KeyBinding { Key = KeyCode.R } },
            { "ThrowDart",      new KeyBinding { Key = KeyCode.F } }
        };
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Input Query API
    // ────────────────────────────────────────────────────────────────────────────────

    public InputState GetActionState(string actionName)
    {
        if (keyBindings.TryGetValue(actionName, out KeyBinding binding))
        {
            return binding.State;
        }
        return InputState.None;
    }

    public Vector2 GetMoveInput()
    {
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
    }

    public Vector2 GetLookInput()
    {
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Rebinding
    // ────────────────────────────────────────────────────────────────────────────────

    public void RebindAction(string actionName, KeyCode newKey)
    {
        if (keyBindings.TryGetValue(actionName, out KeyBinding binding))
        {
            binding.Key = newKey;
            binding.State = InputState.None;
            binding.WasPressedLastFrame = false;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Input Processing
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateAllBindings()
    {
        foreach (var binding in keyBindings.Values)
        {
            bool isPressed          = Input.GetKey(binding.Key);
            bool pressedThisFrame   = Input.GetKeyDown(binding.Key);
            bool releasedThisFrame  = Input.GetKeyUp(binding.Key);

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

    #endregion
}