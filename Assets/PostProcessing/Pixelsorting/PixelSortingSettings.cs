using UnityEngine;

/// <summary>
/// Controls all Pixel Sorting effect parameters at runtime.
/// Access from any script via PixelSortingSettings.Instance.
/// </summary>
public class PixelSortingSettings : MonoBehaviour
{
    public static PixelSortingSettings Instance { get; private set; }

    [Header("General")]
    [Range(0f, 1f)]
    public float intensity = 1f;

    [Header("Threshold")]
    [Range(0f, 1f)]
    public float thresholdMin = 0.2f;

    [Range(0f, 1f)]
    public float thresholdMax = 0.8f;

    [Header("Sorting")]
    [Range(0f, 360f)]
    public float sortAngle = 0f;

    [Range(1, 64)]
    public int sortPasses = 16;

    [Range(1f, 64f)]
    public float sortStepSize = 1f;

    [Header("Sort Criterion")]
    public SortCriterion sortCriterion = SortCriterion.Luminance;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    public DebugMode debugMode = DebugMode.Off;

    public enum SortCriterion
    {
        Luminance = 0,
        Hue = 1,
        Saturation = 2
    }

    public enum DebugMode
    {
        Off = 0,
        RedTint = 1,
        ThresholdMask = 2
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (enableDebugLogs) Debug.Log("[PixelSort] Settings registered.");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
