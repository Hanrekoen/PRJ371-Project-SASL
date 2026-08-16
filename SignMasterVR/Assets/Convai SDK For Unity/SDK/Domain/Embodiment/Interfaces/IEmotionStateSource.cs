using Convai.Domain.Embodiment.Readings;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Authoritative view of the character's current emotion state.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Implemented by <c>Convai.Modules.Emotion</c>. The source is the single point
    ///         of truth for emotion scores; other modules MUST NOT re-derive emotion from
    ///         <c>IEventHub</c> because that bypasses smoothing, micro-expressions, and
    ///         taxonomy resolution.
    ///     </para>
    /// </remarks>
    internal interface IEmotionStateSource
    {
        /// <summary>Latest composed emotion reading sampled at the start of the current frame.</summary>
        EmotionReading Current { get; }
    }
}
