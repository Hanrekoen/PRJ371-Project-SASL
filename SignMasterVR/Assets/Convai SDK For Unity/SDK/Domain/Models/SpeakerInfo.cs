namespace Convai.Domain.Models
{
    /// <summary>
    ///     Represents speaker information for multi-user transcript attribution.
    ///     Maps to the backend's speaker directory metadata sent via final-user-transcription.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This struct carries speaker identification data from the backend's speaker directory
    ///         system. It is used to attribute transcripts to specific speakers in multi-user
    ///         scenarios (e.g., multiple players talking to the same character).
    ///     </para>
    ///     <para>
    ///         <b>Field Mapping from Backend:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><c>SpeakerId</c>: Maps to <c>speaker_id</c> - unique identifier from speaker directory</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>SpeakerName</c>: Maps to <c>speaker_name</c> - human-readable display name</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>ParticipantId</c>: Maps to <c>participant_id</c> - LiveKit participant SID</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public readonly struct SpeakerInfo
    {
        /// <summary>
        ///     Server-generated unique identifier (UUID) for the speaker from the backend's speaker directory.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This is the server-generated UUID, NOT the <c>end_user_id</c> sent during connection.
        ///         The server resolves the <c>end_user_id</c> to a Speaker record and assigns this UUID.
        ///     </para>
        ///     <para>
        ///         The backend uses this ID for:
        ///         <list type="bullet">
        ///             <item>
        ///                 <description>Long-Term Memory (Mem0) with key format: <c>speaker_id:character_id</c></description>
        ///             </item>
        ///             <item>
        ///                 <description>Interaction persistence in the database</description>
        ///             </item>
        ///         </list>
        ///     </para>
        /// </remarks>
        public string SpeakerId { get; }

        /// <summary>
        ///     Human-readable display name for the speaker.
        /// </summary>
        public string SpeakerName { get; }

        /// <summary>
        ///     LiveKit participant ID (SID) for transport-level identification.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Type of speaker (Character, Player, System).
        /// </summary>
        public SpeakerType SpeakerType { get; }

        /// <summary>
        ///     Creates a new SpeakerInfo with all fields.
        /// </summary>
        /// <param name="speakerId">Unique speaker identifier</param>
        /// <param name="speakerName">Human-readable display name</param>
        /// <param name="participantId">LiveKit participant ID</param>
        /// <param name="speakerType">Type of speaker</param>
        public SpeakerInfo(
            string speakerId,
            string speakerName,
            string participantId,
            SpeakerType speakerType = SpeakerType.Player)
        {
            SpeakerId = speakerId ?? string.Empty;
            SpeakerName = speakerName ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            SpeakerType = speakerType;
        }

        /// <summary>
        ///     Gets the default player speaker info for single-user scenarios.
        /// </summary>
        public static SpeakerInfo DefaultPlayer => new("local-player", PlayerDisplayName.Default, string.Empty);

        /// <summary>
        ///     Gets an empty speaker info instance.
        /// </summary>
        public static SpeakerInfo Empty => new(string.Empty, string.Empty, string.Empty, SpeakerType.Unknown);

        /// <summary>
        ///     Checks if this speaker info has valid identification data.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(SpeakerId) || !string.IsNullOrEmpty(SpeakerName);

        /// <summary>
        ///     Checks if this is the default player (no multi-user attribution).
        /// </summary>
        public bool IsDefaultPlayer => SpeakerId == "local-player" || string.IsNullOrEmpty(SpeakerId);

        /// <summary>
        ///     Creates a SpeakerInfo from a TranscriptMessage.
        /// </summary>
        public static SpeakerInfo FromMessage(TranscriptMessage message)
        {
            return new SpeakerInfo(
                message.PlayerOrCharacterId,
                message.DisplayName,
                message.ParticipantId,
                message.SpeakerType
            );
        }

        /// <summary>
        ///     Gets the display name, falling back to speaker ID if name is empty.
        /// </summary>
        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(SpeakerName))
                return SpeakerName;
            if (!string.IsNullOrEmpty(SpeakerId))
                return SpeakerId;
            return "Unknown";
        }

        /// <summary>
        ///     Creates a formatted string representation for logging.
        /// </summary>
        public override string ToString() =>
            $"[{SpeakerType}] {GetDisplayName()} (id: {SpeakerId}, participant: {ParticipantId})";
    }
}
