using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class DashTrackingTurret : MonoBehaviour, INpcInteraction
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Enums & State
    // ────────────────────────────────────────────────────────────────────────────────

    private enum TurretState
    {
        Idle,
        Tracking,
        Charging,
        Firing,
        DelayedFire,
        StunnedEmbedded,
        StunnedResidual
    }

    private TurretState currentState = TurretState.Idle;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – References
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private FPSPlayerController player;
    [SerializeField] private Transform barrelTransform;
    [SerializeField] private Transform firePoint;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Health & Damage
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Health Settings")]
    [SerializeField] private int maxHP = 300;
    [SerializeField] private int meleeDamageReceived = 25;

    private int currentHP;
    
    // Pending throw damage
    private int pendingThrowDamage;
    private bool hasPendingThrowDamage;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Stun
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Stun Settings")]
    [SerializeField] private float residualStunDuration = 2f;
    [SerializeField] private Color embeddedStunColor = Color.magenta;
    [SerializeField] private Color stunnedColor = Color.gray;
    [SerializeField] private Color chargingColor = Color.yellow;

    private float residualStunTimer;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Laser
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Laser Settings")]
    [SerializeField] private float laserDuration = 0.5f;
    [SerializeField] private float laserStartWidth = 0.1f;
    [SerializeField] private float laserEndWidth = 0.5f;
    [SerializeField] private int laserDamage = 50;
    [SerializeField] private LayerMask laserHitMask;

    private LineRenderer lineRenderer;
    private float firingProgress;
    private bool hasLineOfSight;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Events
    // ────────────────────────────────────────────────────────────────────────────────

    public delegate void TurretDestroyedHandler();
    public event TurretDestroyedHandler OnTurretDestroyed;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        currentHP = maxHP;
    }

    private void Start()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<FPSPlayerController>();
        }
        EnterIdle();
    }

    private void Update()
    {
        UpdateBarrelRotation();
        UpdateState();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region State Machine
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateState()
    {
        bool playerIsDashing = player != null && player.IsDashing();

        switch (currentState)
        {
            case TurretState.Idle:
                if (playerIsDashing && hasLineOfSight)
                    EnterTracking();
                break;

            case TurretState.Tracking:
                if (!playerIsDashing)
                    EnterIdle();
                else if (hasLineOfSight)
                    EnterFiring();
                break;

            case TurretState.DelayedFire:
                if (!playerIsDashing)
                    EnterIdle();
                else if (playerIsDashing && hasLineOfSight)
                    EnterFiring();
                break;

            case TurretState.Firing:
                firingProgress += Time.deltaTime;
                float t = firingProgress / laserDuration;
                float width = Mathf.Lerp(laserStartWidth, laserEndWidth, t);

                lineRenderer.startWidth = width;
                lineRenderer.endWidth = width;

                if (firingProgress >= laserDuration)
                    EnterIdle();
                break;

            case TurretState.StunnedEmbedded:
                float pulse = Mathf.PingPong(Time.time * 3f, 1f);
                Color col = Color.Lerp(embeddedStunColor, Color.white, pulse * 0.3f);
                lineRenderer.startColor = lineRenderer.endColor = col;
                break;

            case TurretState.StunnedResidual:
                residualStunTimer -= Time.deltaTime;

                float progress = residualStunTimer / residualStunDuration;
                float resPulse = Mathf.PingPong(Time.time * 2f, 1f);
                Color resCol = Color.Lerp(stunnedColor, chargingColor, resPulse * 0.5f);
                lineRenderer.startColor = lineRenderer.endColor = resCol;

                if (residualStunTimer <= 0f)
                {
                    ApplyPendingThrowDamage();
                    if (currentHP > 0)
                        EnterIdle();
                }
                break;
        }
    }

    private void EnterIdle()
    {
        currentState = TurretState.Idle;
        lineRenderer.enabled = false;
    }

    private void EnterTracking()
    {
        currentState = TurretState.Tracking;
        lineRenderer.enabled = true;
    }

    private void EnterFiring()
    {
        currentState = TurretState.Firing;
        firingProgress = 0f;
        lineRenderer.enabled = true;
        FireLaser();
    }

    private void EnterStunnedEmbedded()
    {
        currentState = TurretState.StunnedEmbedded;
        lineRenderer.enabled = true;
        lineRenderer.startColor = lineRenderer.endColor = embeddedStunColor;
    }

    private void EnterStunnedResidual()
    {
        currentState = TurretState.StunnedResidual;
        residualStunTimer = residualStunDuration;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Aiming & Visuals
    // ────────────────────────────────────────────────────────────────────────────────

    private void UpdateBarrelRotation()
    {
        if (currentState is TurretState.Idle or TurretState.StunnedEmbedded or TurretState.StunnedResidual)
            return;

        if (player == null || barrelTransform == null) return;

        Vector3 targetPos = player.transform.position;
        Vector3 direction = (targetPos - barrelTransform.position).normalized;

        barrelTransform.rotation = Quaternion.LookRotation(direction);

        hasLineOfSight = !Physics.Linecast(firePoint.position, targetPos, laserHitMask);
    }

    private void FireLaser()
    {
        if (firePoint == null || player == null) return;

        Vector3 direction = (player.transform.position - firePoint.position).normalized;
        Vector3 endPoint;

        if (Physics.Raycast(firePoint.position, direction, out RaycastHit hit, 1000f, laserHitMask))
        {
            endPoint = hit.point;

            if (hit.collider.TryGetComponent<FPSPlayerController>(out var hitPlayer))
            {
                hitPlayer.TakeDamage(laserDamage);
            }
        }
        else
        {
            endPoint = firePoint.position + direction * 1000f;
        }

        lineRenderer.SetPosition(0, firePoint.position);
        lineRenderer.SetPosition(1, endPoint);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Damage & Interaction (INpcInteraction)
    // ────────────────────────────────────────────────────────────────────────────────

    public void OnMeeleDamage(int amount)
    {
        currentHP -= meleeDamageReceived;
        if (currentHP <= 0) DestroyTurret();
    }

    public void OnThrowStun(float duration, int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        // Store pending damage
        pendingThrowDamage = damage;
        hasPendingThrowDamage = true;
        residualStunDuration = duration;
        
        EnterStunnedEmbedded();
    }

    public void OnSwordRemoved()
    {
        if (currentState == TurretState.StunnedEmbedded)
        {
            EnterStunnedResidual();
        }
    }

    private void ApplyPendingThrowDamage()
    {
        if (!hasPendingThrowDamage) return;
        
        hasPendingThrowDamage = false;
        currentHP -= pendingThrowDamage;
        
        Debug.Log($"[DashTrackingTurret] Applied throw damage: {pendingThrowDamage}, HP: {currentHP}/{maxHP}");
        
        if (currentHP <= 0) DestroyTurret();
    }

    private void DestroyTurret()
    {
        OnTurretDestroyed?.Invoke();
        Destroy(gameObject);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public Status Queries
    // ────────────────────────────────────────────────────────────────────────────────

    public float GetHPPercent() => (float)currentHP / maxHP;
    public int GetCurrentHP() => currentHP;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Gizmos (Debug Visualization)
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (player == null) return;

        Gizmos.color = hasLineOfSight ? Color.green : Color.red;
        Gizmos.DrawLine(firePoint != null ? firePoint.position : transform.position, player.transform.position);
    }

    #endregion
}