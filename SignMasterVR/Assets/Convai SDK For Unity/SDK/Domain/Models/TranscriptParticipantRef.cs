using System;

namespace Convai.Domain.Models
{
    /// <summary>
    ///     Identifies the participant kind associated with a transcript turn.
    /// </summary>
    public enum TranscriptParticipantKind
    {
        Player = 0,
        Character = 1
    }

    /// <summary>
    ///     Stable participant identity used by room transcript snapshots.
    /// </summary>
    public readonly struct TranscriptParticipantRef : IEquatable<TranscriptParticipantRef>
    {
        public TranscriptParticipantRef(
            TranscriptParticipantKind kind,
            string playerOrCharacterId,
            string displayName,
            string participantId = null)
        {
            Kind = kind;
            PlayerOrCharacterId = playerOrCharacterId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
        }

        public TranscriptParticipantKind Kind { get; }

        public string PlayerOrCharacterId { get; }

        public string DisplayName { get; }

        public string ParticipantId { get; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(PlayerOrCharacterId);

        public bool Equals(TranscriptParticipantRef other)
        {
            return Kind == other.Kind &&
                   string.Equals(PlayerOrCharacterId, other.PlayerOrCharacterId, StringComparison.Ordinal) &&
                   string.Equals(ParticipantId, other.ParticipantId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is TranscriptParticipantRef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^
                           (PlayerOrCharacterId != null ? StringComparer.Ordinal.GetHashCode(PlayerOrCharacterId) : 0);
                hashCode = (hashCode * 397) ^
                           (ParticipantId != null ? StringComparer.Ordinal.GetHashCode(ParticipantId) : 0);
                return hashCode;
            }
        }

        public static bool operator ==(TranscriptParticipantRef left, TranscriptParticipantRef right) =>
            left.Equals(right);

        public static bool operator !=(TranscriptParticipantRef left, TranscriptParticipantRef right) =>
            !left.Equals(right);
    }
}
