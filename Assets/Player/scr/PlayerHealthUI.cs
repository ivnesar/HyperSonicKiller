using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI display for player HP and combat status.
/// HP is the only defensive resource. Dash-blocked and Exhausted remain status icons.
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

    [Header("Status UI")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI dashChargesText;

    [Header("Dash Blocked")]
    [SerializeField] private RawImage dashBlockedIcon;

    [Header("Exhausted")]
    [SerializeField] private RawImage exhaustedIcon;

    [Header("Game Over Panel")]
    [Tooltip("Eltern-GameObject des Game-Over-Panels (wird bei Tod aktiviert)")]
    [SerializeField] private GameObject gameOverPanel;

    [Tooltip("Text für 'Killed by [NPC Name]'")]
    [SerializeField] private TextMeshProUGUI killedByText;

    [Tooltip("Text für die tödliche Schadensmenge")]
    [SerializeField] private TextMeshProUGUI deathDamageText;

    [Header("Colors")]
    [SerializeField] private Color healthyColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color damagedColor = new Color(0.9f, 0.7f, 0.1f);
    [SerializeField] private Color criticalColor = new Color(0.9f, 0.2f, 0.2f);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    // Aktueller Zustand der beiden Action-Icons (für die Prioritäts-Logik).
    private bool isDashBlocked;
    private bool isExhausted;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        // Auto-find player if not assigned.
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
        }

        if (player.Combat != null)
        {
            player.Combat.OnExhausted += HandleExhausted;
            player.Combat.OnExhaustionRecovered += HandleExhaustionRecovered;
            player.Combat.OnCombatStateChanged += UpdateCombatStatus;
        }

        if (player.Dash != null)
        {
            player.Dash.OnChargesChanged += UpdateDashCharges;
            player.Dash.OnDashBlockedChanged += UpdateDashBlockedIcon;
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
        }

        if (player.Combat != null)
        {
            player.Combat.OnExhausted -= HandleExhausted;
            player.Combat.OnExhaustionRecovered -= HandleExhaustionRecovered;
            player.Combat.OnCombatStateChanged -= UpdateCombatStatus;
        }

        if (player.Dash != null)
        {
            player.Dash.OnChargesChanged -= UpdateDashCharges;
            player.Dash.OnDashBlockedChanged -= UpdateDashBlockedIcon;
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
        if (player.Health != null)
        {
            UpdateHealthDisplay(player.Health.CurrentHP, player.Health.MaxHP);
        }

        if (player.Dash != null)
        {
            UpdateDashCharges(player.Dash.CurrentCharges);
        }

        if (dashBlockedIcon != null)
            dashBlockedIcon.gameObject.SetActive(false);

        if (exhaustedIcon != null)
            exhaustedIcon.gameObject.SetActive(false);

        isDashBlocked = false;
        isExhausted = false;

        // Game Over Panel zu Beginn verstecken.
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        UpdateStatus("");
    }

    private void UpdateHealthDisplay(float current, float max)
    {
        float percent = max > 0f ? current / max : 0f;

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

    private void UpdateDashCharges(int charges)
    {
        if (dashChargesText != null)
        {
            dashChargesText.text = $"Dash: {charges}";
        }
    }

    private void UpdateDashBlockedIcon(bool isBlocked)
    {
        isDashBlocked = isBlocked;
        RefreshActionIcons();
    }

    /// <summary>
    /// Entscheidet, welches der beiden Action-Icons sichtbar ist.
    /// Dash-Blocked hat Priorität: Sind beide Zustände gleichzeitig aktiv,
    /// wird nur das Dash-Blocked-Icon gezeigt, damit sich die Icons nicht überlappen.
    /// </summary>
    private void RefreshActionIcons()
    {
        bool showDashBlocked = isDashBlocked;
        bool showExhausted = isExhausted && !isDashBlocked;

        if (dashBlockedIcon != null)
            dashBlockedIcon.gameObject.SetActive(showDashBlocked);

        if (exhaustedIcon != null)
            exhaustedIcon.gameObject.SetActive(showExhausted);
    }

    private void UpdateCombatStatus(PlayerCombat.CombatState state)
    {
        string status = state switch
        {
            PlayerCombat.CombatState.Exhausted => "<color=orange>EXHAUSTED</color>",
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
        ShowGameOverPanel();
    }

    private void HandleRevive()
    {
        CancelInvoke(nameof(ClearStatus));
        UpdateStatus("");
        HideGameOverPanel();
        InitializeUI();
    }

    private void HandleExhausted()
    {
        CancelInvoke(nameof(ClearStatus));
        isExhausted = true;
        RefreshActionIcons();

        UpdateStatus("<color=orange>EXHAUSTED!</color>");
    }

    private void HandleExhaustionRecovered()
    {
        isExhausted = false;
        RefreshActionIcons();

        UpdateStatus("Recovered");

        // Clear status after a moment.
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

    // ════════════════════════════════════════════════════════════════════════
    #region Game Over Panel
    // ════════════════════════════════════════════════════════════════════════

    private void ShowGameOverPanel()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        // Killer-Info aus PlayerCore lesen.
        string killerName = player.LastDamageSourceName;
        float killerDamage = player.LastDamageAmount;

        if (killedByText != null)
        {
            killedByText.text = !string.IsNullOrEmpty(killerName)
                ? $"Killed by {killerName}"
                : "Killed";
        }

        if (deathDamageText != null)
        {
            deathDamageText.text = killerDamage > 0f
                ? $"{Mathf.CeilToInt(killerDamage)} damage"
                : "";
        }
    }

    private void HideGameOverPanel()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    #endregion
}
