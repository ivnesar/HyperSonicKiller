using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// NPC IMPACT TRACKER - Sammelt Impact-Kräfte während des Kampfes
// ════════════════════════════════════════════════════════════════════════════
//
// ERSETZT: NpcRagdollController auf KI-NPCs die den RagdollSwapper nutzen.
//
// WARUM:
//   Der alte NpcRagdollController hatte zwei Aufgaben:
//     1. Impact-Kräfte registrieren (Melee, Bullet, Sword, Explosion)
//     2. Ragdoll am Original-NPC aktivieren (Rigidbodies, Collider, etc.)
//
//   Mit dem RagdollSwapper-System braucht der KI-NPC keine Ragdoll-
//   Komponenten mehr (keine Rigidbodies/CharacterJoints an den Bones).
//   Die Ragdoll-Physik läuft nur noch auf den gespawnten Prefabs.
//
//   Dieser Tracker übernimmt NUR Aufgabe 1: Impact-Kräfte sammeln.
//   Die Daten werden beim Tod an den NpcRagdollSwapper übergeben.
//
// SETUP:
//   1. NpcRagdollController vom KI-NPC-Prefab entfernen
//   2. Ragdoll-Komponenten von den Bones entfernen (Rigidbodies, Collider, Joints)
//   3. Diesen NpcImpactTracker auf das Prefab legen
//   4. NpcRagdollSwapper bleibt wie gehabt
//   5. CapsuleCollider am Root für Gameplay-Hits bleibt bestehen
//
// ════════════════════════════════════════════════════════════════════════════

public class NpcImpactTracker : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────

    [Header("Melee Impact")]
    [Tooltip("Kraft die bei einem Melee-Kill angewendet wird.")]
    [SerializeField] private float meleeImpactForce = 300f;

    [Header("Thrown Sword Impact")]
    [Tooltip("Kraft die bei einem Thrown-Sword-Kill angewendet wird.")]
    [SerializeField] private float thrownSwordImpactForce = 400f;

    [Header("Bullet Impact")]
    [Tooltip("Kraft pro Kugeltreffer. Mehrere Treffer akkumulieren.")]
    [SerializeField] private float bulletImpactForce = 50f;

    [Tooltip("Wenn true, werden mehrere Kugeltreffer addiert.")]
    [SerializeField] private bool accumulateBulletImpacts = true;

    [Header("Explosion Impact")]
    [Tooltip("Kraft bei einer Explosion.")]
    [SerializeField] private float explosionImpactForce = 500f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────

    private Vector3 accumulatedForce;
    private Vector3 lastImpactPoint;
    private bool hasImpact;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Public Properties
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>True wenn mindestens ein Impact registriert wurde.</summary>
    public bool HasAccumulatedImpact => hasImpact;

    /// <summary>Akkumulierte Impact-Kraft (Richtung + Stärke).</summary>
    public Vector3 AccumulatedImpactForce => accumulatedForce;

    /// <summary>Letzter Auftreffpunkt.</summary>
    public Vector3 LastImpactPoint => lastImpactPoint;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Impact Registration
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registriert einen Melee-Impact. Richtung: vom Angreifer weg.
    /// </summary>
    public void RegisterMeleeImpact(Vector3 attackerPosition)
    {
        Vector3 dir = (transform.position - attackerPosition).normalized;
        dir.y = 0f;

        accumulatedForce = dir * meleeImpactForce;
        lastImpactPoint = transform.position + Vector3.up;
        hasImpact = true;

        if (showDebugInfo)
            Debug.Log($"[ImpactTracker] {gameObject.name}: Melee-Impact von {attackerPosition}");
    }

    /// <summary>
    /// Registriert einen Thrown-Sword-Impact.
    /// </summary>
    public void RegisterThrownSwordImpact(Vector3 swordDirection, Vector3 hitPoint)
    {
        accumulatedForce = swordDirection.normalized * thrownSwordImpactForce;
        lastImpactPoint = hitPoint;
        hasImpact = true;

        if (showDebugInfo)
            Debug.Log($"[ImpactTracker] {gameObject.name}: Sword-Impact an {hitPoint}");
    }

    /// <summary>
    /// Registriert einen Kugel-Impact. Mehrere Treffer akkumulieren.
    /// </summary>
    public void RegisterBulletImpact(Vector3 bulletDirection, Vector3 hitPoint)
    {
        Vector3 bulletForce = bulletDirection.normalized * bulletImpactForce;

        if (accumulateBulletImpacts && hasImpact)
            accumulatedForce += bulletForce;
        else
            accumulatedForce = bulletForce;

        lastImpactPoint = hitPoint;
        hasImpact = true;

        if (showDebugInfo)
            Debug.Log($"[ImpactTracker] {gameObject.name}: Bullet-Impact. Total: {accumulatedForce.magnitude:F1}");
    }

    /// <summary>
    /// Registriert einen Explosions-Impact. Richtung: vom Zentrum weg.
    /// </summary>
    public void RegisterExplosionImpact(Vector3 explosionCenter)
    {
        Vector3 dir = (transform.position - explosionCenter).normalized;
        dir.y = 0f;

        accumulatedForce = dir * explosionImpactForce;
        lastImpactPoint = transform.position + Vector3.up;
        hasImpact = true;

        if (showDebugInfo)
            Debug.Log($"[ImpactTracker] {gameObject.name}: Explosion-Impact von {explosionCenter}");
    }

    /// <summary>
    /// Registriert einen generischen Impact.
    /// </summary>
    public void RegisterImpact(Vector3 force, Vector3 hitPoint)
    {
        accumulatedForce = force;
        lastImpactPoint = hitPoint;
        hasImpact = true;
    }

    /// <summary>
    /// Setzt alle akkumulierten Impacts zurück.
    /// </summary>
    public void ClearAccumulatedImpact()
    {
        accumulatedForce = Vector3.zero;
        lastImpactPoint = Vector3.zero;
        hasImpact = false;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Debug
    // ────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!showDebugInfo || !hasImpact) return;

        Gizmos.color = Color.red;
        Vector3 start = lastImpactPoint != Vector3.zero ? lastImpactPoint : transform.position;
        Gizmos.DrawRay(start, accumulatedForce.normalized * 2f);
        Gizmos.DrawWireSphere(start, 0.1f);
    }

    #endregion
}
