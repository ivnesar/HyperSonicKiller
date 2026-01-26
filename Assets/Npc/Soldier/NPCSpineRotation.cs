using UnityEngine;

public class NPCSpineRotation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform spineBone;
    [SerializeField] private Transform targetTransform; // The player or aim target
    
    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 5f;
    [SerializeField] private float maxVerticalAngle = 60f; // Limit how far up/down the spine can rotate
    [SerializeField] private bool onlyRotateWhenAttacking = true;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLines = true;
    
    private NPCEnemyController npcController;
    private Quaternion initialSpineRotation;
    
    void Start()
    {
        if (spineBone == null)
        {
            Debug.LogError($"{gameObject.name}: Spine bone not assigned!");
            enabled = false;
            return;
        }
        
        // Try to get the player automatically if not assigned
        if (targetTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                targetTransform = player.transform;
            }
        }
        
        npcController = GetComponent<NPCEnemyController>();
        
        // Store the initial local rotation of the spine
        initialSpineRotation = spineBone.localRotation;
    }
    
    void LateUpdate()
    {
        if (spineBone == null || targetTransform == null) return;
        
        // Check if we should only rotate during specific states
        if (onlyRotateWhenAttacking && npcController != null)
        {
            bool shouldRotate = npcController.CurrentState == NPCEnemyController.NPCState.Attacking ||
                               npcController.CurrentState == NPCEnemyController.NPCState.Reloading;
            
            if (!shouldRotate)
            {
                // Smoothly return to initial rotation when not attacking
                spineBone.localRotation = Quaternion.Slerp(
                    spineBone.localRotation, 
                    initialSpineRotation, 
                    Time.deltaTime * rotationSpeed
                );
                return;
            }
        }
        
        // Calculate the height difference
        float heightDifference = targetTransform.position.y - spineBone.position.y;
        
        // Calculate horizontal distance (on XZ plane)
        Vector3 targetPos = targetTransform.position;
        Vector3 spinePos = spineBone.position;
        float horizontalDistance = Vector3.Distance(
            new Vector3(targetPos.x, 0, targetPos.z),
            new Vector3(spinePos.x, 0, spinePos.z)
        );
        
        // Calculate the angle needed to aim at the target
        float targetAngle = Mathf.Atan2(heightDifference, horizontalDistance) * Mathf.Rad2Deg;
        
        // Clamp the angle to prevent unnatural rotations
        targetAngle = Mathf.Clamp(targetAngle, -maxVerticalAngle, maxVerticalAngle);
        
        // Create the target rotation (rotate around X-axis for pitch)
        Quaternion targetRotation = initialSpineRotation * Quaternion.Euler(targetAngle, 0, 0);
        
        // Smoothly interpolate to the target rotation
        spineBone.localRotation = Quaternion.Slerp(
            spineBone.localRotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );
        
        // Debug visualization
        if (showDebugLines && Application.isPlaying)
        {
            Debug.DrawLine(spineBone.position, targetTransform.position, Color.cyan);
        }
    }
    
    // Call this if you want to reset the spine rotation manually
    public void ResetSpineRotation()
    {
        if (spineBone != null)
        {
            spineBone.localRotation = initialSpineRotation;
        }
    }
    
    // Adjust rotation speed at runtime if needed
    public void SetRotationSpeed(float speed)
    {
        rotationSpeed = speed;
    }
    
    void OnDrawGizmosSelected()
    {
        if (spineBone == null || targetTransform == null || !Application.isPlaying) return;
        
        // Draw a line showing the aim direction
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(spineBone.position, targetTransform.position);
        
        // Draw a sphere at the spine position
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(spineBone.position, 0.1f);
    }
}