namespace Convai.Domain.Models
{
    /// <summary>
    ///     Query options for filtering room transcript turns.
    /// </summary>
    public sealed class TranscriptQuery
    {
        public TranscriptParticipantKind? ParticipantKind { get; set; }

        public string PlayerOrCharacterId { get; set; }

        public string ParticipantId { get; set; }

        public bool IncludeActiveTurns { get; set; } = true;

        public bool IncludeCommittedTurns { get; set; } = true;
    }
}
