using UnityEngine;

/// <summary>
/// Shield component for Defender NPC.
/// Handles all shield-specific interactions:
/// - Blocks dash attacks from the front → instant counter (500 damage = death)
/// - Blocks thrown sword → reflects sword, exhausts player
/// 
/// SETUP:
/// 1. Attach this script to the Shield mesh GameObject (child of Defender)
/// 2. Ensure Shield has a Collider (trigger or solid)
/// 3. Set Shield GameObject to "Shield" layer
/// 4. Assign references in Inspector or let it auto-find them
/// </summary>
public class DefenderShield : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Counter Attack (Dash Block)")]
    [Tooltip("Damage dealt to player when they dash into the shield from the front")]
    [SerializeField] private int counterDamage = 500;

    [Header("References (Auto-assigned if empty)")]
    [SerializeField] private DefenderNpc defender;

    [Header("Feedback Hooks (Optional - for future use)")]
    [SerializeField] private AudioClip shieldBlockDashSound;
    [SerializeField] private AudioClip shieldBlockSwordSound;
    [SerializeField] private ParticleSystem shieldBlockEffect;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore cachedPlayerCore;
    private AudioSource audioSource;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Auto-find DefenderNpc in parent hierarchy
        if (defender == null)
        {
            defender = GetComponentInParent<DefenderNpc>();
        }

        if (defender == null)
        {
            Debug.LogError($"[DefenderShield] No DefenderNpc found in parent hierarchy of {gameObject.name}!");
        }

        // Get or add AudioSource for feedback
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    private void Start()
    {
        // Cache player reference
        CachePlayerReference();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API - Called by PlayerDash
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by PlayerDash when the shield is within the attack radius during a dash.
    /// Checks if attack comes from the front (shield blocks) or back (attack passes through).
    /// </summary>
    /// <param name="attackOrigin">Position where the dash started</param>
    /// <returns>True if shield blocked the attack (player should take damage), false if attack passes through</returns>
    public bool OnHitByDashAttack(Vector3 attackOrigin)
    {
        if (defender == null) return false;

        // Calculate attack direction (from player to defender)
        Vector3 defenderPosition = defender.transform.position;
        Vector3 attackDirection = (defenderPosition - attackOrigin).normalized;
        
        // Get defender's forward direction (where the shield faces)
        Vector3 shieldForward = defender.transform.forward;
        
        // Calculate angle between attack direction and shield forward
        // If angle < 90°: attack comes from the front → BLOCKED
        // If angle >= 90°: attack comes from behind → PASSES THROUGH
        Vector3 directionToAttacker = (attackOrigin - defenderPosition).normalized;
        float angle = Vector3.Angle(shieldForward, directionToAttacker);

        if (angle < 90f)
        {
            // Attack from front - BLOCKED, counter attack!
            ExecuteCounterAttack();
            PlayBlockFeedback(shieldBlockDashSound);
            Debug.Log($"[DefenderShield] Dash blocked! Angle: {angle:F1}° - Counter attack!");
            return true;
        }
        else
        {
            // Attack from behind - passes through
            Debug.Log($"[DefenderShield] Attack from behind. Angle: {angle:F1}° - Passes through");
            return false;
        }
    }

    /// <summary>
    /// Called by ThrownSword when it hits the shield.
    /// Reflects the sword and exhausts the player.
    /// </summary>
    /// <param name="sword">The thrown sword that hit the shield</param>
    public void OnHitByThrownSword(ThrownSword sword)
    {
        if (sword == null) return;

        EnsurePlayerReference();

        // Force sword to return immediately (no embed, no stun on defender)
        sword.ForceReturnFromShield();

        // Exhaust the player (BlockHP = 0, can't attack for a duration)
        if (cachedPlayerCore != null && cachedPlayerCore.Combat != null)
        {
            cachedPlayerCore.Combat.ForceExhaust();
        }

        PlayBlockFeedback(shieldBlockSwordSound);

        Debug.Log("[DefenderShield] Thrown sword blocked! Player exhausted.");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Logic
    // ════════════════════════════════════════════════════════════════════════

    private void ExecuteCounterAttack()
    {
        EnsurePlayerReference();

        if (cachedPlayerCore != null)
        {
            // Deal lethal damage directly (bypasses block since this IS the counter)
            cachedPlayerCore.TakeDirectDamage(counterDamage);
            
            Debug.Log($"[DefenderShield] Counter attack dealt {counterDamage} damage to player!");
        }
    }

    private void CachePlayerReference()
    {
        if (cachedPlayerCore == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                cachedPlayerCore = player.GetComponent<PlayerCore>();
            }
        }
    }

    private void EnsurePlayerReference()
    {
        if (cachedPlayerCore == null)
        {
            CachePlayerReference();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Feedback (Hooks for future VFX/SFX)
    // ════════════════════════════════════════════════════════════════════════

    private void PlayBlockFeedback(AudioClip clip)
    {
        // Play sound if available
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }

        // Play particle effect if available
        if (shieldBlockEffect != null)
        {
            shieldBlockEffect.Play();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Visualization
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        if (defender == null)
        {
            defender = GetComponentInParent<DefenderNpc>();
        }

        if (defender != null)
        {
            // Draw shield forward direction
            Gizmos.color = Color.cyan;
            Vector3 shieldPos = transform.position;
            Gizmos.DrawRay(shieldPos, defender.transform.forward * 2f);

            // Draw "safe zone" behind the defender
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Vector3 behindPos = defender.transform.position - defender.transform.forward * 1.5f;
            Gizmos.DrawWireSphere(behindPos, 0.5f);
        }
    }

    #endregion
}