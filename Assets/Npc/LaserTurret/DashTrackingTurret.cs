// using UnityEngine;
//
// [RequireComponent(typeof(LineRenderer))]
// public class DashTrackingTurret : MonoBehaviour, INpcInteraction
// {
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Enums & State
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private enum TurretState
//     {
//         Idle,
//         Tracking,
//         Charging,           // currently unused
//         Firing,
//         DelayedFire,
//         StunnedEmbedded,    // sword is stuck → indefinite stun
//         StunnedResidual     // sword removed → temporary stun
//     }
//
//     private TurretState currentState = TurretState.Idle;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Inspector Fields – References
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     [Header("References")]
//     // UPDATED: Using PlayerCore instead of FPSPlayerController
//     [SerializeField] private PlayerCore player;
//     [SerializeField] private Transform barrelTransform;
//     [SerializeField] private Transform firePoint;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Inspector Fields – Health & Damage
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     [Header("Health Settings")]
//     [SerializeField] private int maxHP = 300;
//     [SerializeField] private int meleeDamageReceived = 25;
//     [SerializeField] private int thrownSwordDamageReceived = 50;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Inspector Fields – Tracking & Aiming
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     [Header("Tracking Settings")]
//     [SerializeField] private float rotationSpeedx1000 = 25f;
//     [SerializeField] private LayerMask obstructionMask;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Inspector Fields – Laser Behavior
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     [Header("Laser Settings")]
//     [SerializeField] private float chargeTime = 3f;
//     [SerializeField] private float laserDuration = 0.5f;
//     [SerializeField] private float laserStartWidth = 0.5f;
//     [SerializeField] private float laserEndWidth = 0.05f;
//     [SerializeField] private float delayAfterDashEnd = 0.5f;
//     [SerializeField] private int laserDamage = 25;
//     [SerializeField] private LayerMask damageableMask;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Inspector Fields – Visuals & Feedback
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     [Header("Visual Settings")]
//     [SerializeField] private Color chargingColor = Color.yellow;
//     [SerializeField] private Color firingColor = Color.red;
//     [SerializeField] private Color stunnedColor = Color.blue;
//     [SerializeField] private Color embeddedStunColor = Color.cyan;
//     [SerializeField] private Material laserMaterial;
//
//     [Header("Stun Settings")]
//     [SerializeField] private float residualStunDuration = 3f;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Runtime Variables
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private LineRenderer lineRenderer;
//
//     private Vector3 targetPosition;
//     private Vector3 lastKnownDashPosition;
//
//     private float chargeProgress;
//     private float firingProgress;
//     private float delayTimer;
//     private float residualStunTimer;
//
//     private bool hasLineOfSight;
//     private bool swordEmbedded;
//
//     private int currentHP;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Events
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     public delegate void TurretDestroyedHandler();
//     public event TurretDestroyedHandler OnTurretDestroyed;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Unity Lifecycle
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private void Start()
//     {
//         currentHP = maxHP;
//         lineRenderer = GetComponent<LineRenderer>();
//
//         SetupLineRenderer();
//         FindMissingReferences();
//     }
//
//     private void Update()
//     {
//         if (player == null) return;
//
//         UpdateLineOfSight();
//         UpdateStateMachine();
//         UpdateBarrelRotation();
//     }
//
//     private void LateUpdate()
//     {
//         UpdateLaserVisuals();
//     }
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Initialization & Setup
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private void SetupLineRenderer()
//     {
//         lineRenderer.positionCount = 2;
//         lineRenderer.startWidth = laserStartWidth;
//         lineRenderer.endWidth = laserStartWidth;
//         lineRenderer.material = laserMaterial != null ? laserMaterial : new Material(Shader.Find("Sprites/Default"));
//         lineRenderer.startColor = chargingColor;
//         lineRenderer.endColor = chargingColor;
//         lineRenderer.useWorldSpace = true;
//         lineRenderer.enabled = false;
//     }
//
//     private void FindMissingReferences()
//     {
//         // UPDATED: Find PlayerCore instead of FPSPlayerController
//         if (player == null)
//         {
//             player = FindFirstObjectByType<PlayerCore>();
//             if (player == null) Debug.LogError("DashTrackingTurret: No player found in scene!");
//         }
//
//         if (barrelTransform == null)
//         {
//             barrelTransform = transform;
//             Debug.LogWarning("DashTrackingTurret: No barrel transform assigned → using self");
//         }
//
//         if (firePoint == null)
//             firePoint = barrelTransform;
//     }
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Core Logic – Line of Sight & State Machine
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private void UpdateLineOfSight()
//     {
//         Vector3 direction = player.transform.position - firePoint.position;
//         float distance = direction.magnitude;
//
//         hasLineOfSight = !Physics.Raycast(
//             firePoint.position,
//             direction.normalized,
//             distance,
//             obstructionMask);
//     }
//
//     private void UpdateStateMachine()
//     {
//         // UPDATED: Check player state using PlayerCore
//         bool playerIsDashing = player.CurrentState == PlayerCore.PlayerState.Dashing;
//
//         switch (currentState)
//         {
//             case TurretState.Idle:
//                 if (playerIsDashing && hasLineOfSight)
//                     EnterTracking();
//                 break;
//
//             case TurretState.Tracking:
//                 if (!playerIsDashing)
//                     EnterDelayedFire();
//                 else if (!hasLineOfSight)
//                     EnterIdle();
//                 else
//                 {
//                     TrackPlayer();
//                     chargeProgress += Time.unscaledDeltaTime;
//
//                     if (chargeProgress >= chargeTime)
//                         EnterFiring();
//                 }
//                 break;
//
//             case TurretState.DelayedFire:
//                 delayTimer += Time.deltaTime;
//
//                 if (delayTimer >= delayAfterDashEnd)
//                     EnterFiring();
//                 else if (playerIsDashing && hasLineOfSight)
//                     EnterTracking();
//                 break;
//
//             case TurretState.Firing:
//                 firingProgress += Time.deltaTime;
//                 float t = firingProgress / laserDuration;
//                 float width = Mathf.Lerp(laserStartWidth, laserEndWidth, t);
//
//                 lineRenderer.startWidth = width;
//                 lineRenderer.endWidth = width;
//
//                 if (firingProgress >= laserDuration)
//                     EnterIdle();
//                 break;
//
//             case TurretState.StunnedEmbedded:
//                 // Visual feedback: pulsing color
//                 float pulse = Mathf.PingPong(Time.time * 3f, 1f);
//                 Color col = Color.Lerp(embeddedStunColor, Color.white, pulse * 0.3f);
//                 lineRenderer.startColor = lineRenderer.endColor = col;
//                 break;
//
//             case TurretState.StunnedResidual:
//                 residualStunTimer -= Time.deltaTime;
//
//                 float resPulse = Mathf.PingPong(Time.time * 2f, 1f);
//                 Color resCol = Color.Lerp(stunnedColor, chargingColor, resPulse * 0.5f);
//                 lineRenderer.startColor = lineRenderer.endColor = resCol;
//
//                 if (residualStunTimer <= 0f)
//                     EnterIdle();
//                 break;
//         }
//     }
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Aiming & Visuals
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private void UpdateBarrelRotation()
//     {
//         if (currentState is TurretState.Idle or TurretState.StunnedEmbedded or TurretState.StunnedResidual)
//             return;
//
//         Quaternion targetRot = Quaternion.LookRotation(targetPosition - barrelTransform.position);
//         barrelTransform.rotation = Quaternion.RotateTowards(
//             barrelTransform.rotation,
//             targetRot,
//             rotationSpeedx1000 * 1000f * Time.deltaTime);
//     }
//
//     private void TrackPlayer()
//     {
//         targetPosition = player.transform.position;
//         lastKnownDashPosition = targetPosition;
//     }
//
//     private void UpdateLaserVisuals()
//     {
//         if (!lineRenderer.enabled) return;
//         if (currentState is TurretState.Idle or TurretState.StunnedEmbedded or TurretState.StunnedResidual)
//             return;
//
//         lineRenderer.SetPosition(0, firePoint.position);
//
//         if (currentState == TurretState.Tracking)
//         {
//             Vector3 dir = (targetPosition - firePoint.position).normalized;
//             lineRenderer.SetPosition(1, firePoint.position + dir * 1000f);
//         }
//     }
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region State Transitions
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private void EnterIdle()
//     {
//         currentState = TurretState.Idle;
//         chargeProgress = 0f;
//         firingProgress = 0f;
//         delayTimer = 0f;
//         lineRenderer.enabled = false;
//     }
//
//     private void EnterTracking()
//     {
//         currentState = TurretState.Tracking;
//         chargeProgress = 0f;
//         lineRenderer.enabled = true;
//         lineRenderer.startColor = lineRenderer.endColor = chargingColor;
//         lineRenderer.startWidth = lineRenderer.endWidth = laserStartWidth;
//     }
//
//     private void EnterDelayedFire()
//     {
//         currentState = TurretState.DelayedFire;
//         delayTimer = 0f;
//         targetPosition = lastKnownDashPosition;
//     }
//
//     private void EnterFiring()
//     {
//         currentState = TurretState.Firing;
//         firingProgress = 0f;
//
//         lineRenderer.enabled = true;
//         lineRenderer.startColor = lineRenderer.endColor = firingColor;
//
//         FireLaser();
//     }
//
//     private void EnterStunnedEmbedded()
//     {
//         currentState = TurretState.StunnedEmbedded;
//         swordEmbedded = true;
//
//         lineRenderer.enabled = true;
//         lineRenderer.startColor = lineRenderer.endColor = embeddedStunColor;
//         lineRenderer.startWidth = lineRenderer.endWidth = laserStartWidth * 0.7f;
//
//         // Visual feedback: short downward arc
//         lineRenderer.SetPosition(0, firePoint.position);
//         lineRenderer.SetPosition(1, firePoint.position + Vector3.down * 2f);
//     }
//
//     private void EnterStunnedResidual()
//     {
//         currentState = TurretState.StunnedResidual;
//         swordEmbedded = false;
//         residualStunTimer = residualStunDuration;
//
//         lineRenderer.enabled = true;
//         lineRenderer.startColor = lineRenderer.endColor = stunnedColor;
//         lineRenderer.startWidth = lineRenderer.endWidth = laserStartWidth * 0.5f;
//
//         lineRenderer.SetPosition(0, firePoint.position);
//         lineRenderer.SetPosition(1, firePoint.position + Vector3.down * 1.5f);
//     }
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Laser Firing Logic
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private void FireLaser()
//     {
//         Vector3 direction = (targetPosition - firePoint.position).normalized;
//         RaycastHit hit;
//
//         Vector3 endPoint;
//
//         if (Physics.Raycast(firePoint.position, direction, out hit, Mathf.Infinity, damageableMask))
//         {
//             endPoint = hit.point;
//
//             // UPDATED: Use PlayerCore for damage
//             if (hit.collider.TryGetComponent<PlayerCore>(out var hitPlayer))
//             {
//                 hitPlayer.TakeDamage(laserDamage);
//             }
//         }
//         else
//         {
//             endPoint = firePoint.position + direction * 1000f;
//         }
//
//         lineRenderer.SetPosition(0, firePoint.position);
//         lineRenderer.SetPosition(1, endPoint);
//     }
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Damage & Interaction (INpcInteraction)
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     public void OnMeeleDamage(int amount)
//     {
//         currentHP -= meleeDamageReceived;
//         if (currentHP <= 0) DestroyTurret();
//     }
//
//     public void OnThrowStun(float duration)
//     {
//         EnterStunnedEmbedded();
//     }
//
//     public void OnSwordRemoved()
//     {
//         if (currentState == TurretState.StunnedEmbedded)
//         {
//             EnterStunnedResidual();
//         }
//     }
//
//     public void OnThrowDamage(int amount, Vector3 swordDirection, Vector3 hitPoint)
//     {
//         // Turret doesn't take throw damage
//     }
//
//     private void DestroyTurret()
//     {
//         OnTurretDestroyed?.Invoke();
//         Destroy(gameObject);
//     }
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Public Status Queries
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     public float GetHPPercent() => (float)currentHP / maxHP;
//     public int GetCurrentHP() => currentHP;
//
//     #endregion
//
//     // ────────────────────────────────────────────────────────────────────────────────
//     #region Gizmos (Debug Visualization)
//     // ────────────────────────────────────────────────────────────────────────────────
//
//     private void OnDrawGizmosSelected()
//     {
//         if (player == null) return;
//
//         // Line of sight
//         Gizmos.color = hasLineOfSight ? Color.green : Color.red;
//         Gizmos.DrawLine(firePoint.position, player.transform.position);
//
//         // Target & last known position
//         if (currentState != TurretState.Idle && currentState != TurretState.StunnedEmbedded && currentState != TurretState.StunnedResidual)
//         {
//             Gizmos.color = Color.yellow;
//             Gizmos.DrawWireSphere(targetPosition, 0.3f);
//         }
//
//         if (currentState == TurretState.DelayedFire)
//         {
//             Gizmos.color = Color.magenta;
//             Gizmos.DrawWireSphere(lastKnownDashPosition, 0.5f);
//         }
//
//         // HP bar
//         if (Application.isPlaying)
//         {
//             Vector3 pos = transform.position + Vector3.up * 3f;
//             float pct = GetHPPercent();
//
//             Gizmos.color = Color.red;
//             Gizmos.DrawLine(pos - Vector3.right, pos + Vector3.right);
//
//             Gizmos.color = Color.green;
//             Gizmos.DrawLine(pos - Vector3.right, pos - Vector3.right + Vector3.right * 2f * pct);
//         }
//
//         // Stun indicator
//         if (Application.isPlaying && (currentState is TurretState.StunnedEmbedded or TurretState.StunnedResidual))
//         {
//             Vector3 indPos = transform.position + Vector3.up * 3.5f;
//             Gizmos.color = currentState == TurretState.StunnedEmbedded ? embeddedStunColor : stunnedColor;
//             Gizmos.DrawWireSphere(indPos, 0.3f);
//         }
//     }
//
//     #endregion
// }