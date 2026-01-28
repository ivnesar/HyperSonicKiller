using UnityEngine;
using System;

// ════════════════════════════════════════════════════════════════════════════
// GAME INTERFACES - Central location for all shared interfaces
// ════════════════════════════════════════════════════════════════════════════

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
/// Interface for objects that can receive damage.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage);
    void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection);
}

/// <summary>
/// Combined interface for enemies that can be both damaged and stunned.
/// Most enemies should implement this.
/// </summary>
public interface IEnemy : IDamageable, IStunnable
{
    bool IsDead { get; }
    Transform Transform { get; }
}

/// <summary>
/// Legacy interface - kept for backwards compatibility.
/// Consider migrating to IEnemy instead.
/// </summary>
public interface INpcInteraction
{
    void OnMeeleDamage(int amount);
    void OnThrowStun(float duration);
    void OnSwordRemoved();
    void OnThrowDamage(int amount, Vector3 swordDirection, Vector3 hitPoint);
}