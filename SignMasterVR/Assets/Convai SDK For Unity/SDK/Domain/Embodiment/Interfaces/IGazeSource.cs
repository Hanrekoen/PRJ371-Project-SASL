using Convai.Domain.Embodiment.Readings;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Publishes the character's current gaze decision to the rest of the embodiment
    ///     stack.
    /// </summary>
    /// <remarks>
    ///     Implemented by the Convai Gaze module's controller and registered on the
    ///     character's embodiment context. Consumers read <see cref="Current" /> each frame;
    ///     they never drive the gaze system through this interface (scripted gaze goes
    ///     through the controller's <c>GazeAt</c> API instead).
    /// </remarks>
    internal interface IGazeSource
    {
        /// <summary>Latest gaze reading for the current frame.</summary>
        GazeReading Current { get; }
    }
}
