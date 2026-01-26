using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton manager that tracks all active NPCs.
/// Provides utility methods for finding NPCs (e.g., nearest soldier for defenders).
/// </summary>
public class NpcManager : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Singleton
    // ────────────────────────────────────────────────────────────────────────────────

    private static NpcManager instance;
    public static NpcManager Instance
    {
        get
        {
            if (instance == null)
            {
                // Try to find existing instance
                instance = FindFirstObjectByType<NpcManager>();

                // Create one if none exists
                if (instance == null)
                {
                    GameObject go = new GameObject("NpcManager");
                    instance = go.AddComponent<NpcManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────────────

    private List<NpcBase> allNpcs = new List<NpcBase>();
    private List<NpcBase> soldiers = new List<NpcBase>();
    private List<NpcBase> defenders = new List<NpcBase>();

    // Cache for player reference
    private Transform playerTransform;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Start()
    {
        // Cache player reference
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Registration
    // ────────────────────────────────────────────────────────────────────────────────

    public void RegisterNpc(NpcBase npc)
    {
        if (npc == null || allNpcs.Contains(npc)) return;

        allNpcs.Add(npc);

        switch (npc.GetNpcType())
        {
            case NpcType.Soldier:
                soldiers.Add(npc);
                break;
            case NpcType.Defender:
                defenders.Add(npc);
                break;
        }

        Debug.Log($"[NpcManager] Registered {npc.GetNpcType()}: {npc.gameObject.name}. Total NPCs: {allNpcs.Count}");
    }

    public void UnregisterNpc(NpcBase npc)
    {
        if (npc == null) return;

        allNpcs.Remove(npc);

        switch (npc.GetNpcType())
        {
            case NpcType.Soldier:
                soldiers.Remove(npc);
                break;
            case NpcType.Defender:
                defenders.Remove(npc);
                break;
        }

        Debug.Log($"[NpcManager] Unregistered {npc.GetNpcType()}: {npc.gameObject.name}. Total NPCs: {allNpcs.Count}");
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Query Methods
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the soldier closest to the player that is still alive.
    /// Used by Defenders to find who to protect.
    /// </summary>
    public NpcBase GetSoldierClosestToPlayer()
    {
        if (playerTransform == null || soldiers.Count == 0) return null;

        NpcBase closest = null;
        float closestDistance = float.MaxValue;

        foreach (var soldier in soldiers)
        {
            if (soldier == null || soldier.IsDead) continue;

            float dist = Vector3.Distance(soldier.transform.position, playerTransform.position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closest = soldier;
            }
        }

        return closest;
    }

    /// <summary>
    /// Returns the soldier closest to a given position that is still alive.
    /// </summary>
    public NpcBase GetSoldierClosestTo(Vector3 position)
    {
        if (soldiers.Count == 0) return null;

        NpcBase closest = null;
        float closestDistance = float.MaxValue;

        foreach (var soldier in soldiers)
        {
            if (soldier == null || soldier.IsDead) continue;

            float dist = Vector3.Distance(soldier.transform.position, position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closest = soldier;
            }
        }

        return closest;
    }

    /// <summary>
    /// Returns all living soldiers.
    /// </summary>
    public List<NpcBase> GetAllLivingSoldiers()
    {
        List<NpcBase> result = new List<NpcBase>();

        foreach (var soldier in soldiers)
        {
            if (soldier != null && !soldier.IsDead)
            {
                result.Add(soldier);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all living defenders.
    /// </summary>
    public List<NpcBase> GetAllLivingDefenders()
    {
        List<NpcBase> result = new List<NpcBase>();

        foreach (var defender in defenders)
        {
            if (defender != null && !defender.IsDead)
            {
                result.Add(defender);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all living NPCs of any type.
    /// </summary>
    public List<NpcBase> GetAllLivingNpcs()
    {
        List<NpcBase> result = new List<NpcBase>();

        foreach (var npc in allNpcs)
        {
            if (npc != null && !npc.IsDead)
            {
                result.Add(npc);
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if there are any soldiers left for defenders to protect.
    /// </summary>
    public bool HasLivingSoldiers()
    {
        foreach (var soldier in soldiers)
        {
            if (soldier != null && !soldier.IsDead) return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the position between the player and a target (for defender positioning).
    /// </summary>
    public Vector3 GetInterceptPosition(Vector3 targetPosition, float offsetFromTarget = 2f)
    {
        if (playerTransform == null) return targetPosition;

        Vector3 directionToPlayer = (playerTransform.position - targetPosition).normalized;
        return targetPosition + directionToPlayer * offsetFromTarget;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Cleanup
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes any null references from the lists (cleanup after destroyed objects).
    /// Call periodically or after waves/scene transitions.
    /// </summary>
    public void CleanupNullReferences()
    {
        allNpcs.RemoveAll(npc => npc == null);
        soldiers.RemoveAll(npc => npc == null);
        defenders.RemoveAll(npc => npc == null);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Debug
    // ────────────────────────────────────────────────────────────────────────────────

    public int TotalNpcCount => allNpcs.Count;
    public int SoldierCount => soldiers.Count;
    public int DefenderCount => defenders.Count;

    #endregion
}
