using System;
using UnityEngine;

public class scrNpcBullet : MonoBehaviour
{
    private float speed = 30;
    private float life = 3f;
    private float currentTime;
    private Vector3 lastPosition; // Store the previous frame's position

    private FPSPlayerController asmPlayer;

    private void Awake()
    {
        asmPlayer = scrLocalGameManager.Instance.AsmPlayer;
    }

    void Start()
    {
        lastPosition = transform.position; // Initialize last position
    }

    void Update()
    {
        float speedProcessor = speed * Time.deltaTime;
        transform.Translate(Vector3.forward * speedProcessor);

        Vector3 currentPosition = transform.position;
        Vector3 movementThisFrame = currentPosition - lastPosition;
        float distanceMoved = movementThisFrame.magnitude * 3;


        Ray ray = new Ray(currentPosition, -movementThisFrame.normalized);
        RaycastHit hit;


        Debug.DrawRay(currentPosition, -movementThisFrame.normalized * distanceMoved * 3, Color.green, Time.deltaTime);


        if (Physics.Raycast(ray, out hit, distanceMoved))
        {
            if (hit.collider.CompareTag("Wall")) 
            {
                Destroy(gameObject); 
                return;
            }
            
            if (hit.collider.CompareTag("Player")) 
            {
                if (scrLocalGameManager.Instance.TimeDialation == 1)
                {
                    //asmPlayer.TriggerCameraShake();
                }
                else
                {
                    Debug.Log("bullet deflect");
                    //asmPlayer.Deflection();
                    //asmPlayer.TriggerCameraShake();
                }
                
                Debug.Log(hit.collider.name + " has been hit by the player");
                Destroy(gameObject); 
                return;
            }
            
        }
        

        lastPosition = currentPosition;
        
        currentTime += Time.deltaTime;
        if (currentTime >= life * scrLocalGameManager.Instance.TimeDialation)
        {
            Destroy(gameObject);
        }
    }
}