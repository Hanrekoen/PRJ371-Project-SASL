namespace Convai.Infrastructure.Networking.Models
{
    /// <summary>
    ///     Immutable descriptor describing a Convai Character routing metadata.
    ///     Used when routing character state and audio-related metadata.
    /// </summary>
    public readonly struct CharacterDescriptor
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="CharacterDescriptor" /> struct.
        /// </summary>
        /// <param name="instanceId">Unity instance ID for the character GameObject.</param>
        /// <param name="characterId">Convai character identifier.</param>
        /// <param name="characterName">Human-readable display name.</param>
        /// <param name="participantId">Participant identifier.</param>
        /// <param name="isMuted">Whether the character is muted.</param>
        /// <param name="ownerId">Owner player ID for multi-character support.</param>
        public CharacterDescriptor(
            string instanceId,
            string characterId,
            string characterName,
            string participantId,
            bool isMuted,
            string ownerId = null)
        {
            InstanceId = instanceId ?? string.Empty;
            CharacterId = characterId ?? string.Empty;
            CharacterName = characterName ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            IsMuted = isMuted;
            OwnerId = ownerId ?? string.Empty;
        }

        /// <summary>
        ///     Unity instance ID for the character GameObject.
        /// </summary>
        public string InstanceId { get; }

        /// <summary>
        ///     Convai character ID.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     Human-readable display name for the character.
        /// </summary>
        public string CharacterName { get; }

        /// <summary>
        ///     Participant ID assigned when the character joins the room.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Whether this character's audio is muted.
        /// </summary>
        public bool IsMuted { get; }

        /// <summary>
        ///     Owner player ID for multi-character support.
        ///     Empty string indicates the local player owns this character.
        /// </summary>
        public string OwnerId { get; }

        /// <summary>Creates a copy with the specified participant ID.</summary>
        public CharacterDescriptor WithParticipantId(string participantId) =>
            new(InstanceId, CharacterId, CharacterName, participantId, IsMuted, OwnerId);

        /// <summary>Creates a copy with the specified mute state.</summary>
        public CharacterDescriptor WithMuteState(bool isMuted) =>
            new(InstanceId, CharacterId, CharacterName, ParticipantId, isMuted, OwnerId);

        /// <summary>Creates a copy with the specified owner ID.</summary>
        public CharacterDescriptor WithOwnerId(string ownerId) =>
            new(InstanceId, CharacterId, CharacterName, ParticipantId, IsMuted, ownerId);
    }
}
