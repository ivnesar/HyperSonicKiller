using UnityEngine;

/// <summary>
/// Defender NPC - Schild-Träger der auf den Spieler zuläuft.
/// Kein eigener Angriff - das Schild (DefenderShield.cs) handhabt Kollisionen.
/// 
/// Verhaltensweisen:
/// - Stationary: Bleibt an Position, dreht sich zum Spieler
/// - Pursuing: Läuft auf den Spieler zu (stoppt bei approachDistance)
/// 
/// Awareness-System:
/// - Reagiert verzögert wenn Spieler aus Sicht verschwindet
/// - Dreht sich langsamer wenn Spieler nicht sichtbar
/// </summary>
public class DefenderNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Approach Behavior")]
    [Tooltip("Wie nah der Defender an den Spieler heran geht")]
    [SerializeField] private float approachDistance = 1.5f;

    [Tooltip("Ab welcher Distanz der Defender wieder anfängt zu laufen")]
    [SerializeField] private float reengageDistance = 3f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors (für States)
    // ════════════════════════════════════════════════════════════════════════

    public float ApproachDistance => approachDistance;
    public float ReengageDistance => reengageDistance;
    public Transform PlayerTransform => playerTransform;
    public new bool CanSeePlayer => canSeePlayer;
    public new bool CanDetectPlayer => canDetectPlayer;
    public new bool CanReactToPlayerLoss => base.CanReactToPlayerLoss;
    public new bool HasValidPathToPlayer => hasValidPathToPlayer;
    public new bool HasLostPlayer => base.HasLostPlayer;
    public new Vector3 LastKnownPlayerPosition => lastKnownPlayerPosition;
    public Animator NpcAnimator => animator;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<DefenderNpc> currentState;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

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

        if (showDebugInfo)
            Debug.Log($"[{gameObject.name}] State → {newState?.StateName}");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Movement Helpers (Public für States)
    // ════════════════════════════════════════════════════════════════════════

    public new void MoveToward(Vector3 position, float speedMultiplier = 1f) =>
        base.MoveToward(position, speedMultiplier);

    public new void StopMovement() => base.StopMovement();

    public new void RotateToward(Vector3 position, float speedMultiplier = 1f) =>
        base.RotateToward(position, speedMultiplier);

    public new void RotateTowardLastKnownPosition(float speedMultiplier = 1f) =>
        base.RotateTowardLastKnownPosition(speedMultiplier);

    public new float GetDistanceToPlayer() => base.GetDistanceToPlayer();

    public new Vector3 GetDirectionToPlayer() => base.GetDirectionToPlayer();

    public new bool HasReachedDestination() => base.HasReachedDestination();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // Approach Distance (wo der Defender stehen bleibt)
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, approachDistance);

        // Reengage Distance (ab wann er wieder losläuft)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, reengageDistance);
    }

    #endregion
}
