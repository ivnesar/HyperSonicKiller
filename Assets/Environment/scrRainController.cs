using UnityEngine;

public class RainFallSpeedController : MonoBehaviour
{
    [Header("Rain Speed Settings")]
    [Range(0.1f, 100.0f)]
    public float baseRainFallSpeed = 10.0f;

    [Range(0.01f, 1.0f)]
    public float speedMultiplier = 1.0f;

    [Header("Particle Lifetime Settings")]
    [Range(0.1f, 10.0f)]
    public float baseParticleLifetime = 3.0f;

    private ParticleSystem rainParticleSystem;

    void Awake()
    {
        rainParticleSystem = GetComponent<ParticleSystem>();

        if (rainParticleSystem == null)
        {
            Debug.LogError("RainFallSpeedController: No ParticleSystem found on this GameObject.", this);
            enabled = false;
        }
    }

    void Update()
    {
        if (rainParticleSystem == null) return;

        float effectiveFallSpeed = baseRainFallSpeed * speedMultiplier;

        float adjustedParticleLifetime = baseParticleLifetime / speedMultiplier;

        var mainModule = rainParticleSystem.main;

        mainModule.startSpeed = effectiveFallSpeed;

        mainModule.startLifetime = adjustedParticleLifetime;
    }

    public void SetBaseFallSpeed(float newSpeed)
    {
        baseRainFallSpeed = newSpeed;
    }

    public void SetSpeedMultiplier(float newMultiplier)
    {
        speedMultiplier = Mathf.Clamp(newMultiplier, 0.01f, 1.0f);
    }

    public void SetBaseParticleLifetime(float newLifetime)
    {
        baseParticleLifetime = newLifetime;
    }
}