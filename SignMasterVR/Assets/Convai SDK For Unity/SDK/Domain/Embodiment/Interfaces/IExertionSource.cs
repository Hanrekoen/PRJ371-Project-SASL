namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Publishes the character's current physical exertion from locomotion effort to the
    ///     rest of the embodiment stack.
    /// </summary>
    /// <remarks>
    ///     Implemented by the Convai Body Animation module's controller and registered on the
    ///     character's embodiment context (mirrors <see cref="IGazeSource" />). Consumers (e.g.
    ///     Body Language's breathing director) read <see cref="Exertion01" /> each tick to fold
    ///     effort into their own output; absence degrades to a single null check with no
    ///     behavior change (multiplier stays at the identity value).
    /// </remarks>
    internal interface IExertionSource
    {
        /// <summary>
        ///     Current exertion, 0 (rested) .. 1 (full sustained run effort). Rises while moving
        ///     at jog/run pace and settles back toward 0 at rest or while walking.
        /// </summary>
        float Exertion01 { get; }
    }
}
