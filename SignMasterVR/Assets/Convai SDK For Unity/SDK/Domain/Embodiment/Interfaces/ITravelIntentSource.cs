using Convai.Domain.Embodiment.Readings;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Publishes where the character is going to the rest of the embodiment stack, so a peer
    ///     module can behave differently while it travels.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Implemented by <c>Convai.Runtime.Embodiment.ConvaiTravelIntent</c> and registered on
    ///         the character's embodiment context. It lives in Runtime rather than in an animation
    ///         module on purpose: movement is a character-level fact, and Gaze needs it whether or
    ///         not any animation module is installed.
    ///     </para>
    ///     <para>
    ///         Consumers read <see cref="Current" /> once per tick. Absence degrades to a single null
    ///         check with no behavior change — the character keeps whatever gaze it had before travel
    ///         was ever a concept (mirrors <see cref="IExertionSource" />).
    ///     </para>
    /// </remarks>
    internal interface ITravelIntentSource
    {
        /// <summary>
        ///     This frame's travel reading. Check <see cref="TravelIntent.IsTraveling" /> before
        ///     reading anything else — the other members are undefined while it is false.
        /// </summary>
        TravelIntent Current { get; }
    }
}
