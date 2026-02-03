using UnityEngine;

/// <summary>
/// Debug-Komponente für NPC-Werte im Inspector.
/// </summary>
public class NpcDebugInspector : MonoBehaviour
{
    private NpcBase npc;

    [Header("General")]
    [SerializeField] private string currentState;
    [SerializeField] private BehaviorMode behaviorMode;

    [Header("Health")]
    [SerializeField] private int currentHealth;
    [SerializeField] private bool isDead;
    [SerializeField] private bool isStunned;

    [Header("Target")]
    [SerializeField] private float distanceToTarget;
    [SerializeField] private bool canReachPlayer;

    private void Awake()
    {
        npc = GetComponent<NpcBase>();
        if (npc == null) enabled = false;
    }

    private void Update()
    {
        if (npc == null) return;

        currentState = npc.GetCurrentStateName();
        behaviorMode = npc.CurrentBehaviorMode;
        currentHealth = npc.CurrentHealth;
        isDead = npc.IsDead;
        isStunned = npc.IsStunned;
        distanceToTarget = npc.DistanceToTarget;
        canReachPlayer = npc.CanReachPlayer;
    }
}
