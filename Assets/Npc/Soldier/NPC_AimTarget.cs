using UnityEngine;

public class NPC_AimTarget : MonoBehaviour
{
    
    public Transform target;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(target == null) Debug.LogError("Missing bone constrain target !!!");
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = target.position;
    }
}
