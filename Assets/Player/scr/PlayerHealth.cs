using UnityEngine;
using System;

/// <summary>
/// Single defensive resource for the player.
/// HP takes all incoming damage, regenerates after a short delay,
/// and reaching 0 HP triggers game over.
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerHealth : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    public event Action OnDeath;
    public event Action<float, float> OnHealthChanged;  // (current, max)
    public event Action<float> OnDamageTaken;           // actual damage amount
    public event Action<float> OnHealed;                // actual heal amount

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Health Settings")]
    [SerializeField] private float maxHP = 100f;

    [Header("HP Regeneration")]
    [Tooltip("Seconds after taking damage before HP starts regenerating.")]
    [SerializeField] private float hpRegenDelay = 1f;

    [Tooltip("HP regenerated per second after the delay.")]
    [SerializeField] private float hpRegenRate = 30f;

    [Header("Debug (Read Only)")]
    [SerializeField] private float currentHP;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private float lastDamageTime = -Mathf.Infinity;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float HPPercent => maxHP > 0f ? currentHP / maxHP : 0f;
    public float HPRegenDelay => hpRegenDelay;
    public float HPRegenRate => hpRegenRate;

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

    private void Update()
    {
        HandleHPRegeneration();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply damage directly to HP.
    /// Returns true when HP was actually reduced.
    /// </summary>
    public bool TakeDamage(float damage)
    {
        if (IsDead || damage <= 0f || currentHP <= 0f) return false;

        float previousHP = currentHP;
        currentHP = Mathf.Max(0f, currentHP - damage);
        lastDamageTime = Time.time;

        float actualDamage = previousHP - currentHP;
        if (actualDamage <= 0f) return false;

        OnDamageTaken?.Invoke(actualDamage);
        OnHealthChanged?.Invoke(currentHP, maxHP);

        if (currentHP <= 0f)
        {
            Die();
        }

        return true;
    }

    /// <summary>
    /// Heal the player.
    /// </summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;

        float previousHP = currentHP;
        currentHP = Mathf.Min(currentHP + amount, maxHP);

        float actualHeal = currentHP - previousHP;
        if (actualHeal > 0f)
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
        lastDamageTime = -Mathf.Infinity;
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
    #region Regeneration
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Regenerates HP using the previous block-style behavior:
    /// only while combat is idle, only after a delay, and gradually over time.
    /// </summary>
    private void HandleHPRegeneration()
    {
        if (IsDead) return;
        if (currentHP >= maxHP) return;
        if (Time.time < lastDamageTime + hpRegenDelay) return;

        // Matches the old block behavior: no regeneration during attack,
        // disarmed, or exhausted combat states.
        if (core != null && core.Combat != null &&
            core.Combat.CurrentState != PlayerCombat.CombatState.Idle)
        {
            return;
        }

        float previousHP = currentHP;
        currentHP = Mathf.MoveTowards(currentHP, maxHP, hpRegenRate * Time.deltaTime);

        if (!Mathf.Approximately(previousHP, currentHP))
        {
            OnHealthChanged?.Invoke(currentHP, maxHP);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal
    // ════════════════════════════════════════════════════════════════════════

    private void Die()
    {
        // Event only fires once because PlayerCore switches to Dead afterwards.
        OnDeath?.Invoke();
        Debug.Log("[PlayerHealth] Player died!");
    }

    #endregion
}
