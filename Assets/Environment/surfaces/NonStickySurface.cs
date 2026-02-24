using UnityEngine;

/// <summary>
/// Explicit marker for surfaces that are NOT sticky.
/// 
/// The player CANNOT stick to these surfaces and does NOT recharge dash charges.
/// This component is optional — any surface without StickySurface is already
/// treated as non-sticky. Use this for:
///   - Clarity in the Inspector (explicit "this is intentionally non-sticky")
///   - Future extensibility (e.g. slide-off effects, custom landing behavior)
/// 
/// When a dash ends on a NonStickySurface, a Debug.Log is fired as a hook
/// for future systems (e.g. slide, bounce, damage).
/// </summary>
public class NonStickySurface : MonoBehaviour
{
    // Currently a pure marker component.
    // Future extensions could go here, e.g.:
    //   - bool dealsDamageOnLanding;
    //   - float slideForce;
    //   - bool bouncePlayer;
}
