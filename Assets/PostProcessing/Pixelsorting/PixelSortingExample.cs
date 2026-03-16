using UnityEngine;

public class PixelSortingExample : MonoBehaviour
{
    public KeyCode triggerKey = KeyCode.Space;
    public float rampSpeed = 2f;

    private float _targetIntensity = 0f;

    private void Update()
    {
        if (PixelSortingSettings.Instance == null) return;

        if (Input.GetKeyDown(triggerKey))
            _targetIntensity = _targetIntensity > 0.5f ? 0f : 1f;

        PixelSortingSettings.Instance.intensity = Mathf.MoveTowards(
            PixelSortingSettings.Instance.intensity,
            _targetIntensity,
            rampSpeed * Time.deltaTime
        );
    }
}
