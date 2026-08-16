namespace Convai.Infrastructure.Networking.Audio
{
    /// <summary>
    ///     Reason for audio routing state change.
    /// </summary>
    internal enum AudioRoutingChangeReason
    {
        /// <summary>User requested the change.</summary>
        UserRequested,

        /// <summary>Track was subscribed.</summary>
        TrackSubscribed,

        /// <summary>Track was unsubscribed.</summary>
        TrackUnsubscribed,

        /// <summary>Participant disconnected.</summary>
        ParticipantDisconnected,

        /// <summary>Session ended.</summary>
        SessionEnded,

        /// <summary>An error occurred.</summary>
        Error
    }
}
