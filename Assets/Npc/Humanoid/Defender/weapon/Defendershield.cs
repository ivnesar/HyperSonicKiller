using UnityEngine;

/// <summary>
/// Shield component for Defender NPC — FOV-based parry system.
/// 
/// HOW IT WORKS:
/// Instead of a physical collider, the shield uses an invisible detection cone
/// (FOV) projected in the NPC's forward direction. If the player is inside this
/// cone when they attack (melee dash or thrown sword), the attack is parried
/// and the player is forced into the Exhausted state.
/// 
/// The shield mesh is purely visual — it has no gameplay role.
/// 
/// SETUP:
/// 1. Attach this script to the Defender NPC GameObject (same as DefenderNpc).
/// 2. Tweak FOV angle, range, and height offset in the Inspector.
/// 3. Remove any collider from the shield mesh (or disable interaction).
/// 4. Player scripts call IsBlockingAttackFrom() before dealing damage.
/// </summary>
public class DefenderShield : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Shield FOV Detection")]
    [Tooltip("Total cone angle in degrees (e.g. 120 = 60° per side)")]
    [SerializeField] private float shieldAngle = 120f;

    [Tooltip("How far the shield cone reaches")]
    [SerializeField] private float shieldRange = 2f;

    [Tooltip("Height offset above root position for the cone origin")]
    [SerializeField] private float heightOffset = 1.5f;

    [Header("Feedback Hooks (Optional)")]
    [SerializeField] private AudioClip parrySound;
    [SerializeField] private ParticleSystem parryEffect;

    [Header("Gizmo Settings")]
    [SerializeField] private Color gizmoColor = new Color(0f, 0.8f, 1f, 0.25f);
    [SerializeField] private bool alwaysShowGizmo = false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private DefenderNpc defender;
    private AudioSource audioSource;
    private PlayerCore cachedPlayerCore;
    private PlayerCombat cachedPlayerCombat;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        defender = GetComponent<DefenderNpc>();

        if (defender == null)
        {
            Debug.LogError($"[DefenderShield] No DefenderNpc found on {gameObject.name}! " +
                           "This script must be on the same GameObject as DefenderNpc.");
            enabled = false;
            return;
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void Start()
    {
        CachePlayerReferences();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API — Called by Player Scripts
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks whether the shield is currently blocking an attack from the given position.
    /// Call this BEFORE dealing damage to the Defender.
    /// 
    /// Returns true if the attacker is inside the shield's FOV cone.
    /// </summary>
    public bool IsBlockingAttackFrom(Vector3 attackerPosition)
    {
        if (defender == null || defender.IsDead || defender.IsStunned)
            return false;

        Vector3 origin = GetConeOrigin();
        Vector3 toAttacker = attackerPosition - origin;

        // Flatten to horizontal for angle check (shield doesn't care about vertical angle)
        Vector3 toAttackerFlat = new Vector3(toAttacker.x, 0f, toAttacker.z);
        Vector3 forwardFlat = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

        if (toAttackerFlat.sqrMagnitude < 0.001f)
            return false;

        // Range check (horizontal distance)
        float distance = toAttackerFlat.magnitude;
        if (distance > shieldRange)
            return false;

        // Angle check
        float angle = Vector3.Angle(forwardFlat, toAttackerFlat.normalized);
        float halfAngle = shieldAngle * 0.5f;

        return angle <= halfAngle;
    }

    /// <summary>
    /// Called by PlayerDash/PlayerCombat when a melee attack is parried.
    /// Exhausts the player, triggers the Defender's shield hit reaction, and plays feedback.
    /// </summary>
    public void ParryMeleeAttack()
    {
        EnsurePlayerReferences();

        if (cachedPlayerCombat != null)
            cachedPlayerCombat.ForceExhaust();

        // Defender spielt Schild-Treffer-Reaktion (One-Shot auf Upper-Layer)
        if (defender != null)
            defender.OnShieldBlocked();

        PlayParryFeedback();

        Debug.Log("[DefenderShield] Melee attack parried! Player exhausted.");
    }

    /// <summary>
    /// Called by ThrownSword when it hits the Defender and the shield blocks it.
    /// Forces the sword to return and exhausts the player.
    /// </summary>
    public void ParryThrownSword(ThrownSword sword)
    {
        if (sword == null) return;

        EnsurePlayerReferences();

        // Force sword to return immediately
        sword.ForceReturnFromShield();

        // Exhaust the player
        if (cachedPlayerCombat != null)
            cachedPlayerCombat.ForceExhaust();

        PlayParryFeedback();

        Debug.Log("[DefenderShield] Thrown sword parried! Sword returned, player exhausted.");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the world-space origin of the shield detection cone.
    /// </summary>
    private Vector3 GetConeOrigin()
    {
        return transform.position + Vector3.up * heightOffset;
    }

    private void CachePlayerReferences()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            cachedPlayerCore = player.GetComponent<PlayerCore>();
            cachedPlayerCombat = player.GetComponent<PlayerCombat>();
        }
    }

    private void EnsurePlayerReferences()
    {
        if (cachedPlayerCombat == null)
            CachePlayerReferences();
    }

    private void PlayParryFeedback()
    {
        if (parrySound != null && audioSource != null)
            audioSource.PlayOneShot(parrySound);

        if (parryEffect != null)
            parryEffect.Play();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmo Visualization
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (alwaysShowGizmo)
            DrawShieldCone();
    }

    private void OnDrawGizmosSelected()
    {
        if (!alwaysShowGizmo)
            DrawShieldCone();
    }

    private void DrawShieldCone()
    {
        Vector3 origin = transform.position + Vector3.up * heightOffset;
        Vector3 forward = transform.forward;
        float halfAngle = shieldAngle * 0.5f;

        // ── Solid cone fill ──────────────────────────────────────────────
        Gizmos.color = gizmoColor;

        int segments = 20;
        Vector3 previousPoint = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 direction = Quaternion.Euler(0f, currentAngle, 0f) * forward;
            Vector3 endPoint = origin + direction * shieldRange;

            // Lines from origin to arc edge
            Gizmos.DrawLine(origin, endPoint);

            // Arc line connecting edge points
            if (i > 0)
                Gizmos.DrawLine(previousPoint, endPoint);

            previousPoint = endPoint;
        }

        // ── Wireframe outline (brighter) ─────────────────────────────────
        Color outlineColor = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.8f);
        Gizmos.color = outlineColor;

        // Left and right boundary lines
        Vector3 leftDir = Quaternion.Euler(0f, -halfAngle, 0f) * forward;
        Vector3 rightDir = Quaternion.Euler(0f, halfAngle, 0f) * forward;
        Gizmos.DrawLine(origin, origin + leftDir * shieldRange);
        Gizmos.DrawLine(origin, origin + rightDir * shieldRange);

        // Forward direction indicator
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(origin, origin + forward * (shieldRange * 0.5f));

        // Origin point
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(origin, 0.1f);

        // ── Height indicator lines ───────────────────────────────────────
        Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
        Gizmos.DrawLine(transform.position, origin);
    }

    #endregion
}
