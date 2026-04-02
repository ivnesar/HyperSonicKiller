using UnityEngine;
using UnityEngine.AI;

// ════════════════════════════════════════════════════════════════════════════
// CIVILIAN NPC - Set-Dressing oder panisch flüchtender Zivilist
// ════════════════════════════════════════════════════════════════════════════
//
// ZWECK:
//   Rein narrativer NPC. Greift nicht an, interagiert nicht mit dem Spieler.
//   Kann zum Set-Dressing genutzt werden (Animation-Loop) oder als
//   flüchtender Zivilist.
//
// VERHALTEN (Inspector-Enum):
//   SetDresser → Steht still, loopt eine zugewiesene Animation. Reagiert auf nichts.
//   Fleeing    → Flüchtet panisch vom Spieler weg.
//
// FLUCHTVERHALTEN:
//   - Erkennt den Spieler innerhalb von detectionDistance (sofort, keine Reaktionszeit).
//   - Kann den Spieler NICHT erkennen wenn dieser im Dash-State ist.
//   - Flüchtet zu einem zufälligen NavMesh-Punkt außerhalb von fleeDistance zum Spieler.
//   - Chaotische Richtungswechsel in zufälligen Intervallen während der Flucht.
//   - Wartet am Fluchtpunkt bis der Spieler wieder in detectionDistance kommt.
//   - Nutzt "letzte bekannte Position" des Spielers wenn dieser dasht.
//
// SETUP:
//   1. Prefab erstellen mit NavMeshAgent + CivilianNpc + CivilianAnimationManager
//   2. NavMeshAgent konfigurieren:
//      - Obstacle Avoidance: High Quality
//      - Speed wird per moveSpeed im Inspector gesteuert
//   3. CivilianAnimationManager auf das Model-Kind legen
//   4. Im Inspector: CivilianBehavior wählen + Clips zuweisen
//
// ════════════════════════════════════════════════════════════════════════════

public enum CivilianBehavior
{
    SetDresser,  // Steht still, loopt eine Animation (Set-Dressing)
    Fleeing      // Flüchtet vor dem Spieler
}

