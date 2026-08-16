using Convai.Domain.Embodiment.Readings;

namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Character-scoped, read-only co-speech performance plan shared by body animation,
    ///     body language, gaze, and facial-expression modules.
    /// </summary>
    /// <remarks>
    ///     Implementations publish immutable snapshots. Consumers poll once in their normal
    ///     embodiment tick and compare <see cref="CoSpeechPerformanceReading.GestureSequence" />
    ///     to detect a new discrete request. Absence is a supported configuration: modules
    ///     must retain their existing speech-energy behavior when no source is registered.
    /// </remarks>
    internal interface ICoSpeechPerformanceSource
    {
        CoSpeechPerformanceReading Current { get; }
    }
}
