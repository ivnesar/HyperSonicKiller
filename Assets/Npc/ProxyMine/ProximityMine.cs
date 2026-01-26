using UnityEngine;

public class ProximityMine : MonoBehaviour
{
    public float detectionRange = 5f; // Adjust this value as needed for the proximity distance
    public GameObject explosionSpherePrefab; // Assign the explosion sphere prefab in the Inspector
    public float detonationDelay = 1f; // Detonation delay in seconds

    private Transform playerTransform;
    private bool isTriggered = false;
    private float triggerTimer = 0f;

    private FPSPlayerController asmPlayer;
    private void Awake()
    {
        asmPlayer = scrLocalGameManager.Instance.AsmPlayer;
    }
    
    void Start()
    {
        playerTransform = asmPlayer.transform;
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
                // Perform raycast to check for line of sight (no walls or obstacles blocking)
                Ray ray = new Ray(transform.position, directionToPlayer.normalized);
                RaycastHit hit;

                // If no obstacle hit within the distance or the hit is directly on the player, trigger explosion
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
            triggerTimer += Time.deltaTime / scrLocalGameManager.Instance.TimeDialation;
            if (triggerTimer >= detonationDelay)
            {
                TriggerExplosion();
                Destroy(gameObject); // Destroy the mine after triggering
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