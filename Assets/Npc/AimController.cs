using UnityEngine;
using RootMotion.FinalIK;

/// <summary>
/// Generischer Wrapper für die AimIK-Komponente von RootMotion Final IK.
/// Kann von jedem NPC-Typ verwendet werden (Soldier, Sniper, Grenadier, etc.).
///
/// Bietet eine einfache API: EnableAim(), DisableAim(), SetTargetPosition().
/// Kümmert sich intern um Weight-Blending und Target-Interpolation.
///
/// Vorteile gegenüber manueller Bone-Rotation:
/// - Verteilt Rotation über mehrere Spine-Bones (natürlicheres Ergebnis)
/// - Volle 3D-Richtung (nicht nur Pitch)
/// - Smoothes Weight-Blending eingebaut
/// - Arbeitet additiv auf Animationen (Animancer-kompatibel)
///
/// SETUP:
///   1. AimIK-Komponente auf das Model-Kind (mit Animator) legen.
///   2. In AimIK:
///      - "Transform" = Muzzle/Waffe (das was zum Ziel zeigen soll)
///      - "Axis" = lokale Forward-Achse der Waffe (meist Vector3.forward)
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

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private float currentWeight;
    private float targetWeight;
    private bool isInitialized;

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

        // Weight smooth blenden
        currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, blendSpeed * Time.deltaTime);
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
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aktiviert das Aiming. Weight blendet smooth auf 1.
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
    /// Wird jeden Frame vom NPC aufgerufen.
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
