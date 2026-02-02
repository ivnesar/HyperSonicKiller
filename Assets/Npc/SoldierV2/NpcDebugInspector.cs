using UnityEngine;

/// <summary>
/// Debug-Komponente die wichtige NPC-Werte im Inspector anzeigt.
/// Einfach zum NPC GameObject hinzufügen - funktioniert mit Soldier und Defender.
/// 
/// HINWEIS: Alle Felder sind readonly und werden nur zur Laufzeit aktualisiert.
/// </summary>
public class NpcDebugInspector : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region References (Auto-assigned)
    // ════════════════════════════════════════════════════════════════════════

    private NpcBase npcBase;
    private SoldierNpc soldierNpc;
    private DefenderNpc defenderNpc;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Display - General
    // ════════════════════════════════════════════════════════════════════════

    [Header("═══ GENERAL ═══")]
    [SerializeField] private NpcType npcType;
    [SerializeField] private string currentState = "—";
    [SerializeField] private int stateID;
    [SerializeField] private BehaviorMode behaviorMode;

    [Header("═══ HEALTH ═══")]
    [SerializeField] private int currentHealth;
    [SerializeField] private int maxHealth;
    [SerializeField] private bool isDead;

    [Header("═══ STUN ═══")]
    [SerializeField] private bool isStunned;
    [SerializeField] private float remainingStunTime;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Display - Awareness
    // ════════════════════════════════════════════════════════════════════════

    [Header("═══ AWARENESS ═══")]
    [Tooltip("Tracking FOV - kann aktiv anvisieren")]
    [SerializeField] private bool canSeePlayer;
    
    [Tooltip("Detection FOV - spürt dass Spieler da ist")]
    [SerializeField] private bool canDetectPlayer;
    
    [Tooltip("Reaktionsverzögerung abgelaufen")]
    [SerializeField] private bool canReactToPlayerLoss;

    [Tooltip("Kann letzte Position sehen aber Spieler nicht dort")]
    [SerializeField] private bool hasLostPlayer;
    
    [SerializeField] private Vector3 lastKnownPlayerPosition;
    [SerializeField] private float distanceToPlayer;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Display - Pathfinding
    // ════════════════════════════════════════════════════════════════════════

    [Header("═══ PATHFINDING ═══")]
    [SerializeField] private bool hasValidPathToPlayer;
    [SerializeField] private bool hasReachedDestination;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Display - Soldier Specific
    // ════════════════════════════════════════════════════════════════════════

    [Header("═══ SOLDIER SPECIFIC ═══")]
    [SerializeField] private int shotsFiredInSalvo;
    [SerializeField] private int shotsPerSalvo;
    [SerializeField] private bool isInShootingRange;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug Display - Defender Specific
    // ════════════════════════════════════════════════════════════════════════

    [Header("═══ DEFENDER SPECIFIC ═══")]
    [SerializeField] private float approachDistance;
    [SerializeField] private float reengageDistance;
    [SerializeField] private bool isInApproachRange;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Auto-find NPC components
        npcBase = GetComponent<NpcBase>();
        soldierNpc = GetComponent<SoldierNpc>();
        defenderNpc = GetComponent<DefenderNpc>();

        if (npcBase == null)
        {
            Debug.LogError($"[NpcDebugInspector] No NpcBase found on {gameObject.name}!");
            enabled = false;
        }
    }

    private void Update()
    {
        if (npcBase == null) return;

        UpdateGeneralInfo();
        UpdateAwarenessInfo();
        UpdatePathfindingInfo();

        if (soldierNpc != null)
            UpdateSoldierInfo();

        if (defenderNpc != null)
            UpdateDefenderInfo();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Update Methods
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateGeneralInfo()
    {
        npcType = npcBase.GetNpcType();
        currentState = npcBase.GetCurrentStateName();
        stateID = npcBase.GetStateID();
        behaviorMode = npcBase.CurrentBehaviorMode;

        currentHealth = npcBase.CurrentHealth;
        maxHealth = npcBase.MaxHealth;
        isDead = npcBase.IsDead;

        isStunned = npcBase.IsStunned;
        remainingStunTime = npcBase.RemainingStunTime;
    }

    private void UpdateAwarenessInfo()
    {
        canSeePlayer = npcBase.CanSeePlayer;
        canDetectPlayer = npcBase.CanDetectPlayer;
        canReactToPlayerLoss = npcBase.CanReactToPlayerLoss;
        hasLostPlayer = npcBase.HasLostPlayer;
        lastKnownPlayerPosition = npcBase.LastKnownPlayerPosition;

        // Distance calculation
        if (npcBase.Transform != null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                distanceToPlayer = Vector3.Distance(npcBase.Transform.position, player.transform.position);
            }
        }
    }

    private void UpdatePathfindingInfo()
    {
        hasValidPathToPlayer = npcBase.HasValidPathToPlayer;
        
        // HasReachedDestination über Reflection oder public machen
        // Für jetzt: NavMeshAgent direkt prüfen
        var navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null && navAgent.enabled)
        {
            hasReachedDestination = !navAgent.pathPending && 
                                    navAgent.remainingDistance <= navAgent.stoppingDistance;
        }
    }

    private void UpdateSoldierInfo()
    {
        shotsFiredInSalvo = soldierNpc.ShotsFiredInSalvo;
        shotsPerSalvo = soldierNpc.ShotsPerSalvo;

        // Check if in shooting range
        isInShootingRange = distanceToPlayer >= soldierNpc.MinShootingRange &&
                           distanceToPlayer <= soldierNpc.MaxShootingRange;
    }

    private void UpdateDefenderInfo()
    {
        approachDistance = defenderNpc.ApproachDistance;
        reengageDistance = defenderNpc.ReengageDistance;
        isInApproachRange = distanceToPlayer <= defenderNpc.ApproachDistance;
    }

    #endregion
}
