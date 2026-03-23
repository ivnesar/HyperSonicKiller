using UnityEngine;

/// <summary>
/// Defender NPC - Läuft mit Schild auf den Spieler zu.
///
/// ANIMANCER MIGRATION:
///   - NpcAnimator Property entfernt → AnimManager (DefenderAnimationManager) stattdessen.
///   - States rufen typsichere Methoden auf AnimManager auf (z.B. AnimManager.PlayWalk()).
///   - Kein direkter Animator-Zugriff mehr in States.
///
/// RAGDOLL MIGRATION:
///   - NpcRagdollController entfernt → NpcImpactTracker + NpcRagdollSwapper stattdessen.
///   - Ragdoll-Physik läuft nur noch auf gespawnten Prefabs.
///
/// KEIN AIM-IK:
///   Der Defender zielt nicht auf den Spieler — er läuft nur mit dem Schild auf ihn zu.
///   Die Body-Rotation zum Spieler wird über RotateTowardTarget() in den States gesteuert.
///   AimIK und AimController werden NICHT benötigt (nicht auf dem Prefab).
/// </summary>
public class DefenderNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Approach")]
    [Tooltip("Stoppt bei dieser Distanz zum Spieler")]
    [SerializeField] private float approachDistance = 1.5f;

    [Tooltip("Fängt wieder an zu laufen ab dieser Distanz")]
    [SerializeField] private float reengageDistance = 3f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors
    // ════════════════════════════════════════════════════════════════════════

    public float ApproachDistance => approachDistance;
    public float ReengageDistance => reengageDistance;

    /// <summary>
    /// Typed animation manager reference for DefenderStates.
    /// </summary>
    public DefenderAnimationManager AnimManager { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<DefenderNpc> currentState;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();

        // Typsichere Referenz auf den Animation Manager
        AnimManager = GetComponentInChildren<DefenderAnimationManager>();
        if (AnimManager == null)
        {
            Debug.LogWarning($"[DefenderNpc] No DefenderAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }
    }

    protected override void OnStart()
    {
        ChangeState(new DefenderStates.Idle());
    }

    protected override void UpdateBehavior()
    {
        if (currentState == null) return;

        var nextState = currentState.Update(this);
        if (nextState != null)
            ChangeState(nextState);
    }

    protected override void OnStunStart() => ChangeState(new DefenderStates.Stunned());

    protected override void OnStunEnd()
    {
        if (CurrentBehaviorMode == BehaviorMode.Pursuing)
            ChangeState(new DefenderStates.Approaching());
        else
            ChangeState(new DefenderStates.Idle());
    }

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Defender;
    public override int GetStateID() => currentState?.StateID ?? 0;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<DefenderNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers
    // ════════════════════════════════════════════════════════════════════════

    public bool IsCloseEnough() => DistanceToTarget <= approachDistance;
    public bool IsTooFar() => DistanceToTarget > reengageDistance;

    public new void MoveTowardTarget(float speed = 1f) => base.MoveTowardTarget(speed);
    public new void StopMovement() => base.StopMovement();
    public new void RotateTowardTarget() => base.RotateTowardTarget();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, approachDistance);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, reengageDistance);
    }

    #endregion
}
