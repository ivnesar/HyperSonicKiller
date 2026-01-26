using UnityEngine;

public class ExplosionSphere : MonoBehaviour
{
    public float maxRadius = 10f; // Maximum radius the sphere expands to
    public float expandDuration = 1f; // Time in seconds to fully expand

    private SphereCollider sphereCollider;
    private float currentRadius = 0f;
    private float timer = 0f;

    void Start()
    {
        // Add or get the SphereCollider and set it as a trigger
        sphereCollider = gameObject.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = gameObject.AddComponent<SphereCollider>();
        }
        sphereCollider.isTrigger = true;
        sphereCollider.radius = currentRadius;

        // Optional: If you want a visual sphere, add a mesh here (e.g., create a sphere primitive in the prefab)
        // For now, assuming the prefab might have a Sphere mesh with initial scale matching radius
    }

    void Update()
    {
        timer += Time.deltaTime / scrLocalGameManager.Instance.TimeDialation;
        float t = Mathf.Clamp01(timer / expandDuration);
        currentRadius = Mathf.Lerp(0f, maxRadius, t);
        sphereCollider.radius = currentRadius;

        // Optional: If there's a visual mesh, scale it uniformly (assuming initial scale is Vector3.one and radius matches)
        transform.localScale = Vector3.one * (currentRadius * 2f); // Multiply by 2 if default sphere mesh has radius 0.5

        if (timer >= expandDuration)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Apply effect to player (e.g., damage, knockback, etc.)
            // Replace with your player health/damage system
            Debug.Log("Player affected by explosion!");
            // Example: other.GetComponent<PlayerHealth>().TakeDamage(50f);
        }
    }
}