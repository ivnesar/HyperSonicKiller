using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Zentraler Time Manager — Singleton.
/// Verwaltet Time.timeScale über ein Prioritätssystem mit Layern.
/// 
/// Stellt außerdem GameDeltaTime bereit:
///   - Verhält sich wie Time.unscaledDeltaTime (ignoriert SlowMo)
///   - Wird aber bei Pause und HitStop auf 0 gesetzt
///   - Nutze dies überall wo bisher Time.unscaledDeltaTime stand,
///     AUSSER der Code soll auch bei Pause weiterlaufen (z.B. Pause-UI)
///
/// ═══════════════════════════════════════════════════════════════════
/// PRIORITÄTEN (höchste zuerst):
///   Pause       (100)  → timeScale = 0     blockiert ALLES
///   HitStop     (50)   → timeScale = 0     kurzer Freeze bei Treffer
///   DashSlowMo  (10)   → timeScale = 0.1   Slow-Motion während Dash
///   (Default)   (0)    → timeScale = 1.0   Normalzustand
/// ═══════════════════════════════════════════════════════════════════
///
/// USAGE:
///   // Layer setzen (z.B. Dash-Start):
///   TimeManager.Instance.SetLayer("DashSlowMo", 0.1f, 10);
///   
///   // Layer entfernen (z.B. Dash-Ende):
///   TimeManager.Instance.RemoveLayer("DashSlowMo");
///   
///   // Temporären Layer setzen (z.B. HitStop für 0.05s):
///   TimeManager.Instance.SetTemporaryLayer("HitStop", 0f, 50, 0.05f);
///   
///   // In Update statt Time.unscaledDeltaTime:
///   float dt = TimeManager.Instance.GameDeltaTime;
///
/// SETUP:
///   Lege ein leeres GameObject in die Szene und hänge TimeManager dran.
///   Oder: Der TimeManager erstellt sich automatisch wenn er zum ersten Mal
///   über Instance angesprochen wird (lazy init).
/// </summary>
public class TimeManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Singleton
    // ════════════════════════════════════════════════════════════════════════

    private static TimeManager instance;

    public static TimeManager Instance
    {
        get
        {
            if (instance == null)
            {
                // Versuche existierendes Objekt zu finden
                instance = FindFirstObjectByType<TimeManager>();

                if (instance == null)
                {
                    // Lazy init: erstelle neues GameObject
                    var go = new GameObject("[TimeManager]");
                    instance = go.AddComponent<TimeManager>();
                }
            }

            return instance;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Events
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gefeuert wenn sich der effektive TimeScale ändert.
    /// Parameter: neuer timeScale-Wert.
    /// </summary>
    public event Action<float> OnTimeScaleChanged;

    /// <summary>
    /// Gefeuert wenn das Spiel pausiert oder fortgesetzt wird.
    /// Parameter: true = pausiert, false = fortgesetzt.
    /// </summary>
    public event Action<bool> OnPauseChanged;

    /// <summary>
    /// Gefeuert wenn ein HitStop beginnt oder endet.
    /// Parameter: true = HitStop aktiv, false = HitStop vorbei.
    /// </summary>
    public event Action<bool> OnHitStopChanged;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Vordefinierte Prioritäten
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Nutze diese Konstanten als Priorität beim Aufruf von SetLayer().</summary>
    public const int PRIORITY_DEFAULT = 0;
    public const int PRIORITY_SLOW_MO = 10;
    public const int PRIORITY_HITSTOP = 50;
    public const int PRIORITY_PAUSE = 100;

    /// <summary>Vordefinierte Layer-Namen.</summary>
    public const string LAYER_DASH_SLOW_MO = "DashSlowMo";
    public const string LAYER_HITSTOP = "HitStop";
    public const string LAYER_PAUSE = "Pause";

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal Types
    // ════════════════════════════════════════════════════════════════════════

    private class TimeLayer
    {
        public string Name;
        public float TimeScale;
        public int Priority;

        /// <summary>Nur für temporäre Layer: verbleibende Echtzeit-Dauer.</summary>
        public float RemainingDuration;

        /// <summary>True wenn dieser Layer automatisch abläuft.</summary>
        public bool IsTemporary;

        /// <summary>True wenn dieser Layer GameDeltaTime blockiert (= auf 0 setzt).</summary>
        public bool BlocksGameTime;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private readonly List<TimeLayer> activeLayers = new List<TimeLayer>();

    /// <summary>Der aktuell wirksame timeScale (vom höchst-priorisierten Layer).</summary>
    private float currentEffectiveTimeScale = 1f;

    /// <summary>True wenn mindestens ein Layer mit BlocksGameTime aktiv ist.</summary>
    private bool isGameTimeFrozen;

    /// <summary>Cached: war im letzten Frame pausiert?</summary>
    private bool wasPaused;

    /// <summary>Cached: war im letzten Frame HitStop aktiv?</summary>
    private bool wasHitStopped;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DeltaTime die von Gameplay-Code genutzt werden soll, der:
    ///   - Während SlowMo mit normaler Geschwindigkeit laufen soll
    ///   - Bei Pause und HitStop aber stoppen soll
    /// 
    /// Ersetzt Time.unscaledDeltaTime in: PlayerLook, GenTwo, CameraFX, etc.
    /// </summary>
    public float GameDeltaTime => isGameTimeFrozen ? 0f : Time.unscaledDeltaTime;

    /// <summary>
    /// True wenn das Spiel pausiert ist (Pause-Layer aktiv).
    /// </summary>
    public bool IsPaused => HasLayer(LAYER_PAUSE);

    /// <summary>
    /// True wenn ein HitStop aktiv ist.
    /// </summary>
    public bool IsHitStopActive => HasLayer(LAYER_HITSTOP);

    /// <summary>
    /// True wenn irgendein SlowMo-Layer aktiv ist.
    /// </summary>
    public bool IsSlowMoActive => HasLayer(LAYER_DASH_SLOW_MO);

    /// <summary>
    /// True wenn GameDeltaTime gerade 0 ist (Pause oder HitStop).
    /// </summary>
    public bool IsGameTimeFrozen => isGameTimeFrozen;

    /// <summary>
    /// Der aktuelle effektive timeScale-Wert.
    /// </summary>
    public float CurrentTimeScale => currentEffectiveTimeScale;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Singleton-Schutz
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[TimeManager] Duplicate detected — destroying this one.");
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        UpdateTemporaryLayers();
        ApplyTimeScale();
        FireStateChangeEvents();
    }

    private void OnDestroy()
    {
        // Sicherheitsnetz: timeScale auf 1 zurücksetzen
        if (instance == this)
        {
            Time.timeScale = 1f;
            instance = null;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API — Layer Management
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Setzt einen Zeit-Layer. Wenn ein Layer mit diesem Namen bereits existiert,
    /// wird er überschrieben.
    /// </summary>
    /// <param name="layerName">Eindeutiger Name (z.B. "DashSlowMo")</param>
    /// <param name="timeScale">Gewünschter timeScale (0 = freeze, 0.1 = slow, 1 = normal)</param>
    /// <param name="priority">Höhere Priorität gewinnt über niedrigere</param>
    /// <param name="blocksGameTime">True = GameDeltaTime wird 0 (für Pause/HitStop)</param>
    public void SetLayer(string layerName, float timeScale, int priority, bool blocksGameTime = false)
    {
        // Existierenden Layer mit gleichem Namen entfernen
        RemoveLayerInternal(layerName);

        var layer = new TimeLayer
        {
            Name = layerName,
            TimeScale = timeScale,
            Priority = priority,
            IsTemporary = false,
            BlocksGameTime = blocksGameTime
        };

        activeLayers.Add(layer);
        SortLayers();

       
    }

    /// <summary>
    /// Setzt einen temporären Layer der nach einer bestimmten Echtzeit-Dauer
    /// automatisch entfernt wird.
    /// </summary>
    /// <param name="layerName">Eindeutiger Name</param>
    /// <param name="timeScale">Gewünschter timeScale</param>
    /// <param name="priority">Priorität</param>
    /// <param name="duration">Dauer in Echtzeit-Sekunden (unscaled)</param>
    /// <param name="blocksGameTime">True = GameDeltaTime wird 0</param>
    public void SetTemporaryLayer(string layerName, float timeScale, int priority, float duration, bool blocksGameTime = false)
    {
        RemoveLayerInternal(layerName);

        var layer = new TimeLayer
        {
            Name = layerName,
            TimeScale = timeScale,
            Priority = priority,
            IsTemporary = true,
            RemainingDuration = duration,
            BlocksGameTime = blocksGameTime
        };

        activeLayers.Add(layer);
        SortLayers();

        Debug.Log($"[TimeManager] Temporary layer set: '{layerName}' (timeScale={timeScale}, priority={priority}, duration={duration}s, blocksGame={blocksGameTime})");
    }

    /// <summary>
    /// Entfernt einen Layer nach Name.
    /// </summary>
    public void RemoveLayer(string layerName)
    {
        if (RemoveLayerInternal(layerName))
        {
            Debug.Log($"[TimeManager] Layer removed: '{layerName}'");
        }
    }

    /// <summary>
    /// Prüft ob ein Layer mit diesem Namen aktiv ist.
    /// </summary>
    public bool HasLayer(string layerName)
    {
        for (int i = 0; i < activeLayers.Count; i++)
        {
            if (activeLayers[i].Name == layerName)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Entfernt ALLE aktiven Layer und setzt timeScale auf 1.
    /// Nützlich bei Szenen-Wechsel oder Reset.
    /// </summary>
    public void ClearAllLayers()
    {
        activeLayers.Clear();
        ApplyTimeScale();

        Debug.Log("[TimeManager] All layers cleared.");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API — Convenience Methods
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Startet Dash-SlowMo. Aufruf bei Dash-Start.
    /// </summary>
    public void StartDashSlowMo(float timeScale = 0.1f)
    {
        SetLayer(LAYER_DASH_SLOW_MO, timeScale, PRIORITY_SLOW_MO, blocksGameTime: false);
    }

    /// <summary>
    /// Beendet Dash-SlowMo. Aufruf bei Dash-Ende.
    /// </summary>
    public void StopDashSlowMo()
    {
        RemoveLayer(LAYER_DASH_SLOW_MO);
    }

    /// <summary>
    /// Triggert einen HitStop (kurzer Freeze).
    /// </summary>
    /// <param name="duration">Dauer in Echtzeit-Sekunden (typisch: 0.03 - 0.08)</param>
    public void TriggerHitStop(float duration)
    {
        SetTemporaryLayer(LAYER_HITSTOP, 0f, PRIORITY_HITSTOP, duration, blocksGameTime: true);
    }

    /// <summary>
    /// Pausiert das Spiel.
    /// </summary>
    public void Pause()
    {
        SetLayer(LAYER_PAUSE, 0f, PRIORITY_PAUSE, blocksGameTime: true);
    }

    /// <summary>
    /// Setzt das Spiel fort.
    /// </summary>
    public void Unpause()
    {
        RemoveLayer(LAYER_PAUSE);
    }

    /// <summary>
    /// Toggle: Pausiert oder setzt fort.
    /// </summary>
    public void TogglePause()
    {
        if (IsPaused)
            Unpause();
        else
            Pause();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Internal — Layer Processing
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aktualisiert temporäre Layer (countdown in Echtzeit).
    /// </summary>
    private void UpdateTemporaryLayers()
    {
        bool layerRemoved = false;

        for (int i = activeLayers.Count - 1; i >= 0; i--)
        {
            var layer = activeLayers[i];

            if (!layer.IsTemporary) continue;

            layer.RemainingDuration -= Time.unscaledDeltaTime;

            if (layer.RemainingDuration <= 0f)
            {
                Debug.Log($"[TimeManager] Temporary layer expired: '{layer.Name}'");
                activeLayers.RemoveAt(i);
                layerRemoved = true;
            }
        }

        if (layerRemoved)
        {
            // Keine Neusortierung nötig — Reihenfolge hat sich nicht geändert
        }
    }

    /// <summary>
    /// Bestimmt den effektiven timeScale aus dem höchst-priorisierten Layer
    /// und wendet ihn auf Time.timeScale an.
    /// </summary>
    private void ApplyTimeScale()
    {
        float previousTimeScale = currentEffectiveTimeScale;

        if (activeLayers.Count > 0)
        {
            // Erster Layer hat höchste Priorität (Liste ist sortiert)
            var topLayer = activeLayers[0];
            currentEffectiveTimeScale = topLayer.TimeScale;
        }
        else
        {
            // Kein Layer aktiv → Normalzustand
            currentEffectiveTimeScale = 1f;
        }

        Time.timeScale = currentEffectiveTimeScale;

        // GameDeltaTime frozen-Check: irgendein aktiver Layer mit BlocksGameTime?
        isGameTimeFrozen = false;
        for (int i = 0; i < activeLayers.Count; i++)
        {
            if (activeLayers[i].BlocksGameTime)
            {
                isGameTimeFrozen = true;
                break;
            }
        }

        // Event feuern wenn sich timeScale geändert hat
        if (!Mathf.Approximately(previousTimeScale, currentEffectiveTimeScale))
        {
            OnTimeScaleChanged?.Invoke(currentEffectiveTimeScale);
        }
    }

    /// <summary>
    /// Feuert Pause/HitStop Events wenn sich der Zustand geändert hat.
    /// </summary>
    private void FireStateChangeEvents()
    {
        bool isPaused = IsPaused;
        bool isHitStopped = IsHitStopActive;

        if (isPaused != wasPaused)
        {
            OnPauseChanged?.Invoke(isPaused);
            wasPaused = isPaused;
        }

        if (isHitStopped != wasHitStopped)
        {
            OnHitStopChanged?.Invoke(isHitStopped);
            wasHitStopped = isHitStopped;
        }
    }

    /// <summary>
    /// Entfernt einen Layer nach Name (ohne Log). Gibt true zurück wenn gefunden.
    /// </summary>
    private bool RemoveLayerInternal(string layerName)
    {
        for (int i = activeLayers.Count - 1; i >= 0; i--)
        {
            if (activeLayers[i].Name == layerName)
            {
                activeLayers.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Sortiert Layer nach Priorität (höchste zuerst).
    /// </summary>
    private void SortLayers()
    {
        activeLayers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gibt eine Debug-Übersicht aller aktiven Layer zurück.
    /// </summary>
    public string GetDebugInfo()
    {
        if (activeLayers.Count == 0)
            return "TimeManager: No active layers (timeScale=1.0)";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"TimeManager: timeScale={currentEffectiveTimeScale:F2} | GameFrozen={isGameTimeFrozen}");

        for (int i = 0; i < activeLayers.Count; i++)
        {
            var l = activeLayers[i];
            string temp = l.IsTemporary ? $" [{l.RemainingDuration:F3}s left]" : "";
            string blocks = l.BlocksGameTime ? " [BLOCKS GAME]" : "";
            sb.AppendLine($"  [{i}] {l.Name} (prio={l.Priority}, scale={l.TimeScale:F2}){temp}{blocks}");
        }

        return sb.ToString();
    }

    #endregion
}
