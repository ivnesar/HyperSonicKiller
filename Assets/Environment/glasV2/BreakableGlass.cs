using UnityEngine;

/// <summary>
/// Zerbrechliches Glas im PS1-Stil.
///
/// Wird durch einen externen Aufruf von Shatter() zerbrochen,
/// typischerweise vom GlassBreaker auf bewegten Objekten.
///
/// Beim Zerbrechen:
/// - Particle System wird abgespielt (Richtung & Geschwindigkeit aus Velocity)
/// - VelocityOverLifetime-Modul wird mit Vorwärtsschub modifiziert
/// - Glas-Mesh wird unsichtbar gemacht
/// - Glas-Collider wird deaktiviert
///
/// Hinweis: Der Collider muss ein Trigger sein, damit Objekte ungehindert
/// hindurchlaufen können. Reset() setzt das automatisch.
///
/// Wichtig für das ParticleSystem-Setup:
/// - VelocityOverLifetime-Modul muss aktiviert sein (World Space)
/// - Linear Y im Inspector für Schwerkraft/Fallen einstellen (bleibt unangetastet)
/// - Linear X und Z auf 0 lassen (werden vom Skript überschrieben)
/// </summary>
[RequireComponent(typeof(Collider))]
public class BreakableGlass : MonoBehaviour
{
    [Header("Referenzen")]
    [Tooltip("Das Particle System, das beim Zerbrechen abgespielt wird. " +
             "Sollte ein Child-GameObject sein, damit es nicht mit deaktiviert wird.")]
    [SerializeField] private ParticleSystem shatterParticles;

    [Tooltip("Renderer des Glases (wird beim Zerbrechen ausgeblendet).")]
    [SerializeField] private Renderer glassRenderer;

    [Tooltip("Collider des Glases (wird beim Zerbrechen deaktiviert).")]
    [SerializeField] private Collider glassCollider;

    [Header("Start Speed (Streuung)")]
    [Tooltip("Wie stark die Velocity die initiale Partikel-Streugeschwindigkeit beeinflusst.")]
    [SerializeField] private float velocityInfluence = 0.5f;

    [Tooltip("Minimale Start-Streugeschwindigkeit, auch bei langsamem Treffer.")]
    [SerializeField] private float minParticleSpeed = 2f;

    [Header("Forward Push (gerichteter Schub)")]
    [Tooltip("Wie stark die Velocity den gerichteten Vorwärtsschub beeinflusst. " +
             "Höher = Partikel fliegen kräftiger in Bewegungsrichtung des Treffers.")]
    [SerializeField] private float pushInfluence = 1.0f;

    [Tooltip("Minimaler Vorwärtsschub, auch bei langsamem Treffer.")]
    [SerializeField] private float minPushSpeed = 1f;

    private bool isShattered = false;

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Reset()
    {
        // Wird im Editor aufgerufen, wenn die Komponente hinzugefügt wird.
        glassRenderer = GetComponent<Renderer>();
        glassCollider = GetComponent<Collider>();
        if (glassCollider != null)
        {
            glassCollider.isTrigger = true;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lässt das Glas zerbrechen. Wird typischerweise vom GlassBreaker aufgerufen,
    /// kann aber auch von Geschoss-Logik, Skript-Events oder anderen Quellen kommen.
    /// </summary>
    /// <param name="hitterVelocity">
    /// Velocity des Objekts, das das Glas zerbricht. Beeinflusst Partikelrichtung
    /// und -geschwindigkeit. Vector3.zero für ein "stilles" Zerbrechen.
    /// </param>
    public void Shatter(Vector3 hitterVelocity)
    {
        if (isShattered) return;
        isShattered = true;

        PlayShatterEffect(hitterVelocity);

        if (glassRenderer != null) glassRenderer.enabled = false;
        if (glassCollider != null) glassCollider.enabled = false;
    }

    /// <summary>True, wenn das Glas bereits zerbrochen wurde.</summary>
    public bool IsShattered => isShattered;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Particle Effect
    // ════════════════════════════════════════════════════════════════════════

    private void PlayShatterEffect(Vector3 hitterVelocity)
    {
        if (shatterParticles == null) return;

        float velocityMagnitude = hitterVelocity.magnitude;

        // 1. Start Speed (Streuung in alle Richtungen, basierend auf Shape-Modul)
        float startSpeed = Mathf.Max(velocityMagnitude * velocityInfluence, minParticleSpeed);
        ParticleSystem.MainModule main = shatterParticles.main;
        main.startSpeed = startSpeed;

        // 2. Forward Push via VelocityOverLifetime (X/Z-Achsen in World Space)
        //    Y-Achse bleibt unangetastet (Schwerkraft-Setting des Inspectors).
        ApplyForwardPush(hitterVelocity, velocityMagnitude);

        // 3. Particle System in Bewegungsrichtung ausrichten (für ggf. gerichtete Shapes)
        if (velocityMagnitude > 0.01f)
        {
            shatterParticles.transform.rotation = Quaternion.LookRotation(hitterVelocity.normalized);
        }

        shatterParticles.Play();
    }

    private void ApplyForwardPush(Vector3 hitterVelocity, float velocityMagnitude)
    {
        ParticleSystem.VelocityOverLifetimeModule velocityModule = shatterParticles.velocityOverLifetime;

        if (velocityMagnitude < 0.01f)
        {
            // Kein Hitter-Velocity → X/Z auf 0 setzen, kein Push
            velocityModule.x = 0f;
            velocityModule.z = 0f;
            return;
        }

        // Push-Stärke basierend auf Velocity
        float pushStrength = Mathf.Max(velocityMagnitude * pushInfluence, minPushSpeed);

        // Push-Vektor: Bewegungsrichtung horizontal (Y bleibt frei für Schwerkraft)
        Vector3 pushDirection = hitterVelocity.normalized;
        Vector3 pushVector = pushDirection * pushStrength;

        // Nur X und Z setzen – Y bleibt im Inspector kontrolliert (Schwerkraft)
        velocityModule.x = pushVector.x;
        velocityModule.z = pushVector.z;
    }

    #endregion
}
