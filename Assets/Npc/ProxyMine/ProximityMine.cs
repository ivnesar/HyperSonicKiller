using UnityEngine;

public class ProximityMine : MonoBehaviour
{
    public float detectionRange = 5f;
    public GameObject explosionSpherePrefab;
    public float detonationDelay = 1f;

    private Transform playerTransform;
    private bool isTriggered = false;
    private float triggerTimer = 0f;

    // UPDATED: Using PlayerCore instead of FPSPlayerController
    private PlayerCore playerCore;

    private void Awake()
    {
        // Fallback: find PlayerCore directly
        if (playerCore == null)
        {
            playerCore = FindFirstObjectByType<PlayerCore>();
        }
    }

    void Start()
    {
        if (playerCore != null)
        {
            playerTransform = playerCore.transform;
        }
        else
        {
            // Fallback: try to find by tag
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
                playerCore = player.GetComponent<PlayerCore>();
            }
        }
    }

    void Update()
    {
        if (playerTransform == null) return;

        if (!isTriggered)
        {
            Vector3 directionToPlayer = playerTransform.position - transform.position;
            float distanceToPlayer = directionToPlayer.magnitude;

            if (distanceToPlayer <= detectionRange)
            {
                // Perform raycast to check for line of sight
                Ray ray = new Ray(transform.position, directionToPlayer.normalized);
                RaycastHit hit;

                if (!Physics.Raycast(ray, out hit, distanceToPlayer) || hit.transform == playerTransform)
                {
                    isTriggered = true;
                    triggerTimer = 0f;
                }
            }
        }
        else
        {
            // Increment timer and check if delay has passed
            float timeScale = (scrLocalGameManager.Instance != null) ? scrLocalGameManager.Instance.TimeDialation : 1f;
            triggerTimer += Time.deltaTime / timeScale;
            
            if (triggerTimer >= detonationDelay)
            {
                TriggerExplosion();
                Destroy(gameObject);
            }
        }
    }

    void TriggerExplosion()
    {
        if (explosionSpherePrefab != null)
        {
            Instantiate(explosionSpherePrefab, transform.position, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning("Explosion sphere prefab not assigned.");
        }
    }
    
}