using System.Collections.Generic;
using UnityEngine;

public class scrLocalGameManager : MonoBehaviour
{
    public static scrLocalGameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist across scenes
        }
        else
        {
            Destroy(gameObject); // Destroy duplicate instances
        }
    }

    public FPSPlayerController AsmPlayer;
    public float TimeDialation = 1;
    
    public List<NPCEnemyController> NpcBaseSoldiers = new List<NPCEnemyController>();
    public List<scrNpc_GenOne> NpcGenOnes = new List<scrNpc_GenOne>();
    public List<scrPlayerProjectile>  PlayerProjectiles = new List<scrPlayerProjectile>();
    
    public float meleeRange = 1.5f; // Distance for melee interaction
    public float attackTimeWindow = 0.3f; // Seconds for "simultaneous" attack detection
    public LayerMask entityMask; // Layer for player/enemies (set in Inspector)
    public Collider[] hitBuffer = new Collider[10];
}