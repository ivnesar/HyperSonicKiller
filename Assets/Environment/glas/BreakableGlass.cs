using UnityEngine;

/// <summary>
/// Dekoratives, zerstörbares Glas im PS1-Stil.
///
/// Verhalten:
/// - Wird von Kugeln, dem Spieler-Dash oder jedem anderen IDamageable-Sender zerstört.
/// - Beim Zerbrechen: Glas-Mesh + Collider werden deaktiviert, Particle System wird aktiviert.
/// - Jeder Partikel bekommt eine Velocity, die von der Einschlagsrichtung + Entfernung
///   zum Einschlagspunkt abhängt (Partikel nahe am Treffer = schneller).
///
/// Setup im Editor:
/// - GameObject mit MeshFilter + MeshRenderer (das sichtbare Glas)
/// - Collider (Box oder Mesh). Wichtig: "Is Trigger" = true, damit der Spieler
///   beim Dash hindurchfliegen kann.
/// - Child-GameObject mit einem Particle System (ShatterParticles), das die
///   Glassplitter darstellt. Emission sollte auf "Rate over Time = 0" und
///   "Bursts" mit fester Anzahl konfiguriert sein (z.B. 40 Partikel in einem Burst).
/// - Die ShatterParticles-Referenz im Inspector verknüpfen.
/// - Auf einen eigenen Layer legen (z.B. "Glass") und diesen in die
///   hitMask der SoldierBullet und dashSurfaceLayer NICHT aufnehmen,
///   damit Kugeln/Dashes nicht vom Glas gestoppt werden. Stattdessen nutzt
///   das Glas OnTriggerEnter für Kollisionen mit Kugel/Spieler.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BreakableGlass : MonoBehaviour, IDamageable
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("References")]
    [Tooltip("Particle System, das die Glassplitter darstellt. Wird beim Zerbrechen aktiviert.")]
    [SerializeField] private ParticleSystem shatterParticles;

    [Tooltip("Das sichtbare Glas-Mesh. Wird beim Zerbrechen ausgeblendet. " +
             "Wenn leer, wird automatisch der MeshRenderer dieses GameObjects verwendet.")]
    [SerializeField] private MeshRenderer glassRenderer;

    [Header("Impact Settings")]
    [Tooltip("Einflussbereichsradius um den Einschlagspunkt. " +
             "Partikel innerhalb dieses Radius bekommen zusätzliche Velocity.")]
    [SerializeField] private float impactRadius = 1.5f;

    [Tooltip("Maximale zusätzliche Geschwindigkeit am Einschlagspunkt (Distanz = 0). " +
             "Fällt mit Entfernung gemäß falloffCurve ab.")]
    [SerializeField] private float maxImpactVelocity = 15f;

    [Tooltip("Wie die Velocity mit der Distanz zum Einschlagspunkt abfällt. " +
             "X-Achse: 0 = am Einschlag, 1 = am Rand des impactRadius. " +
             "Y-Achse: 1 = volle maxImpactVelocity, 0 = keine zusätzliche Velocity.")]
    [SerializeField] private AnimationCurve falloffCurve =
        AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Tooltip("Grundgeschwindigkeit, die ALLE Partikel in Einschlagsrichtung bekommen " +
             "(auch solche außerhalb des impactRadius).")]
    [SerializeField] private float minVelocity = 1f;

    [Tooltip("Zufällige Streuung der Velocity pro Partikel (für natürlicheren Look). " +
             "Wird als zusätzlicher Vector mit zufälliger Richtung addiert.")]
    [SerializeField] private float randomVelocityJitter = 2f;

    [Header("Health")]
    [Tooltip("Lebenspunkte des Glases. Bei 1 zerbricht es beim ersten Treffer.")]
    [SerializeField] private float maxHealth = 1f;

    [Header("Dash Interaction")]
    [Tooltip("Ob das Glas auch zerbricht, wenn der Spieler beim normalen Gehen (nicht Dash) dagegenläuft. " +
             "Empfohlen: false, damit nur Dash/Geschosse Glas zerstören.")]
    [SerializeField] private bool breakOnPlayerTouch = false;

    [Tooltip("Schaden, den der Spieler beim Dash durch das Glas verursacht.")]
    [SerializeField] private float dashDamage = 999f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private float currentHealth;
    private bool isBroken;
    private Collider glassCollider;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        currentHealth = maxHealth;

        glassCollider = GetComponent<Collider>();

        // Auto-resolve glass renderer if not set
        if (glassRenderer == null)
        {
            glassRenderer = GetComponent<MeshRenderer>();
        }

        // Sanity check: warn if particle system is missing
        if (shatterParticles == null)
        {
            Debug.LogWarning($"[BreakableGlass] '{name}' hat kein Shatter-ParticleSystem zugewiesen!", this);
        }
        else
        {
            // Sicherstellen, dass es nicht schon spielt
            shatterParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // ── DEBUG: Log JEDEN Trigger, damit wir sehen ob OnTriggerEnter überhaupt feuert ──
        Debug.Log($"[BreakableGlass] OnTriggerEnter gefeuert von: '{other.name}' (Tag: '{other.tag}', Layer: {LayerMask.LayerToName(other.gameObject.layer)})", this);

        HandleTriggerContact(other, "OnTriggerEnter");
    }

    private void OnTriggerStay(Collider other)
    {
        // Fallback: Falls OnTriggerEnter bei schnellem Dash übersprungen wird,
        // greift OnTriggerStay solange der Spieler im Trigger ist.
        if (isBroken) return;

        // Weniger Spam: nur loggen wenn Player drin
        if (other.CompareTag("Player"))
        {
            Debug.Log($"[BreakableGlass] OnTriggerStay mit Player (State={GetPlayerState(other)})", this);
        }

        HandleTriggerContact(other, "OnTriggerStay");
    }

    private string GetPlayerState(Collider other)
    {
        var pc = other.GetComponentInParent<PlayerCore>();
        return pc != null ? pc.CurrentState.ToString() : "NO_PLAYERCORE";
    }

    private void HandleTriggerContact(Collider other, string source)
    {
        if (isBroken) return;

        // Spieler hindurch-dashen lassen = Glas zerbrechen
        if (other.CompareTag("Player"))
        {
            PlayerCore player = other.GetComponentInParent<PlayerCore>();
            if (player == null)
            {
                Debug.LogWarning($"[BreakableGlass/{source}] ABBRUCH: PlayerCore nicht gefunden.", this);
                return;
            }

            bool isDashing = player.CurrentState == PlayerCore.PlayerState.Dashing ||
                             player.CurrentState == PlayerCore.PlayerState.SprintDashing ||
                             player.CurrentState == PlayerCore.PlayerState.DashingToSword;

            if (isDashing || breakOnPlayerTouch)
            {
                Vector3 dashDir = player.transform.forward;
                if (isDashing && player.CameraTransform != null)
                {
                    dashDir = player.CameraTransform.forward;
                }

                Vector3 hitPoint = glassCollider.ClosestPoint(other.transform.position);
                Debug.Log($"[BreakableGlass/{source}] → Shatter wird ausgelöst! State={player.CurrentState}", this);
                TakeDamage(dashDamage, hitPoint, dashDir);
            }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region IDamageable Implementation
    // ════════════════════════════════════════════════════════════════════════

    public void TakeDamage(float damage)
    {
        // Fallback ohne Positionsinfo: Einschlag in Glasmitte, keine Richtung
        TakeDamage(damage, transform.position, transform.forward);
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        Debug.Log($"[BreakableGlass] TakeDamage aufgerufen: damage={damage}, currentHealth={currentHealth}, isBroken={isBroken}", this);

        if (isBroken) return;

        currentHealth -= damage;

        if (currentHealth <= 0f)
        {
            Shatter(hitPoint, hitDirection);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Shatter Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Zerbricht das Glas: Mesh + Collider aus, Particle System an,
    /// und jeder Partikel bekommt seine Velocity basierend auf Distanz zum Einschlag.
    /// </summary>
    private void Shatter(Vector3 hitPoint, Vector3 hitDirection)
    {
        Debug.Log($"[BreakableGlass] Shatter() START. shatterParticles={(shatterParticles != null ? shatterParticles.name : "NULL")}, glassRenderer={(glassRenderer != null ? glassRenderer.name : "NULL")}", this);

        isBroken = true;

        // 1. Glas-Mesh ausblenden
        if (glassRenderer != null)
        {
            glassRenderer.enabled = false;
        }

        // 2. Collider deaktivieren (nichts soll mehr hindurch-triggern)
        if (glassCollider != null)
        {
            glassCollider.enabled = false;
        }

        // 3. Particle System starten und Velocity pro Partikel berechnen
        if (shatterParticles != null)
        {
            // Erst emittieren lassen (spielt alle Bursts ab)
            shatterParticles.Play();

            // DEBUG: Wie viele Partikel wurden wirklich emittiert?
            Debug.Log($"[BreakableGlass] Nach Play(): particleCount = {shatterParticles.particleCount}, isPlaying = {shatterParticles.isPlaying}, emission.enabled = {shatterParticles.emission.enabled}, emission.rateOverTime = {shatterParticles.emission.rateOverTime.constant}, emission.burstCount = {shatterParticles.emission.burstCount}", this);

            // Partikel-Velocity anpassen (muss nach Play() passieren,
            // da GetParticles() nur existierende Partikel lesen kann).
            ApplyImpactVelocityToParticles(hitPoint, hitDirection.normalized);
        }
        else
        {
            Debug.LogError("[BreakableGlass] ABBRUCH bei Shatter: shatterParticles ist NULL!", this);
        }
    }

    /// <summary>
    /// Geht alle lebenden Partikel durch und addiert eine Velocity basierend auf
    /// der Distanz zum Einschlagspunkt. Je näher, desto schneller.
    /// </summary>
    private void ApplyImpactVelocityToParticles(Vector3 hitPoint, Vector3 hitDirectionNormalized)
    {
        int particleCount = shatterParticles.particleCount;
        if (particleCount == 0) return;

        // Array für alle Partikel holen
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[particleCount];
        int actualCount = shatterParticles.GetParticles(particles);

        // Particle-Space: Unity-Partikel liegen je nach simulationSpace in world
        // oder local space. Wir konvertieren bei local auf world für die
        // Distanzberechnung.
        bool isLocalSpace = shatterParticles.main.simulationSpace == ParticleSystemSimulationSpace.Local;
        Transform psTransform = shatterParticles.transform;

        for (int i = 0; i < actualCount; i++)
        {
            // Partikel-Weltposition bestimmen
            Vector3 particleWorldPos = isLocalSpace
                ? psTransform.TransformPoint(particles[i].position)
                : particles[i].position;

            // Distanz zum Einschlagspunkt
            float distance = Vector3.Distance(particleWorldPos, hitPoint);

            // Einfluss per Falloff-Kurve
            float impactStrength = 0f;
            if (distance < impactRadius)
            {
                float normalizedDist = distance / impactRadius; // 0..1
                impactStrength = falloffCurve.Evaluate(normalizedDist) * maxImpactVelocity;
            }

            // Gesamt-Velocity: existierende Velocity (falls PS welche gibt)
            // + Grundgeschwindigkeit in Einschlagsrichtung
            // + impactStrength in Einschlagsrichtung
            // + etwas Zufall
            Vector3 baseVelocity = hitDirectionNormalized * (minVelocity + impactStrength);
            Vector3 jitter = Random.insideUnitSphere * randomVelocityJitter;

            // simulationSpace beachten: bei Local-Space müssen wir die Velocity
            // in den Local-Space des Particle Systems transformieren.
            Vector3 finalVelocity = baseVelocity + jitter;
            if (isLocalSpace)
            {
                finalVelocity = psTransform.InverseTransformDirection(finalVelocity);
            }

            particles[i].velocity += finalVelocity;
        }

        // Partikel zurückschreiben
        shatterParticles.SetParticles(particles, actualCount);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // Visualisierung des Impact-Radius in Glasmitte
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, impactRadius);
    }

    #endregion
}
