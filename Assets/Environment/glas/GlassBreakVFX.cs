using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// GLASS BREAK VFX - Partikelbasierter Glasbruch-Effekt
// ════════════════════════════════════════════════════════════════════════════
//
// Funktionsweise:
//   Steuert ein oder mehrere ParticleSystems, die beim Glasbruch einen
//   Burst an Partikeln spawnen. Nach dem Spawn wird die Velocity jedes
//   Partikels basierend auf seiner Distanz zum Einschlagspunkt angepasst:
//   Je näher am Einschlag, desto schneller fliegt der Partikel weg.
//
//   Zusätzlich bekommen alle Partikel einen Richtungsimpuls in
//   Einschlagrichtung (z.B. Schussrichtung), damit sie nicht nur
//   radial, sondern auch "mit dem Schuss" wegfliegen.
//
// Setup:
//   1. ParticleSystem(s) als Children des Glas-Prefabs erstellen
//   2. ParticleSystem auf "Looping = OFF", "Play On Awake = OFF" setzen
//   3. Emission-Modul: Burst mit gewünschter Partikelanzahl einrichten
//   4. Collision-Modul: aktivieren, Type = World, Bounce/Lifetime etc. einstellen
//   5. Dieses Script auf das gleiche GameObject oder ein Parent legen
//   6. Die ParticleSystems im Inspector zuweisen
//
// WICHTIG:
//   - Start Speed im ParticleSystem auf 0 oder sehr niedrig setzen,
//     da dieses Script die Velocity manuell überschreibt.
//   - Simulation Space muss auf "World" stehen, damit die Partikel
//     sich unabhängig vom Parent bewegen.
//   - Collision-Modul im ParticleSystem selbst konfigurieren
//     (Type: World, Bounce, Dampen, Lifetime Loss etc.)
//
// ════════════════════════════════════════════════════════════════════════════

