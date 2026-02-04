using UnityEngine;

/// <summary>
/// Soldier NPC - Schießt auf den Spieler aus der Distanz.
/// Benötigt freie Sichtlinie zum Schießen.
/// Enthält dynamische Bone-Rotation für vertikales Zielen.
/// </summary>
public class SoldierNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Combat - Ranges")]
    [SerializeField] private float minShootingRange = 6f;
    [SerializeField] private float maxShootingRange = 18f;
    [SerializeField] private float preferredRange = 12f;

    [Header("Combat - Timing")]
    [SerializeField] private float aimDuration = 0.6f;
    [SerializeField] private float timeBetweenShots = 0.15f;
    [SerializeField] private int shotsPerSalvo = 5;
    [SerializeField] private float reloadDuration = 2.0f;

    [Header("Combat - Accuracy")]
    [SerializeField] private float baseAccuracy = 0.85f;
    [SerializeField] private float accuracySpreadAngle = 5f;

    [Header("Combat - Damage")]
    [SerializeField] private int damagePerShot = 10;

    [Header("Weapon")]
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private SoldierBullet bulletPrefab;
    [Tooltip("Layer für Line-of-Sight Check (sollte Player + Hindernisse enthalten)")]
    [SerializeField] private LayerMask lineOfSightMask;

    [Header("Aim Bone Rotation")]
    [Tooltip("Der Bone der sich vertikal neigen soll (z.B. Stomach, Spine, Chest)")]
    [SerializeField] private Transform aimBone;
    
    [Tooltip("Lokale Rotationsachse für Pitch (Up/Down). Abhängig vom Rig.")]
    [SerializeField] private Vector3 pitchAxis = Vector3.right;
    
    [Tooltip("Invertiert die Pitch-Richtung falls nötig")]
    [SerializeField] private bool invertPitch = false;
    
    [Tooltip("Maximale Neigung nach oben in Grad")]
    [SerializeField] private float maxPitchUp = 45f;
    
    [Tooltip("Maximale Neigung nach unten in Grad")]
    [SerializeField] private float maxPitchDown = 30f;
    
    [Tooltip("Geschwindigkeit der Pitch-Interpolation")]
    [SerializeField] private float pitchSmoothSpeed = 8f;
    
    [Tooltip("Geschwindigkeit beim Ein-/Ausblenden des Aim-Pitch")]
    [SerializeField] private float aimBlendSpeed = 6f;

    [Header("Audio/VFX")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField] private ParticleSystem muzzleFlash;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Public Accessors
    // ════════════════════════════════════════════════════════════════════════

    public float MinShootingRange => minShootingRange;
    public float MaxShootingRange => maxShootingRange;
    public float PreferredRange => preferredRange;
    public float AimDuration => aimDuration;
    public float TimeBetweenShots => timeBetweenShots;
    public int ShotsPerSalvo => shotsPerSalvo;
    public float ReloadDuration => reloadDuration;
    public Animator NpcAnimator => animator;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private INpcState<SoldierNpc> currentState;
    public int ShotsFiredInSalvo { get; set; }
    public float NextShotTime { get; set; }

    // Aim Bone Rotation
    private float currentPitch = 0f;
    private float aimBlendWeight = 0f;
    
    /// <summary>
    /// Wird von States gesetzt um die Bone-Rotation zu aktivieren/deaktivieren.
    /// True = Bone neigt sich zum Spieler, False = Bone kehrt zur Animation zurück.
    /// </summary>
    public bool IsAiming { get; set; }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Implementation
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStart()
    {
        ChangeState(new SoldierStates.Idle());
    }

    protected override void UpdateBehavior()
    {
        if (currentState == null) return;

        var nextState = currentState.Update(this);
        if (nextState != null)
            ChangeState(nextState);
    }

    protected override void OnStunStart() => ChangeState(new SoldierStates.Stunned());
    protected override void OnStunEnd() => ChangeState(new SoldierStates.Idle());

    public override string GetCurrentStateName() => currentState?.StateName ?? "None";
    public override NpcType GetNpcType() => NpcType.Soldier;
    public override int GetStateID() => currentState?.StateID ?? 0;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Aim Bone Rotation
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LateUpdate wird NACH der Animation aufgerufen.
    /// Hier addieren wir unsere Pitch-Rotation zur Animation-Rotation.
    /// </summary>
    private void LateUpdate()
    {
        if (isDead) return;
        
        UpdateAimBoneRotation();
    }

    private void UpdateAimBoneRotation()
    {
        // Blend-Weight aktualisieren (smooth ein-/ausblenden)
        float targetBlend = IsAiming ? 1f : 0f;
        aimBlendWeight = Mathf.MoveTowards(aimBlendWeight, targetBlend, aimBlendSpeed * Time.deltaTime);

        // Früher Ausstieg wenn komplett ausgeblendet und kein Bone
        if (aimBlendWeight < 0.001f || aimBone == null) return;

        // Ziel-Pitch berechnen
        float targetPitch = CalculateTargetPitch();
        
        // Smooth zum Ziel interpolieren
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, pitchSmoothSpeed * Time.deltaTime);

        // Finale Rotation mit Blend-Weight anwenden
        float finalPitch = currentPitch * aimBlendWeight;
        
        // Rotation auf die konfigurierte Achse anwenden
        Quaternion pitchRotation = Quaternion.AngleAxis(finalPitch, pitchAxis.normalized);
        aimBone.localRotation *= pitchRotation;
    }

    /// <summary>
    /// Berechnet den benötigten Pitch-Winkel zum Spieler.
    /// Positiv = nach oben, Negativ = nach unten.
    /// </summary>
    private float CalculateTargetPitch()
    {
        if (playerTransform == null || aimBone == null) return 0f;

        // Richtung vom Bone zum Spieler (Brusthöhe)
        Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
        Vector3 toTarget = targetPoint - aimBone.position;

        // Horizontale Distanz (XZ-Ebene)
        float horizontalDistance = new Vector2(toTarget.x, toTarget.z).magnitude;
        
        // Vertikaler Unterschied
        float verticalDifference = toTarget.y;

        // Pitch-Winkel berechnen (Arctan von vertikal/horizontal)
        float pitchAngle = Mathf.Atan2(verticalDifference, horizontalDistance) * Mathf.Rad2Deg;

        // Invertieren falls nötig
        if (invertPitch)
            pitchAngle = -pitchAngle;

        // Auf erlaubten Bereich begrenzen
        pitchAngle = Mathf.Clamp(pitchAngle, -maxPitchDown, maxPitchUp);

        return pitchAngle;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region State Management
    // ════════════════════════════════════════════════════════════════════════

    public void ChangeState(INpcState<SoldierNpc> newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Combat
    // ════════════════════════════════════════════════════════════════════════

    public bool IsInShootingRange()
    {
        float dist = DistanceToTarget;
        return dist >= minShootingRange && dist <= maxShootingRange;
    }

    /// <summary>
    /// Prüft ob der Soldier freie Sicht zum Spieler hat.
    /// Nutzt Raycast von der Mündung zur Spieler-Brust.
    /// </summary>
    public bool HasLineOfSight()
    {
        if (playerTransform == null) return false;
        
        // Fallback wenn kein muzzlePoint gesetzt
        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.2f;
        Vector3 targetPoint = TargetPosition + Vector3.up * 1f; // Brusthöhe des Spielers
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        // Raycast: Trifft er etwas auf dem Weg zum Spieler?
        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, lineOfSightMask))
        {
            // Freie Sicht nur wenn wir den Spieler direkt treffen
            return hit.collider.CompareTag("Player");
        }

        // Nichts in der Maske getroffen = freie Sicht
        return true;
    }

    /// <summary>
    /// Prüft ob der Soldier schießen kann (in Reichweite UND freie Sicht).
    /// </summary>
    public bool CanShoot()
    {
        return IsInShootingRange() && HasLineOfSight();
    }

    public void FireShot()
    {
        if (muzzlePoint == null || bulletPrefab == null) return;

        Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
        Vector3 direction = (targetPoint - muzzlePoint.position).normalized;
        direction = ApplySpread(direction);

        var bullet = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.identity);
        if (bullet != null)
            bullet.Initialize(direction, damagePerShot, transform, lineOfSightMask);

        if (animator != null)
            animator.SetTrigger("Fire");
        
        PlaySound(fireSound);
        
        if (muzzleFlash != null)
            muzzleFlash.Play();
    }

    private Vector3 ApplySpread(Vector3 direction)
    {
        float spread = Random.value <= baseAccuracy 
            ? accuracySpreadAngle * 0.2f 
            : accuracySpreadAngle;

        return Quaternion.Euler(
            Random.Range(-spread, spread),
            Random.Range(-spread, spread),
            0
        ) * direction;
    }

    public void PlayReloadSound() => PlaySound(reloadSound);

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Helpers für States
    // ════════════════════════════════════════════════════════════════════════

    public new void MoveTowardTarget(float speed = 1f) => base.MoveTowardTarget(speed);
    public void MoveToward(Vector3 position, float speed = 1f) => base.MoveToward(position, speed);
    public new void StopMovement() => base.StopMovement();
    public new void RotateTowardTarget() => base.RotateTowardTarget();
    public new void SetStateTimer(float t) => base.SetStateTimer(t);
    public new bool UpdateStateTimer() => base.UpdateStateTimer();
    public new Vector3 GetDirectionToTarget() => base.GetDirectionToTarget();

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // Schussreichweiten
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, minShootingRange);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, preferredRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxShootingRange);

        // Line of Sight Visualisierung
        if (Application.isPlaying && playerTransform != null)
        {
            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.2f;
            Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
            
            Gizmos.color = HasLineOfSight() ? Color.green : Color.red;
            Gizmos.DrawLine(origin, targetPoint);
        }

        // Aim Bone Visualisierung
        if (Application.isPlaying && aimBone != null && playerTransform != null)
        {
            // Zeige die Zielrichtung des Aim-Bones
            Vector3 targetPoint = TargetPosition + Vector3.up * 1f;
            Gizmos.color = IsAiming ? Color.cyan : Color.gray;
            Gizmos.DrawLine(aimBone.position, targetPoint);
            Gizmos.DrawWireSphere(aimBone.position, 0.1f);
        }
    }

    #endregion
}
