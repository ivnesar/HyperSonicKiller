using System;
using UnityEngine;

public class HomingMissile : MonoBehaviour
{
    public float speed = 10f; // Forward speed of the missile
    public float turnSpeed = 5f; // Controls how sharply the missile turns towards the target. Lower values make turns slower and less accurate, allowing faster players to dodge.

    private Rigidbody rb;

    private FPSPlayerController asmPlayer;
    private void Awake()
    {
        asmPlayer = scrLocalGameManager.Instance.AsmPlayer;
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        Destroy(gameObject, 5);
    }

    void FixedUpdate()
    {
        // Calculate direction to target
        Vector3 direction = asmPlayer.playerCamera.transform.position - transform.position;
        direction.Normalize();

        // Create target rotation
        Quaternion targetRotation = Quaternion.LookRotation(direction);

        // Gradually rotate towards the target
        float turnVel = (turnSpeed * Time.fixedDeltaTime) / scrLocalGameManager.Instance.TimeDialation;
        rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRotation, turnVel));

        // Move forward
        rb.linearVelocity = (transform.forward * speed) / scrLocalGameManager.Instance.TimeDialation;
    }
}