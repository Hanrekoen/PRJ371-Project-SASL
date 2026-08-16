using Convai.Domain.Models;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Callback interface for receiving player transcription and speaking state events.
    ///     Supports multi-user speaker attribution via <see cref="SpeakerInfo" />.
    /// </summary>
    public interface IConvaiPlayerEvents
    {
        /// <summary>
        ///     Gets called when a transcription is received.
        /// </summary>
        /// <param name="transcript">Transcribed text.</param>
        /// <param name="transcriptionPhase">Transcription phase associated with the update.</param>
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase);

        /// <summary>
        ///     Gets called when a transcription is received with speaker attribution.
        ///     Used for multi-user scenarios where speaker identity is known.
        /// </summary>
        /// <param name="transcript">Transcribed text.</param>
        /// <param name="transcriptionPhase">Transcription phase associated with the update.</param>
        /// <param name="speakerInfo">Speaker attribution data from the backend.</param>
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
            SpeakerInfo speakerInfo);

        /// <summary>
        ///     Handles the event triggered when speaking starts.
        /// </summary>
        /// <param name="sessionId">Unique identifier for the speaking session.</param>
        public void OnPlayerStartedSpeaking(string sessionId);

        /// <summary>
        ///     Handles the event triggered when speaking has stopped.
        /// </summary>
        /// <param name="sessionId">Unique identifier for the speaking session.</param>
        /// <param name="didProduceFinalTranscript">Whether the session produced a final transcript.</param>
        public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript);
    }
}
