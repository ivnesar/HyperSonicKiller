using UnityEngine;
using System;

/// <summary>
/// Manages player health with a two-layer system:
/// 1. Shield HP (sword blocking) - absorbs damage first
/// 2. Base HP - takes damage when shield is broken
/// 
/// When shield breaks: 2 second cooldown before shield regenerates to full
/// When shield is damaged but not broken: 1 second delay, then regenerates to full
/// While shield is broken: player cannot attack or throw
/// While sword is thrown: player cannot block
/// </summary>
public class PlayerHealthSystem : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Events
    // ────────────────────────────────────────────────────────────────────────────────

    public event Action OnDeath;
    public event Action OnShieldBroken;
    public event Action OnShieldRestored;
    public event Action<float, float> OnShieldChanged;  // (current, max)
    public event Action<float, float> OnHealthChanged;  // (current, max)
    public event Action<float> OnDamageTaken;           // damage amount

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Base Health")]
    [SerializeField] private float maxBaseHP = 100f;

    [Header("Shield (Sword Block)")]
    [SerializeField] private float maxShieldHP = 100f;
    [SerializeField] private float shieldRegenDelay = 1f;           // Zeit ohne Schaden bevor Regen startet
    [SerializeField] private float shieldRegenDuration = 1f;        // Zeit bis Schild voll ist
    [SerializeField] private float shieldBrokenCooldown = 2f;       // Zeit bis Schild wieder verfügbar

    [Header("Debug (Read Only)")]
    [SerializeField] private float currentBaseHP;
    [SerializeField] private float currentShieldHP;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime State
    // ────────────────────────────────────────────────────────────────────────────────

    private float lastDamageTime;
    private float shieldBrokenTime;
    private bool isShieldBroken;
    private bool isDead;

    // Referenz zum Combat System für Status-Checks
    private SwordCombatSystem combatSystem;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Properties
    // ────────────────────────────────────────────────────────────────────────────────

    public float CurrentBaseHP => currentBaseHP;
    public float MaxBaseHP => maxBaseHP;
    public float CurrentShieldHP => currentShieldHP;
    public float MaxShieldHP => maxShieldHP;
    
    public float BaseHPPercent => currentBaseHP / maxBaseHP;
    public float ShieldHPPercent => currentShieldHP / maxShieldHP;

    public bool IsShieldBroken => isShieldBroken;
    public bool IsDead => isDead;
    
    /// <summary>
    /// Kann der Spieler gerade blocken? (Schild nicht gebrochen UND Schwert nicht geworfen)
    /// </summary>
    public bool CanBlock => !isShieldBroken && !IsSwordThrown();

    /// <summary>
    /// Kann der Spieler gerade angreifen/werfen? (Schild nicht gebrochen)
    /// </summary>
    public bool CanAttackOrThrow => !isShieldBroken;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        currentBaseHP = maxBaseHP;
        currentShieldHP = maxShieldHP;
        combatSystem = GetComponent<SwordCombatSystem>();
    }

    private void Update()
    {
        if (isDead) return;

        HandleShieldRegeneration();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public API - Damage
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hauptfunktion für eingehenden Schaden.
    /// Prüft ob geblockt wird, verarbeitet Schild und Grund-HP entsprechend.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (isDead || damage <= 0) return;

        lastDamageTime = Time.time;
        OnDamageTaken?.Invoke(damage);

        // Prüfe ob Spieler gerade blockt
        if (IsActivelyBlocking())
        {
            ApplyDamageToShield(damage);
        }
        else
        {
            ApplyDamageToBaseHP(damage);
        }
    }

    /// <summary>
    /// Direkter Schaden an Grund-HP (ignoriert Schild komplett).
    /// Für spezielle Angriffe die nicht geblockt werden können.
    /// </summary>
    public void TakeDirectDamage(float damage)
    {
        if (isDead || damage <= 0) return;

        OnDamageTaken?.Invoke(damage);
        ApplyDamageToBaseHP(damage);
    }

    /// <summary>
    /// Heilung für Grund-HP
    /// </summary>
    public void Heal(float amount)
    {
        if (isDead || amount <= 0) return;

        currentBaseHP = Mathf.Min(currentBaseHP + amount, maxBaseHP);
        OnHealthChanged?.Invoke(currentBaseHP, maxBaseHP);
    }

    /// <summary>
    /// Setzt Spieler zurück (z.B. bei Checkpoint)
    /// </summary>
    public void ResetHealth()
    {
        currentBaseHP = maxBaseHP;
        currentShieldHP = maxShieldHP;
        isShieldBroken = false;
        isDead = false;

        OnHealthChanged?.Invoke(currentBaseHP, maxBaseHP);
        OnShieldChanged?.Invoke(currentShieldHP, maxShieldHP);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Damage Processing
    // ────────────────────────────────────────────────────────────────────────────────

    private void ApplyDamageToShield(float damage)
    {
        float remainingDamage = damage - currentShieldHP;
        currentShieldHP -= damage;

        if (currentShieldHP <= 0)
        {
            // Schild ist gebrochen!
            currentShieldHP = 0;
            BreakShield();

            // Überschüssiger Schaden geht an Grund-HP
            if (remainingDamage > 0)
            {
                ApplyDamageToBaseHP(remainingDamage);
            }
        }

        OnShieldChanged?.Invoke(currentShieldHP, maxShieldHP);
    }

    private void ApplyDamageToBaseHP(float damage)
    {
        currentBaseHP -= damage;
        OnHealthChanged?.Invoke(currentBaseHP, maxBaseHP);

        if (currentBaseHP <= 0)
        {
            currentBaseHP = 0;
            Die();
        }
    }

    private void BreakShield()
    {
        if (isShieldBroken) return;

        isShieldBroken = true;
        shieldBrokenTime = Time.time;
        OnShieldBroken?.Invoke();

        Debug.Log("[PlayerHealth] Shield broken! Cannot attack or throw for " + shieldBrokenCooldown + " seconds.");
    }

    private void Die()
    {
        if (isDead) return;

        isDead = true;
        OnDeath?.Invoke();

        Debug.Log("[PlayerHealth] Player died!");
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Shield Regeneration
    // ────────────────────────────────────────────────────────────────────────────────

    private void HandleShieldRegeneration()
    {
        if (isShieldBroken)
        {
            // Warte auf Cooldown nach Schild-Bruch
            if (Time.time >= shieldBrokenTime + shieldBrokenCooldown)
            {
                RestoreShield();
            }
        }
        else if (currentShieldHP < maxShieldHP)
        {
            // Schild beschädigt aber nicht gebrochen - regeneriere nach Delay
            if (Time.time >= lastDamageTime + shieldRegenDelay)
            {
                RegenerateShield();
            }
        }
    }

    private void RegenerateShield()
    {
        // Regeneriere über shieldRegenDuration auf Maximum
        float regenRate = maxShieldHP / shieldRegenDuration;
        currentShieldHP = Mathf.MoveTowards(currentShieldHP, maxShieldHP, regenRate * Time.deltaTime);
        OnShieldChanged?.Invoke(currentShieldHP, maxShieldHP);
    }

    private void RestoreShield()
    {
        isShieldBroken = false;
        currentShieldHP = maxShieldHP;
        OnShieldRestored?.Invoke();
        OnShieldChanged?.Invoke(currentShieldHP, maxShieldHP);

        Debug.Log("[PlayerHealth] Shield restored!");
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Status Checks
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prüft ob Spieler aktiv blockt (Block-Taste gedrückt UND Schwert verfügbar)
    /// </summary>
    private bool IsActivelyBlocking()
    {
        if (combatSystem == null) return false;
        if (isShieldBroken) return false;
        if (IsSwordThrown()) return false;

        return combatSystem.GetCurrentState() == SwordCombatSystem.CombatState.Blocking;
    }

    /// <summary>
    /// Prüft ob das Schwert gerade geworfen ist
    /// </summary>
    private bool IsSwordThrown()
    {
        if (combatSystem == null) return false;
        return combatSystem.GetCurrentState() == SwordCombatSystem.CombatState.Thrown;
    }

    #endregion
}