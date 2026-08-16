namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Information about a remote audio track for playback.
    /// </summary>
    internal struct RemoteAudioTrackInfo
    {
        /// <summary>Participant ID who owns this track.</summary>
        public string ParticipantId { get; set; }

        /// <summary>Participant identity who owns this track.</summary>
        public string ParticipantIdentity { get; set; }

        /// <summary>Track's unique identifier (SID).</summary>
        public string TrackSid { get; set; }

        /// <summary>Track name if available.</summary>
        public string TrackName { get; set; }

        /// <summary>
        ///     Creates a new RemoteAudioTrackInfo instance.
        /// </summary>
        public RemoteAudioTrackInfo(string participantId, string participantIdentity, string trackSid,
            string trackName = null)
        {
            ParticipantId = participantId;
            ParticipantIdentity = participantIdentity;
            TrackSid = trackSid;
            TrackName = trackName;
        }
    }
}
