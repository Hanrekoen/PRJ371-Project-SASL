namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Event args for track subscription events.
    /// </summary>
    public readonly struct TrackSubscriptionEventArgs
    {
        /// <summary>
        ///     The track that was subscribed/unsubscribed.
        /// </summary>
        public IRemoteTrack Track { get; }

        /// <summary>
        ///     The participant who owns the track.
        /// </summary>
        public IRemoteParticipant Participant { get; }

        /// <summary>
        ///     Publication information about the track.
        /// </summary>
        public TrackPublicationInfo Publication { get; }

        /// <summary>
        ///     Creates new track subscription event args.
        /// </summary>
        public TrackSubscriptionEventArgs(IRemoteTrack track, IRemoteParticipant participant,
            TrackPublicationInfo publication)
        {
            Track = track;
            Participant = participant;
            Publication = publication;
        }
    }
}
