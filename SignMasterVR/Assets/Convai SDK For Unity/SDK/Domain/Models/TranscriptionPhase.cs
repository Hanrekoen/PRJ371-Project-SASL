namespace Convai.Domain.Models
{
    /// <summary>
    ///     Represents the phase of a player transcription session.
    /// </summary>
    public enum TranscriptionPhase
    {
        /// <summary>
        ///     No transcription is active.
        /// </summary>
        Idle,

        /// <summary>
        ///     The system detected speech onset and is preparing to stream text.
        /// </summary>
        Listening,

        /// <summary>
        ///     The player transcript is streaming; the text is not yet final.
        /// </summary>
        Interim,

        /// <summary>
        ///     Automatic speech recognition produced a final hypothesis.
        /// </summary>
        AsrFinal,

        /// <summary>
        ///     The transcript has been post-processed by the server.
        /// </summary>
        ProcessedFinal,

        /// <summary>
        ///     The transcription session ended.
        /// </summary>
        Completed
    }
}
