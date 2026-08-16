namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Information about a subscribed track.
    /// </summary>
    public struct TrackInfo
    {
        /// <summary>Track's unique identifier (SID).</summary>
        public string TrackSid { get; set; }

        /// <summary>Participant ID who owns this track.</summary>
        public string ParticipantId { get; set; }

        /// <summary>Participant identity who owns this track.</summary>
        public string ParticipantIdentity { get; set; }

        /// <summary>Kind of track (audio/video).</summary>
        public TrackKind Kind { get; set; }

        /// <summary>Track name/label if available.</summary>
        public string Name { get; set; }

        /// <summary>
        ///     Creates a new TrackInfo instance.
        /// </summary>
        public TrackInfo(string trackSid, string participantId, string participantIdentity, TrackKind kind,
            string name = null)
        {
            TrackSid = trackSid;
            ParticipantId = participantId;
            ParticipantIdentity = participantIdentity;
            Kind = kind;
            Name = name;
        }
    }
}
