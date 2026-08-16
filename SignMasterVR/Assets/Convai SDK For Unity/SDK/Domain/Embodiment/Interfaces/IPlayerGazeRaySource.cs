using UnityEngine;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Supplies the world-space ray the player is currently looking along, so a character
    ///     can tell whether the player is looking at it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The default signal is the main camera's forward ray, which works on desktop out
    ///         of the box ("is the character centered on screen"). XR eye tracking plugs in by
    ///         implementing this interface over an OpenXR / vendor eye-tracking adapter in user
    ///         code and registering it — no XR package dependency is taken by the SDK.
    ///     </para>
    ///     <para>
    ///         Return <c>false</c> when no ray is available this frame (tracking lost, headset
    ///         off) so the consumer can fall back to the camera ray.
    ///     </para>
    /// </remarks>
    public interface IPlayerGazeRaySource
    {
        /// <summary>
        ///     Attempts to produce the player's current gaze ray in world space (origin at the
        ///     player's eye, direction along their line of sight).
        /// </summary>
        /// <param name="ray">The resolved gaze ray when the method returns <c>true</c>.</param>
        /// <returns><c>true</c> when a ray is available this frame; otherwise <c>false</c>.</returns>
        bool TryGetPlayerGazeRay(out Ray ray);
    }
}
