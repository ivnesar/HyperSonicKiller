using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Debug-Script zum Ändern der Panini Projection Distance zur Laufzeit.
/// Tasten 1-4 setzen vordefinierte Werte.
/// 
/// Setup: Dieses Script auf das "Global Volume" GameObject im Player-Prefab legen.
/// Es holt sich die Volume-Komponente automatisch vom selben GameObject.
/// 
/// WICHTIG: Das Script erstellt eine Runtime-Kopie des Volume Profiles,
/// damit das Original-Asset im Editor nicht überschrieben wird.
/// </summary>
[RequireComponent(typeof(Volume))]
public class PaniniProjectionDebug : MonoBehaviour
{
    private PaniniProjection paniniProjection;
    private VolumeProfile runtimeProfile;

    private void Start()
    {
        Volume volume = GetComponent<Volume>();

        // Runtime-Kopie des Profiles erstellen, damit das Asset geschützt bleibt
        runtimeProfile = Instantiate(volume.sharedProfile);
        volume.profile = runtimeProfile;

        // Panini Projection aus dem kopierten Profile holen
        if (runtimeProfile.TryGet(out PaniniProjection panini))
        {
            paniniProjection = panini;
            Debug.Log($"[PaniniDebug] Panini Projection gefunden. Aktuelle Distance: {paniniProjection.distance.value}");
        }
        else
        {
            Debug.LogError("[PaniniDebug] Keine Panini Projection im Volume Profile gefunden! Bitte als Override hinzufügen.");
            enabled = false;
        }
    }

    private void Update()
    {
        if (paniniProjection == null) return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SetDistance(0f);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SetDistance(0.33f);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SetDistance(0.66f);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SetDistance(1f);
        }
    }

    private void SetDistance(float value)
    {
        paniniProjection.distance.value = value;
        Debug.Log($"[PaniniDebug] Panini Distance → {value}");
    }

    private void OnDestroy()
    {
        // Runtime-Kopie aufräumen, um Memory Leaks zu vermeiden
        if (runtimeProfile != null)
        {
            Destroy(runtimeProfile);
        }
    }
}
