using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// ANTI-DASH GRENADE - Projektil des Grenadier NPC
// ════════════════════════════════════════════════════════════════════════════
//
// Flugbahn:
//   - Fester Aufschlagspunkt (beim Abschuss berechnet)
//   - Parabelbahn (manuell im Update, KEIN Rigidbody)
//   - Ignoriert alle Kollisionen unterwegs
//   - Detoniert am Zielpunkt und spawnt eine AntiDashZone
//
// Berechnung:
//   Die Granate bekommt beim Initialize() den Zielpunkt und die Flugdauer.
//   Daraus wird die benötigte Startgeschwindigkeit berechnet, sodass die
//   Granate nach genau flightDuration Sekunden am Zielpunkt ankommt.
//
//   position(t) = start + velocity * t + 0.5 * gravity * t²
//
//   Auflösen nach velocity:
//   velocity = (target - start - 0.5 * gravity * flightDuration²) / flightDuration
//
// SETUP:
//   1. Prefab erstellen: Empty GameObject mit diesem Script
//   2. Optional: Mesh/Partikel als Child für Visuals
//   3. AntiDashZone-Prefab zuweisen (wird beim Einschlag gespawnt)
//   4. Granate wird vom GrenadierNpc.FireGrenade() instanziiert
//
// ════════════════════════════════════════════════════════════════════════════

public class AntiDashGrenade : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Flight")]
    [Tooltip("Flugdauer in Sekunden vom Abschuss bis zum Einschlag")]
    [SerializeField] private float flightDuration = 1.2f;

    [Tooltip("Schwerkraft die auf die Granate wirkt (positiver Wert = nach unten)")]
    [SerializeField] private float gravity = 20f;

    [Header("Impact")]
    [Tooltip("Prefab der Anti-Dash Zone die beim Einschlag gespawnt wird")]
    [SerializeField] private AntiDashZone zonePrefab;

    [Tooltip("Radius der gespawnten Zone (0 = Default aus dem Zone-Prefab)")]
    [SerializeField] private float zoneRadius = 6f;

    [Tooltip("Dauer der gespawnten Zone in Sekunden (0 = Default aus dem Zone-Prefab)")]
    [SerializeField] private float zoneDuration = 5f;

    [Header("VFX")]
    [Tooltip("Optionaler Partikeleffekt beim Einschlag")]
    [SerializeField] private GameObject impactEffectPrefab;

    [Header("Audio")]
    [SerializeField] private AudioClip launchSound;
    [SerializeField] private AudioClip impactSound;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private Vector3 velocity;
    private float elapsedTime;
    private bool isFlying;
    private AudioSource audioSource;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialisiert die Granate und startet den Flug.
    /// Wird von GrenadierNpc.FireGrenade() aufgerufen.
    /// </summary>
    /// <param name="target">Zielpunkt am Boden wo die Granate einschlagen soll</param>
    public void Initialize(Vector3 target)
    {
        startPosition = transform.position;
        targetPosition = target;
        elapsedTime = 0f;

        // Startgeschwindigkeit berechnen damit Granate nach flightDuration am Ziel ist
        // Formel: v = (target - start - 0.5 * g * t²) / t
        Vector3 gravityVector = Vector3.down * gravity;
        velocity = (targetPosition - startPosition - 0.5f * gravityVector * flightDuration * flightDuration) / flightDuration;

        isFlying = true;

        // Granate in Flugrichtung drehen
        if (velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(velocity);

        PlaySound(launchSound);
    }

    /// <summary>
    /// Erlaubt dem Grenadier, die Zone-Parameter zu überschreiben.
    /// </summary>
    public void SetZoneParameters(float radius, float duration)
    {
        if (radius > 0f) zoneRadius = radius;
        if (duration > 0f) zoneDuration = duration;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void Update()
    {
        if (!isFlying) return;

        elapsedTime += Time.deltaTime;

        // Position auf der Parabelbahn berechnen
        // p(t) = start + v * t + 0.5 * g * t²
        Vector3 gravityVector = Vector3.down * gravity;
        Vector3 newPosition = startPosition
            + velocity * elapsedTime
            + 0.5f * gravityVector * elapsedTime * elapsedTime;

        transform.position = newPosition;

        // Granate in Flugrichtung drehen (Tangente der Parabelbahn)
        // v(t) = v0 + g * t
        Vector3 currentVelocity = velocity + gravityVector * elapsedTime;
        if (currentVelocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(currentVelocity);

        // Einschlag prüfen
        if (elapsedTime >= flightDuration)
        {
            OnImpact();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Impact
    // ════════════════════════════════════════════════════════════════════════

    private void OnImpact()
    {
        isFlying = false;

        // Position exakt auf Zielpunkt setzen
        transform.position = targetPosition;

        // Anti-Dash Zone spawnen
        SpawnZone();

        // Einschlag-Effekt
        if (impactEffectPrefab != null)
        {
            Instantiate(impactEffectPrefab, targetPosition, Quaternion.identity);
        }

        PlaySound(impactSound);

        // Granate zerstören (kurze Verzögerung für Sound)
        Destroy(gameObject, 0.5f);
    }

    private void SpawnZone()
    {
        if (zonePrefab == null)
        {
            Debug.LogError("[AntiDashGrenade] Kein Zone-Prefab zugewiesen! Zone wird nicht gespawnt.");
            return;
        }

        // Zone am Einschlagspunkt spawnen
        AntiDashZone zone = Instantiate(zonePrefab, targetPosition, Quaternion.identity);
        zone.Initialize(zoneRadius, zoneDuration);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !isFlying) return;

        // Flugbahn visualisieren
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, targetPosition);

        // Zielpunkt
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(targetPosition, zoneRadius);

        // Aktueller Punkt
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.position, 0.15f);
    }

    #endregion
}
