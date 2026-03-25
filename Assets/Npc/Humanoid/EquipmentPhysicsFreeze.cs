using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// EQUIPMENT PHYSICS FREEZE - Friert Equipment-Rigidbodies nach einem Delay ein
// ════════════════════════════════════════════════════════════════════════════
//
// Einfache Hilfskomponente die nach einer einstellbaren Zeit
// alle Rigidbodies am GameObject auf isKinematic = true setzt.
//
// Wird vom NpcRagdollSwapper auf gedroptes Equipment gelegt,
// damit Waffen etc. nach dem Runterfallen nicht weiter simuliert werden.
//
// ════════════════════════════════════════════════════════════════════════════

public class EquipmentPhysicsFreeze : MonoBehaviour
{
    private float freezeDelay;
    private float freezeTimer;

    /// <summary>
    /// Startet den Freeze-Timer.
    /// </summary>
    /// <param name="delay">Sekunden bis zum Einfrieren.</param>
    public void Initialize(float delay)
    {
        freezeDelay = delay;
        freezeTimer = 0f;
    }

    private void Update()
    {
        freezeTimer += Time.deltaTime;

        if (freezeTimer >= freezeDelay)
        {
            Freeze();
        }
    }

    private void Freeze()
    {
        // Alle Rigidbodies einfrieren (meistens nur einer beim Equipment)
        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rigidbodies)
        {
            if (rb == null) continue;
            rb.isKinematic = true;
        }

        // Nicht mehr gebraucht — deaktivieren spart Update-Overhead
        enabled = false;
    }
}