public class CivilianNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Hidden Base Fields
    // ════════════════════════════════════════════════════════════════════════

    public override string[] HiddenBaseFields => new[]
    {
        "behaviorMode",      // Civilian nutzt eigenes CivilianBehavior-Enum
    };

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Civilian — Verhaltensmodus")]
    [Tooltip("SetDresser: Loopt eine Animation, reagiert auf nichts.\n" +
             "Fleeing: Flüchtet vor dem Spieler.")]
    [SerializeField] private CivilianBehavior civilianBehavior = CivilianBehavior.Fleeing;

    [Header("Civilian — Flucht-Erkennung")]
    [Tooltip("Wie nah der Spieler dem NPC kommen darf bevor er flieht.\n" +
             "Muss größer als fleeDistance sein, sonst flieht der NPC in einer Endlosschleife.")]
    [SerializeField] private float detectionDistance = 15f;

    [Tooltip("Mindest-Distanz die ein Fluchtpunkt vom Spieler entfernt sein muss.\n" +
             "= Wie weit der NPC vom Spieler wegrennt bevor er stehen bleibt.\n" +
             "Muss kleiner als detectionDistance sein.")]
    [SerializeField] private float fleeDistance = 10f;

    [Header("Civilian — Fluchtverhalten")]
    [Tooltip("Suchradius für Fluchtpunkte auf dem NavMesh.")]
    [SerializeField] private float fleeSearchRadius = 15f;

    [Tooltip("Wie viele Fluchtpunkt-Kandidaten pro Suche getestet werden.")]
    [SerializeField] private int fleeSearchAttempts = 8;

    [Header("Civilian — Richtungswechsel")]
    [Tooltip("Minimale Zeit zwischen chaotischen Richtungswechseln während der Flucht.")]
    [SerializeField] private float minDirectionChangeInterval = 1.5f;

    [Tooltip("Maximale Zeit zwischen Richtungswechseln.")]
    [SerializeField] private float maxDirectionChangeInterval = 3.5f;

    [Header("Audio")]
    [Tooltip("Zufällige Panik-Sounds (Schreie, Schluchzen, etc.).")]
    [SerializeField] private AudioClip[] panicSounds;

    [Tooltip("Minimale Zeit zwischen Panik-Sounds.")]
    [SerializeField] private float minPanicSoundInterval = 3f;

    [Tooltip("Maximale Zeit zwischen Panik-Sounds.")]
    [SerializeField] private float maxPanicSoundInterval = 6f;

    [Header("Civilian — Hinfallen")]
    [Tooltip("Distanz zum Spieler ab der der NPC hinfällt.\n" +
             "Gilt für beide Verhaltensmodi (SetDresser + Fleeing).")]
    [SerializeField] private float fallTriggerDistance = 2f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors (für CivilianStates)
    // ════════════════════════════════════════════════════════════════════════

    public CivilianBehavior Behavior => civilianBehavior;
    public float DetectionDistance => detectionDistance;
    public float FleeDistance => fleeDistance;
    public float FleeSearchRadius => fleeSearchRadius;
    public int FleeSearchAttempts => fleeSearchAttempts;
    public float MinDirectionChangeInterval => minDirectionChangeInterval;
    public float MaxDirectionChangeInterval => maxDirectionChangeInterval;
    public float FallTriggerDistance => fallTriggerDistance;

    /// <summary>True wenn der NPC bereits hingefallen ist (kann nicht zurückgesetzt werden).</summary>
    public bool HasFallen { get; private set; }

    /// <summary>
    /// Typsichere Referenz auf den CivilianAnimationManager.
    /// </summary>
    public CivilianAnimationManager AnimManager { get; private set; }

    /// <summary>
    /// Die letzte bekannte Position des Spielers.
    /// Wird jeden Frame aktualisiert, solange der Spieler NICHT dasht.
    /// Wenn der Spieler dasht, bleibt der letzte Wert stehen.
    /// </summary>
    public Vector3 LastKnownPlayerPosition { get; private set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<CivilianNpc> currentState;

    /// <summary>Nächster Zeitpunkt für einen Panik-Sound.</summary>
    private float nextPanicSoundTime;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();

        AnimManager = GetComponentInChildren<CivilianAnimationManager>();
        if (AnimManager == null)
        {
            Debug.LogWarning($"[CivilianNpc] No CivilianAnimationManager found on {gameObject.name}! " +
                             "Animations will not work.");
        }
    }

    protected override void OnStart()
    {
        // Initiale Spielerposition setzen (falls vorhanden)
        if (playerTransform != null)
            LastKnownPlayerPosition = playerTransform.position;

        if (civilianBehavior == CivilianBehavior.SetDresser)
        {
            // SetDresser: NavAgent deaktivieren, State Machine mit SetDressing-State starten
            if (navAgent != null)
                navAgent.enabled = false;

            ChangeState(new CivilianStates.SetDressing());
        }
        else
        {
            // Fleeing: NavAgent-Speed setzen, State Machine starten
            // Rotation wird NICHT vom NavAgent gesteuert (updateRotation bleibt false von NpcBase).
            // Stattdessen rufen die States manuell RotateTowardMovementDirection() auf.
            if (navAgent != null)
                navAgent.speed = moveSpeed;

            ChangeState(new CivilianStates.Idle());
        }
    }

    protected override void UpdateBehavior()
    {
        // Spielerposition tracken (nur wenn Spieler NICHT dasht)
        UpdateLastKnownPlayerPosition();

        // Fall-Check: gilt für BEIDE Verhaltensmodi, aber nur einmal
        if (!HasFallen && !(currentState is CivilianStates.Fallen))
        {
            if (playerTransform != null && DistanceToTarget <= fallTriggerDistance)
            {
                HasFallen = true;

                // NavAgent deaktivieren (falls aktiv)
                if (navAgent != null && navAgent.enabled)
                {
                    navAgent.isStopped = true;
                    navAgent.ResetPath();
                }

                ChangeState(new CivilianStates.Fallen());
                return;
            }
        }

        // State Machine
        if (currentState == null) return;

        var nextState = currentState.Update(this);
        if (nextState != null)
            ChangeState(nextState);
    }

    protected override void OnStunStart()
    {
        // Fallen-NPCs bleiben am Boden — kein Stun-Overwrite
        if (HasFallen) return;

        ChangeState(new CivilianStates.Stunned());
    }

    protected override void OnStunEnd()
    {
        // Fallen-NPCs bleiben am Boden
        if (HasFallen) return;

        if (civilianBehavior == CivilianBehavior.SetDresser)
        {
            // SetDresser geht zurück in seine Animation
            ChangeState(new CivilianStates.SetDressing());
        }
        else
        {
            // Fleeing: Nach Stun sofort fliehen (wurde gerade angegriffen)
            ChangeState(new CivilianStates.Fleeing());
        }
    }

    public override string GetCurrentStateName()
    {
        return currentState?.StateName ?? "None";
    }

    public override NpcType GetNpcType() => NpcType.Civilian;

    public override int GetStateID()
    {
        return currentState?.StateID ?? 0;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<CivilianNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Spieler-Tracking
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aktualisiert die letzte bekannte Spielerposition.
    /// Wird NUR aktualisiert wenn der Spieler NICHT dasht.
    /// Wenn der Spieler dasht, bleibt der letzte bekannte Wert stehen.
    /// </summary>
    private void UpdateLastKnownPlayerPosition()
    {
        if (playerTransform == null) return;

        // Nur aktualisieren wenn der Spieler nicht im Dash ist
        if (!IsPlayerDashing)
        {
            LastKnownPlayerPosition = playerTransform.position;
        }
    }

    /// <summary>
    /// Prüft ob der Spieler aktuell sichtbar ist (= nicht im Dash).
    /// </summary>
    public bool CanSeePlayer()
    {
        return !IsPlayerDashing;
    }

    /// <summary>
    /// Distanz zur letzten bekannten Spielerposition.
    /// Nutzt die Live-Position wenn der Spieler sichtbar ist,
    /// sonst die letzte bekannte Position.
    /// </summary>
    public float DistanceToLastKnownPosition()
    {
        return Vector3.Distance(transform.position, LastKnownPlayerPosition);
    }

    /// <summary>
    /// Prüft ob der Spieler (basierend auf letzter bekannter Position)
    /// innerhalb der detectionDistance ist UND sichtbar (nicht dashend).
    /// </summary>
    public bool IsPlayerDetected()
    {
        // Spieler im Dash = unsichtbar = nicht erkannt
        if (!CanSeePlayer()) return false;

        return DistanceToTarget <= detectionDistance;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Fluchtpunkt-Suche
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sucht einen Fluchtpunkt auf dem NavMesh der mindestens fleeDistance
    /// vom Spieler entfernt ist.
    ///
    /// SUCHSTRATEGIE: Zufällige Punkte in einem Ring um die letzte bekannte
    /// Spielerposition herum, mit Radius fleeDistance bis fleeSearchRadius.
    ///
    /// SCORING-PRIORITÄT (je näher am Spieler, desto strikter):
    ///   1. Punkte in der Forward-Richtung des NPC (weg vom Spieler) werden stark bevorzugt
    ///   2. Seitliche Punkte sind akzeptabel als Ausweichoption
    ///   3. 180°-Wendung (zurück zum Spieler) wird nur als letzter Ausweg gewählt
    /// </summary>
    public bool TryFindFleePoint(out Vector3 result)
    {
        result = Vector3.zero;

        if (navAgent == null) return false;

        Vector3 playerPos = LastKnownPlayerPosition;

        // Hauptfluchtrichtung: vom Spieler weg
        Vector3 awayFromPlayer = (transform.position - playerPos);
        awayFromPlayer.y = 0f;
        float currentDistToPlayer = awayFromPlayer.magnitude;
        Vector3 awayDir = currentDistToPlayer > 0.1f ? awayFromPlayer / currentDistToPlayer : transform.forward;

        Vector3 bestPoint = Vector3.zero;
        float bestScore = float.MinValue;
        bool foundAny = false;

        for (int i = 0; i < fleeSearchAttempts; i++)
        {
            // Zufällige Richtung auf der XZ-Ebene
            Vector2 randomDir2D = Random.insideUnitCircle.normalized;
            Vector3 randomDir = new Vector3(randomDir2D.x, 0f, randomDir2D.y);

            // Zufällige Distanz zwischen fleeDistance und fleeSearchRadius
            float randomDist = Random.Range(fleeDistance, fleeSearchRadius);

            // Suchcenter = Spielerposition + zufällige Richtung × Distanz
            Vector3 searchCenter = playerPos + randomDir * randomDist;

            if (!NavMesh.SamplePosition(searchCenter, out NavMeshHit hit, fleeSearchRadius * 0.5f, NavMesh.AllAreas))
                continue;

            Vector3 candidate = hit.position;

            // Pfad prüfen — muss erreichbar sein
            NavMeshPath path = new NavMeshPath();
            if (!navAgent.CalculatePath(candidate, path) || path.status != NavMeshPathStatus.PathComplete)
                continue;

            // Score berechnen
            float score = ScoreFleePoint(candidate, awayDir, currentDistToPlayer);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = candidate;
                foundAny = true;
            }
        }

        if (foundAny)
        {
            result = bestPoint;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Bewertet einen Fluchtpunkt. Höherer Score = besser.
    ///
    /// WEIGHTING:
    ///   - Distanz zum Spieler (muss >= fleeDistance sein, sonst harter Malus)
    ///   - Richtungs-Alignment: Punkte in der Fluchtrichtung (weg vom Spieler) werden bevorzugt.
    ///     Je näher der NPC am Spieler ist, desto stärker wird die Forward-Richtung erzwungen.
    ///   - Kleine Zufallskomponente für natürliches Verhalten
    /// </summary>
    private float ScoreFleePoint(Vector3 point, Vector3 awayFromPlayerDir, float currentDistToPlayer)
    {
        float score = 0f;

        // ── 1. Distanz zum Spieler ──────────────────────────────────────
        float distFromPlayer = Vector3.Distance(point, LastKnownPlayerPosition);

        // Harter Malus: unter fleeDistance → fast nie wählen
        if (distFromPlayer < fleeDistance)
            score -= 100f;

        // Moderater Bonus für Distanz (nicht zu stark, sonst rennt NPC immer maximal weit)
        score += distFromPlayer;

        // ── 2. Richtungs-Alignment (Kernlogik) ─────────────────────────
        // Richtung vom NPC zum Kandidaten
        Vector3 toCandidate = (point - transform.position);
        toCandidate.y = 0f;
        if (toCandidate.sqrMagnitude > 0.01f)
        {
            toCandidate.Normalize();

            // Dot-Product: +1 = perfekt in Fluchtrichtung, -1 = zurück zum Spieler
            float alignment = Vector3.Dot(toCandidate, awayFromPlayerDir);

            // Proximity-Faktor: je näher am Spieler, desto wichtiger ist die Richtung
            // Bei detectionDistance oder weiter → Faktor ~0 (Richtung egal)
            // Bei 0 Distanz → Faktor 1 (Richtung kritisch)
            float proximityFactor = Mathf.Clamp01(1f - (currentDistToPlayer / detectionDistance));

            // Richtungs-Score: stark gewichtet wenn nah am Spieler
            // alignment geht von -1 bis +1, wir mappen auf 0 bis 1 für den Score
            float directionScore = (alignment + 1f) * 0.5f; // 0 = weg vom Ziel, 1 = perfekt

            // Gewichtung: proximity bestimmt wie wichtig die Richtung ist
            // Nah am Spieler: bis zu 40 Punkte Bonus für gute Richtung
            // Weit weg: nur bis zu 5 Punkte (fast egal)
            float directionWeight = Mathf.Lerp(5f, 40f, proximityFactor);
            score += directionScore * directionWeight;
        }

        // ── 3. Zufällige Komponente ─────────────────────────────────────
        score += Random.Range(0f, 3f);

        return score;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Audio
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spielt einen zufälligen Panik-Sound ab (mit Cooldown).
    /// </summary>
    public void TryPlayPanicSound()
    {
        if (panicSounds == null || panicSounds.Length == 0) return;
        if (Time.time < nextPanicSoundTime) return;

        AudioClip clip = panicSounds[Random.Range(0, panicSounds.Length)];
        PlaySound(clip);

        nextPanicSoundTime = Time.time + Random.Range(minPanicSoundInterval, maxPanicSoundInterval);
    }

    /// <summary>
    /// Erzwingt sofort einen Panik-Sound (z.B. bei Flucht-Start).
    /// </summary>
    public void PlayPanicSoundImmediate()
    {
        if (panicSounds == null || panicSounds.Length == 0) return;

        AudioClip clip = panicSounds[Random.Range(0, panicSounds.Length)];
        PlaySound(clip);

        nextPanicSoundTime = Time.time + Random.Range(minPanicSoundInterval, maxPanicSoundInterval);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers für States
    // ════════════════════════════════════════════════════════════════════════

    public new void StopMovement() => base.StopMovement();
    public new void SetStateTimer(float t) => base.SetStateTimer(t);
    public new bool UpdateStateTimer() => base.UpdateStateTimer();

    /// <summary>
    /// Bewegt den Civilian zu einem Fluchtpunkt.
    /// Nutzt moveSpeed aus NpcBase für die Geschwindigkeit.
    /// </summary>
    public void MoveToFleePoint(Vector3 position)
    {
        if (navAgent == null || !navAgent.enabled || IsStunned) return;

        navAgent.SetDestination(position);
        navAgent.isStopped = false;
        navAgent.speed = moveSpeed;
    }

    /// <summary>
    /// Dreht den Civilian in seine aktuelle Bewegungsrichtung.
    /// Nutzt navAgent.velocity als Richtung und maxRotationSpeed für sanfte Drehung.
    /// 
    /// Analog zu SoldierNpc.RotateTowardTarget(), aber statt zum Spieler
    /// dreht der Civilian dorthin wo er hinläuft.
    /// </summary>
    public void RotateTowardMovementDirection()
    {
        if (navAgent == null || !navAgent.enabled || IsStunned) return;

        // NavAgent-Velocity als Bewegungsrichtung nutzen
        Vector3 velocity = navAgent.velocity;
        velocity.y = 0f;

        // Keine Rotation wenn der NPC stillsteht
        if (velocity.sqrMagnitude < 0.1f) return;

        Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
        float maxAngle = maxRotationSpeed * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxAngle);
    }

    /// <summary>
    /// Dreht den Civilian smooth zum Spieler (Y-Achse only).
    /// Wird vom Fallen-State aufgerufen damit die Fall-Animation zum Spieler zeigt.
    /// Nutzt maxRotationSpeed wie alle anderen Rotations-Methoden.
    /// </summary>
    public void RotateTowardPlayer()
    {
        if (playerTransform == null) return;

        Vector3 direction = playerTransform.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        float maxAngle = maxRotationSpeed * 4 * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxAngle);
    }

    /// <summary>
    /// Prüft ob der NavAgent sein aktuelles Ziel erreicht hat.
    /// </summary>
    public bool HasReachedDestination()
    {
        if (navAgent == null || !navAgent.enabled) return true;
        if (navAgent.pathPending) return false;

        return navAgent.remainingDistance <= navAgent.stoppingDistance + 0.2f;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Validation
    // ════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Warnung wenn fleeDistance >= detectionDistance
        if (civilianBehavior == CivilianBehavior.Fleeing && fleeDistance >= detectionDistance)
        {
            Debug.LogWarning(
                $"[CivilianNpc] {gameObject.name}: fleeDistance ({fleeDistance}) muss kleiner sein als " +
                $"detectionDistance ({detectionDistance}), sonst flieht der NPC in einer Endlosschleife!",
                this
            );
        }
    }
#endif

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // Fall Trigger Distance (rot) — gilt für beide Verhaltensmodi
        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, fallTriggerDistance);

        if (civilianBehavior == CivilianBehavior.SetDresser) return;

        // Detection Distance (orange)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionDistance);

        // Flee Distance (gelb) — nur zur Visualisierung am NPC
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, fleeDistance);

        // Letzte bekannte Spielerposition (wenn im Play-Mode)
        if (Application.isPlaying)
        {
            Gizmos.color = IsPlayerDashing ? Color.gray : Color.white;
            Gizmos.DrawWireSphere(LastKnownPlayerPosition, 0.5f);

            // Flee Distance um die letzte bekannte Position
            Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(LastKnownPlayerPosition, fleeDistance);
        }
    }

    #endregion
}
