using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Central non-health HUD controller for mutually exclusive reticle/icon variants
/// and the dash-state frame.
///
/// Reticle priority, highest first:
/// 1. Sword missing
/// 2. Dash externally blocked / stunned
/// 3. Out of dash charges
/// 4. Non-sticky surface
/// 5. Normal
///
/// Setup:
/// - Put this on a Canvas GameObject.
/// - Assign one GameObject per reticle/icon state. Only one is active at a time.
/// - Assign dashStateFrame to a full-screen frame image if desired.
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector References
    // ════════════════════════════════════════════════════════════════════════

    [Header("Player Reference")]
    [SerializeField] private PlayerCore player;

    [Header("Reticle / Icon Variants")]
    [Tooltip("Default reticle. Used for normal aiming, open air, and sticky surfaces.")]
    [SerializeField] private GameObject reticleDefault;

    [Tooltip("Shown when the player has thrown the sword / does not currently own it. Highest priority.")]
    [FormerlySerializedAs("swordMissingIcon")]
    [SerializeField] private GameObject reticleSwordMissing;

    [Tooltip("Shown when dash is externally blocked/stunned.")]
    [SerializeField] private GameObject reticleDashBlocked;

    [Tooltip("Shown when the player has no dash charges.")]
    [SerializeField] private GameObject reticleNoDashCharges;

    [Tooltip("Shown when the crosshair points at a surface that is not a StickySurface.")]
    [FormerlySerializedAs("reticleStickySurface")]
    [SerializeField] private GameObject reticleNonStickySurface;

    [Header("Dash State")]
    [Tooltip("Full-screen frame image that is visible only while PlayerState == Dashing.")]
    [SerializeField] private GameObject dashStateFrame;

    [Header("Options")]
    [SerializeField] private bool hideReticleWhileDead = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private GameObject activeReticle;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerCore>();
        }

        if (player == null)
        {
            Debug.LogError("[PlayerHUD] No PlayerCore found!");
            enabled = false;
            return;
        }

        SubscribeToEvents();
        RefreshAll();
    }

    private void Update()
    {
        RefreshReticle();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Subscription
    // ════════════════════════════════════════════════════════════════════════

    private void SubscribeToEvents()
    {
        if (player == null) return;

        player.OnStateChanged += HandlePlayerStateChanged;
        player.OnPlayerDeath += RefreshAll;
        player.OnPlayerRevive += RefreshAll;

        if (player.Dash != null)
        {
            player.Dash.OnChargesChanged += HandleDashChargesChanged;
            player.Dash.OnDashBlockedChanged += HandleDashBlockedChanged;
        }

        if (player.SwordThrow != null)
        {
            player.SwordThrow.OnSwordThrown += RefreshReticle;
            player.SwordThrow.OnSwordRecalled += RefreshReticle;
            player.SwordThrow.OnSwordCaught += RefreshReticle;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (player == null) return;

        player.OnStateChanged -= HandlePlayerStateChanged;
        player.OnPlayerDeath -= RefreshAll;
        player.OnPlayerRevive -= RefreshAll;

        if (player.Dash != null)
        {
            player.Dash.OnChargesChanged -= HandleDashChargesChanged;
            player.Dash.OnDashBlockedChanged -= HandleDashBlockedChanged;
        }

        if (player.SwordThrow != null)
        {
            player.SwordThrow.OnSwordThrown -= RefreshReticle;
            player.SwordThrow.OnSwordRecalled -= RefreshReticle;
            player.SwordThrow.OnSwordCaught -= RefreshReticle;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Refresh
    // ════════════════════════════════════════════════════════════════════════

    private void RefreshAll()
    {
        RefreshReticle();
        RefreshDashFrame();
    }

    private void RefreshReticle()
    {
        if (player == null || player.Dash == null)
        {
            SetActiveReticle(null);
            return;
        }

        if (hideReticleWhileDead && player.IsDead)
        {
            SetActiveReticle(null);
            return;
        }

        PlayerDash.DashAimInfo aimInfo = player.Dash.GetDashAimInfo();

        // Priority, highest first:
        // 2. Dash externally blocked / stunned
        // 3. Out of dash charges
        // 4. Non-sticky surface
        // 1. Sword missing
        // 5. Normal
        
        if (aimInfo.IsExternallyBlocked)
        {
            SetActiveReticle(reticleDashBlocked);
        }
        else if (!aimInfo.HasDashCharges)
        {
            SetActiveReticle(reticleNoDashCharges);
        }
        else if (aimInfo.HasSurfaceHit && !aimInfo.IsStickySurface)
        {
            SetActiveReticle(reticleNonStickySurface);
        }
        else if (IsSwordMissing())
        {
            SetActiveReticle(reticleSwordMissing);
        }
        else
        {
            SetActiveReticle(reticleDefault);
        }
    }

    private void RefreshDashFrame()
    {
        if (dashStateFrame != null)
        {
            dashStateFrame.SetActive(player != null && player.CurrentState == PlayerCore.PlayerState.Dashing);
        }
    }

    private bool IsSwordMissing()
    {
        return player != null && player.SwordThrow != null && !player.SwordThrow.HasSword;
    }

    private void SetActiveReticle(GameObject nextReticle)
    {
        SetReticleActive(reticleDefault, false);
        SetReticleActive(reticleSwordMissing, false);
        SetReticleActive(reticleDashBlocked, false);
        SetReticleActive(reticleNoDashCharges, false);
        SetReticleActive(reticleNonStickySurface, false);

        activeReticle = nextReticle;
        SetReticleActive(activeReticle, true);
    }

    private void SetReticleActive(GameObject reticle, bool active)
    {
        if (reticle != null)
        {
            reticle.SetActive(active);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandlePlayerStateChanged(PlayerCore.PlayerState oldState, PlayerCore.PlayerState newState)
    {
        RefreshDashFrame();
        RefreshReticle();
    }

    private void HandleDashChargesChanged(int charges)
    {
        RefreshReticle();
    }

    private void HandleDashBlockedChanged(bool blocked)
    {
        RefreshReticle();
    }

    #endregion
}
