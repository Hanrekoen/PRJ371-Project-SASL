using System;

namespace Convai.Domain.DomainEvents.Participant
{
    /// <summary>
    ///     Immutable data structure representing participant information in a Convai session.
    /// </summary>
    /// <remarks>
    ///     ParticipantInfo provides a unified view of all participant types in a room session,
    ///     including local players, remote players (multiplayer), and Characters.
    ///     It is carried by room-level participant domain events published through EventHub and
    ///     surfaced by manager-owned runtime event facades.
    /// </remarks>
    public readonly struct ParticipantInfo : IEquatable<ParticipantInfo>
    {
        /// <summary>
        ///     Unique identifier for the participant in the transport layer (e.g., LiveKit SID).
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Identity string for the participant (e.g., character ID or user ID).
        /// </summary>
        public string Identity { get; }

        /// <summary>
        ///     Human-readable display name for the participant.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        ///     Type of participant (LocalPlayer, RemotePlayer, Character).
        /// </summary>
        public ParticipantType ParticipantType { get; }

        /// <summary>
        ///     Whether this participant is the local user.
        /// </summary>
        public bool IsLocal { get; }

        /// <summary>
        ///     Gets a value indicating whether the participant's audio is muted locally.
        /// </summary>
        public bool IsMuted { get; }

        /// <summary>
        ///     Creates a new ParticipantInfo instance.
        /// </summary>
        /// <param name="participantId">Participant ID</param>
        /// <param name="identity">Identity string</param>
        /// <param name="displayName">Human-readable display name</param>
        /// <param name="participantType">Type of participant</param>
        /// <param name="isLocal">Whether this is the local participant</param>
        /// <param name="isMuted">Whether the participant's audio is muted locally</param>
        public ParticipantInfo(
            string participantId,
            string identity,
            string displayName,
            ParticipantType participantType,
            bool isLocal,
            bool isMuted = false)
        {
            ParticipantId = participantId ?? string.Empty;
            Identity = identity ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ParticipantType = participantType;
            IsLocal = isLocal;
            IsMuted = isMuted;
        }

        /// <summary>
        ///     Creates a new ParticipantInfo with the mute state changed.
        /// </summary>
        /// <param name="muted">The new mute state.</param>
        /// <returns>A new ParticipantInfo instance with the updated mute state.</returns>
        public ParticipantInfo WithMuted(bool muted) =>
            new(ParticipantId, Identity, DisplayName, ParticipantType, IsLocal, muted);

        /// <summary>
        ///     Creates an empty/default ParticipantInfo.
        /// </summary>
        public static ParticipantInfo Empty =>
            new(string.Empty, string.Empty, string.Empty, ParticipantType.Unknown, false);

        /// <summary>
        ///     Creates a ParticipantInfo for a Convai character.
        /// </summary>
        public static ParticipantInfo ForCharacter(string participantId, string identity, string displayName) =>
            new(participantId, identity, displayName, ParticipantType.Character, false);

        /// <summary>
        ///     Creates a ParticipantInfo for the local player.
        /// </summary>
        public static ParticipantInfo ForLocalPlayer(string participantId, string identity, string displayName) =>
            new(participantId, identity, displayName, ParticipantType.LocalPlayer, true);

        /// <summary>
        ///     Creates a ParticipantInfo for a remote player.
        /// </summary>
        public static ParticipantInfo ForRemotePlayer(string participantId, string identity, string displayName) =>
            new(participantId, identity, displayName, ParticipantType.RemotePlayer, false);

        /// <inheritdoc />
        public bool Equals(ParticipantInfo other) =>
            ParticipantId == other.ParticipantId &&
            Identity == other.Identity &&
            DisplayName == other.DisplayName &&
            ParticipantType == other.ParticipantType &&
            IsLocal == other.IsLocal &&
            IsMuted == other.IsMuted;

        /// <inheritdoc />
        public override bool Equals(object obj) =>
            obj is ParticipantInfo other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            HashCode.Combine(ParticipantId, Identity, DisplayName, ParticipantType, IsLocal, IsMuted);

        /// <inheritdoc />
        public override string ToString() =>
            $"ParticipantInfo({ParticipantType}: {DisplayName ?? Identity ?? ParticipantId})";

        /// <summary>Equality operator.</summary>
        public static bool operator ==(ParticipantInfo left, ParticipantInfo right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(ParticipantInfo left, ParticipantInfo right) => !left.Equals(right);
    }
}
