// ===== SIMPLIFIED DEFENDER BEHAVIOR =====
using UnityEngine;

public class NPCDefenderBehavior : MonoBehaviour, INPCBehavior
{
    [Header("Defender Settings")]
    [SerializeField] private float coverDistance = 1.5f;
    [SerializeField] private float updateInterval = 0.3f;

    private NPCEnemyController controller;
    private scrLocalGameManager lgm;
    private float lastUpdateTime;

    void Awake()
    {
        controller = GetComponent<NPCEnemyController>();
        lgm = scrLocalGameManager.Instance;
    }

    void Update()
    {
        // Position to cover allies
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            lastUpdateTime = Time.time;
            PositionToDefend();
        }
    }

    public bool CanAttack(float distanceToPlayer, bool hasLOS)
    {
        // Defenders don't attack
        return false;
    }

    public void OnStartAttack() { }
    public void UpdateAttack() { }
    public void OnStopAttack() { }

    // Defenders don't reload
    public bool ShouldReload() => false;
    public void OnStartReload() { }
    public void UpdateReload() { }
    public void OnStopReload() { }
    public bool IsReloadComplete() => true;

    private void PositionToDefend()
    {
        if (lgm == null || controller.Player == null) return;

        // Find center point of all non-defender NPCs
        Vector3 allyCenter = Vector3.zero;
        int allyCount = 0;

        foreach (var npc in lgm.NpcBaseSoldiers)
        {
            if (npc == controller || npc == null) continue;
            if (npc.GetComponent<NPCDefenderBehavior>() != null) continue;

            allyCenter += npc.transform.position;
            allyCount++;
        }

        if (allyCount == 0) return;

        allyCenter /= allyCount;

        // Position between allies and player
        Vector3 directionToPlayer = (controller.Player.transform.position - allyCenter).normalized;
        Vector3 defendPosition = allyCenter + directionToPlayer * coverDistance;

        controller.Agent.SetDestination(defendPosition);
        controller.RotateTowardsTarget(controller.Player.transform.position);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 1f);
    }
}