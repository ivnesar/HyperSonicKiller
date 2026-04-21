using UnityEngine;

/// <summary>
/// TEMPORÄRES DEBUG-SCRIPT
/// Zieh das auf den Player, um zu sehen was beim Dash mit der Position passiert.
/// Nach dem Bugfix entfernen.
/// </summary>
public class DashPositionDebug : MonoBehaviour
{
    private PlayerCore core;
    private Vector3 lastPos;
    private PlayerCore.PlayerState lastState;

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        lastPos = transform.position;
        lastState = core.CurrentState;
    }

    private void Update()
    {
        // State-Wechsel loggen
        if (core.CurrentState != lastState)
        {
            Debug.Log($"[DashDebug] State-Wechsel: {lastState} → {core.CurrentState}  @ Pos {transform.position}");
            lastState = core.CurrentState;
        }

        // Jeden Frame die zurückgelegte Distanz loggen (nur während Dash)
        if (core.CurrentState == PlayerCore.PlayerState.Dashing ||
            core.CurrentState == PlayerCore.PlayerState.SprintDashing ||
            core.CurrentState == PlayerCore.PlayerState.DashingToSword)
        {
            float frameDistance = Vector3.Distance(transform.position, lastPos);
        }

        lastPos = transform.position;
    }
}
