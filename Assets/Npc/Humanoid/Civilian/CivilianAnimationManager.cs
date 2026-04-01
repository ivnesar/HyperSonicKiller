using Animancer;
using UnityEngine;

/// <summary>
/// Manages all Civilian NPC animations via Animancer.
///
/// Einfacher als der SoldierAnimationManager — nur ein Layer (Fullbody),
/// da der Civilian keine Waffe hat und keine Oberkörper-Overrides braucht.
///
/// ANIMATIONEN:
///   SetDresser:  SetDressing (Loop) — beliebige Animation zum Szene-Beleben
///   Fleeing:     Idle (Loop), PanicRun (Loop)
///   Fallen:      Fall (OneShot) → FallIdle (Loop)
///   Stun:        Stunned (Loop) — von NpcBase gesteuert
///
/// SETUP:
///   1. AnimancerComponent auf das Civilian-Model (Kind mit Animator) legen.
///   2. AnimatorController vom Animator entfernen (leer lassen).
///   3. Alle benötigten ClipTransitions im Inspector zuweisen.
/// </summary>
public class CivilianAnimationManager : MonoBehaviour, INpcAnimationHandler
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Animancer Reference")]
    [Tooltip("AnimancerComponent auf dem Civilian-Model. Auto-found wenn leer.")]
    [SerializeField] private AnimancerComponent animancer;

    // ── Set Dressing ─────────────────────────────────────────────────────

    [Header("Set Dressing Clip (Looping)")]
    [Tooltip("Beliebige Loop-Animation zum Szene-Beleben (z.B. Tür öffnen, PC bedienen, Kehren).\n" +
             "Wird nur im SetDresser-Modus verwendet.")]
    [SerializeField] private ClipTransition setDressingClip;

    // ── Fleeing Clips ────────────────────────────────────────────────────

    [Header("Fleeing Clips (Looping)")]
    [SerializeField] private ClipTransition idle;

    [Tooltip("Panisches Rennen — Arme hochgerissen, chaotisch.")]
    [SerializeField] private ClipTransition panicRun;

    // ── Fall Clips ───────────────────────────────────────────────────────

    [Header("Fall Clips")]
    [Tooltip("Fall-Animation (OneShot) — NPC fällt hin wenn Spieler zu nah kommt.")]
    [SerializeField] private ClipTransition fall;

    [Tooltip("Fall-Idle (Loop) — NPC liegt am Boden nach dem Hinfallen.")]
    [SerializeField] private ClipTransition fallIdle;

    // ── Stun Clip ────────────────────────────────────────────────────────

    [Header("Stun Clip (Looping)")]
    [SerializeField] private ClipTransition stunned;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private AnimancerLayer baseLayer;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (animancer == null)
            animancer = GetComponentInChildren<AnimancerComponent>();

        if (animancer == null)
        {
            Debug.LogError($"[CivilianAnimationManager] No AnimancerComponent found on {gameObject.name}!");
            enabled = false;
            return;
        }

        // Layer-Referenz sofort in Awake holen,
        // damit sie bereit ist wenn NpcBase.Start() → OnStart() aufgerufen wird.
        EnsureLayerInitialized();
    }

    private void Start()
    {
        EnsureLayerInitialized();
        // Startet mit Idle — CivilianNpc.OnStart() ruft dann den richtigen State auf.
        baseLayer.Play(idle);
    }

    /// <summary>
    /// Holt die Layer-Referenz. Idempotent — kann mehrfach aufgerufen werden.
    /// Muss in Awake laufen, weil NpcBase.Start() → OnStart() vor
    /// CivilianAnimationManager.Start() aufgerufen werden kann.
    /// </summary>
    private void EnsureLayerInitialized()
    {
        if (baseLayer != null) return;
        if (animancer == null) return;

        baseLayer = animancer.Graph.Layers[0];
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region INpcAnimationHandler (called by NpcBase)
    // ════════════════════════════════════════════════════════════════════════

    public void PlayStunStart()
    {
        baseLayer.Play(stunned);
    }

    public void PlayStunEnd()
    {
        baseLayer.Play(idle);
    }

    public void UpdateMovement(float normalizedSpeed)
    {
        // Movement blending wird von den States über PlayIdle/PlayPanicRun gesteuert.
    }

    public void DisableForRagdoll()
    {
        if (animancer != null)
        {
            animancer.Stop();
            if (animancer.Animator != null)
                animancer.Animator.enabled = false;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Civilian-Specific Methods (called by CivilianNpc / CivilianStates)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set-Dressing-Animation loopen (SetDresser-Modus).
    /// </summary>
    public void PlaySetDressing()
    {
        if (setDressingClip != null && setDressingClip.Clip != null)
        {
            baseLayer.Play(setDressingClip);
        }
        else
        {
            Debug.LogWarning($"[CivilianAnimationManager] No Set Dressing clip assigned on {gameObject.name}! " +
                             "Falling back to Idle.");
            baseLayer.Play(idle);
        }
    }

    /// <summary>
    /// Ruhiges Stehen — Wartet am Fluchtpunkt.
    /// </summary>
    public void PlayIdle()
    {
        baseLayer.Play(idle);
    }

    /// <summary>
    /// Panisches Rennen — Arme hoch, chaotische Bewegung.
    /// </summary>
    public void PlayPanicRun()
    {
        baseLayer.Play(panicRun);
    }

    /// <summary>
    /// Fall-Animation (OneShot). Ruft onComplete auf wenn die Animation fertig ist.
    /// Der Caller (Fallen-State) startet dann PlayFallIdle().
    /// </summary>
    public void PlayFall(System.Action onComplete)
    {
        if (fall != null && fall.Clip != null)
        {
            AnimancerState state = baseLayer.Play(fall);
            state.Events(this).OnEnd = () => onComplete?.Invoke();
        }
        else
        {
            Debug.LogWarning($"[CivilianAnimationManager] No Fall clip assigned on {gameObject.name}! " +
                             "Skipping to FallIdle.");
            // Kein Fall-Clip → sofort FallIdle oder Idle als Fallback
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// Fall-Idle-Animation loopen (NPC liegt am Boden).
    /// </summary>
    public void PlayFallIdle()
    {
        if (fallIdle != null && fallIdle.Clip != null)
        {
            baseLayer.Play(fallIdle);
        }
        else
        {
            Debug.LogWarning($"[CivilianAnimationManager] No FallIdle clip assigned on {gameObject.name}!");
        }
    }

    /// <summary>
    /// Stunned-Animation (z.B. nach Sword-Embed).
    /// </summary>
    public void PlayStunnedFromPanic()
    {
        baseLayer.Play(stunned);
    }

    #endregion
}
