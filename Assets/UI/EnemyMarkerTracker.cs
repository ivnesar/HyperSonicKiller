using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// ENEMY MARKER TRACKER - Attached to each NPC alongside NpcBase
// ════════════════════════════════════════════════════════════════════════════
//
// Responsibilities:
// - Self-registers with EnemyMarkerManager on enable
// - Self-unregisters on disable (or after death delay)
// - Provides marker-relevant data to the UI (type, state, alive, position)
//
// SETUP:
// 1. Add this component to every NPC prefab (next to NpcBase)
// 2. markerAnchorOffset adjusts the world-space height above the NPC
//
// ════════════════════════════════════════════════════════════════════════════

public class EnemyMarkerTracker : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Marker Position")]
    [Tooltip("Offset above the NPC's pivot for the marker anchor point")]
    [SerializeField] private Vector3 markerAnchorOffset = new Vector3(0f, 2.5f, 0f);

    [Header("Death Delay")]
    [Tooltip("How long the marker stays visible after the NPC dies (seconds)")]
    [SerializeField] private float deathLingerDuration = 1f;

    [Header("State Icons (Texture2D)")]
    [Tooltip("Icon for Idle state (also used for walking, reloading, etc.)")]
    [SerializeField] private Texture2D iconIdle;

    [Tooltip("Icon for Charging/Aiming state (warning)")]
    [SerializeField] private Texture2D iconCharging;

    [Tooltip("Icon for Attacking/Firing state (danger)")]
    [SerializeField] private Texture2D iconAttacking;

    [Tooltip("Icon for Stunned state")]
    [SerializeField] private Texture2D iconStunned;

    [Tooltip("Icon for Dead state")]
    [SerializeField] private Texture2D iconDead;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npc;
    private bool hasRegistered;
    private bool deathTriggered;
    private Sprite[] cachedStateIcons;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    private static Sprite TextureToSprite(Texture2D tex)
    {
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>World-space position where the marker should anchor when on-screen.</summary>
    public Vector3 MarkerWorldPosition => transform.position + markerAnchorOffset;

    /// <summary>The NPC type (Soldier, Defender, GenOne, GenTwo).</summary>
    public NpcType Type => npc != null ? npc.GetNpcType() : NpcType.Soldier;

    /// <summary>Current state name for display.</summary>
    public string StateName => npc != null ? npc.GetCurrentStateName() : "None";

    /// <summary>True if the NPC is dead.</summary>
    public bool IsDead => npc != null && npc.IsDead;

    /// <summary>True if the NPC is stunned.</summary>
    public bool IsStunned => npc != null && npc.IsStunned;

    /// <summary>
    /// Returns a simplified marker state used to pick the correct icon.
    /// Maps the various NPC states to a small set of visual categories.
    /// </summary>
    public MarkerState CurrentMarkerState
    {
        get
        {
            if (npc == null) return MarkerState.Idle;
            if (npc.IsDead) return MarkerState.Dead;
            if (npc.IsStunned) return MarkerState.Stunned;

            string state = npc.GetCurrentStateName();
            return state switch
            {
                // Charging / aiming (warning phase)
                "Aiming" => MarkerState.Charging,
                "Charging" => MarkerState.Charging,
                "InPosition" => MarkerState.Charging,

                // Attacking (active danger)
                "Firing" => MarkerState.Attacking,
                "Dashing" => MarkerState.Attacking,

                // Everything else is Idle
                _ => MarkerState.Idle
            };
        }
    }

    /// <summary>Distance from this NPC to the player (cached from NpcBase).</summary>
    public float DistanceToPlayer => npc != null ? npc.DistanceToTarget : float.MaxValue;

    /// <summary>Icon sprites indexed by MarkerState. Converted from Texture2D once and cached.</summary>
    public Sprite[] StateIcons
    {
        get
        {
            if (cachedStateIcons == null)
            {
                cachedStateIcons = new Sprite[]
                {
                    TextureToSprite(iconIdle),       // MarkerState.Idle = 0
                    TextureToSprite(iconCharging),   // MarkerState.Charging = 1
                    TextureToSprite(iconAttacking),  // MarkerState.Attacking = 2
                    TextureToSprite(iconStunned),    // MarkerState.Stunned = 3
                    TextureToSprite(iconDead)        // MarkerState.Dead = 4
                };
            }
            return cachedStateIcons;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();

        if (npc == null)
        {
            Debug.LogError($"[EnemyMarkerTracker] No NpcBase found on {gameObject.name}! Disabling tracker.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        Register();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void Update()
    {
        // Detect death and start linger countdown
        if (!deathTriggered && npc != null && npc.IsDead)
        {
            deathTriggered = true;
            Invoke(nameof(UnregisterAfterDeath), deathLingerDuration);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Registration
    // ════════════════════════════════════════════════════════════════════════

    private void Register()
    {
        if (hasRegistered) return;

        var manager = EnemyMarkerManager.Instance;
        if (manager != null)
        {
            manager.RegisterTracker(this);
            hasRegistered = true;
        }
        else
        {
            // Manager might not exist yet — retry next frame
            Invoke(nameof(RetryRegister), 0f);
        }
    }

    private void RetryRegister()
    {
        if (hasRegistered) return;

        var manager = EnemyMarkerManager.Instance;
        if (manager != null)
        {
            manager.RegisterTracker(this);
            hasRegistered = true;
        }
        else
        {
            Debug.LogWarning($"[EnemyMarkerTracker] No EnemyMarkerManager found in scene! Marker for {gameObject.name} will not appear.");
        }
    }

    private void Unregister()
    {
        if (!hasRegistered) return;

        var manager = EnemyMarkerManager.Instance;
        if (manager != null)
        {
            manager.UnregisterTracker(this);
        }

        hasRegistered = false;
    }

    private void UnregisterAfterDeath()
    {
        Unregister();
    }

    #endregion
}

// ════════════════════════════════════════════════════════════════════════════
// MARKER STATE ENUM - Simplified visual categories for marker icons
// ════════════════════════════════════════════════════════════════════════════

public enum MarkerState
{
    Idle,       // Everything that isn't a critical state
    Charging,   // Aiming, charging — warning phase
    Attacking,  // Firing, dashing — active danger
    Stunned,    // Stunned
    Dead        // Eliminated
}
