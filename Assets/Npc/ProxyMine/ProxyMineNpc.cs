using UnityEngine;

// ════════════════════════════════════════════════════════════════════════════
// PROXY MINE - Statische Mine die bei Spieler-Nähe explodiert
// ════════════════════════════════════════════════════════════════════════════
//
// Auslöser (jeder startet den Fuse-Timer):
//   1. Spieler betritt Radius + Line-of-Sight
//   2. Schaden nach Sword-Removal + Stun-Ende
//   3. Explosions-Schaden von einer anderen Mine / Quelle
//
// Besonderheiten:
//   - Kein HP-System: jeder Schaden aktiviert den Zünder
//   - Kann durch Sword-Throw gestunnt werden (normales Embed/Remove)
//   - Stun pausiert den Fuse-Timer
//   - Kein NavMeshAgent / Animator nötig auf dem GameObject
//
// ════════════════════════════════════════════════════════════════════════════

public class ProxyMineNpc : NpcBase
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Fields
    // ════════════════════════════════════════════════════════════════════════

    [Header("Mine - Detection")]
    [Tooltip("Radius in dem der Spieler erkannt wird")]
    [SerializeField] private float detectionRadius = 8f;

    [Tooltip("Offset für den Raycast-Start (verhindert Start in Boden/Wand)")]
    [SerializeField] private Vector3 raycastOffset = new Vector3(0f, 0.5f, 0f);

    [Tooltip("LayerMask für LoS-Blockierung (Wände, Boden etc.)")]
    [SerializeField] private LayerMask lineOfSightBlockers;

    [Header("Mine - Trigger")]
    [Tooltip("Verzögerung zwischen Auslösung und Explosion (Sekunden)")]
    [SerializeField] private float fuseTime = 1f;

    [Header("Mine - Audio")]
    [SerializeField] private AudioClip fuseSound;

    [Header("Mine - Spawns")]
    [Tooltip("Explosions-Prefab (braucht ExplosionSphere-Script)")]
    [SerializeField] private GameObject explosionPrefab;

    [Tooltip("Partikel-Prefab (wird parallel gespawnt, zerstört sich selbst)")]
    [SerializeField] private GameObject particlePrefab;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private enum MineState
    {
        Idle,       // Wartet auf Spieler
        Triggered,  // Fuse-Timer läuft
        Exploded    // Hat bereits explodiert
    }

    private MineState mineState = MineState.Idle;
    private float fuseTimer;

    // Merkt sich ob Schaden eingetroffen ist während die Mine gestunnt war.
    // Nach Stun-Ende wird dann der Fuse gestartet.
    private bool damagePendingFuse;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStart()
    {
        behaviorMode = BehaviorMode.Stationary;
    }

    protected override void UpdateBehavior()
    {
        switch (mineState)
        {
            case MineState.Idle:
                CheckForPlayer();
                break;

            case MineState.Triggered:
                UpdateFuseTimer();
                break;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Detection
    // ════════════════════════════════════════════════════════════════════════

    private void CheckForPlayer()
    {
        if (playerTransform == null) return;
        if (DistanceToTarget > detectionRadius) return;
        if (!HasLineOfSight()) return;

        StartFuse();
    }

    private bool HasLineOfSight()
    {
        Vector3 origin = transform.position + raycastOffset;
        Vector3 targetPos = playerTransform.position + Vector3.up * 0.5f;
        Vector3 direction = targetPos - origin;
        float distance = direction.magnitude;

        return !Physics.Raycast(origin, direction.normalized, distance, lineOfSightBlockers);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Fuse Timer
    // ════════════════════════════════════════════════════════════════════════

    private void StartFuse()
    {
        if (mineState != MineState.Idle) return;

        mineState = MineState.Triggered;
        fuseTimer = fuseTime;
        PlaySound(fuseSound);
    }

    private void UpdateFuseTimer()
    {
        if (isStunned) return;

        fuseTimer -= Time.deltaTime;

        if (fuseTimer <= 0f)
        {
            Explode();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Explosion
    // ════════════════════════════════════════════════════════════════════════

    private void Explode()
    {
        mineState = MineState.Exploded;

        Vector3 spawnPos = transform.position;

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, spawnPos, Quaternion.identity);
        }

        if (particlePrefab != null)
        {
            Instantiate(particlePrefab, spawnPos, Quaternion.identity);
        }

        Destroy(gameObject);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Stun
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnStunStart()
    {
        // Fuse-Timer wird automatisch pausiert, da NpcBase bei Stun
        // UpdateBehavior() nicht aufruft.
    }

    protected override void OnStunEnd()
    {
        // Nach Stun-Ende: wenn Schaden aufgelaufen ist → Fuse starten
        if (damagePendingFuse)
        {
            damagePendingFuse = false;
            StartFuse();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Damage
    // ════════════════════════════════════════════════════════════════════════

    // ── Generischer Schaden (z.B. von ExplosionSphere) ──────────────────

    public override void TakeDamage(float damage)
    {
        if (mineState == MineState.Exploded) return;
        TriggerFuseFromDamage();
    }

    public override void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (mineState == MineState.Exploded) return;
        TriggerFuseFromDamage();
    }

    // ── Melee: ignoriert (Mine reagiert nicht auf Schwerthiebe) ──────────

    public override void OnMeleeDamage(int damage) { }

    // ── Bullet: aktiviert Fuse ──────────────────────────────────────────

    public override void OnBulletDamage(int damage, Vector3 bulletDirection, Vector3 hitPoint)
    {
        if (mineState == MineState.Exploded) return;
        TriggerFuseFromDamage();
    }

    // ── Sword Throw: normales NpcBase-Verhalten (Stun + pending damage) ─
    // OnThrownSwordHit, OnSwordEmbedded bleiben von NpcBase.
    // Nach Sword-Removal + Residual-Stun-Ende → OnStunEnd() → StartFuse()

    public override void OnSwordRemoved(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;

        // Schaden merken → nach Stun-Ende wird Fuse gestartet
        if (damage > 0)
        {
            damagePendingFuse = true;
        }

        // Kein pending sword damage (Mine hat keine HP)
        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        stunEndTime = Time.time + residualStunDuration;
    }

    public override void OnSwordDashRemoval(int damage, float residualStunDuration)
    {
        if (!hasSwordEmbedded) return;

        hasSwordEmbedded = false;

        // Bei Dash-Removal: Schaden kommt eigentlich sofort,
        // aber Mine hat keine HP → Fuse merken, startet nach Stun-Ende
        if (damage > 0)
        {
            damagePendingFuse = true;
        }

        hasPendingSwordDamage = false;
        pendingSwordRemovalDamage = 0;

        stunEndTime = Time.time + residualStunDuration;
    }

    // ── Gemeinsame Fuse-Aktivierung durch Schaden ───────────────────────

    private void TriggerFuseFromDamage()
    {
        if (isStunned)
        {
            damagePendingFuse = true;
            return;
        }

        StartFuse();
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region NpcBase Overrides - Death (Mine stirbt nicht normal)
    // ════════════════════════════════════════════════════════════════════════

    protected override void Die()
    {
        // Mine stirbt nicht durch HP-Verlust, sie explodiert.
        if (mineState != MineState.Exploded)
        {
            Explode();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Abstract Implementations
    // ════════════════════════════════════════════════════════════════════════

    public override string GetCurrentStateName() => mineState.ToString();
    public override NpcType GetNpcType() => NpcType.ProxyMine;
    public override int GetStateID() => (int)mineState;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Debug
    // ════════════════════════════════════════════════════════════════════════

    protected override void OnDrawGizmosSelected()
    {
        // Detection Radius
        Gizmos.color = mineState == MineState.Triggered
            ? new Color(1f, 0f, 0f, 0.3f)
            : new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Raycast Origin
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position + raycastOffset, 0.1f);

        // LoS zum Spieler (Laufzeit)
        if (Application.isPlaying && playerTransform != null)
        {
            Vector3 origin = transform.position + raycastOffset;
            Vector3 target = playerTransform.position + Vector3.up * 0.5f;

            bool hasLos = HasLineOfSight();
            Gizmos.color = hasLos ? Color.green : Color.red;
            Gizmos.DrawLine(origin, target);
        }
    }

    #endregion
}
