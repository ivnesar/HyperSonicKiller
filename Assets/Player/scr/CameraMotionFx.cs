using UnityEngine;

/// <summary>
/// Directional camera rotation "punch" for attack feedback.
/// Rotates smoothly toward a target angle, then smoothly back to neutral.
/// Sits on the Cam_motionFx GameObject in the camera hierarchy.
/// Subscribes to PlayerCombat.OnAttack automatically.
/// 
/// Hierarchy: PlayerGO → CameraParent → Cam_shakeFx → [Cam_motionFx] → Camera
/// </summary>
public class CameraMotionFx : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Player Reference")]
    [Tooltip("Auto-found if left empty")]
    [SerializeField] private PlayerCore player;

    [Header("Attack Punch Settings")]
    [Tooltip("Rotation angles for the punch (Z = roll is most noticeable)")]
    [SerializeField] private Vector3 attackPunchAngle = new Vector3(-2f, 0f, 4f);

    [Tooltip("Total duration of the punch (forward + return)")]
    [SerializeField] private float attackPunchDuration = 0.25f;

    [Tooltip("How much of the duration is spent reaching the target angle (0-1). Rest is return.")]
    [SerializeField, Range(0.1f, 0.5f)] private float attackForwardRatio = 0.3f;

    [Header("Randomization")]
    [Tooltip("Randomly flip the Z-roll direction for variety")]
    [SerializeField] private bool randomizeDirection = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private Vector3 currentPunchAngle;
    private float punchDuration;
    private float forwardRatio;
    private float punchTimer;
    private bool isPunching;

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
            Debug.LogError("[CameraMotionFx] No PlayerCore found in parent hierarchy!");
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
        if (!isPunching) return;

        punchTimer += Time.unscaledDeltaTime;

        float forwardTime = punchDuration * forwardRatio;
        float returnTime = punchDuration - forwardTime;

        if (punchTimer <= forwardTime)
        {
            // Phase 1: Rotate toward punch angle
            float t = punchTimer / forwardTime;
            t = EaseOutCubic(t);
            transform.localRotation = Quaternion.Euler(currentPunchAngle * t);
        }
        else if (punchTimer <= punchDuration)
        {
            // Phase 2: Return to neutral
            float t = (punchTimer - forwardTime) / returnTime;
            t = EaseInOutCubic(t);
            transform.localRotation = Quaternion.Euler(currentPunchAngle * (1f - t));
        }
        else
        {
            // Done
            transform.localRotation = Quaternion.identity;
            isPunching = false;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Subscription
    // ════════════════════════════════════════════════════════════════════════

    private void SubscribeToEvents()
    {
        if (player.Combat != null)
            player.Combat.OnAttack += HandleAttack;
    }

    private void UnsubscribeFromEvents()
    {
        if (player == null) return;

        if (player.Combat != null)
            player.Combat.OnAttack -= HandleAttack;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleAttack()
    {
        Punch(attackPunchAngle, attackPunchDuration, attackForwardRatio);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Trigger a directional rotation punch.
    /// </summary>
    /// <param name="angle">Target Euler angles to punch toward</param>
    /// <param name="duration">Total duration of the punch effect</param>
    /// <param name="fwdRatio">Fraction of duration spent reaching the target (0.1-0.5)</param>
    public void Punch(Vector3 angle, float duration, float fwdRatio = 0.3f)
    {
        currentPunchAngle = angle;

        // Randomly flip Z-roll direction for variety
        if (randomizeDirection && Random.value > 0.5f)
            currentPunchAngle.z = -currentPunchAngle.z;

        punchDuration = duration;
        forwardRatio = Mathf.Clamp(fwdRatio, 0.1f, 0.5f);
        punchTimer = 0f;
        isPunching = true;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Easing Helpers
    // ════════════════════════════════════════════════════════════════════════

    private static float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    #endregion
}
