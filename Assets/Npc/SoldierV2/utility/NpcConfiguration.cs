using UnityEngine;

/// <summary>
/// ScriptableObject for configuring NPC stats and behavior.
/// Create different presets for different enemy variants (e.g., "Elite Soldier", "Rookie Soldier").
/// </summary>
[CreateAssetMenu(fileName = "NpcConfig", menuName = "Game/NPC Configuration")]
public class NpcConfiguration : ScriptableObject
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region General
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("General")]
    public string displayName = "Enemy";
    public int maxHealth = 100;
    public float moveSpeed = 4f;
    public float rotationSpeed = 10f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Detection
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Detection")]
    public float detectionRange = 25f;
    public float fieldOfView = 120f;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Soldier-Specific
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Soldier - Ranges")]
    public float preferredShootingRange = 12f;
    public float minShootingRange = 6f;
    public float maxShootingRange = 18f;

    [Header("Soldier - Combat Timing")]
    public float aimDuration = 0.6f;
    public float timeBetweenShots = 0.15f;
    public int shotsPerSalvo = 5;
    public float reloadDuration = 2.0f;

    [Header("Soldier - Accuracy")]
    [Range(0f, 1f)]
    public float baseAccuracy = 0.85f;
    public float accuracySpreadAngle = 5f;
    public int damagePerShot = 10;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Defender-Specific
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Defender - Protection")]
    public float protectDistance = 2.5f;
    public float repositionThreshold = 1.5f;

    [Header("Defender - Blocking")]
    public float blockDetectionRange = 4f;
    public float blockAngle = 90f;
    public float blockDuration = 0.8f;
    public float blockCooldown = 0.3f;
    public float perfectBlockWindow = 0.15f;

    [Header("Defender - Counter")]
    public float counterDuration = 0.6f;
    public float counterRange = 2.5f;
    public int counterDamage = 25;
    public float counterKnockback = 5f;

    #endregion
}
