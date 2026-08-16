using System;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when the microphone mute state changes.
    ///     Surfaces mic state via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever the microphone is muted or unmuted.
    ///     Typically use EventDeliveryPolicy.MainThread for UI updates and
    ///     EventDeliveryPolicy.Immediate for lightweight logging.
    /// </remarks>
    public readonly struct MicMuteChanged
    {
        /// <summary>
        ///     Whether the microphone is currently muted.
        /// </summary>
        public bool IsMuted { get; }

        /// <summary>
        ///     When the mute state changed (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional participant ID if this is for a specific participant.
        ///     Null if this is for the local player's microphone.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Creates a new MicMuteChanged event.
        /// </summary>
        public MicMuteChanged(
            bool isMuted,
            DateTime timestamp,
            string participantId = null)
        {
            IsMuted = isMuted;
            Timestamp = timestamp;
            ParticipantId = participantId;
        }

        /// <summary>
        ///     Creates a MicMuteChanged event with the current UTC timestamp.
        /// </summary>
        /// <param name="isMuted">Whether the microphone is muted</param>
        /// <param name="participantId">Optional participant ID</param>
        /// <returns>A new MicMuteChanged event</returns>
        public static MicMuteChanged Create(
            bool isMuted,
            string participantId = null)
        {
            return new MicMuteChanged(
                isMuted,
                DateTime.UtcNow,
                participantId
            );
        }

        /// <summary>
        ///     Checks if the microphone is unmuted.
        /// </summary>
        public bool IsUnmuted => !IsMuted;

        /// <summary>
        ///     Checks if this event is for the local player (no participant ID).
        /// </summary>
        public bool IsLocalPlayer => string.IsNullOrEmpty(ParticipantId);

        /// <summary>
        ///     Checks if this event is for a specific participant.
        /// </summary>
        public bool IsRemoteParticipant => !string.IsNullOrEmpty(ParticipantId);
    }
}
