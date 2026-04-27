using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// ANTI-DASH ZONE - Temporäre Dash-Sperrzone (z.B. von Granaten erzeugt)
// ════════════════════════════════════════════════════════════════════════════
//
// Ablauf (analog zu ProxyMineNpc):
//   1. WARNING: Beim Spawn ist die Warn-Sphere sichtbar, Farbverlauf läuft,
//      Dash funktioniert noch normal (faire Reaktionszeit).
//   2. ACTIVE:  Nach warnDuration startet die scharfe Zone für volle duration:
//               - Spieler in Zone → Dash blockiert
//               - Spieler dasht in Zone → Dash wird nach kurzer Verzögerung abgebrochen
//               - Spieler verlässt Zone → Dash wieder erlaubt
//   3. EXPIRED: Zone deaktiviert sich, Billboard verschwindet, GameObject zerstört sich.
//
// Unterschiede zur Drone:
//   - Zeitbegrenzt (zerstört sich nach Ablauf)
//   - Ist KEIN NPC (kein IEnemy, kein NpcBase, nicht angreifbar)
//   - Wird von AntiDashGrenade gespawnt, nicht manuell platziert
//   - Hat ein Billboard das immer zur Kamera zeigt
//
// SETUP:
//   Wird als Prefab erstellt mit:
//   1. Root: Empty GameObject mit diesem Script
//   2. Child: Quad (Billboard) mit transparentem Material
//      → Wird automatisch auf effectRadius * 2 skaliert
//      → Material wird zur Laufzeit als Instanz geklont (Farbverlauf)
//
// ════════════════════════════════════════════════════════════════════════════

