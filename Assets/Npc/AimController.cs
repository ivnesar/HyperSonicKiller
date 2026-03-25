using UnityEngine;
using RootMotion.FinalIK;

/// <summary>
/// Generischer Wrapper für die AimIK-Komponente von RootMotion Final IK.
/// Kann von jedem NPC-Typ verwendet werden (Soldier, Defender, Sniper, etc.).
///
/// Bietet eine einfache API: EnableAim(), DisableAim(), SetTargetPosition().
/// Kümmert sich intern um Weight-Blending und Target-Interpolation.
///
/// AIM-IK STEUERUNG:
///   - NpcBase setzt IsAimActive (bool) basierend auf dem aktuellen NPC-State.
///   - NpcBase ruft jeden Frame UpdateAimController() auf, das EnableAim/DisableAim
///     und SetTargetPosition weiterleitet.
///   - Der AimController blendet den Weight smooth ein/aus.
///
/// DASH-OVERRIDE:
///   - Wenn der Spieler dasht, blendet der AimController den Weight smooth auf 0
///     über dashBlendOutDuration, UNABHÄNGIG von IsAimActive.
///   - Wenn der Dash endet und IsAimActive noch true ist, blendet er smooth zurück.
///   - Die Dash-Erkennung läuft über PlayerCore, das in NpcBase gecached wird.
///
/// SETUP:
///   1. AimIK-Komponente auf das Model-Kind (mit Animator) legen.
///   2. In AimIK:
///      - "Transform" = Muzzle/Waffe oder Chest-Bone (das was zum Ziel zeigen soll)
///      - "Axis" = lokale Forward-Achse (meist Vector3.forward)
///      - "Bones" = Spine-Kette (z.B. Spine → Chest → UpperChest)
///      - "Clamp Weight" = 0.3–0.5 (verhindert extreme Verdrehung)
///   3. Diese Komponente auf dasselbe GameObject wie die NPC-Klasse legen.
///   4. aimIK-Referenz im Inspector zuweisen (oder wird auto-gefunden).
///   5. aimTarget zuweisen ODER leer lassen (wird dann automatisch erstellt).
///
/// TIMING:
///   AimIK läuft intern in LateUpdate (über SolverManager).
///   Kein manueller LateUpdate nötig — wir setzen nur Target + Weight.
/// </summary>
public class AimController : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("AimIK Reference")]
    [Tooltip("Die AimIK-Komponente auf dem Model-Kind. Wird auto-gefunden wenn leer.")]
    [SerializeField] private AimIK aimIK;

    [Header("Target")]
    [Tooltip("Transform das als AimIK-Target dient. Wird automatisch erstellt wenn leer.")]
    [SerializeField] private Transform aimTarget;

    [Header("Blending")]
    [Tooltip("Geschwindigkeit des Weight Ein-/Ausblendens (höher = schneller).")]
    [SerializeField] private float blendSpeed = 6f;

    [Header("Target Offset")]
    [Tooltip("Vertikaler Offset zum Spieler-Ziel (Brusthöhe).")]
    [SerializeField] private float targetHeightOffset = 1f;

    [Tooltip("Geschwindigkeit mit der das AimTarget der Zielposition folgt.")]
    [SerializeField] private float targetFollowSpeed = 12f;

    [Header("Dash Override")]
    [Tooltip("Dauer in Sekunden, über die der AimIK-Weight auf 0 ausblendet wenn der Spieler dasht.")]
    [SerializeField] private float dashBlendOutDuration = 0.2f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private float currentWeight;
    private float targetWeight;
    private bool isInitialized;

    /// <summary>
    /// Referenz auf PlayerCore für Dash-Erkennung.
    /// Wird von NpcBase über SetPlayerCore() gesetzt.
    /// </summary>
    private PlayerCore playerCore;

    /// <summary>
    /// True wenn der Dash-Override aktiv ist (Spieler dasht → Weight wird auf 0 erzwungen).
    /// </summary>
    private bool isDashOverrideActive;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // AimIK auto-finden
        if (aimIK == null)
            aimIK = GetComponentInChildren<AimIK>();

        if (aimIK == null)
        {
            Debug.LogError($"[AimController] Keine AimIK-Komponente gefunden auf {gameObject.name}! " +
                           "Bitte AimIK auf das Model-Kind legen.");
            enabled = false;
            return;
        }

        // AimTarget erstellen wenn keins zugewiesen
        if (aimTarget == null)
        {
            var targetGO = new GameObject($"{gameObject.name}_AimTarget");
            aimTarget = targetGO.transform;
        }

        // AimIK konfigurieren
        aimIK.solver.target = aimTarget;
        aimIK.solver.IKPositionWeight = 0f;

        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized) return;

        // Dash-Override prüfen
        UpdateDashOverride();

        // Effektiven Target-Weight bestimmen
        float effectiveTarget = isDashOverrideActive ? 0f : targetWeight;

        // Blend-Speed bestimmen: bei Dash-Override nutze dashBlendOutDuration
        float speed;
        if (isDashOverrideActive && currentWeight > 0f)
        {
            // Smooth auf 0 über dashBlendOutDuration
            speed = dashBlendOutDuration > 0f ? (1f / dashBlendOutDuration) : 100f;
        }
        else
        {
            speed = blendSpeed;
        }

        // Weight smooth blenden
        currentWeight = Mathf.MoveTowards(currentWeight, effectiveTarget, speed * Time.deltaTime);
        aimIK.solver.IKPositionWeight = currentWeight;
    }

    private void OnDestroy()
    {
        // Aufräumen: automatisch erstelltes Target zerstören
        if (aimTarget != null && aimTarget.gameObject.name.EndsWith("_AimTarget"))
        {
            Destroy(aimTarget.gameObject);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Dash Override
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prüft ob der Spieler gerade dasht und setzt den Dash-Override entsprechend.
    /// </summary>
    private void UpdateDashOverride()
    {
        if (playerCore == null)
        {
            isDashOverrideActive = false;
            return;
        }

        bool playerIsDashing = playerCore.CurrentState == PlayerCore.PlayerState.Dashing
                            || playerCore.CurrentState == PlayerCore.PlayerState.DashingToSword;

        isDashOverrideActive = playerIsDashing;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Setzt die PlayerCore-Referenz für Dash-Erkennung.
    /// Wird von NpcBase.Start() aufgerufen.
    /// </summary>
    public void SetPlayerCore(PlayerCore core)
    {
        playerCore = core;
    }

    /// <summary>
    /// Aktiviert das Aiming. Weight blendet smooth auf 1.
    /// Wird vom Dash-Override übersteuert wenn der Spieler dasht.
    /// </summary>
    public void EnableAim()
    {
        targetWeight = 1f;
    }

    /// <summary>
    /// Deaktiviert das Aiming. Weight blendet smooth auf 0.
    /// </summary>
    public void DisableAim()
    {
        targetWeight = 0f;
    }

    /// <summary>
    /// Setzt die Zielposition (Weltkoordinaten).
    /// Wird jeden Frame von NpcBase aufgerufen.
    /// </summary>
    public void SetTargetPosition(Vector3 worldPosition)
    {
        if (aimTarget == null) return;

        Vector3 target = worldPosition + Vector3.up * targetHeightOffset;

        // Smooth follow damit das Aiming nicht ruckt
        aimTarget.position = Vector3.Lerp(aimTarget.position, target, targetFollowSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Setzt die Zielposition sofort (ohne Interpolation).
    /// Nützlich beim ersten Aktivieren, damit kein "Nachziehen" sichtbar ist.
    /// </summary>
    public void SetTargetPositionImmediate(Vector3 worldPosition)
    {
        if (aimTarget == null) return;

        aimTarget.position = worldPosition + Vector3.up * targetHeightOffset;
    }

    /// <summary>
    /// Sofort ausschalten (z.B. bei Tod oder Stun). Kein Blending.
    /// </summary>
    public void DisableImmediate()
    {
        targetWeight = 0f;
        currentWeight = 0f;

        if (aimIK != null)
            aimIK.solver.IKPositionWeight = 0f;
    }

    /// <summary>
    /// True wenn das Aim-Weight aktuell > 0 ist (also aktiv blendet oder voll aktiv).
    /// </summary>
    public bool IsActive => currentWeight > 0.001f;

    #endregion
}
