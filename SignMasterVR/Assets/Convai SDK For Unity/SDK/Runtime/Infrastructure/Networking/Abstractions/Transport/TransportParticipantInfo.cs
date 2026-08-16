namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Information about a remote participant.
    /// </summary>
    public struct TransportParticipantInfo
    {
        /// <summary>Participant's unique identifier (SID).</summary>
        public string ParticipantId { get; set; }

        /// <summary>Participant's identity string.</summary>
        public string Identity { get; set; }

        /// <summary>Whether this is the local participant.</summary>
        public bool IsLocal { get; set; }

        /// <summary>Additional metadata associated with participant.</summary>
        public string Metadata { get; set; }

        /// <summary>
        ///     Creates a new TransportParticipantInfo instance.
        /// </summary>
        public TransportParticipantInfo(string participantId, string identity, bool isLocal = false,
            string metadata = null)
        {
            ParticipantId = participantId;
            Identity = identity;
            IsLocal = isLocal;
            Metadata = metadata;
        }
    }
}
