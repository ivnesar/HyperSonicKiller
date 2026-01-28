using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple UI display for player health and sword status.
/// Shows BlockHP, accumulated damage, and sword disabled status.
/// </summary>
public class PlayerHealthUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FPSPlayerController player;
    
    [Header("UI Elements")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI dashText;
    [SerializeField] private TextMeshProUGUI playerState;
    [SerializeField] private Image swordStatusIcon;
    
    [Header("Visual Settings")]
    [SerializeField] private Color healthyColor = Color.green;
    [SerializeField] private Color damagedColor = Color.yellow;
    [SerializeField] private Color criticalColor = Color.red;
    [SerializeField] private Color swordEnabledColor = Color.white;
    [SerializeField] private Color swordDisabledColor = Color.red;

    private void Start()
    {
        // Find player if not assigned
        if (player == null)
        {
            player = FindObjectOfType<FPSPlayerController>();
        }

        if (player == null)
        {
            Debug.LogError("[PlayerHealthUI] No FPSPlayerController found!");
            enabled = false;
            return;
        }

        // Subscribe to player events
        //player.OnPlayerDamaged += HandlePlayerDamaged;
        //player.OnSwordDisabled += HandleSwordDisabled;
        //player.OnSwordRecovered += HandleSwordRecovered;
        player.OnPlayerDeath += HandlePlayerDeath;

        UpdateUI();
    }

    private void OnDestroy()
    {
        if (player != null)
        {
            //player.OnPlayerDamaged -= HandlePlayerDamaged;
            //player.OnSwordDisabled -= HandleSwordDisabled;
            //player.OnSwordRecovered -= HandleSwordRecovered;
            player.OnPlayerDeath -= HandlePlayerDeath;
        }
    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (player == null) return;

        //int currentHP = player.GetCurrentHP();
        //int maxHP = player.GetMaxHP();
        //int accumulatedDamage = player.GetAccumulatedDamage();
        //bool swordDisabled = player.IsSwordDisabled();
        int dashes = player.currentDashCharges;

        // Update health slider
        if (healthSlider != null)
        {
            //healthSlider.maxValue = maxHP;
            //healthSlider.value = currentHP;

            // Color based on health percentage
            // healthPercent = (float)currentHP / maxHP;
            // if (healthPercent > 0.6f)
            //     healthSlider.fillRect.GetComponent<Image>().color = healthyColor;
            // else if (healthPercent > 0.3f)
            //     healthSlider.fillRect.GetComponent<Image>().color = damagedColor;
            // else
            //     healthSlider.fillRect.GetComponent<Image>().color = criticalColor;
        }

        // Update health text
        if (healthText != null)
        {
            //healthText.text = $"HP: {currentHP} / {maxHP}";
        }
        
        
        if (dashText != null)
        {
            dashText.text = $"HP: {dashes}";
        }

        
        if (playerState != null)
        {
            playerState.text = $"HP: {player.GetCurrentState()}";
        }
        
        
        // Update status text
        // if (statusText != null)
        // {
        //     string status = "";
        //     
        //     if (player.IsDead())
        //     {
        //         status = "<color=red>DEAD</color>";
        //     }
        //     else if (swordDisabled)
        //     {
        //         status = "<color=red>SWORD DISABLED!</color>";
        //     }
        //     else
        //     {
        //         status = $"Damage Taken: {accumulatedDamage}";
        //     }
        //     
        //     statusText.text = status;
        // }
        //
        // // Update sword icon
        // if (swordStatusIcon != null)
        // {
        //     swordStatusIcon.color = swordDisabled ? swordDisabledColor : swordEnabledColor;
        // }
    }

    private void HandlePlayerDamaged(int damage, int currentHP, int maxHP)
    {
        UpdateUI();
    }

    private void HandleSwordDisabled(float recoveryTime)
    {
        Debug.Log($"[PlayerHealthUI] Sword disabled for {recoveryTime} seconds!");
        UpdateUI();
    }

    private void HandleSwordRecovered()
    {
        Debug.Log("[PlayerHealthUI] Sword recovered!");
        UpdateUI();
    }

    private void HandlePlayerDeath()
    {
        Debug.Log("[PlayerHealthUI] Player died!");
        UpdateUI();
    }
}