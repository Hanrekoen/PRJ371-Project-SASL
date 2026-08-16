using Convai.Domain.Embodiment.Readings;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Publishes the character's current nonverbal body-language state to the rest of the
    ///     embodiment stack.
    /// </summary>
    /// <remarks>
    ///     Implemented by the Convai Body Language module's controller and registered on the
    ///     character's embodiment context (mirrors <see cref="IGazeSource" />). Consumers read
    ///     <see cref="Current" /> each frame; they never drive the body through this interface —
    ///     scripted requests go through the controller's <c>Nod</c>/<c>PulseGesture</c> API
    ///     instead.
    /// </remarks>
    internal interface IBodyLanguageSource
    {
        /// <summary>Latest body-language reading for the current frame.</summary>
        BodyLanguageReading Current { get; }
    }
}
