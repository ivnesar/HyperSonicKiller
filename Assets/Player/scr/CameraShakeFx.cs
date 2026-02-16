using UnityEngine;

/// <summary>
/// Random camera shake effect for damage and block feedback.
/// Sits on the Cam_shakeFx GameObject in the camera hierarchy.
/// Subscribes to PlayerHealth and PlayerCombat events automatically.
/// 
/// Hierarchy: PlayerGO → CameraParent → [Cam_shakeFx] → Cam_motionFx → Camera
/// </summary>
public class CameraShakeFx : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Player Reference")]
    [Tooltip("Auto-found if left empty")]
    [SerializeField] private PlayerCore player;

    [Header("Damage Shake")]
    [SerializeField] private float damageShakeIntensity = 0.15f;
    [SerializeField] private float damageShakeDuration = 0.2f;
    [SerializeField] private float damageRotationIntensity = 3f;

    [Header("Block Shake")]
    [SerializeField] private float blockShakeIntensity = 0.06f;
    [SerializeField] private float blockShakeDuration = 0.1f;
    [SerializeField] private float blockRotationIntensity = 1f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private float currentIntensity;
    private float currentRotationIntensity;
    private float shakeDuration;
    private float shakeTimer;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        if (player == null)
            player = GetComponentInParent<PlayerCore>();

        if (player == null)
        {
            Debug.LogError("[CameraShakeFx] No PlayerCore found in parent hierarchy!");
            enabled = false;
            return;
        }

        SubscribeToEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void Update()
    {
        if (shakeTimer <= 0f)
        {
            // Reset to neutral when shake is done
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            return;
        }

        shakeTimer -= Time.unscaledDeltaTime;

        // Fade out intensity over duration
        float progress = shakeTimer / shakeDuration;
        float fadeIntensity = currentIntensity * progress;
        float fadeRotation = currentRotationIntensity * progress;

        // Random offset for position
        Vector3 posOffset = new Vector3(
            Random.Range(-fadeIntensity, fadeIntensity),
            Random.Range(-fadeIntensity, fadeIntensity),
            0f
        );

        // Random offset for rotation (Z-roll + slight X/Y)
        Vector3 rotOffset = new Vector3(
            Random.Range(-fadeRotation * 0.5f, fadeRotation * 0.5f),
            Random.Range(-fadeRotation * 0.5f, fadeRotation * 0.5f),
            Random.Range(-fadeRotation, fadeRotation)
        );

        transform.localPosition = posOffset;
        transform.localRotation = Quaternion.Euler(rotOffset);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Subscription
    // ════════════════════════════════════════════════════════════════════════

    private void SubscribeToEvents()
    {
        if (player.Health != null)
            player.Health.OnDamageTaken += HandleDamageTaken;

        if (player.Combat != null)
            player.Combat.OnBlockedHit += HandleBlockedHit;
    }

    private void UnsubscribeFromEvents()
    {
        if (player == null) return;

        if (player.Health != null)
            player.Health.OnDamageTaken -= HandleDamageTaken;

        if (player.Combat != null)
            player.Combat.OnBlockedHit -= HandleBlockedHit;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDamageTaken(float damage)
    {
        Shake(damageShakeIntensity, damageShakeDuration, damageRotationIntensity);
    }

    private void HandleBlockedHit()
    {
        Shake(blockShakeIntensity, blockShakeDuration, blockRotationIntensity);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Trigger a camera shake. Overwrites any active shake if stronger.
    /// </summary>
    public void Shake(float intensity, float duration, float rotationIntensity = 0f)
    {
        // Only overwrite if new shake is stronger or current shake is almost done
        if (intensity >= currentIntensity || shakeTimer <= 0.05f)
        {
            currentIntensity = intensity;
            currentRotationIntensity = rotationIntensity;
            shakeDuration = duration;
            shakeTimer = duration;
        }
    }

    #endregion
}
