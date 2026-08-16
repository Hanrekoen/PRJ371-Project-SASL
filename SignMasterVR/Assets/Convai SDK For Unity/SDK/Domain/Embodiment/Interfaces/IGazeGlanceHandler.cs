using UnityEngine;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for the module that owns scripted glance requests — a brief, low-priority
    ///     look at a point before gaze returns to whatever it was already doing. Other
    ///     modules (e.g. Body Animation, when the character points at something) request a
    ///     glance through this interface instead of referencing the gaze assembly directly; when
    ///     no handler is registered the request is silently skipped.
    /// </summary>
    /// <remarks>
    ///     Implemented by the Convai Gaze module's controller and registered on the character's
    ///     embodiment context (mirrors <see cref="ICharacterReorientationHandler" />). Safe to
    ///     call every frame — a low-priority scripted request that never outranks an explicit
    ///     gaze/eye-contact lock.
    /// </remarks>
    internal interface IGazeGlanceHandler
    {
        /// <summary>
        ///     Requests a brief glance at <paramref name="worldPosition" />, held for
        ///     <paramref name="durationSeconds" /> before gaze returns to its prior target.
        /// </summary>
        /// <param name="worldPosition">World-space point to glance at.</param>
        /// <param name="durationSeconds">Hold duration in seconds.</param>
        void RequestGlance(Vector3 worldPosition, float durationSeconds);
    }
}
