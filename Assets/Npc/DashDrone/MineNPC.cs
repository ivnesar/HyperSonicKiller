using UnityEngine;
using System.Collections;

public class MineNPC : MonoBehaviour, INpcInteraction
{
    // ────────────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private FPSPlayerController playerController;
    [SerializeField] private MeshRenderer mineRenderer;
    [SerializeField] private AudioSource audioSource;

    [Header("Detection")]
    [SerializeField] private float detectionRadius = 5f;
    [SerializeField] private float dashCancelDelay = 0.2f;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.yellow;
    [SerializeField] private Color armedColor = Color.red;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Events
    // ────────────────────────────────────────────────────────────────────────────────

    public delegate void DashMineDestroyedHandler();
    public event DashMineDestroyedHandler OnDashMineDestroyed;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Runtime State
    // ────────────────────────────────────────────────────────────────────────────────

    private bool playerInRange;
    private bool isDashDisabled;
    private Coroutine dashCancelCoroutine;

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ────────────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (playerController == null)
        {
            playerController = FindFirstObjectByType<FPSPlayerController>();
        }
        SetMineColor(normalColor);
    }

    private void Update()
    {
        if (playerController == null) return;

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
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────────────
    #region Detection Logic
    // ────────────────────────────────────────────────────────────────────────────────

    private void OnPlayerEnterRange()
    {
        SetMineColor(armedColor);

        if (playerController.IsDashing())
        {
            if (dashCancelCoroutine != null)
                StopCoroutine(dashCancelCoroutine);

            dashCancelCoroutine = StartCoroutine(DelayedDashCancel());
        }
        else
        {
            playerController.DisableDash();
            isDashDisabled = true;
        }
    }

    private void OnPlayerExitRange()
    {
        SetMineColor(normalColor);

        if (dashCancelCoroutine != null)
        {
            StopCoroutine(dashCancelCoroutine);
            dashCancelCoroutine = null;
        }

        if (isDashDisabled)
        {
            playerController.EnableDash();
            isDashDisabled = false;
        }
    }

    private IEnumerator DelayedDashCancel()
    {
        yield return new WaitForSeconds(dashCancelDelay);

        if (playerInRange && playerController != null)
        {
            playerController.CancelDash(true);
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
        DestroyThis();
    }

    public void OnThrowStun(float duration, int damage, Vector3 swordDirection, Vector3 hitPoint)
    {
        // Mine is instantly destroyed by thrown sword - no delayed damage needed
        DestroyThis();
    }

    public void OnSwordRemoved()
    {
        // Nothing to do - mine is already destroyed
    }

    private void DestroyThis()
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
            playerController.EnableDash();
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