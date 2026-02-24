using UnityEngine;

/// <summary>
/// Marker component for surfaces that the player can stick to (walls)
/// and recharge dash charges on (walls + floors).
/// 
/// Place this on any GameObject with a collider that should allow:
///   - Wall stick (if the surface normal counts as a wall)
///   - Dash charge recharge (both walls and floors)
/// 
/// GameObjects WITHOUT this component are treated as non-sticky by default.
/// You can also add NonStickySurface for explicit marking + future extensibility.
/// </summary>
public class StickySurface : MonoBehaviour
{
    // Currently a pure marker component.
    // Future extensions could go here, e.g.:
    //   - int chargesGranted = 3;
    //   - bool allowWallStick = true;
    //   - float stickDuration = Mathf.Infinity;
}
