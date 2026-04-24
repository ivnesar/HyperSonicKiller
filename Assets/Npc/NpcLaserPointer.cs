using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// NPC LASER POINTER - Visueller Warnstrahl für bevorstehende Angriffe
// ════════════════════════════════════════════════════════════════════════════
//
// Standard-Laser für NPCs die auf den Spieler zielen
// (Soldier, Sniper, Defender, etc.).
//
// ARCHITEKTUR:
//   - Dieses Script liegt am NPC-Root und steuert nur die Darstellung
//     (Farbe, Breite, Sichtbarkeit) eines LaserRenderers.
//   - Der LaserRenderer selbst liegt am Waffen-GameObject im Prefab und
//     kümmert sich um Position, Richtung, Raycast und LineRenderer.
//   - Dadurch rotiert der Laser automatisch mit der Waffe — keine Achsen-
//     Probleme mehr.
//
// Funktionsweise:
//   - Wenn npc.IsLaserActive == true: LaserRenderer wird sichtbar geschaltet.
//   - Breite und Farbe interpolieren entlang npc.AimProgress (0 → 1).
//   - Kein eigener Wiggle — der Wiggle wird in NpcBase auf das AimIK-Target
//     gelegt, wodurch die gesamte Waffe (und der Laser) mitwackelt.
//   - Beim Dash des Spielers blendet AimIK-Weight auf 0 (im AimController) →
//     Waffe dreht zurück in Ruhepose, Laser folgt natürlich mit.
//
// SETUP:
//   1. Am Waffen-GameObject im NPC-Prefab die LaserRenderer-Komponente anlegen.
//   2. Dort direction (z.B. (0,0,1)), maxDistance und hitMask konfigurieren.
//   3. Am NPC-Root die LaserRenderer-Referenz in dieses Script ziehen.
//
// Für Dash-basierte NPCs (GenTwo etc.) siehe LaserPointer_Dash.
//
// ════════════════════════════════════════════════════════════════════════════

[RequireComponent(typeof(NpcBase))]
public class NpcLaserPointer : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("References")]
    [Tooltip("Der LaserRenderer am Waffen-GameObject. Wird von diesem Script " +
             "in Farbe, Breite und Sichtbarkeit gesteuert.")]
    [SerializeField] private LaserRenderer laserRenderer;

    [Header("Laser Width")]
    [Tooltip("Breite des Lasers am Anfang der Aim-Phase (AimProgress = 0).")]
    [SerializeField] private float earlyWidth = 0.01f;

    [Tooltip("Breite des Lasers wenn eingelockt (AimProgress = 1).")]
    [SerializeField] private float lockedWidth = 0.06f;

    [Header("Laser Color")]
    [Tooltip("Farbe des Lasers am Anfang der Aim-Phase (AimProgress = 0).")]
    [SerializeField] private Color earlyColor = new Color(1f, 1f, 0f, 0.5f);

    [Tooltip("Farbe des Lasers wenn eingelockt (AimProgress = 1).")]
    [SerializeField] private Color lockedColor = new Color(1f, 0f, 0f, 1f);

    [Tooltip("Steuert den Verlauf von Farbe und Breite über AimProgress (0→1). " +
             "X = AimProgress, Y = Interpolationswert. " +
             "Default: Quadratische Kurve (langsamer Start, schnelleres Ende).")]
    [SerializeField] private AnimationCurve colorWidthCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(1f, 1f, 2f, 0f)
    );

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npc;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        npc = GetComponent<NpcBase>();

        if (laserRenderer == null)
        {
            Debug.LogError($"[NpcLaserPointer] Kein LaserRenderer zugewiesen auf {gameObject.name}! " +
                           "Bitte im Inspector die Referenz auf den LaserRenderer am Waffen-GameObject setzen.");
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        // Anfangszustand: versteckt und mit Early-Farbe/Breite
        // (Start statt Awake, damit der LaserRenderer seine eigene Initialisierung
        //  abgeschlossen hat — der erstellt seinen LineRenderer in Start.)
        laserRenderer.IsVisible = false;
        laserRenderer.Color = earlyColor;
        laserRenderer.LineWidth = earlyWidth;
    }

    private void LateUpdate()
    {
        // LateUpdate, damit AimProgress bereits aktualisiert ist und die Waffe
        // durch AimIK ihre finale Pose hat.

        if (npc == null || npc.IsDead || !npc.IsLaserActive)
        {
            laserRenderer.IsVisible = false;
            return;
        }

        laserRenderer.IsVisible = true;
        UpdateWidthAndColor(npc.AimProgress);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Width & Color Update
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateWidthAndColor(float progress)
    {
        float easedProgress = colorWidthCurve.Evaluate(progress);

        laserRenderer.LineWidth = Mathf.Lerp(earlyWidth, lockedWidth, easedProgress);
        laserRenderer.Color = Color.Lerp(earlyColor, lockedColor, easedProgress);
    }

    #endregion
}
