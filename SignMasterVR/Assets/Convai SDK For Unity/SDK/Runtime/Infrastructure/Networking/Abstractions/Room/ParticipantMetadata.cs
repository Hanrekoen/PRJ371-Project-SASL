using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Metadata associated with a participant.
    /// </summary>
    public readonly struct ParticipantMetadata : IEquatable<ParticipantMetadata>
    {
        /// <summary>
        ///     Raw metadata string (typically JSON).
        /// </summary>
        public string RawValue { get; }

        /// <summary>
        ///     Participant's display name if available.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        ///     Whether this participant is an AI agent/bot.
        /// </summary>
        public bool IsAgent { get; }

        /// <summary>
        ///     Character ID if this participant represents a Convai character.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     Creates a new participant metadata instance.
        /// </summary>
        public ParticipantMetadata(string rawValue, string displayName = null, bool isAgent = false,
            string characterId = null)
        {
            RawValue = rawValue ?? string.Empty;
            DisplayName = displayName;
            IsAgent = isAgent;
            CharacterId = characterId;
        }

        /// <summary>
        ///     Creates an empty metadata instance.
        /// </summary>
        public static ParticipantMetadata Empty => new(string.Empty);

        public bool Equals(ParticipantMetadata other) => RawValue == other.RawValue;

        public override bool Equals(object obj) => obj is ParticipantMetadata other && Equals(other);

        public override int GetHashCode() => RawValue?.GetHashCode() ?? 0;

        public static bool operator ==(ParticipantMetadata left, ParticipantMetadata right) => left.Equals(right);

        public static bool operator !=(ParticipantMetadata left, ParticipantMetadata right) => !left.Equals(right);
    }
}
