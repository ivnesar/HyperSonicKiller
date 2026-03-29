using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

// ════════════════════════════════════════════════════════════════════════════
// BLOOD DECAL POOL - Verwaltet und recycelt Blutfleck-Decals
// ════════════════════════════════════════════════════════════════════════════
//
// Singleton der in der Szene existiert und Decal-Objekte verwaltet.
// Wenn das Maximum erreicht ist, werden die ältesten Decals recycelt.
//
// SETUP:
//   1. Leeres GameObject in der Szene erstellen
//   2. BloodDecalPool-Komponente drauflegen
//   3. maxDecals einstellen (z.B. 30)
//   4. Fertig — RagdollBloodDecals nutzt den Pool automatisch
//
// WARUM EIN POOL?
//   - URP Decal Projectors sind relativ leichtgewichtig, aber bei
//     vielen Kills sammeln sich schnell hunderte Decals an
//   - Der Pool limitiert die Gesamtanzahl und recycelt alte Decals
//   - Keine Destroy/Instantiate-Aufrufe im laufenden Spiel
//
// ════════════════════════════════════════════════════════════════════════════

public class BloodDecalPool : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────
    #region Singleton
    // ────────────────────────────────────────────────────────────────────────

    public static BloodDecalPool Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BloodDecalPool] Doppelter Pool gefunden! " +
                             "Dieser wird zerstört.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Inspector Fields
    // ────────────────────────────────────────────────────────────────────────

    [Header("Pool Settings")]
    [Tooltip("Maximale Anzahl Blutfleck-Decals die gleichzeitig existieren. " +
             "Älteste werden recycelt wenn das Limit erreicht ist.")]
    [SerializeField] private int maxDecals = 30;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Runtime Data
    // ────────────────────────────────────────────────────────────────────────

    // Ring-Buffer: ältestes Decal wird überschrieben
    private List<GameObject> pool = new List<GameObject>();
    private int nextIndex = 0;

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Public API
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gibt ein Decal-GameObject aus dem Pool zurück.
    /// Falls das Limit noch nicht erreicht ist, wird ein neues erzeugt.
    /// Sonst wird das älteste recycelt.
    ///
    /// Das zurückgegebene Objekt ist DEAKTIVIERT — der Aufrufer muss
    /// es nach dem Konfigurieren selbst aktivieren (SetActive(true)).
    /// </summary>
    public GameObject GetDecal()
    {
        GameObject decalObj;

        if (pool.Count < maxDecals)
        {
            // Pool noch nicht voll — neues Objekt erzeugen
            decalObj = CreateNewDecal();
            pool.Add(decalObj);

            if (showDebugInfo)
                Debug.Log($"[BloodDecalPool] Neues Decal erzeugt ({pool.Count}/{maxDecals})");
        }
        else
        {
            // Pool voll — ältestes recyceln
            decalObj = pool[nextIndex];

            if (showDebugInfo)
                Debug.Log($"[BloodDecalPool] Decal recycelt (Index {nextIndex})");

            nextIndex = (nextIndex + 1) % maxDecals;
        }

        // Deaktiviert zurückgeben — Aufrufer konfiguriert und aktiviert
        decalObj.SetActive(false);
        return decalObj;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Internal
    // ────────────────────────────────────────────────────────────────────────

    private GameObject CreateNewDecal()
    {
        GameObject obj = new GameObject("BloodDecal");
        obj.transform.SetParent(transform); // Unter dem Pool-Objekt organisiert

        DecalProjector projector = obj.AddComponent<DecalProjector>();
        projector.scaleMode = DecalScaleMode.InheritFromHierarchy;

        obj.SetActive(false);
        return obj;
    }

    #endregion
}