public class GlassBreakVFX : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Particle Systems")]
    [Tooltip("Alle ParticleSystems die bei einem Bruch gesteuert werden sollen.\n" +
             "Erstes System = Haupt-Splitter, weitere = Deko-Partikel etc.")]
    [SerializeField] private ParticleSystem[] particleSystems;

    [Header("Velocity-Steuerung")]
    [Tooltip("Maximale Geschwindigkeit für Partikel direkt am Einschlagspunkt")]
    [SerializeField] private float maxVelocity = 8f;

    [Tooltip("Minimale Geschwindigkeit für Partikel am Rand des Radius")]
    [SerializeField] private float minVelocity = 1f;

    [Tooltip("Radius innerhalb dessen Partikel beeinflusst werden.\n" +
             "Partikel außerhalb bekommen minVelocity.")]
    [SerializeField] private float influenceRadius = 2f;

    [Tooltip("Wie die Geschwindigkeit mit der Distanz abfällt.\n" +
             "1 = linear, 2 = quadratisch (stärkerer Abfall), 0.5 = weicher")]
    [SerializeField] private float falloffExponent = 1f;

    [Header("Richtungsimpuls")]
    [Tooltip("Anteil der Einschlagrichtung an der finalen Velocity (0-1).\n" +
             "0 = rein radial vom Einschlag weg, 1 = rein in Schussrichtung")]
    [SerializeField, Range(0f, 1f)] private float directionalInfluence = 0.3f;

    [Header("Zufälligkeit")]
    [Tooltip("Zufällige Abweichung der Velocity-Richtung in Grad")]
    [SerializeField] private float randomSpreadAngle = 15f;

    [Tooltip("Zufällige Variation der Geschwindigkeit (Multiplikator 0-1).\n" +
             "0 = keine Variation, 0.3 = ±30% Variation")]
    [SerializeField, Range(0f, 1f)] private float randomSpeedVariation = 0.2f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private bool hasPlayed;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Löst den Partikel-Effekt aus und passt die Velocities an.
    /// </summary>
    /// <param name="impactPoint">Punkt an dem der Einschlag stattfand</param>
    /// <param name="impactDirection">Richtung des Einschlags (z.B. Schussrichtung)</param>
    public void Play(Vector3 impactPoint, Vector3 impactDirection)
    {
        if (hasPlayed) return;
        if (particleSystems == null || particleSystems.Length == 0) return;

        hasPlayed = true;

        Vector3 impactDirNormalized = impactDirection.normalized;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps == null) continue;

            // Partikel-Burst auslösen
            ps.Play();

            // Einen Frame warten ist nicht nötig — nach Play() sind die
            // Partikel sofort im System. Wir greifen sie direkt ab.
            ApplyVelocities(ps, impactPoint, impactDirNormalized);
        }
    }

    /// <summary>
    /// Setzt den Effekt zurück, damit er erneut abgespielt werden kann.
    /// Nützlich für Object-Pooling.
    /// </summary>
    public void ResetEffect()
    {
        hasPlayed = false;

        if (particleSystems == null) return;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] != null)
            {
                particleSystems[i].Clear();
                particleSystems[i].Stop();
            }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Velocity Logic
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Greift auf alle aktiven Partikel zu und setzt ihre Velocity
    /// basierend auf der Distanz zum Einschlagspunkt.
    /// </summary>
    private void ApplyVelocities(ParticleSystem ps, Vector3 impactPoint, Vector3 impactDir)
    {
        int particleCount = ps.particleCount;
        if (particleCount == 0) return;

        // Partikel-Array holen
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[particleCount];
        int count = ps.GetParticles(particles);

        for (int i = 0; i < count; i++)
        {
            // Position des Partikels in World Space ermitteln.
            // Bei Simulation Space = World ist particle.position bereits in World Space.
            // Bei Simulation Space = Local müssten wir transformieren.
            Vector3 particleWorldPos = GetParticleWorldPosition(ps, particles[i]);

            // Richtung vom Einschlag zum Partikel (radial weg)
            Vector3 radialDir = (particleWorldPos - impactPoint);
            float distance = radialDir.magnitude;

            // Normalisieren (mit Fallback falls Partikel genau am Einschlagpunkt sitzt)
            if (distance < 0.001f)
            {
                radialDir = Random.onUnitSphere;
            }
            else
            {
                radialDir /= distance; // normalize
            }

            // ── Geschwindigkeit berechnen ──
            // Normalisierte Distanz (0 = am Einschlag, 1 = am Rand des Radius)
            float normalizedDist = Mathf.Clamp01(distance / influenceRadius);

            // Falloff anwenden: 0 → maxVelocity, 1 → minVelocity
            float falloff = Mathf.Pow(normalizedDist, falloffExponent);
            float speed = Mathf.Lerp(maxVelocity, minVelocity, falloff);

            // Zufällige Speed-Variation
            if (randomSpeedVariation > 0f)
            {
                float variation = Random.Range(-randomSpeedVariation, randomSpeedVariation);
                speed *= (1f + variation);
            }

            // ── Richtung berechnen ──
            // Mischung aus radialer Richtung und Einschlagrichtung
            Vector3 finalDir = Vector3.Lerp(radialDir, impactDir, directionalInfluence).normalized;

            // Zufällige Streuung
            if (randomSpreadAngle > 0f)
            {
                finalDir = AddRandomSpread(finalDir, randomSpreadAngle);
            }

            // ── Velocity setzen ──
            // Bei World-Space: velocity direkt setzen
            // Bei Local-Space: in lokalen Raum transformieren
            Vector3 worldVelocity = finalDir * speed;
            particles[i].velocity = ConvertToParticleSpace(ps, worldVelocity);
        }

        // Partikel zurückschreiben
        ps.SetParticles(particles, count);
    }

    /// <summary>
    /// Ermittelt die World-Space-Position eines Partikels,
    /// unabhängig vom Simulation Space des ParticleSystems.
    /// </summary>
    private Vector3 GetParticleWorldPosition(ParticleSystem ps, ParticleSystem.Particle particle)
    {
        var main = ps.main;

        if (main.simulationSpace == ParticleSystemSimulationSpace.World)
        {
            return particle.position;
        }
        else
        {
            // Local Space → in World Space transformieren
            return ps.transform.TransformPoint(particle.position);
        }
    }

    /// <summary>
    /// Konvertiert eine World-Space-Velocity in den Raum des ParticleSystems.
    /// </summary>
    private Vector3 ConvertToParticleSpace(ParticleSystem ps, Vector3 worldVelocity)
    {
        var main = ps.main;

        if (main.simulationSpace == ParticleSystemSimulationSpace.World)
        {
            return worldVelocity;
        }
        else
        {
            // World → Local Space
            return ps.transform.InverseTransformDirection(worldVelocity);
        }
    }

    /// <summary>
    /// Fügt eine zufällige Richtungsabweichung innerhalb eines Kegelwinkels hinzu.
    /// </summary>
    private Vector3 AddRandomSpread(Vector3 direction, float maxAngle)
    {
        // Zufällige Rotation innerhalb des Kegels
        Quaternion randomRotation = Quaternion.Euler(
            Random.Range(-maxAngle, maxAngle),
            Random.Range(-maxAngle, maxAngle),
            Random.Range(-maxAngle, maxAngle)
        );

        return (randomRotation * direction).normalized;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        // Einflussradius visualisieren
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, influenceRadius);
    }

    #endregion
}