public class AntiDashZone : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Zone Settings")]
    [Tooltip("Radius der Dash-Sperrzone in Metern")]
    [SerializeField] private float effectRadius = 6f;

    [Tooltip("Wie lange die Zone aktiv bleibt (in Sekunden, NACH der Warn-Phase)")]
    [SerializeField] private float duration = 5f;

    [Tooltip("Verzögerung (unscaled Sekunden) bevor ein aktiver Dash abgebrochen wird")]
    [SerializeField] private float dashCancelDelay = 0.1f;

    [Header("Warning Phase")]
    [Tooltip("Wie lange die Warn-Phase dauert bevor die Zone scharf wird (Sekunden)")]
    [SerializeField] private float warnDuration = 1f;

    [Tooltip("Renderer der Warn-Sphere (separates Child mit transparentem Material). " +
             "Wird nur während der Warn-Phase angezeigt.")]
    [SerializeField] private MeshRenderer warnSphereRenderer;

    [Tooltip("Startfarbe der Warn-Sphere (Beginn der Warn-Phase)")]
    [SerializeField] private Color warnColorStart = new Color(1f, 0.9f, 0f, 0.15f);

    [Tooltip("Endfarbe der Warn-Sphere (kurz vor Aktivierung)")]
    [SerializeField] private Color warnColorEnd = new Color(1f, 0f, 0f, 0.4f);

    [Tooltip("Farbverlauf über die Warn-Zeit (X: 0=Start, 1=Aktivierung / Y: 0=StartColor, 1=EndColor)")]
    [SerializeField] private AnimationCurve warnColorCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Billboard (Active Phase)")]
    [Tooltip("Child-Quad/Sprite das den Zonenradius während der aktiven Phase visualisiert")]
    [SerializeField] private Transform billboardTransform;

    [Header("Visual Feedback")]
    [Tooltip("Wenn true, wird das Billboard kurz vor Ablauf ausgeblendet (Blink-Effekt)")]
    [SerializeField] private bool blinkBeforeExpiry = true;

    [Tooltip("Wann das Blinken beginnt (Sekunden vor Ablauf der Active-Phase)")]
    [SerializeField] private float blinkStartTime = 1.5f;

    [Tooltip("Blink-Geschwindigkeit (Zyklen pro Sekunde)")]
    [SerializeField] private float blinkFrequency = 4f;

    [Header("Audio")]
    [Tooltip("Wird beim Übergang von Warning → Active abgespielt")]
    [SerializeField] private AudioClip activateSound;

    [Tooltip("Wird beim Übergang von Active → Expired abgespielt")]
    [SerializeField] private AudioClip deactivateSound;

    [Tooltip("Optional: Wird beim Spawn (Beginn der Warn-Phase) abgespielt")]
    [SerializeField] private AudioClip warnSound;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private enum ZoneState
    {
        Warning,    // Warn-Phase: Sphere sichtbar, Dash noch erlaubt
        Active,     // Scharfe Phase: Dash wird in Zone blockiert
        Expired     // Zone abgelaufen, wird gerade zerstört
    }

    private ZoneState zoneState = ZoneState.Warning;

    // Player-Referenzen
    private PlayerCore playerCore;
    private PlayerDash playerDash;
    private Transform playerTransform;

    // Zone-Tracking (nur in Active-Phase relevant)
    private bool playerInZone;
    private bool isDashDisabled;
    private float dashCancelTimer;
    private bool dashCancelActive;

    // Timer
    private float warnTimer;        // läuft hoch von 0 → warnDuration
    private float remainingTime;    // läuft runter von duration → 0

    // Billboard / Warn-Sphere
    private Transform cameraTransform;
    private AudioSource audioSource;
    private Material warnSphereMaterial;

    // Shader-Property-ID für die Hauptfarbe (gecacht für Performance)
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialisiert die Zone mit optionalen Override-Werten.
    /// Wird von AntiDashGrenade aufgerufen nach Instantiate().
    /// Wenn die Werte 0 oder negativ sind, werden die Inspector-Defaults benutzt.
    /// </summary>
    public void Initialize(float overrideRadius = -1f, float overrideDuration = -1f)
    {
        if (overrideRadius > 0f) effectRadius = overrideRadius;
        if (overrideDuration > 0f) duration = overrideDuration;
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

    private void Start()
    {
        // Spieler finden
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            playerCore = player.GetComponent<PlayerCore>();
            playerDash = player.GetComponent<PlayerDash>();
        }

        if (playerCore == null || playerDash == null)
        {
            Debug.LogError($"[AntiDashZone] {name}: PlayerCore/PlayerDash nicht gefunden! Zone wird zerstört.");
            Destroy(gameObject);
            return;
        }

        // Kamera cachen für Billboard
        cameraTransform = Camera.main != null ? Camera.main.transform : null;

        // Setup
        SetupWarnSphere();
        SetupBillboard();

        // Active-Billboard zu Beginn ausblenden (wird erst in Active-Phase sichtbar)
        if (billboardTransform != null)
            billboardTransform.gameObject.SetActive(false);

        // In Warn-Phase starten
        zoneState = ZoneState.Warning;
        warnTimer = 0f;
        remainingTime = duration;

        PlaySound(warnSound);
    }

    private void Update()
    {
        switch (zoneState)
        {
            case ZoneState.Warning:
                UpdateWarningPhase();
                break;

            case ZoneState.Active:
                UpdateActivePhase();
                break;
        }
    }

    private void OnDestroy()
    {
        // Sicherheit: Dash immer wieder aktivieren wenn Zone zerstört wird
        if (isDashDisabled && playerDash != null)
        {
            playerDash.SetDashEnabled(true);
        }

        // Material-Instanz aufräumen (verhindert Memory Leak)
        if (warnSphereMaterial != null)
        {
            Destroy(warnSphereMaterial);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Phase: Warning
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateWarningPhase()
    {
        warnTimer += Time.deltaTime;

        // Farbverlauf der Warn-Sphere
        UpdateWarnSphereColor();

        // Warn-Sphere zur Kamera drehen (falls sie ein Billboard-Quad ist)
        UpdateWarnSphereRotation();

        // Warn-Phase abgelaufen → Zone scharf schalten
        if (warnTimer >= warnDuration)
        {
            ActivateZone();
        }
    }

    /// <summary>
    /// Aktualisiert die Farbe der Warn-Sphere basierend auf dem Warn-Fortschritt.
    /// Analog zu ProxyMineNpc.UpdateWarnSphereColor().
    /// </summary>
    private void UpdateWarnSphereColor()
    {
        if (warnSphereMaterial == null) return;

        // Warn-Fortschritt: 0 = gerade gestartet, 1 = kurz vor Aktivierung
        float warnProgress = warnTimer / warnDuration;
        warnProgress = Mathf.Clamp01(warnProgress);

        // AnimationCurve auswerten → steuert den Blend zwischen den Farben
        float curveValue = warnColorCurve.Evaluate(warnProgress);

        Color currentColor = Color.Lerp(warnColorStart, warnColorEnd, curveValue);
        warnSphereMaterial.SetColor(BaseColorID, currentColor);
    }

    private void UpdateWarnSphereRotation()
    {
        if (warnSphereRenderer == null || cameraTransform == null) return;

        // Nur drehen wenn die Sphere ein flaches Billboard ist.
        // Bei einer echten 3D-Sphere ist das harmlos (sieht aus wie immer).
        // Falls du das nicht willst, kannst du diese Methode komplett entfernen.
    }

    private void SetupWarnSphere()
    {
        if (warnSphereRenderer == null)
        {
            Debug.LogWarning($"[AntiDashZone] '{name}': Kein WarnSphere-Renderer zugewiesen! Warn-Phase wird ohne Visual ablaufen.", this);
            return;
        }

        // Material-Instanz erstellen (verhindert, dass alle Zones dasselbe Material teilen)
        warnSphereMaterial = warnSphereRenderer.material;

        // Größe an Effekt-Radius anpassen
        float diameter = effectRadius * 2f;
        warnSphereRenderer.transform.localScale = Vector3.one * diameter;

        // Sichtbar während Warn-Phase
        warnSphereRenderer.enabled = true;

        // Startfarbe sofort setzen (statt auf ersten Update-Frame zu warten)
        warnSphereMaterial.SetColor(BaseColorID, warnColorStart);
    }

    private void HideWarnSphere()
    {
        if (warnSphereRenderer == null) return;
        warnSphereRenderer.enabled = false;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Phase Transition: Warning → Active
    // ════════════════════════════════════════════════════════════════════════

    private void ActivateZone()
    {
        zoneState = ZoneState.Active;
        remainingTime = duration;

        // Warn-Sphere ausblenden, scharfes Billboard einblenden
        HideWarnSphere();

        if (billboardTransform != null)
            billboardTransform.gameObject.SetActive(true);

        PlaySound(activateSound);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Phase: Active
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateActivePhase()
    {
        // Timer herunterzählen
        remainingTime -= Time.deltaTime;

        if (remainingTime <= 0f)
        {
            Deactivate();
            return;
        }

        // Zone-Check (gleiche Logik wie AntiDashDroneNpc)
        UpdateZoneCheck();

        // Billboard zur Kamera drehen
        UpdateBillboard();

        // Blink-Effekt kurz vor Ablauf
        if (blinkBeforeExpiry)
            UpdateBlink();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Zone Logic (aus AntiDashDroneNpc übernommen)
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateZoneCheck()
    {
        if (playerCore == null || playerDash == null) return;

        float distance = Vector3.Distance(transform.position, playerTransform.position);
        bool wasInZone = playerInZone;
        playerInZone = distance <= effectRadius;

        // Spieler betritt Zone
        if (playerInZone && !wasInZone)
        {
            OnPlayerEnterZone();
        }
        // Spieler verlässt Zone
        else if (!playerInZone && wasInZone)
        {
            OnPlayerExitZone();
        }

        // Dash-Abbruch prüfen
        if (playerInZone)
        {
            CheckDashCancellation();
        }
    }

    private void OnPlayerEnterZone()
    {
        // Neue Dashes blockieren
        if (!isDashDisabled)
        {
            playerDash.SetDashEnabled(false);
            isDashDisabled = true;
        }

        // Laufender Dash → Cancel-Timer starten
        if (playerCore.CurrentState == PlayerCore.PlayerState.Dashing)
        {
            StartDashCancelTimer();
        }
    }

    private void OnPlayerExitZone()
    {
        // Dash wieder erlauben
        if (isDashDisabled)
        {
            playerDash.SetDashEnabled(true);
            isDashDisabled = false;
        }

        dashCancelActive = false;
        dashCancelTimer = 0f;
    }

    private void CheckDashCancellation()
    {
        bool isPlayerDashing = playerCore.CurrentState == PlayerCore.PlayerState.Dashing;

        if (isPlayerDashing)
        {
            if (!dashCancelActive)
            {
                StartDashCancelTimer();
            }
            else
            {
                // Unscaled Time damit es auch während SlowMo funktioniert
                dashCancelTimer -= Time.unscaledDeltaTime;

                if (dashCancelTimer <= 0f)
                {
                    playerDash.ForceCancelDash();
                    dashCancelActive = false;
                }
            }
        }
        else
        {
            dashCancelActive = false;
        }
    }

    private void StartDashCancelTimer()
    {
        dashCancelActive = true;
        dashCancelTimer = dashCancelDelay;
    }

    private void DisableZone()
    {
        if (isDashDisabled && playerDash != null)
        {
            playerDash.SetDashEnabled(true);
            isDashDisabled = false;
        }

        playerInZone = false;
        dashCancelActive = false;
        dashCancelTimer = 0f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Phase Transition: Active → Expired
    // ════════════════════════════════════════════════════════════════════════

    private void Deactivate()
    {
        zoneState = ZoneState.Expired;
        DisableZone();
        PlaySound(deactivateSound);

        // Billboard verstecken
        if (billboardTransform != null)
            billboardTransform.gameObject.SetActive(false);

        // Kurz warten damit der Deactivate-Sound noch abgespielt wird
        Destroy(gameObject, 1f);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Billboard (Active Phase)
    // ════════════════════════════════════════════════════════════════════════

    private void SetupBillboard()
    {
        if (billboardTransform == null) return;

        float diameter = effectRadius * 2f;
        billboardTransform.localScale = new Vector3(diameter, diameter, diameter);
    }

    private void UpdateBillboard()
    {
        if (billboardTransform == null || cameraTransform == null) return;
        if (!billboardTransform.gameObject.activeSelf) return;

        // Billboard zeigt immer zur Kamera
        billboardTransform.LookAt(
            billboardTransform.position + cameraTransform.forward
        );
    }

    private void UpdateBlink()
    {
        if (billboardTransform == null) return;

        if (remainingTime <= blinkStartTime)
        {
            // Sin-basiertes Blinken: 0-1-0-1...
            float blinkValue = Mathf.Sin(Time.time * blinkFrequency * Mathf.PI * 2f);
            billboardTransform.gameObject.SetActive(blinkValue > 0f);
        }
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
        // Zone-Radius immer zeigen (leicht transparent)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, effectRadius);
    }

    private void OnDrawGizmosSelected()
    {
        // Farbe je nach Phase
        Color sphereColor = zoneState switch
        {
            ZoneState.Warning => new Color(1f, 0.9f, 0f, 0.25f),    // gelb
            ZoneState.Active  => new Color(1f, 0.5f, 0f, 0.25f),    // orange
            _                 => new Color(0.5f, 0.5f, 0.5f, 0.15f) // grau
        };

        Gizmos.color = sphereColor;
        Gizmos.DrawSphere(transform.position, effectRadius);

        Gizmos.color = new Color(sphereColor.r, sphereColor.g, sphereColor.b, 0.8f);
        Gizmos.DrawWireSphere(transform.position, effectRadius);

        // Spieler-Verbindung zur Laufzeit
        if (Application.isPlaying && playerTransform != null)
        {
            Gizmos.color = playerInZone ? Color.red : Color.green;
            Gizmos.DrawLine(transform.position, playerTransform.position);
        }
    }

    #endregion
}
