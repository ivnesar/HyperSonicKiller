using UnityEngine;

/// <summary>
/// Debug-Komponente für Spieler-Werte im Inspector.
/// Zeigt Dash-Richtung, Zustände und Combat-Infos.
/// Orientiert sich an NpcDebugInspector.
/// 
/// UPDATED: Added Sprint debug info (isSprinting, sprintDashing, cooldown).
/// </summary>
public class PlayerDebugInspector : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region References
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private PlayerDash dash;
    private PlayerSprint sprint;
    private PlayerMovement movement;
    private PlayerHealth health;
    private PlayerCombat combat;
    private PlayerSwordThrow swordThrow;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector - Read Only State
    // ════════════════════════════════════════════════════════════════════════

    [Header("Player State")]
    [SerializeField] private string currentState;
    [SerializeField] private bool isDead;
    [SerializeField] private bool isInvulnerable;

    [Header("Health")]
    [SerializeField] private float currentHP;
    [SerializeField] private float maxHP;

    [Header("Combat")]
    [SerializeField] private string combatState;
    [SerializeField] private float blockHP;
    [SerializeField] private bool isExhausted;
    [SerializeField] private bool hasSword;

    [Header("Dash")]
    [SerializeField] private int dashCharges;
    [SerializeField] private bool isDashing;
    [SerializeField] private bool isSwordDashing;
    [SerializeField] private bool isStuckToSurface;

    [Header("Sprint")]
    [SerializeField] private bool isSprinting;
    [SerializeField] private bool isSprintDashing;
    [SerializeField] private bool sprintDashOnCooldown;
    [SerializeField] private float sprintDashCooldownRemaining;

    [Header("Movement")]
    [SerializeField] private bool isGrounded;
    [SerializeField] private float verticalVelocity;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector - Debug Visualization Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Dash Ray Visualization")]
    [Tooltip("Zeigt die Dash-Richtung als Ray im Scene View")]
    [SerializeField] private bool showDashRay = true;

    [Tooltip("Farbe des Dash-Richtungs-Rays")]
    [SerializeField] private Color dashRayColor = Color.cyan;

    [Tooltip("Farbe des Sword-Dash-Rays")]
    [SerializeField] private Color swordDashRayColor = Color.magenta;

    [Tooltip("Länge des Richtungs-Rays wenn nicht im Dash")]
    [SerializeField] private float lookDirectionRayLength = 10f;

    [Header("Camera Direction")]
    [Tooltip("Zeigt die Kamera-Blickrichtung als Ray (= potenzielle Dash-Richtung)")]
    [SerializeField] private bool showLookDirection = true;

    [SerializeField] private Color lookDirectionColor = new Color(0.5f, 0.5f, 1f, 0.5f);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Cached Dash Data
    // ════════════════════════════════════════════════════════════════════════

    private Vector3 cachedDashDirection;
    private Vector3 cachedDashTarget;
    private bool hasDashData;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();

        if (core == null)
        {
            Debug.LogError("[PlayerDebugInspector] No PlayerCore found!");
            enabled = false;
            return;
        }

        dash = GetComponent<PlayerDash>();
        sprint = GetComponent<PlayerSprint>();
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        combat = GetComponent<PlayerCombat>();
        swordThrow = GetComponent<PlayerSwordThrow>();
    }

    private void Update()
    {
        if (core == null) return;

        UpdateStateInfo();
        UpdateHealthInfo();
        UpdateCombatInfo();
        UpdateDashInfo();
        UpdateSprintInfo();
        UpdateMovementInfo();
        UpdateDashVisualization();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Data Updates
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateStateInfo()
    {
        currentState = core.CurrentState.ToString();
        isDead = core.IsDead;
        isInvulnerable = core.IsInvulnerable;
    }

    private void UpdateHealthInfo()
    {
        if (health == null) return;
        currentHP = health.CurrentHP;
        maxHP = health.MaxHP;
    }

    private void UpdateCombatInfo()
    {
        if (combat == null) return;
        combatState = combat.CurrentState.ToString();
        blockHP = combat.CurrentBlockHP;
        isExhausted = combat.IsExhausted;
        hasSword = swordThrow != null ? swordThrow.HasSword : true;
    }

    private void UpdateDashInfo()
    {
        if (dash == null) return;
        dashCharges = dash.CurrentCharges;
        isDashing = dash.IsDashing;
        isSwordDashing = dash.IsSwordDashing;
        isStuckToSurface = dash.IsStuck;
    }

    private void UpdateSprintInfo()
    {
        if (sprint == null) return;
        isSprinting = sprint.IsSprinting;
        isSprintDashing = sprint.IsDashing;
        sprintDashOnCooldown = sprint.IsDashOnCooldown;
        sprintDashCooldownRemaining = sprint.CooldownRemaining;
    }

    private void UpdateMovementInfo()
    {
        if (movement == null) return;
        isGrounded = movement.IsGrounded;
        verticalVelocity = movement.VerticalVelocity;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Ray Visualization
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateDashVisualization()
    {
        if (!showDashRay) return;

        Vector3 origin = transform.position + Vector3.up;

        if (dash != null && dash.IsDashing)
        {
            // Aktiver Dash: zeige Dash-Richtung
            Vector3 dashDir = core.CameraTransform.forward;
            Debug.DrawRay(origin, dashDir * 30f, dashRayColor);
            hasDashData = true;
            cachedDashDirection = dashDir;
        }
        else if (dash != null && dash.IsSwordDashing && swordThrow != null && swordThrow.ActiveSword != null)
        {
            // Sword Dash: zeige Richtung zum Schwert
            Vector3 toSword = (swordThrow.ActiveSword.transform.position - transform.position).normalized;
            Debug.DrawRay(origin, toSword * 30f, swordDashRayColor);
            hasDashData = true;
            cachedDashDirection = toSword;
        }
        else
        {
            hasDashData = false;
        }

        // Kamera-Blickrichtung (= potenzielle Dash-Richtung)
        if (showLookDirection && core.CameraTransform != null)
        {
            Vector3 camOrigin = core.CameraTransform.position;
            Vector3 camForward = core.CameraTransform.forward;
            Debug.DrawRay(camOrigin, camForward * lookDirectionRayLength, lookDirectionColor);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (core == null) return;

        Vector3 origin = transform.position + Vector3.up;

        // Dash-Richtungs-Pfeil während Dash
        if (showDashRay && hasDashData)
        {
            Gizmos.color = dash != null && dash.IsSwordDashing ? swordDashRayColor : dashRayColor;

            // Pfeil-Spitze am Ende des Rays
            Vector3 arrowTip = origin + cachedDashDirection * 5f;
            Gizmos.DrawLine(origin, arrowTip);
            Gizmos.DrawWireSphere(arrowTip, 0.2f);
        }

        // Stuck-Position Markierung
        if (dash != null && dash.IsStuck)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.5f);

            // Surface Normal
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, dash.StuckSurfaceNormal * 2f);
        }

        // Invulnerability-Indikator
        if (core.IsInvulnerable)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 1.5f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        if (core == null) return;

        // Dash-Reichweite
        if (dash != null)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, 15f); // dashMaxDistance Approximation
        }

        // Attack-Radius während Dash
        if (dash != null && dash.IsDashing)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, dash.AttackDashRadius);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region OnGUI Debug Overlay
    // ════════════════════════════════════════════════════════════════════════

    [Header("Screen Overlay")]
    [SerializeField] private bool showScreenOverlay = true;

    private void OnGUI()
    {
        if (!showScreenOverlay || core == null) return;

        // Oben links: kompaktes Status-Overlay
        GUILayout.BeginArea(new Rect(10, 10, 220, 250));

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 12;
        style.normal.textColor = Color.white;

        string stateColor = core.CurrentState switch
        {
            PlayerCore.PlayerState.SprintDashing => "<color=orange>SPRINT DASH</color>",
            PlayerCore.PlayerState.Dashing => "<color=cyan>DASHING</color>",
            PlayerCore.PlayerState.DashingToSword => "<color=magenta>SWORD DASH</color>",
            PlayerCore.PlayerState.StuckToSurface => "<color=yellow>STUCK</color>",
            PlayerCore.PlayerState.Airborne => "<color=white>AIRBORNE</color>",
            PlayerCore.PlayerState.Dead => "<color=red>DEAD</color>",
            _ => "<color=green>NORMAL</color>"
        };

        style.richText = true;
        GUILayout.Label($"State: {stateColor}", style);
        GUILayout.Label($"HP: {currentHP:F0}/{maxHP:F0}", style);

        if (combat != null)
        {
            string exhaustColor = isExhausted ? "<color=orange>EXHAUSTED</color>" : $"{blockHP:F0}";
            GUILayout.Label($"Block: {exhaustColor}", style);
        }

        if (dash != null)
        {
            GUILayout.Label($"Dash: {dashCharges}/{dash.MaxCharges}", style);
        }

        if (sprint != null)
        {
            string sprintState = isSprintDashing
                ? "<color=orange>DASH</color>"
                : isSprinting
                    ? "<color=green>ON</color>"
                    : "OFF";
            GUILayout.Label($"Sprint: {sprintState}", style);

            if (sprintDashOnCooldown)
            {
                GUILayout.Label($"Sprint CD: {sprintDashCooldownRemaining:F1}s", style);
            }
        }

        GUILayout.EndArea();
    }

    #endregion
}
