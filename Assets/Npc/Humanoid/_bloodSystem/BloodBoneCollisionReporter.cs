using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// BLOOD BONE COLLISION REPORTER - Meldet Bone-Kollisionen
// ════════════════════════════════════════════════════════════════════════════
//
// Kleine Helper-Komponente die per Code auf jeden überwachten Bone
// gelegt wird. Leitet Kollisionen an RagdollBloodDecals weiter.
//
// Wird automatisch von RagdollBloodDecals.Initialize() erzeugt —
// NICHT manuell zuweisen!
//
// WARUM EINE EIGENE KOMPONENTE?
//   Unity sendet OnCollisionStay nur an das GameObject das den
//   Collider/Rigidbody hat. Da jeder Ragdoll-Bone sein eigenes
//   Rigidbody hat, muss der Collision-Callback direkt auf dem
//   Bone-GameObject sitzen. Diese Komponente macht genau das
//   und leitet die Info an das zentrale RagdollBloodDecals weiter.
//
// ════════════════════════════════════════════════════════════════════════════

public class BloodBoneCollisionReporter : MonoBehaviour
{
    private RagdollBloodDecals owner;
    private int boneIndex;
    private bool isInitialized;

    /// <summary>
    /// Wird von RagdollBloodDecals aufgerufen.
    /// </summary>
    public void Initialize(RagdollBloodDecals owner, int boneIndex)
    {
        this.owner = owner;
        this.boneIndex = boneIndex;
        isInitialized = true;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!isInitialized) return;
        if (owner == null) return;

        owner.OnBoneCollision(boneIndex, collision);
    }
}
