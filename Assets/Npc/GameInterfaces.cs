using UnityEngine;
using System;

// ════════════════════════════════════════════════════════════════════════════
// SHARED INTERFACES - Place in a central location to avoid duplicates
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Interface for NPC interaction with player attacks.
/// Implement on any enemy or destructible object.
/// </summary>
public interface INpcInteraction
{
    void OnMeeleDamage(int amount);
    void OnThrowStun(float duration);
    void OnSwordRemoved();
    void OnThrowDamage(int amount, Vector3 swordDirection, Vector3 hitPoint);
}

/// <summary>
/// Interface for thrown sword behavior.
/// Implement this on your thrown sword prefab.
/// </summary>
public interface IThrownSword
{
    event Action OnRecalled;
    void Initialize(Vector3 direction, float force, float maxDistance, LayerMask layers);
    void Recall(Transform target);
}

/// <summary>
/// Generic damage interface.
/// Implement on enemies, destructibles, etc.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage);
}
