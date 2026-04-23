using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Macht einen NPC temporär "durch Wände sichtbar" indem eine Liste von
/// Reveal-GameObjects aktiviert/deaktiviert wird. Am Ende der Reveal-Zeit
/// fadet der Effekt aus (Material-Alpha von 1 auf 0), danach werden die
/// GameObjects deaktiviert.
///
/// VORAUSSETZUNG:
/// Die Duplicate-Meshes nutzen ein Material mit dem Shader "Custom/XRayUnlit"
/// (oder einen kompatiblen Unlit-Transparent-Shader mit ZTest Always).
/// WICHTIG: Es darf KEIN URP Render Feature aktiv sein, das diese Meshes
/// nochmal überrendered — dann würde das lokale Alpha ignoriert.
///
/// ABLAUFPHASEN:
///   1. Hidden        — GameObjects deaktiviert, Alpha = 1 (für nächsten Reveal)
///   2. FullVisible   — GameObjects aktiv, Alpha = 1, läuft 'revealDuration' lang
///   3. FadingOut     — Alpha fadet über 'fadeDuration' von 1 auf 0
///   4. Hidden        — GameObjects deaktiviert, Alpha zurück auf 1
///
/// PREFAB SETUP:
/// 1. Body-Mesh: SkinnedMeshRenderer-Child duplizieren → XRay-Material zuweisen
/// 2. Waffen o.ä.: Unter demselben Bone wie das Original duplizieren →
///    Logik-Komponenten entfernen (Collider, Scripts, etc.) → XRay-Material
/// 3. Alle Duplicates per default auf SetActive(false)
/// 4. Diese Komponente ans Root-GameObject, alle Duplicates in die Liste ziehen
/// </summary>
public class NpcReveal : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Phases
    // ════════════════════════════════════════════════════════════════════════

    private enum Phase
    {
        Hidden,
        FullVisible,
        FadingOut
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector
    // ════════════════════════════════════════════════════════════════════════

    [Header("Reveal Setup")]
    [Tooltip("GameObjects die beim Reveal aktiviert werden. " +
             "Typisch: Body-Duplicate, Waffen-Duplicate(s), weitere Attachments.")]
    [SerializeField] private GameObject[] revealObjects;

    [Header("Fade")]
    [Tooltip("Dauer der Fade-Out-Phase am Ende (in Sekunden). 0 = kein Fade.")]
    [SerializeField] private float fadeDuration = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private Phase phase = Phase.Hidden;
    private float fullVisibleEndTime;
    private float fadeEndTime;

    // Gecachte Renderer für Alpha-Manipulation
    private readonly List<Renderer> revealRenderers = new List<Renderer>();
    private int colorPropertyId;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Properties
    // ════════════════════════════════════════════════════════════════════════

    public bool IsRevealed => phase != Phase.Hidden;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (revealObjects == null || revealObjects.Length == 0)
        {
            Debug.LogError($"[NpcReveal] Keine Reveal-Objects zugewiesen auf {name}! " +
                           "Siehe Setup-Anleitung im Script-Header.", this);
            enabled = false;
            return;
        }

        CacheRenderers();
        DetectColorProperty();

        // Sicherstellen dass alle Reveal-Objects zu Beginn versteckt sind
        SetAllActive(false);
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.FullVisible:
                if (Time.time >= fullVisibleEndTime)
                    StartFadeOut();
                break;

            case Phase.FadingOut:
                UpdateFadeOut();
                break;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aktiviert den X-Ray-Effekt für die angegebene Dauer, danach Fade-Out.
    /// Gesamt-Sichtbarkeit = duration + fadeDuration.
    ///
    /// Wenn der NPC bereits markiert ist, wird die Sichtbarkeitszeit verlängert
    /// (aber nicht verkürzt). Ein laufender Fade wird abgebrochen und resettet.
    /// </summary>
    public void Reveal(float duration)
    {
        if (!enabled) return;

        float newEndTime = Time.time + duration;

        // Wenn gerade gefadet wird oder versteckt: komplett neu starten
        if (phase == Phase.Hidden || phase == Phase.FadingOut)
        {
            SetAllActive(true);
            SetAlpha(1f);
            phase = Phase.FullVisible;
            fullVisibleEndTime = newEndTime;

            if (logDebug)
                Debug.Log($"[NpcReveal] {name} revealed for {duration}s " +
                          $"(+ {fadeDuration}s fade)", this);
            return;
        }

        // Phase.FullVisible: Timer nur verlängern, nie verkürzen
        if (newEndTime > fullVisibleEndTime)
            fullVisibleEndTime = newEndTime;
    }

    /// <summary>
    /// Beendet den X-Ray-Effekt sofort (ohne Fade).
    /// </summary>
    public void Hide()
    {
        if (phase == Phase.Hidden) return;

        SetAllActive(false);
        SetAlpha(1f); // Reset für nächsten Reveal
        phase = Phase.Hidden;

        if (logDebug)
            Debug.Log($"[NpcReveal] {name} hidden (immediate)", this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Phase Transitions
    // ════════════════════════════════════════════════════════════════════════

    private void StartFadeOut()
    {
        if (fadeDuration <= 0f)
        {
            // Kein Fade → direkt verstecken
            Hide();
            return;
        }

        phase = Phase.FadingOut;
        fadeEndTime = Time.time + fadeDuration;

        if (logDebug)
            Debug.Log($"[NpcReveal] {name} starting fade out", this);
    }

    private void UpdateFadeOut()
    {
        float remaining = fadeEndTime - Time.time;

        if (remaining <= 0f)
        {
            Hide();
            return;
        }

        // Lineares Fade von 1 auf 0
        float alpha = remaining / fadeDuration;
        SetAlpha(alpha);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Rendering Helpers
    // ════════════════════════════════════════════════════════════════════════

    private void CacheRenderers()
    {
        revealRenderers.Clear();
        foreach (var obj in revealObjects)
        {
            if (obj == null) continue;

            // Alle Renderer im Reveal-Object und seinen Children sammeln
            var renderers = obj.GetComponentsInChildren<Renderer>(includeInactive: true);
            revealRenderers.AddRange(renderers);
        }
    }

    /// <summary>
    /// Prüft ob das Material "_BaseColor" oder "_Color" verwendet.
    /// </summary>
    private void DetectColorProperty()
    {
        if (revealRenderers.Count == 0)
        {
            colorPropertyId = BaseColorId;
            return;
        }

        var mat = revealRenderers[0].sharedMaterial;
        if (mat != null && mat.HasProperty(BaseColorId))
        {
            colorPropertyId = BaseColorId;
        }
        else if (mat != null && mat.HasProperty(ColorId))
        {
            colorPropertyId = ColorId;
        }
        else
        {
            Debug.LogWarning($"[NpcReveal] Material auf {name} hat weder '_BaseColor' " +
                             $"noch '_Color' Property — Fade funktioniert nicht!", this);
            colorPropertyId = BaseColorId;
        }
    }

    private void SetAlpha(float alpha)
    {
        foreach (var renderer in revealRenderers)
        {
            if (renderer == null) continue;

            // .material erzeugt pro Renderer eine Instanz
            var mat = renderer.material;
            if (!mat.HasProperty(colorPropertyId)) continue;

            Color c = mat.GetColor(colorPropertyId);
            c.a = alpha;
            mat.SetColor(colorPropertyId, c);
        }
    }

    private void SetAllActive(bool active)
    {
        for (int i = 0; i < revealObjects.Length; i++)
        {
            if (revealObjects[i] != null)
                revealObjects[i].SetActive(active);
        }
    }

    #endregion
}
