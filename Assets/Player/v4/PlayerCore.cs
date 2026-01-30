using UnityEngine;
using System;

/// <summary>
/// Central coordinator for all player subsystems.
/// Acts as the single point of contact for external systems (enemies, pickups, UI).
/// Manages player state machine and routes events between subsystems.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputHandler))]
public class PlayerCore : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Enums
    // ════════════════════════════════════════════════════════════════════════

    public enum PlayerState
    {
        Normal,
        Dashing,
        DashingToSword,  // NEW: Invulnerable dash to retrieve thrown sword
        StuckToSurface,
        Airborne,
        Dead
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events (External systems subscribe to these)
    // ════════════════════════════════════════════════════════════════════════

    public event Action<PlayerState, PlayerState> OnStateChanged;  // (oldState, newState)
    public event Action OnPlayerDeath;
    public event Action OnPlayerRevive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Shared Components (Subsystems access these)
    // ════════════════════════════════════════════════════════════════════════

    [HideInInspector] public CharacterController Controller { get; private set; }
    [HideInInspector] public PlayerInputHandler Input { get; private set; }
    [HideInInspector] public Camera PlayerCamera { get; private set; }
    [HideInInspector] public Transform CameraTransform { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Subsystem References
    // ════════════════════════════════════════════════════════════════════════

    [HideInInspector] public PlayerMovement Movement { get; private set; }
    [HideInInspector] public PlayerDash Dash { get; private set; }
    [HideInInspector] public PlayerLook Look { get; private set; }
    [HideInInspector] public PlayerCombat Combat { get; private set; }
    [HideInInspector] public PlayerHealth Health { get; private set; }
    [HideInInspector] public PlayerSwordThrow SwordThrow { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State
    // ════════════════════════════════════════════════════════════════════════

    public PlayerState CurrentState = PlayerState.Normal;
    public bool IsDead => CurrentState == PlayerState.Dead;
    
    /// <summary>
    /// Returns true if player is currently invulnerable (e.g., during sword dash).
    /// </summary>
    public bool IsInvulnerable => CurrentState == PlayerState.DashingToSword;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Core components
        Controller = GetComponent<CharacterController>();
        Input = GetComponent<PlayerInputHandler>();
        PlayerCamera = GetComponentInChildren<Camera>();
        CameraTransform = PlayerCamera != null ? PlayerCamera.transform : Camera.main.transform;

        // Subsystems (all on same GameObject)
        Movement = GetComponent<PlayerMovement>();
        Dash = GetComponent<PlayerDash>();
        Look = GetComponent<PlayerLook>();
        Combat = GetComponent<PlayerCombat>();
        Health = GetComponent<PlayerHealth>();
        SwordThrow = GetComponent<PlayerSwordThrow>();

        // Lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Start()
    {
        // Subscribe to health events
        if (Health != null)
        {
            Health.OnDeath += HandleDeath;
        }

        // Subscribe to dash events for state changes
        if (Dash != null)
        {
            Dash.OnDashStarted += () => SetState(PlayerState.Dashing);
            Dash.OnDashCompleted += HandleDashCompleted;
            Dash.OnWallStick += () => SetState(PlayerState.StuckToSurface);
            Dash.OnUnstick += () => SetState(PlayerState.Airborne);
            Dash.OnSwordDashStarted += () => SetState(PlayerState.DashingToSword);
            Dash.OnSwordDashCompleted += HandleSwordDashCompleted;
        }

        // Subscribe to movement for airborne detection
        if (Movement != null)
        {
            Movement.OnBecameAirborne += () => { if (CurrentState == PlayerState.Normal) SetState(PlayerState.Airborne); };
            Movement.OnLanded += () => { if (CurrentState == PlayerState.Airborne) SetState(PlayerState.Normal); };
        }
    }

    private void OnDestroy()
    {
        if (Health != null) Health.OnDeath -= HandleDeath;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API (For external systems like enemies, pickups, UI)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Main entry point for dealing damage to the player.
    /// Routes to Combat (if blocking) or Health (if not).
    /// Returns false if damage was ignored (e.g., player is invulnerable).
    /// </summary>
    public bool TakeDamage(float damage)
    {
        if (IsDead || damage <= 0) return false;
        
        // NEW: Ignore damage while dashing to sword (invulnerable)
        if (IsInvulnerable) return false;

        // Combat handles block logic, Health handles actual HP
        if (Combat != null && Combat.IsBlocking)
        {
            Combat.TakeBlockDamage(damage);
        }
        else
        {
            Health?.TakeDamage(damage);
        }
        
        return true;
    }

    /// <summary>
    /// Direct damage that bypasses blocking (e.g., environmental hazards).
    /// Still respects invulnerability.
    /// </summary>
    public bool TakeDirectDamage(float damage)
    {
        if (IsDead || damage <= 0) return false;
        
        // NEW: Ignore damage while dashing to sword (invulnerable)
        if (IsInvulnerable) return false;
        
        Health?.TakeDamage(damage);
        return true;
    }

    /// <summary>
    /// Heal the player.
    /// </summary>
    public void Heal(float amount)
    {
        Health?.Heal(amount);
    }

    /// <summary>
    /// Revive the player at current position.
    /// </summary>
    public void Revive()
    {
        if (!IsDead) return;

        Health?.ResetHealth();
        Combat?.ResetCombat();
        SetState(PlayerState.Normal);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        OnPlayerRevive?.Invoke();
    }

    /// <summary>
    /// Force the player into a specific state (use sparingly).
    /// </summary>
    public void ForceState(PlayerState newState)
    {
        SetState(newState);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Machine
    // ════════════════════════════════════════════════════════════════════════

    public void SetState(PlayerState newState)
    {
        if (CurrentState == newState) return;

        PlayerState oldState = CurrentState;
        ExitState(oldState);
        CurrentState = newState;
        EnterState(newState);

        OnStateChanged?.Invoke(oldState, newState);
    }

    private void EnterState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Normal:
                Dash?.ResetCharges();
                break;

            case PlayerState.StuckToSurface:
                Dash?.ResetCharges();
                break;
                
            case PlayerState.DashingToSword:
                // Player is invulnerable during this dash
                Debug.Log("[PlayerCore] Sword dash started - player is invulnerable");
                break;

            case PlayerState.Dead:
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
        }
    }

    private void ExitState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Dead:
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                break;
                
            case PlayerState.DashingToSword:
                Debug.Log("[PlayerCore] Sword dash ended - invulnerability removed");
                break;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDeath()
    {
        SetState(PlayerState.Dead);
        OnPlayerDeath?.Invoke();
        Debug.Log("[PlayerCore] Player died!");
    }

    private void HandleDashCompleted(bool hitSurface)
    {
        if (hitSurface && !Controller.isGrounded)
        {
            SetState(PlayerState.StuckToSurface);
        }
        else if (Controller.isGrounded)
        {
            SetState(PlayerState.Normal);
        }
        else
        {
            SetState(PlayerState.Airborne);
        }
    }
    
    private void HandleSwordDashCompleted()
    {
        // After sword dash, determine state based on grounding
        if (Controller.isGrounded)
        {
            SetState(PlayerState.Normal);
        }
        else
        {
            SetState(PlayerState.Airborne);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helper Queries (For subsystems)
    // ════════════════════════════════════════════════════════════════════════

    public bool CanMove => CurrentState != PlayerState.Dead && 
                           CurrentState != PlayerState.Dashing && 
                           CurrentState != PlayerState.DashingToSword &&
                           CurrentState != PlayerState.StuckToSurface;
                           
    public bool CanDash => CurrentState == PlayerState.Normal || 
                           CurrentState == PlayerState.Airborne || 
                           CurrentState == PlayerState.StuckToSurface;
                           
    public bool CanAttack => CurrentState != PlayerState.Dead && 
                             CurrentState != PlayerState.Dashing &&
                             CurrentState != PlayerState.DashingToSword;
                             
    public bool CanBlock => CurrentState != PlayerState.Dead && 
                            CurrentState != PlayerState.Dashing &&
                            CurrentState != PlayerState.DashingToSword;
    
    /// <summary>
    /// Returns true if player can initiate a sword dash (sword is stuck somewhere).
    /// </summary>
    public bool CanSwordDash => CurrentState != PlayerState.Dead && 
                                CurrentState != PlayerState.Dashing &&
                                CurrentState != PlayerState.DashingToSword &&
                                SwordThrow != null && 
                                SwordThrow.IsSwordStuck;

    #endregion
}