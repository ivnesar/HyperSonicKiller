using UnityEngine;

/// <summary>
/// Visualisiert das Dash-Ziel: Platziert das DashDestination GO am Punkt,
/// an dem der Player mit LMB hindasht, und richtet dessen +Y Achse an der
/// Oberflächennormale des getroffenen Faces aus.
///
/// Der Marker wird nur für Attack Dashes angezeigt, die tatsächlich eine
/// Oberfläche treffen (Open-Air-Dashes zeigen keinen Marker).
///
/// Beim Start wird das GO entparentet, damit es beim Dashen fest in der
/// Welt stehen bleibt und nicht mit dem Player mitbewegt wird.
///
/// Auf das Player GameObject setzen (gleiche Ebene wie PlayerDash).
/// </summary>
[RequireComponent(typeof(PlayerCore))]
[RequireComponent(typeof(PlayerDash))]
public class PlayerDashDestinationMarker : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    #region Inspector Settings
    // ════════════════════════════════════════════════════════════════════════

    [Header("Marker Reference")]
    [Tooltip("Das DashDestination GameObject aus dem Player-Prefab. " +
             "Wird beim Start entparentet und bleibt dann fest in der Welt.")]
    [SerializeField] private GameObject destinationMarker;

    [Header("Placement")]
    [Tooltip("Abstand des Markers von der Oberfläche entlang der Normale (in Metern). " +
             "Positive Werte schieben den Marker von der Fläche weg, negative leicht hinein. " +
             "Nützlich um Z-Fighting zu vermeiden oder den Marker bündig an der Fläche zu platzieren.")]
    [SerializeField] private float surfaceOffset = 0f;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Runtime State
    // ════════════════════════════════════════════════════════════════════════

    private PlayerCore core;
    private PlayerDash dash;

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        core = GetComponent<PlayerCore>();
        dash = GetComponent<PlayerDash>();
    }

    private void Start()
    {
        if (destinationMarker == null)
        {
            Debug.LogError("[PlayerDashDestinationMarker] destinationMarker is not assigned!");
            enabled = false;
            return;
        }

        // Vom Player entparenten, damit der Marker fest in der Welt bleibt
        // und nicht mit dem Player mitbewegt wird.
        destinationMarker.transform.SetParent(null, worldPositionStays: true);

        // Initial deaktivieren
        destinationMarker.SetActive(false);

        // Auf Dash-Events registrieren
        dash.OnDashStarted += HandleDashStarted;

        // State-Wechsel decken ALLE Arten von Dash-Ende ab:
        // normaler Complete, Cancel durch Spieler, Force-Cancel durch äußere Einflüsse, Tod, etc.
        core.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        if (dash != null)
        {
            dash.OnDashStarted -= HandleDashStarted;
        }

        if (core != null)
        {
            core.OnStateChanged -= HandleStateChanged;
        }

        // Marker mit aufräumen, da er nicht mehr Kind des Players ist
        if (destinationMarker != null)
        {
            Destroy(destinationMarker);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Event Handlers
    // ════════════════════════════════════════════════════════════════════════

    private void HandleDashStarted()
    {
        // Nur für Attack Dashes, die wirklich eine Oberfläche treffen.
        // Sword Dash ist ein separater State (DashingToSword) und feuert
        // nicht OnDashStarted, sondern OnSwordDashStarted — also hier egal.
        if (!dash.IsDashing) return;

        // Wenn der Dash keine Oberfläche getroffen hat (Open-Air), keinen Marker zeigen
        if (!dash.DashHitSurface) return;

        ShowMarker();
    }

    private void HandleStateChanged(PlayerCore.PlayerState oldState, PlayerCore.PlayerState newState)
    {
        // Sobald wir den Dashing-State verlassen (aus welchem Grund auch immer:
        // normaler Complete, Cancel durch Spieler, Force-Cancel, Tod, ...),
        // Marker ausblenden — das Ziel ist nicht mehr aktiv.
        if (oldState == PlayerCore.PlayerState.Dashing)
        {
            HideMarker();
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    #region Marker Logic
    // ════════════════════════════════════════════════════════════════════════

    private void ShowMarker()
    {
        Vector3 surfaceNormal = dash.StuckSurfaceNormal;

        // Offset entlang der Oberflächennormale anwenden.
        // Hinweis: dash.DashTargetPosition enthält bereits den internen wallStickOffset
        // (damit der Player nicht in der Wand landet). Der hier eingestellte
        // surfaceOffset ist rein für die Marker-Platzierung additiv.
        Vector3 targetPosition = dash.DashTargetPosition + surfaceNormal * surfaceOffset;

        // +Y des Markers soll an der Oberflächennormale ausgerichtet sein.
        // FromToRotation rotiert die Weltachse Y auf den normal-Vektor.
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);

        destinationMarker.transform.SetPositionAndRotation(targetPosition, rotation);
        destinationMarker.SetActive(true);
    }

    private void HideMarker()
    {
        if (destinationMarker != null && destinationMarker.activeSelf)
        {
            destinationMarker.SetActive(false);
        }
    }

    #endregion
}
