using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple UI display for player health and combat status.
/// Subscribes to events from the new player system.
/// </summary>
public class PlayerHealthUI : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector References
    // ════════════════════════════════════════════════════════════════════════

    [Header("Player Reference")]
    [SerializeField] private PlayerCore player;

    [Header("Health UI")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Image healthFill;

    [Header("Block/Shield UI")]
    [SerializeField] private Slider blockSlider;
    [SerializeField] private TextMeshProUGUI blockText;
    [SerializeField] private Image blockFill;

    [Header("Status UI")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI dashChargesText;

    [Header("Colors")]
    [SerializeField] private Color healthyColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color damagedColor = new Color(0.9f, 0.7f, 0.1f);
    [SerializeField] private Color criticalColor = new Color(0.9f, 0.2f, 0.2f);
    [SerializeField] private Color blockFullColor = new Color(0.3f, 0.6f, 0.9f);
    [SerializeField] private Color blockLowColor = new Color(0.9f, 0.4f, 0.1f);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        // Auto-find player if not assigned
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerCore>();
        }

        if (player == null)
        {
            Debug.LogError("[PlayerHealthUI] No PlayerCore found!");
            enabled = false;
            return;
        }

        SubscribeToEvents();
        InitializeUI();
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
        if (player.Health != null)
        {
            player.Health.OnHealthChanged += UpdateHealthDisplay;
            player.Health.OnDeath += HandleDeath;
        }

        if (player.Combat != null)
        {
            player.Combat.OnBlockHPChanged += UpdateBlockDisplay;
            player.Combat.OnGuardBroken += HandleGuardBroken;
            player.Combat.OnGuardRestored += HandleGuardRestored;
            player.Combat.OnCombatStateChanged += UpdateCombatStatus;
        }

        if (player.Dash != null)
        {
            player.Dash.OnChargesChanged += UpdateDashCharges;
        }

        player.OnPlayerDeath += HandleDeath;
        player.OnPlayerRevive += HandleRevive;
    }

    private void UnsubscribeFromEvents()
    {
        if (player == null) return;

        if (player.Health != null)
        {
            player.Health.OnHealthChanged -= UpdateHealthDisplay;
            player.Health.OnDeath -= HandleDeath;
        }

        if (player.Combat != null)
        {
            player.Combat.OnBlockHPChanged -= UpdateBlockDisplay;
            player.Combat.OnGuardBroken -= HandleGuardBroken;
            player.Combat.OnGuardRestored -= HandleGuardRestored;
            player.Combat.OnCombatStateChanged -= UpdateCombatStatus;
        }

        if (player.Dash != null)
        {
            player.Dash.OnChargesChanged -= UpdateDashCharges;
        }

        player.OnPlayerDeath -= HandleDeath;
        player.OnPlayerRevive -= HandleRevive;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region UI Updates
    // ════════════════════════════════════════════════════════════════════════

    private void InitializeUI()
    {
        // Set initial values
        if (player.Health != null)
        {
            UpdateHealthDisplay(player.Health.CurrentHP, player.Health.MaxHP);
        }

        if (player.Combat != null)
        {
            UpdateBlockDisplay(player.Combat.CurrentBlockHP, player.Combat.MaxBlockHP);
        }

        if (player.Dash != null)
        {
            UpdateDashCharges(player.Dash.CurrentCharges);
        }

        UpdateStatus("");
    }

    private void UpdateHealthDisplay(float current, float max)
    {
        float percent = current / max;

        if (healthSlider != null)
        {
            healthSlider.maxValue = max;
            healthSlider.value = current;
        }

        if (healthText != null)
        {
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        if (healthFill != null)
        {
            if (percent > 0.6f)
                healthFill.color = healthyColor;
            else if (percent > 0.3f)
                healthFill.color = damagedColor;
            else
                healthFill.color = criticalColor;
        }
    }

    private void UpdateBlockDisplay(float current, float max)
    {
        float percent = current / max;

        if (blockSlider != null)
        {
            blockSlider.maxValue = max;
            blockSlider.value = current;
        }

        if (blockText != null)
        {
            blockText.text = $"Block: {Mathf.CeilToInt(current)}";
        }

        if (blockFill != null)
        {
            blockFill.color = Color.Lerp(blockLowColor, blockFullColor, percent);
        }
    }

    private void UpdateDashCharges(int charges)
    {
        if (dashChargesText != null)
        {
            dashChargesText.text = $"Dash: {charges}";
        }
    }

    private void UpdateCombatStatus(PlayerCombat.CombatState state)
    {
        string status = state switch
        {
            PlayerCombat.CombatState.Blocking => "BLOCKING",
            PlayerCombat.CombatState.Stunned => "<color=red>STUNNED</color>",
            PlayerCombat.CombatState.Disarmed => "Sword Thrown",
            PlayerCombat.CombatState.Attacking => "ATTACK",
            _ => ""
        };

        UpdateStatus(status);
    }

    private void UpdateStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDeath()
    {
        UpdateStatus("<color=red>DEAD</color>");
    }

    private void HandleRevive()
    {
        UpdateStatus("");
        InitializeUI();
    }

    private void HandleGuardBroken()
    {
        UpdateStatus("<color=red>GUARD BROKEN!</color>");
    }

    private void HandleGuardRestored()
    {
        UpdateStatus("Guard Restored");
        
        // Clear status after a moment
        Invoke(nameof(ClearStatus), 1f);
    }

    private void ClearStatus()
    {
        if (player != null && !player.IsDead && player.Combat != null)
        {
            if (player.Combat.CurrentState == PlayerCombat.CombatState.Idle)
            {
                UpdateStatus("");
            }
        }
    }

    #endregion
}
