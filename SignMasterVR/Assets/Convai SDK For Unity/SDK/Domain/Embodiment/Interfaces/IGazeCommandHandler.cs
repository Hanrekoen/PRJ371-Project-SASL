using UnityEngine;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Contract for the module that owns scripted attention requests beyond a one-shot glance
    ///     (<see cref="IGazeGlanceHandler" />) — sustained gaze holds and prioritized
    ///     points-of-interest requested by Runtime action composites (point-at, present, gaze
    ///     tour, lead-the-way glance-back). Runtime requests attention through this interface
    ///     instead of referencing the gaze assembly directly; when no handler is registered every
    ///     request is a no-op that returns <c>false</c> so the caller can skip the flourish and
    ///     continue the action.
    /// </summary>
    /// <remarks>
    ///     Implemented by the Convai Gaze module's controller and registered on the character's
    ///     embodiment context (mirrors <see cref="ICharacterReorientationHandler" />). Safe to call
    ///     every frame. A higher <paramref name="priority" /> (larger value) request takes the
    ///     focus over a lower one; a request that cannot win priority is rejected (<c>false</c>)
    ///     rather than queued, so a caller can fall back immediately instead of waiting indefinitely.
    /// </remarks>
    /// <remarks>
    ///     <b>World positions only, deliberately.</b> A world position is unambiguous and cannot go
    ///     stale, unlike an opaque tracked-target id that the handler would have to resolve back to a
    ///     <see cref="Transform" /> on every call.
    /// </remarks>
    internal interface IGazeCommandHandler
    {
        /// <summary>
        ///     Requests a sustained gaze hold on <paramref name="worldPosition" />. A duration of
        ///     0 or less holds until <see cref="ReleaseGaze" /> is called explicitly. Returns
        ///     <c>true</c> when the request wins (or already held) the focus at
        ///     <paramref name="priority" />.
        /// </summary>
        bool RequestSustainedGaze(Vector3 worldPosition, float durationSeconds, int priority);

        /// <summary>
        ///     Requests a brief, lower-commitment glance at <paramref name="worldPosition" />
        ///     before gaze returns to whatever it was already doing. Returns <c>true</c> when
        ///     accepted at <paramref name="priority" />.
        /// </summary>
        bool RequestGlance(Vector3 worldPosition, float durationSeconds, int priority);

        /// <summary>
        ///     Releases the caller's currently held sustained gaze or glance (if any), blending
        ///     gaze back to whatever it would otherwise be doing. Safe to call when nothing is held.
        /// </summary>
        void ReleaseGaze();
    }
}
