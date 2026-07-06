using UnityEngine;
using System;

/// <summary>
/// Central coordinator for all player subsystems.
/// Acts as the single point of contact for external systems (enemies, pickups, UI).
/// Manages player state machine and routes events between subsystems.
/// 
/// UPDATED: Adjusted for new dash-attack system where LMB = Dash with auto-attack.
/// UPDATED: Added SprintDashing state and PlayerSprint subsystem.
/// UPDATED: Block removed. HP is now the only defensive resource and handles regeneration.
/// UPDATED: Actual HP damage requests sword recall as soon as possible.
/// UPDATED: Stores player movement detection segment for reliable high-speed laser/mine checks.
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
        SprintDashing,      // Sprint-dash (short dodge, NOT cancellable)
        Dashing,            // Attack dash (NOT invulnerable)
        StuckToSurface,
        Airborne,
        Dead
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Laser Target")]
    [Tooltip("Transform auf das NPC-Laser zeigen sollen (z.B. Brusthöhe). " +
             "Wenn leer, wird transform.position als Fallback genutzt.")]
    [SerializeField] private Transform laserTarget;

    [Header("Movement Detection")]
    [Tooltip("Radius um die gespeicherte Player-Detection-Position. Wird z.B. von ProxyMineNpc genutzt, damit schnelle Bewegungen nicht zwischen Frames durch Laser springen.")]
    [Min(0.01f)]
    [SerializeField] private float movementDetectionRadius = 0.35f;

    [Header("Dash Recharge")]
    [Tooltip("Layer, die als Boden für die Sticky-Surface-Pruefung beim Aufladen zaehlen. " +
             "Im Inspector auf den Surface-Layer setzen, damit Trigger oder Deko die Pruefung nicht verfaelschen.")]
    [SerializeField] private LayerMask groundStickyMask = ~0;

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
    [HideInInspector] public PlayerSprint Sprint { get; private set; }
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
    /// Returns true if player is currently invulnerable.
    /// Normal attack dash does NOT grant invulnerability.
    /// </summary>
    public bool IsInvulnerable => false;

    /// <summary>
    /// Zielpunkt auf den NPC-Laser zeigen sollen.
    /// Gibt das zugewiesene Transform zurück, oder null wenn keins gesetzt ist.
    /// NpcLaserPointer nutzt dies als automatisches Target.
    /// </summary>
    public Transform LaserTarget => laserTarget;

    /// <summary>Radius um die gespeicherte Detection-Position des Spielers.</summary>
    public float MovementDetectionRadius => Mathf.Max(0.01f, movementDetectionRadius);

    /// <summary>Detection-Position vor der letzten zentral registrierten CharacterController-Bewegung.</summary>
    public Vector3 PreviousDetectionPosition { get; private set; }

    /// <summary>Aktuelle Detection-Position nach der letzten zentral registrierten CharacterController-Bewegung.</summary>
    public Vector3 CurrentDetectionPosition { get; private set; }

    /// <summary>Letzte registrierte Detection-Bewegung. Nützlich für Debug/High-Speed-Checks.</summary>
    public Vector3 LastDetectionDelta => CurrentDetectionPosition - PreviousDetectionPosition;

    /// <summary>Frame, in dem MovePlayer() zuletzt ein Detection-Segment geschrieben hat.</summary>
    public int LastDetectionMoveFrame { get; private set; }

    // ── Last Damage Source (für Game Over Screen + Death Camera) ──
    private string lastDamageSourceName = "";
    private float lastDamageAmount;
    private Transform lastDamageSourceTransform;     // Live-Referenz: Kamera folgt diesem Transform
    private Vector3 lastKnownSourcePosition;         // Fallback-Snapshot für den Fall, dass der Transform zerstört wird
    private bool hasDeathTarget;

    /// <summary>Name der letzten Schadensquelle (z.B. "Soldier", "Proxy Mine").</summary>
    public string LastDamageSourceName => lastDamageSourceName;

    /// <summary>Schadensmenge des letzten Treffers.</summary>
    public float LastDamageAmount => lastDamageAmount;

    /// <summary>
    /// Welt-Position der letzten Schadensquelle.
    /// Folgt dem NPC live, solange dessen Transform existiert.
    /// Wird der Transform zerstört (NPC stirbt nach dem Spieler), gibt diese Property
    /// die zuletzt bekannte Position zurück — die Kamera "friert" dann auf der
    /// Position ein, an der der NPC zuletzt war.
    /// Nur gültig wenn HasDeathTarget true ist.
    /// </summary>
    public Vector3 DeathTargetPosition
    {
        get
        {
            // Wenn der Transform noch lebt: aktuelle Position holen UND als
            // Snapshot updaten (für später, falls er gleich zerstört wird).
            if (lastDamageSourceTransform != null)
            {
                lastKnownSourcePosition = lastDamageSourceTransform.position;
                return lastKnownSourcePosition;
            }
            // Transform tot → letzten Snapshot zurückgeben (Kamera bleibt fix dort).
            return lastKnownSourcePosition;
        }
    }

    /// <summary>
    /// True, wenn beim Tod eine gültige Killer-Position bekannt ist.
    /// False z.B. bei Umgebungsschaden ohne Source-Transform.
    /// </summary>
    public bool HasDeathTarget => hasDeathTarget;

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
        Sprint = GetComponent<PlayerSprint>();
        Look = GetComponent<PlayerLook>();
        Combat = GetComponent<PlayerCombat>();
        Health = GetComponent<PlayerHealth>();
        SwordThrow = GetComponent<PlayerSwordThrow>();

        ResetMovementDetectionPositions();

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
    /// Damage goes directly to HP. Block HP has been removed.
    /// Returns false if damage was ignored (e.g., player is invulnerable).
    /// 
    /// NOTE: Player is NOT invulnerable during attack dash - only during sword dash.
    /// </summary>
    public bool TakeDamage(float damage)
    {
        return TakeDamage(damage, Vector3.zero, "", null);
    }

    /// <summary>
    /// Damage with attack direction — triggers camera nudge toward attacker.
    /// attackDirection = direction FROM attacker TO player (normalized).
    /// Pass Vector3.zero if direction is unknown.
    /// </summary>
    public bool TakeDamage(float damage, Vector3 attackDirection)
    {
        return TakeDamage(damage, attackDirection, "", null);
    }

    /// <summary>
    /// Damage with attack direction and source name for kill tracking.
    /// </summary>
    public bool TakeDamage(float damage, Vector3 attackDirection, string sourceName)
    {
        return TakeDamage(damage, attackDirection, sourceName, null);
    }

    /// <summary>
    /// Full damage entry point with attacker transform — used for death camera.
    /// sourceTransform is the root GameObject transform of the attacker.
    /// The death camera will follow this transform live, until either the player revives
    /// or the transform is destroyed (then it stays on the last known position).
    /// </summary>
    public bool TakeDamage(float damage, Vector3 attackDirection, string sourceName, Transform sourceTransform)
    {
        if (IsDead || damage <= 0) return false;
        
        // Only invulnerable during sword dash
        if (IsInvulnerable) return false;

        // Track damage source — the last hit that connects matters
        TrackDamageSource(sourceName, damage, sourceTransform);

        // HP is the only defensive resource. Exhausted is handled separately by special cases.
        bool hpWasDamaged = Health != null && Health.TakeDamage(damage);
        if (hpWasDamaged)
        {
            SwordThrow?.RequestRecallBecausePlayerTookHpDamage();
        }

        // Trigger hit direction nudge (Look handles state checks internally)
        if (attackDirection != Vector3.zero && Look != null)
        {
            Look.NudgeTowardAttackDirection(attackDirection);
        }
        
        return true;
    }

    /// <summary>
    /// Direct damage for environmental hazards or other non-directional sources.
    /// Still respects invulnerability.
    /// </summary>
    public bool TakeDirectDamage(float damage)
    {
        return TakeDirectDamage(damage, "", null);
    }

    /// <summary>
    /// Direct damage with source name for kill tracking.
    /// </summary>
    public bool TakeDirectDamage(float damage, string sourceName)
    {
        return TakeDirectDamage(damage, sourceName, null);
    }

    /// <summary>
    /// Direct damage with source name and transform — used for death camera.
    /// </summary>
    public bool TakeDirectDamage(float damage, string sourceName, Transform sourceTransform)
    {
        if (IsDead || damage <= 0) return false;
        
        // Only invulnerable during sword dash
        if (IsInvulnerable) return false;

        TrackDamageSource(sourceName, damage, sourceTransform);
        
        bool hpWasDamaged = Health != null && Health.TakeDamage(damage);
        if (hpWasDamaged)
        {
            SwordThrow?.RequestRecallBecausePlayerTookHpDamage();
        }
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
        ClearDamageSource();
        Look?.ResetDeathCamera();
        SetState(PlayerState.Normal);
        ResetMovementDetectionPositions();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        OnPlayerRevive?.Invoke();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Movement Detection API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Central wrapper for CharacterController.Move().
    /// Any player subsystem that moves the controller should use this method
    /// so external systems can reliably test the segment between the previous
    /// and current player positions, even during very fast unscaled movement.
    /// </summary>
    public CollisionFlags MovePlayer(Vector3 motion)
    {
        if (Controller == null || !Controller.enabled)
        {
            return CollisionFlags.None;
        }

        PreviousDetectionPosition = CurrentDetectionPosition;
        CollisionFlags flags = Controller.Move(motion);
        CurrentDetectionPosition = GetMovementDetectionPosition();
        LastDetectionMoveFrame = Time.frameCount;
        return flags;
    }

    /// <summary>
    /// Resets the stored movement segment to the player's current detection position.
    /// Use after teleports, respawns, or manual transform changes where the previous
    /// frame segment should not trigger hazards.
    /// </summary>
    public void ResetMovementDetectionPositions()
    {
        Vector3 currentPosition = GetMovementDetectionPosition();
        PreviousDetectionPosition = currentPosition;
        CurrentDetectionPosition = currentPosition;
        LastDetectionMoveFrame = Time.frameCount;
    }

    private Vector3 GetMovementDetectionPosition()
    {
        if (laserTarget != null)
        {
            return laserTarget.position;
        }

        if (Controller != null)
        {
            return transform.TransformPoint(Controller.center);
        }

        return transform.position;
    }

    #endregion


    // ════════════════════════════════════════════════════════════════════════
    #region Damage Source Tracking
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Merkt sich die letzte Schadensquelle. Wird bei jedem Treffer aufgerufen.
    /// Leerer Name wird ignoriert (alte Aufrufe ohne sourceName behalten den vorherigen Wert).
    /// Transform wird als Live-Referenz gespeichert — die Kamera folgt ihm.
    /// Zusätzlich speichern wir die aktuelle Position als Snapshot, falls der
    /// Transform später zerstört wird.
    /// </summary>
    private void TrackDamageSource(string sourceName, float damage, Transform sourceTransform)
    {
        if (!string.IsNullOrEmpty(sourceName))
        {
            lastDamageSourceName = sourceName;
        }
        lastDamageAmount = damage;

        if (sourceTransform != null)
        {
            lastDamageSourceTransform = sourceTransform;
            lastKnownSourcePosition = sourceTransform.position;
            hasDeathTarget = true;
        }
        // Wenn null übergeben wurde, behalten wir die vorherige Quelle —
        // so hat zumindest der letzte Hit, der *eine* Quelle hatte, Gültigkeit.
    }

    /// <summary>
    /// Setzt die Schadensquelle zurück (z.B. bei Revive).
    /// </summary>
    private void ClearDamageSource()
    {
        lastDamageSourceName = "";
        lastDamageAmount = 0f;
        lastDamageSourceTransform = null;
        lastKnownSourcePosition = Vector3.zero;
        hasDeathTarget = false;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API - Misc
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Force the player into a specific state (use sparingly).
    /// </summary>
    public void ForceState(PlayerState newState)
    {
        SetState(newState);
    }

    /// <summary>
    /// Resolves the player state after an attack dash was cancelled.
    /// A dash can be cancelled while the CharacterController is already touching
    /// the floor. In that case we must enter Normal immediately so the existing
    /// ground recharge check can run; otherwise PlayerMovement may never fire a
    /// fresh landing event because it already considered the player grounded.
    /// </summary>
    public void ResolveStateAfterDashCancel()
    {
        if (HasGroundContact())
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
                // Check if standing on a sticky surface — recharge charges
                if (IsGroundSurfaceSticky())
                {
                    Dash?.ResetCharges();
                }
                break;

            case PlayerState.StuckToSurface:
                // Charges reset is handled by PlayerDash.CompleteDash → HandleDashCompleted
                break;
                
            case PlayerState.SprintDashing:
                // Sprint-dash: stop normal sprint (will resume after dash if Shift held)
                Sprint?.StopSprint();
                break;
                
            case PlayerState.Dashing:
                // Normal attack dash - NOT invulnerable
                break;
                

            case PlayerState.Dead:
                // Cancel any active dash (this also resets Time.timeScale)
                Dash?.ForceCancelDash();
                Sprint?.ForceCancelDash();
                Sprint?.StopSprint();
                TimeManager.Instance.ClearAllLayers(); // Alles zurücksetzen bei Tod
                
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
                
                
            case PlayerState.Dashing:
                break;
                
            case PlayerState.SprintDashing:
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

    private void HandleDashCompleted(bool hitSurface, bool hitWall, bool isStickyLanding)
    {
        if (isStickyLanding)
        {
            // Sticky surface — recharge charges
            Dash?.ResetCharges();

            if (hitWall && !Controller.isGrounded)
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
        else
        {
            // Non-sticky surface or open air — no recharge, no wall stick
            if (Controller.isGrounded)
            {
                SetState(PlayerState.Normal);
            }
            else
            {
                SetState(PlayerState.Airborne);
            }
        }
    }
    

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helper Queries (For subsystems)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if player can move normally (WASD).
    /// </summary>
    public bool CanMove => CurrentState != PlayerState.Dead && 
                           CurrentState != PlayerState.Dashing && 
                           CurrentState != PlayerState.StuckToSurface &&
                           CurrentState != PlayerState.SprintDashing;
    
    /// <summary>
    /// Returns true if player can initiate an attack dash (LMB).
    /// </summary>
    public bool CanDash => CurrentState == PlayerState.Normal || 
                           CurrentState == PlayerState.Airborne || 
                           CurrentState == PlayerState.StuckToSurface;
    

    /// <summary>
    /// DEPRECATED: Manual attacks no longer exist.
    /// Kept for backwards compatibility - always returns false.
    /// Attacks are now automatic during dash.
    /// </summary>
    [Obsolete("Manual attacks removed - attacks are automatic during dash")]
    public bool CanAttack => false;
    
    /// <summary>
    /// Block has been removed. Kept for backwards compatibility with older scene scripts.
    /// </summary>
    [Obsolete("Block has been removed. HP is the only defensive resource.")]
    public bool CanBlock => false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Surface Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Raycasts downward to check if the ground beneath the player has a StickySurface component.
    /// Used when landing (entering Normal state) to decide whether to recharge dash charges.
    /// </summary>
    private bool IsGroundSurfaceSticky()
    {
        float checkDistance = Controller.height / 2f + 0.5f + Controller.skinWidth;

        // LayerMask + Trigger ignorieren, damit nur echte Boden-Collider zaehlen.
        // Vorher konnte ein beliebiger Collider oder Trigger knapp unter dem Spieler
        // die Pruefung verfaelschen, wodurch das Aufladen sporadisch fehlschlug.
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit,
                            checkDistance, groundStickyMask, QueryTriggerInteraction.Ignore))
        {
            return hit.collider.GetComponentInParent<StickySurface>() != null;
        }

        return false;
    }

    /// <summary>
    /// Returns true when the CharacterController is grounded, with a short raycast
    /// fallback for frames where isGrounded has not caught up yet after manual
    /// dash movement.
    /// </summary>
    private bool HasGroundContact()
    {
        if (Controller == null || !Controller.enabled)
        {
            return false;
        }

        if (Controller.isGrounded)
        {
            return true;
        }

        float checkDistance = Controller.skinWidth + 0.15f;
        return Physics.Raycast(transform.position, Vector3.down, checkDistance,
                               groundStickyMask, QueryTriggerInteraction.Ignore);
    }

    #endregion
}
