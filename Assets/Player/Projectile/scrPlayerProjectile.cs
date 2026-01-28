using System;
using System.Collections;
using UnityEngine;

public class scrPlayerProjectile : MonoBehaviour
{
    // public float speed;
    // private FPSPlayerController asmPlayer;
    //
    // private float life = 3f;
    // private float currentTime;
    //
    // private Vector3 lastPosition; // Store the previous frame's position
    //
    // private void Awake()
    // {
    //     asmPlayer = scrLocalGameManager.Instance.AsmPlayer;
    //     scrLocalGameManager.Instance.PlayerProjectiles.Add(this);
    // }
    //
    // void Start()
    // {
    //     lastPosition = transform.position; // Initialize last position
    // }
    //
    // private void Update()
    // {
    //     float speedProcessor = speed * Time.deltaTime;
    //     transform.Translate(Vector3.forward * speedProcessor);
    //
    //     Vector3 currentPosition = transform.position;
    //     Vector3 movementThisFrame = currentPosition - lastPosition;
    //     float distanceMoved = movementThisFrame.magnitude * 1;
    //
    //
    //     Ray ray = new Ray(currentPosition, movementThisFrame.normalized);
    //     RaycastHit hit;
    //
    //
    //     Debug.DrawRay(currentPosition, movementThisFrame.normalized * distanceMoved, Color.red, Time.deltaTime);
    //
    //
    //     if (Physics.Raycast(ray, out hit, distanceMoved))
    //     {
    //         if (hit.collider.CompareTag("Solid")) 
    //         {
    //             StartCoroutine(KillTime());
    //             return;
    //         }
    //         
    //         if (hit.collider.CompareTag("Enemy")) 
    //         {
    //             Debug.Log(hit.collider.name + " has been hit by the player");
    //             Destroy(gameObject); 
    //             return;
    //         }
    //         
    //     }
    //     
    //
    //     lastPosition = currentPosition;
    //     
    //     currentTime += Time.deltaTime;
    //     if (currentTime >= life * scrLocalGameManager.Instance.TimeDialation)
    //     {
    //         Destroy(gameObject);
    //     }
    // }
    //
    // IEnumerator KillTime()
    // {
    //     speed = 0;
    //     yield return new WaitForSeconds(1f);
    //     Destroy(gameObject);
    // }
}
