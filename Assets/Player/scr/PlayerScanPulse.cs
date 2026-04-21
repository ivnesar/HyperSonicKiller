using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Scan-Puls-Mechanik: Bei Tastendruck (Q) sendet der Spieler einen Puls aus,
/// dessen Radius mit konstanter Geschwindigkeit wächst. NPCs, die vom Puls
/// erfasst werden, werden für eine Hold-Duration "durch Wände sichtbar" gemacht.
///
/// ABLAUF:
/// 1. Q gedrückt       -> Puls startet bei Radius 0
/// 2. Wachstumsphase   -> Radius wächst mit pulseSpeed bis maxRadius
/// 3. Hold-Phase       -> maxRadius steht, bereits markierte NPCs bleiben aktiv
/// 4. Hold-Timer läuft ab -> NpcReveal.Hide() wird automatisch durch die
///                          revealDuration am NPC selbst ausgelöst (siehe NpcReveal)
///
/// INTEGRATION:
/// - Braucht PlayerCore auf dem gleichen GameObject.
/// - Nutzt NpcRegistry.GetAll() um alle lebenden NPCs abzufragen.
/// - Jeder NPC braucht die NpcReveal-Komponente.
/// - Input: Q (hardgecoded - später eventuell in PlayerInputHandler migrieren).
/// </summary>
[RequireComponent(typeof(PlayerCore))]
public class PlayerScanPulse : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector
    // ════════════════════════════════════════════════════════════════════════

    [Header("Input")]
    [SerializeField] private KeyCode scanKey = KeyCode.Q;

    [Header("Pulse")]
    [Tooltip("Geschwindigkeit, mit der der Puls-Radius wächst (units/sec).")]
    [SerializeField] private float pulseSpeed = 20f;

    [Tooltip("Maximaler Radius, den der Puls erreicht.")]
    [SerializeField] private float maxRadius = 40f;

    [Tooltip("Wie lange markierte NPCs sichtbar bleiben, nachdem sie erfasst wurden.")]
    [SerializeField] private float revealDuration = 3f;

    [Tooltip("Wie lange der Puls nach Erreichen von maxRadius noch aktiv bleibt " +
             "(in dieser Zeit werden auch Nachzügler erfasst, die erst jetzt in den " +
             "Radius kommen - z.B. sich bewegende NPCs). Meist 0 oder sehr kurz.")]
    [SerializeField] private float holdDuration = 0.2f;

    [Header("Cooldown")]
    [Tooltip("Minimale Zeit zwischen zwei Scans.")]
    [SerializeField] private float cooldown = 1f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color pulseColor = new Color(0f, 0.8f, 1f, 0.5f);
    [SerializeField] private bool logDebug = false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;

    private bool isPulsing;
    private float currentRadius;
    private float pulseEndTime;      // Zeitpunkt, an dem der Puls komplett endet
    private float lastScanTime = -999f;
    
    
    // NPCs die bereits markiert wurden (um doppelte Reveal-Aufrufe pro Puls zu vermeiden)
    private readonly HashSet<NpcBase> markedThisPulse = new HashSet<NpcBase>();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public bool IsPulsing => isPulsing;
    public float CurrentRadius => currentRadius;
    public float MaxRadius => maxRadius;
    public float CooldownRemaining => Mathf.Max(0f, (lastScanTime + cooldown) - Time.time);
    public bool IsOnCooldown => CooldownRemaining > 0f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
    }

    private void Update()
    {
        if (core.IsDead) return;

        HandleInput();

        if (isPulsing)
            UpdatePulse();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Input
    // ════════════════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        if (!Input.GetKeyDown(scanKey)) return;
        if (isPulsing) return;
        if (IsOnCooldown) return;

        StartPulse();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Pulse Logic
    // ════════════════════════════════════════════════════════════════════════

    private void StartPulse()
    {
        isPulsing = true;
        currentRadius = 0f;
        markedThisPulse.Clear();
        lastScanTime = Time.time;

        // pulseEndTime wird in UpdatePulse() gesetzt, sobald maxRadius erreicht ist
        pulseEndTime = -1f;

        if (logDebug)
            Debug.Log($"[PlayerScanPulse] Scan gestartet");
    }

    private void UpdatePulse()
    {
        // Wachstumsphase
        if (currentRadius < maxRadius)
        {
            currentRadius += pulseSpeed * Time.unscaledDeltaTime;

            if (currentRadius >= maxRadius)
            {
                currentRadius = maxRadius;
                pulseEndTime = Time.time + holdDuration;
            }
        }

        // NPCs prüfen und ggf. markieren
        CheckAndMarkNpcs();

        // Puls beenden
        if (pulseEndTime > 0f && Time.time >= pulseEndTime)
        {
            EndPulse();
        }
    }

    private void CheckAndMarkNpcs()
    {
        Vector3 origin = transform.position;
        float radiusSqr = currentRadius * currentRadius;

        // NpcRegistry liefert alle lebenden NPCs - deutlich performanter als FindObjectsOfType
        foreach (var npc in NpcRegistry.AliveNpcs)
        {
            if (npc == null || npc.IsDead) continue;
            if (markedThisPulse.Contains(npc)) continue;

            float distSqr = (npc.transform.position - origin).sqrMagnitude;
            if (distSqr > radiusSqr) continue;

            // NPC ist im aktuellen Radius - markieren
            var reveal = npc.GetComponent<NpcReveal>();
            if (reveal != null)
            {
                reveal.Reveal(revealDuration);
                markedThisPulse.Add(npc);

                if (logDebug)
                    Debug.Log($"[PlayerScanPulse] {npc.name} erfasst bei Radius {currentRadius:F1}");
            }
        }
    }

    private void EndPulse()
    {
        isPulsing = false;
        currentRadius = 0f;
        pulseEndTime = -1f;

        if (logDebug)
            Debug.Log($"[PlayerScanPulse] Scan beendet");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Gizmos
    // ════════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !isPulsing) return;

        Gizmos.color = pulseColor;
        Gizmos.DrawWireSphere(transform.position, currentRadius);
    }

    #endregion
}
