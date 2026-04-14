using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// ANTI-DASH ZONE - Temporäre Dash-Sperrzone (z.B. von Granaten erzeugt)
// ════════════════════════════════════════════════════════════════════════════
//
// Gleiche Kernlogik wie AntiDashDroneNpc.UpdateZoneCheck():
//   - Spieler betritt Zone → Dash wird geblockt
//   - Spieler dasht in Zone → Dash wird nach kurzer Verzögerung abgebrochen
//   - Spieler verlässt Zone → Dash wird wieder erlaubt
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

    [Tooltip("Wie lange die Zone aktiv bleibt (in Sekunden)")]
    [SerializeField] private float duration = 5f;

    [Tooltip("Verzögerung (unscaled Sekunden) bevor ein aktiver Dash abgebrochen wird")]
    [SerializeField] private float dashCancelDelay = 0.1f;

    [Header("Billboard")]
    [Tooltip("Child-Quad/Sprite das den Zonenradius visualisiert")]
    [SerializeField] private Transform billboardTransform;

    [Header("Visual Feedback")]
    [Tooltip("Wenn true, wird das Billboard kurz vor Ablauf ausgeblendet (Blink-Effekt)")]
    [SerializeField] private bool blinkBeforeExpiry = true;

    [Tooltip("Wann das Blinken beginnt (Sekunden vor Ablauf)")]
    [SerializeField] private float blinkStartTime = 1.5f;

    [Tooltip("Blink-Geschwindigkeit (Zyklen pro Sekunde)")]
    [SerializeField] private float blinkFrequency = 4f;

    [Header("Audio")]
    [SerializeField] private AudioClip activateSound;
    [SerializeField] private AudioClip deactivateSound;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    // Player-Referenzen
    private PlayerCore playerCore;
    private PlayerDash playerDash;
    private Transform playerTransform;

    // Zone-Tracking
    private bool playerInZone;
    private bool isDashDisabled;
    private float dashCancelTimer;
    private bool dashCancelActive;

    // Timer
    private float remainingTime;
    private bool isActive;

    // Billboard
    private Transform cameraTransform;
    private AudioSource audioSource;

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

        // Billboard skalieren
        SetupBillboard();

        // Zone aktivieren
        remainingTime = duration;
        isActive = true;

        PlaySound(activateSound);
    }

    private void Update()
    {
        if (!isActive) return;

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

    private void OnDestroy()
    {
        // Sicherheit: Dash immer wieder aktivieren wenn Zone zerstört wird
        if (isDashDisabled && playerDash != null)
        {
            playerDash.SetDashEnabled(true);
        }
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
    #region Deactivation
    // ════════════════════════════════════════════════════════════════════════

    private void Deactivate()
    {
        isActive = false;
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
    #region Billboard
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
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
        Gizmos.DrawSphere(transform.position, effectRadius);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
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
