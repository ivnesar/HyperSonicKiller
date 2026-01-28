using UnityEngine;
using System.Collections;

public class MineNPC : MonoBehaviour, INpcInteraction
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Detection & Timing
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Detection Settings")]
    [SerializeField] private float detectionRadius = 5f;
    [SerializeField] private float dashCancelDelay = 0.1f;          // Unscaled time
    [SerializeField] private LayerMask playerLayer;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Visual & Audio Feedback
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Visual Feedback")]
    [SerializeField] private GameObject visualIndicator;            // Optional: effect when armed
    [SerializeField] private Color normalColor = Color.yellow;
    [SerializeField] private Color armedColor = Color.red;
    [SerializeField] private Renderer mineRenderer;

    [Header("Audio (Optional)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip armSound;
    [SerializeField] private AudioClip disarmSound;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields – Debug
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime Variables
    // ────────────────────────────────────────────────────────────────────────────────

    private FPSPlayerController playerController;
    private bool playerInRange = false;
    private bool isDashDisabled = false;
    private Coroutine dashCancelCoroutine;
    private float lastCheckTime;
    private const float CHECK_INTERVAL = 0.1f;                      // Check interval for performance

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        FindPlayer();
        SetupVisuals();
    }

    private void Update()
    {
        if (playerController == null) return;

        // Throttle expensive checks
        if (Time.unscaledTime - lastCheckTime < CHECK_INTERVAL) return;
        lastCheckTime = Time.unscaledTime;

        CheckPlayerProximity();
    }

    private void OnDestroy()
    {
        // Safety cleanup: re-enable dash if mine is destroyed
        if (isDashDisabled && playerController != null)
        {
            playerController.DisableDash(true);
        }
    }

    #endregion

    
    // ────────────────────────────────────────────────────────────────────────────────
    #region Events
    // ────────────────────────────────────────────────────────────────────────────────

    public delegate void DashMineDestroyedHandler();
    public event DashMineDestroyedHandler OnDashMineDestroyed;

    #endregion
    
    
    // ────────────────────────────────────────────────────────────────────────────────
    #region Player Detection Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private void FindPlayer()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null)
        {
            Debug.LogError("MineNPC: No GameObject with 'Player' tag found!");
            return;
        }

        playerController = playerObj.GetComponent<FPSPlayerController>();
        if (playerController == null)
        {
            Debug.LogError("MineNPC: Player found but FPSPlayerController component missing!");
        }
    }

    private void SetupVisuals()
    {
        if (mineRenderer == null)
        {
            mineRenderer = GetComponent<Renderer>();
        }

        if (mineRenderer != null)
        {
            SetMineColor(normalColor);
        }

        if (visualIndicator != null)
        {
            visualIndicator.SetActive(false);
        }
    }

    private void CheckPlayerProximity()
    {
        float distance = Vector3.Distance(transform.position, playerController.transform.position);
        bool wasInRange = playerInRange;
        playerInRange = distance <= detectionRadius;

        if (playerInRange && !wasInRange)
        {
            OnPlayerEnterRange();
        }
        else if (!playerInRange && wasInRange)
        {
            OnPlayerExitRange();
        }
        else if (playerInRange)
        {
            CheckDashState();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Range Enter / Exit Handlers
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnPlayerEnterRange()
    {
        Debug.Log($"MineNPC: Player entered range (Distance: {Vector3.Distance(transform.position, playerController.transform.position):F2}m)");

        if (!isDashDisabled)
        {
            playerController.DisableDash(true);
            isDashDisabled = true;

            SetMineColor(armedColor);
            if (visualIndicator != null) visualIndicator.SetActive(true);
            PlaySound(armSound);
        }

        if (playerController.GetCurrentState() == FPSPlayerController.PlayerState.Dashing)
        {
            StartDashCancelCountdown();
        }
    }

    private void OnPlayerExitRange()
    {
        Debug.Log("MineNPC: Player exited range");

        if (isDashDisabled)
        {
            playerController.DisableDash(true);
            isDashDisabled = false;

            SetMineColor(normalColor);
            if (visualIndicator != null) visualIndicator.SetActive(false);
            PlaySound(disarmSound);
        }

        if (dashCancelCoroutine != null)
        {
            StopCoroutine(dashCancelCoroutine);
            dashCancelCoroutine = null;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Dash Cancellation Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private void CheckDashState()
    {
        if (playerController.GetCurrentState() == FPSPlayerController.PlayerState.Dashing)
        {
            if (dashCancelCoroutine == null)
            {
                StartDashCancelCountdown();
            }
        }
        else
        {
            if (dashCancelCoroutine != null)
            {
                StopCoroutine(dashCancelCoroutine);
                dashCancelCoroutine = null;
            }
        }
    }

    private void StartDashCancelCountdown()
    {
        if (dashCancelCoroutine != null) return;
        dashCancelCoroutine = StartCoroutine(DashCancelCoroutine());
    }

    private IEnumerator DashCancelCoroutine()
    {
        Debug.Log($"MineNPC: Dash detected! Cancelling in {dashCancelDelay}s (unscaled)");

        float elapsed = 0f;
        while (elapsed < dashCancelDelay)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (playerInRange &&
            (playerController.GetCurrentState() == FPSPlayerController.PlayerState.Dashing ||
             playerController.GetCurrentState() == FPSPlayerController.PlayerState.StuckToSurface))
        {
            Debug.Log("MineNPC: Cancelling player dash!");
            playerController.CancelDash(true);
            // ← Here you can add feedback: particles, sound, screen shake, etc.
        }

        dashCancelCoroutine = null;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Visual & Audio Helpers
    // ────────────────────────────────────────────────────────────────────────────────

    private void SetMineColor(Color color)
    {
        if (mineRenderer != null && mineRenderer.material != null)
        {
            mineRenderer.material.color = color;
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region INpcInteraction Implementation
    // ────────────────────────────────────────────────────────────────────────────────

    public void OnMeeleDamage(int amount)
    {
        DestoryThis();
    }

    public void OnThrowStun(float duration)
    {
        DestoryThis();
    }

    public void OnSwordRemoved()
    {
        
    }

    public void OnThrowDamage(int amount, Vector3 swordDirection, Vector3 hitPoint)
    {
        
    }


    private void DestoryThis()
    {
        OnDashMineDestroyed?.Invoke();
        Destroy(gameObject);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Public API (External Control)
    // ────────────────────────────────────────────────────────────────────────────────

    public void TriggerMine()
    {
        if (playerController != null && playerInRange)
        {
            playerController.CancelDash(true);
        }
    }

    public void DisableMine()
    {
        enabled = false;
        if (isDashDisabled && playerController != null)
        {
            playerController.DisableDash(true);
            isDashDisabled = false;
        }
        SetMineColor(Color.gray);
    }

    public void EnableMine()
    {
        enabled = true;
        SetMineColor(normalColor);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Gizmos
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        Gizmos.color = playerInRange ? armedColor : normalColor;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        if (playerInRange && playerController != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, playerController.transform.position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
    }

    #endregion
}