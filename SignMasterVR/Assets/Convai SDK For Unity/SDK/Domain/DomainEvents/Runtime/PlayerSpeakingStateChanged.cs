using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when the player's speaking state changes (VAD detection).
    ///     Surfaces player speech state via EventHub for UI components to react.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever the player starts or stops speaking,
    ///     based on Voice Activity Detection (VAD) from the server.
    ///     Use EventDeliveryPolicy.MainThread for UI updates.
    /// </remarks>
    public readonly struct PlayerSpeakingStateChanged
    {
        /// <summary>
        ///     The session ID for this speaking session.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        ///     Whether the player is currently speaking.
        /// </summary>
        public bool IsSpeaking { get; }

        /// <summary>
        ///     When the speech state changed (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Creates a new PlayerSpeakingStateChanged event.
        /// </summary>
        public PlayerSpeakingStateChanged(
            string sessionId,
            bool isSpeaking,
            DateTime timestamp)
        {
            SessionId = sessionId ?? string.Empty;
            IsSpeaking = isSpeaking;
            Timestamp = timestamp;
        }

        /// <summary>
        ///     Creates a PlayerSpeakingStateChanged event with the current UTC timestamp.
        /// </summary>
        /// <param name="sessionId">The session ID (can be null or empty)</param>
        /// <param name="isSpeaking">Whether the player is speaking</param>
        /// <returns>A new PlayerSpeakingStateChanged event</returns>
        public static PlayerSpeakingStateChanged Create(
            string sessionId,
            bool isSpeaking)
        {
            return new PlayerSpeakingStateChanged(
                sessionId,
                isSpeaking,
                DateTime.UtcNow
            );
        }

        /// <summary>
        ///     Creates a "started speaking" event.
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <returns>A new PlayerSpeakingStateChanged event with IsSpeaking=true</returns>
        public static PlayerSpeakingStateChanged StartedSpeaking(string sessionId = null) => Create(sessionId, true);

        /// <summary>
        ///     Creates a "stopped speaking" event.
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <returns>A new PlayerSpeakingStateChanged event with IsSpeaking=false</returns>
        public static PlayerSpeakingStateChanged StoppedSpeaking(string sessionId = null) => Create(sessionId, false);

        /// <summary>
        ///     Checks if the player is silent (not speaking).
        /// </summary>
        public bool IsSilent => !IsSpeaking;

        /// <summary>
        ///     Checks if this event represents the start of speech.
        /// </summary>
        public bool IsStartOfSpeech => IsSpeaking;

        /// <summary>
        ///     Checks if this event represents the end of speech.
        /// </summary>
        public bool IsEndOfSpeech => !IsSpeaking;
    }
}
