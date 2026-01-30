using UnityEngine;
using System;

/// <summary>
/// Simple health system for BASE HP only.
/// Block/Shield HP is handled by PlayerCombat.
/// This keeps health management clean and focused.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerHealth : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action OnDeath;
    public event Action<float, float> OnHealthChanged;  // (current, max)
    public event Action<float> OnDamageTaken;           // damage amount
    public event Action<float> OnHealed;                // heal amount

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Health Settings")]
    [SerializeField] private float maxHP = 100f;

    [Header("Debug (Read Only)")]
    [SerializeField] private float currentHP;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float HPPercent => currentHP / maxHP;
    
    /// <summary>
    /// Delegates to PlayerCore.IsDead to avoid duplicate state tracking.
    /// </summary>
    public bool IsDead => core != null && core.IsDead;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        currentHP = maxHP;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply damage to base HP.
    /// Called by PlayerCore after block check.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (IsDead || damage <= 0) return;

        currentHP -= damage;
        OnDamageTaken?.Invoke(damage);
        OnHealthChanged?.Invoke(currentHP, maxHP);

        if (currentHP <= 0)
        {
            currentHP = 0;
            Die();
        }
    }

    /// <summary>
    /// Heal the player.
    /// </summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0) return;

        float previousHP = currentHP;
        currentHP = Mathf.Min(currentHP + amount, maxHP);

        float actualHeal = currentHP - previousHP;
        if (actualHeal > 0)
        {
            OnHealed?.Invoke(actualHeal);
            OnHealthChanged?.Invoke(currentHP, maxHP);
        }
    }

    /// <summary>
    /// Reset health to max (e.g., on revive or checkpoint).
    /// </summary>
    public void ResetHealth()
    {
        currentHP = maxHP;
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }

    /// <summary>
    /// Set max HP (e.g., from upgrades).
    /// </summary>
    public void SetMaxHP(float newMax, bool healToFull = false)
    {
        maxHP = Mathf.Max(1f, newMax);
        
        if (healToFull)
        {
            currentHP = maxHP;
        }
        else
        {
            currentHP = Mathf.Min(currentHP, maxHP);
        }

        OnHealthChanged?.Invoke(currentHP, maxHP);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal
    // ════════════════════════════════════════════════════════════════════════

    private void Die()
    {
        // Event nur einmal feuern - PlayerCore setzt dann IsDead auf true
        OnDeath?.Invoke();
        Debug.Log("[PlayerHealth] Player died!");
    }

    #endregion
}