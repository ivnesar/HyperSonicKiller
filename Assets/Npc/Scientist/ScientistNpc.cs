using UnityEngine;
using UnityEngine.AI;

// ════════════════════════════════════════════════════════════════════════════
// SCIENTIST NPC - Wehrloses Kanonenfutter
// ════════════════════════════════════════════════════════════════════════════
//
// Verhalten:
//   1. Sucht einen zufälligen erreichbaren Punkt auf dem NavMesh
//   2. Bevorzugt Punkte in der Nähe von Soldier/Defender NPCs
//   3. Vermeidet Punkte, die zu nah an anderen NPCs sind
//   4. Läuft zum Punkt, wartet dort eine zufällige Zeit, wiederholt
//
// States:
//   Searching → Fleeing → Waiting → Searching → ...
//
// ════════════════════════════════════════════════════════════════════════════

public class ScientistNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region State Enum
    // ════════════════════════════════════════════════════════════════════════

    private enum ScientistState
    {
        Searching = 0,  // Sucht einen neuen Fluchtpunkt
        Fleeing   = 1,  // Läuft zum Fluchtpunkt
        Waiting   = 2   // Wartet am Fluchtpunkt
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Scientist - Fluchtverhalten")]
    [Tooltip("Minimale Wartezeit am Fluchtpunkt")]
    [SerializeField] private float minWaitTime = 1f;

    [Tooltip("Maximale Wartezeit am Fluchtpunkt")]
    [SerializeField] private float maxWaitTime = 4f;

    [Header("Scientist - Punktsuche")]
    [Tooltip("Maximaler Suchradius für zufällige NavMesh-Punkte")]
    [SerializeField] private float searchRadius = 20f;

    [Tooltip("Mindestabstand zu anderen NPCs beim Wählen eines Fluchtpunkts")]
    [SerializeField] private float minDistanceToOtherNpcs = 3f;

    [Tooltip("Anzahl Kandidaten-Punkte pro Suchvorgang (mehr = bessere Auswahl, langsamer)")]
    [SerializeField] private int candidateCount = 8;

    [Tooltip("Maximale Versuche, bevor ein Punkt ohne Scoring akzeptiert wird")]
    [SerializeField] private int maxSearchAttempts = 15;

    [Header("Scientist - Schutzsuche")]
    [Tooltip("Bonus-Score pro Meter Nähe zu einem Soldier/Defender (höher = stärkere Bevorzugung)")]
    [SerializeField] private float guardProximityWeight = 2f;

    [Tooltip("Maximale Distanz, ab der ein Guard noch als relevant gilt")]
    [SerializeField] private float maxGuardConsiderationDistance = 30f;

    [Header("Scientist - Ankunft")]
    [Tooltip("Distanz ab der der Scientist als 'angekommen' gilt")]
    [SerializeField] private float arrivalDistance = 1.5f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private ScientistState currentState = ScientistState.Searching;
    private Vector3 currentFleeTarget;
    private bool hasValidFleeTarget;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStart()
    {
        // Scientist startet sofort mit Suche
        EnterState(ScientistState.Searching);
    }

    protected override void UpdateBehavior()
    {
        switch (currentState)
        {
            case ScientistState.Searching:
                UpdateSearching();
                break;

            case ScientistState.Fleeing:
                UpdateFleeing();
                break;

            case ScientistState.Waiting:
                UpdateWaiting();
                break;
        }
    }

    protected override void OnStunStart()
    {
        // Nichts spezielles — NpcBase stoppt bereits die Bewegung
    }

    protected override void OnStunEnd()
    {
        // Nach Stun sofort neuen Fluchtpunkt suchen
        EnterState(ScientistState.Searching);
    }

    public override string GetCurrentStateName() => currentState.ToString();

    public override NpcType GetNpcType() => NpcType.Scientist;

    public override int GetStateID() => (int)currentState;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Machine
    // ════════════════════════════════════════════════════════════════════════

    private void EnterState(ScientistState newState)
    {
        currentState = newState;

        switch (newState)
        {
            case ScientistState.Searching:
                StopMovement();
                TryFindFleePoint();
                break;

            case ScientistState.Fleeing:
                MoveToward(currentFleeTarget);
                break;

            case ScientistState.Waiting:
                StopMovement();
                SetStateTimer(Random.Range(minWaitTime, maxWaitTime));
                break;
        }
    }

    // ── Searching ────────────────────────────────────────────────────────

    private void UpdateSearching()
    {
        // TryFindFleePoint wird in EnterState aufgerufen.
        // Falls kein Punkt gefunden wurde, jeden Frame erneut versuchen.
        if (!hasValidFleeTarget)
        {
            TryFindFleePoint();
        }
    }

    // ── Fleeing ──────────────────────────────────────────────────────────

    private void UpdateFleeing()
    {
        // Prüfe ob der NavAgent sein Ziel erreicht hat
        if (!navAgent.pathPending && navAgent.remainingDistance <= arrivalDistance)
        {
            EnterState(ScientistState.Waiting);
            return;
        }

        // Falls der Pfad ungültig wird (z.B. NavMesh-Änderung), neuen Punkt suchen
        if (navAgent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            hasValidFleeTarget = false;
            EnterState(ScientistState.Searching);
        }
    }

    // ── Waiting ──────────────────────────────────────────────────────────

    private void UpdateWaiting()
    {
        if (UpdateStateTimer())
        {
            EnterState(ScientistState.Searching);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Flee Point Search
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sucht den besten Fluchtpunkt aus mehreren Kandidaten.
    /// Bevorzugt Punkte nahe Soldiers/Defenders, vermeidet NPC-Überlappung.
    /// </summary>
    private void TryFindFleePoint()
    {
        hasValidFleeTarget = false;

        // Alle lebenden NPCs sammeln (für Überlappungs-Check und Guard-Suche)
        NpcBase[] allNpcs = FindObjectsByType<NpcBase>(FindObjectsSortMode.None);

        Vector3 bestPoint = Vector3.zero;
        float bestScore = float.MinValue;
        bool foundAny = false;

        for (int i = 0; i < maxSearchAttempts; i++)
        {
            // Zufälligen Punkt auf dem NavMesh finden
            if (!TryGetRandomNavMeshPoint(out Vector3 candidate))
                continue;

            // Prüfe Mindestabstand zu anderen NPCs
            if (IsTooCloseToOtherNpcs(candidate, allNpcs))
                continue;

            // Score berechnen (höher = besser)
            float score = ScoreFleePoint(candidate, allNpcs);

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = candidate;
                foundAny = true;
            }

            // Genug gute Kandidaten gesammelt?
            if (foundAny && i >= candidateCount)
                break;
        }

        if (foundAny)
        {
            currentFleeTarget = bestPoint;
            hasValidFleeTarget = true;
            EnterState(ScientistState.Fleeing);
        }
    }

    /// <summary>
    /// Versucht, einen zufälligen begehbaren Punkt auf dem NavMesh zu finden.
    /// </summary>
    private bool TryGetRandomNavMeshPoint(out Vector3 result)
    {
        Vector3 randomDirection = Random.insideUnitSphere * searchRadius;
        randomDirection += transform.position;

        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
        {
            // Prüfe ob der Punkt tatsächlich erreichbar ist (NavMesh-Pfad)
            NavMeshPath path = new NavMeshPath();
            if (navAgent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                result = hit.position;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Prüft ob ein Punkt zu nah an einem anderen lebenden NPC ist.
    /// </summary>
    private bool IsTooCloseToOtherNpcs(Vector3 point, NpcBase[] allNpcs)
    {
        float minDistSqr = minDistanceToOtherNpcs * minDistanceToOtherNpcs;

        foreach (var npc in allNpcs)
        {
            if (npc == this || npc.IsDead) continue;

            if ((npc.transform.position - point).sqrMagnitude < minDistSqr)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Bewertet einen Fluchtpunkt.
    /// Höherer Score = näher an Soldiers/Defenders.
    /// </summary>
    private float ScoreFleePoint(Vector3 point, NpcBase[] allNpcs)
    {
        float score = 0f;

        foreach (var npc in allNpcs)
        {
            if (npc == this || npc.IsDead) continue;

            NpcType type = npc.GetNpcType();
            if (type != NpcType.Soldier && type != NpcType.Defender) continue;

            float distance = Vector3.Distance(point, npc.transform.position);

            if (distance > maxGuardConsiderationDistance) continue;

            // Je näher am Guard, desto höher der Score
            float proximityScore = (maxGuardConsiderationDistance - distance) * guardProximityWeight;
            score += proximityScore;
        }

        return score;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        if (!Application.isPlaying) return;

        // Fluchtpunkt anzeigen
        if (hasValidFleeTarget)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(currentFleeTarget, 0.8f);
            Gizmos.DrawLine(transform.position + Vector3.up, currentFleeTarget + Vector3.up);
        }

        // Suchradius anzeigen
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }

    #endregion
}
