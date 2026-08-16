namespace Convai.Domain.Models
{
    /// <summary>
    ///     Describes how stable a transcript update is within the lifetime of a single turn.
    /// </summary>
    public enum TranscriptLifecycle
    {
        /// <summary>
        ///     Text is still changing and may be replaced by future updates.
        /// </summary>
        Streaming,

        /// <summary>
        ///     The visible text is final for the current point in the turn, but the turn is still open.
        /// </summary>
        Stable,

        /// <summary>
        ///     The turn is closed. Future updates must use a different message identifier.
        /// </summary>
        Completed
    }
}
