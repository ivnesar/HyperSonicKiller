using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GAME INTERFACES - Central location for all shared interfaces
// ════════════════════════════════════════════════════════════════════════════
//
// Interface Hierarchy:
//   IDamageable      - Base: anything that can take damage
//   IStunnable       - Can be stunned (temporary disable)
//   IEnemy           - Full enemy with all combat interactions
//
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Interface for objects that can receive damage.
/// Use for destructible objects, enemies, or any damageable entity.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Simple damage without position info.
    /// </summary>
    void TakeDamage(float damage);

    /// <summary>
    /// Damage with hit information for effects/ragdoll.
    /// </summary>
    void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection);
}

/// <summary>
/// Interface for objects that can be stunned.
/// Implement on enemies that should be affected by thrown sword.
/// </summary>
public interface IStunnable
{
    /// <summary>
    /// Apply stun for the specified duration.
    /// If already stunned, this should extend/reset the stun timer.
    /// </summary>
    void ApplyStun(float duration);

    /// <summary>
    /// Returns true if currently stunned.
    /// </summary>
    bool IsStunned { get; }

    /// <summary>
    /// Returns remaining stun time (0 if not stunned).
    /// </summary>
    float RemainingStunTime { get; }
}

/// <summary>
/// Full enemy interface combining damage, stun, and combat-specific interactions.
/// 
/// Use this for NPCs that:
/// - Can be damaged by melee attacks
/// - Can be hit and stunned by thrown sword
/// - Can have sword embedded in them
/// </summary>
public interface IEnemy : IDamageable, IStunnable
{
    // ────────────────────────────────────────────────────────────────────────
    // Properties
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the enemy is dead.
    /// </summary>
    bool IsDead { get; }

    /// <summary>
    /// Transform of the enemy (for positioning, distance checks).
    /// </summary>
    Transform Transform { get; }

    // ────────────────────────────────────────────────────────────────────────
    // Melee Combat
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when hit by a melee attack (player sword swing).
    /// </summary>
    void OnMeleeDamage(int damage);

    // ────────────────────────────────────────────────────────────────────────
    // Thrown Sword Interaction
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when hit by a thrown sword (projectile phase).
    /// </summary>
    void OnThrownSwordHit(int damage, Vector3 swordDirection, Vector3 hitPoint);

    /// <summary>
    /// Called when the thrown sword embeds into this enemy.
    /// Enemy should enter stunned state indefinitely until sword is removed.
    /// </summary>
    void OnSwordEmbedded();

    /// <summary>
    /// Called when the embedded sword is recalled/removed.
    /// Deals damage to the enemy and applies residual stun.
    /// </summary>
    /// <param name="damage">Damage dealt when sword is removed</param>
    /// <param name="residualStunDuration">Duration of stun after sword removal</param>
    void OnSwordRemoved(int damage, float residualStunDuration);

    // ────────────────────────────────────────────────────────────────────────
    // Ranged Damage
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when hit by a bullet or projectile.
    /// </summary>
    void OnBulletDamage(int damage, Vector3 bulletDirection, Vector3 hitPoint);
}