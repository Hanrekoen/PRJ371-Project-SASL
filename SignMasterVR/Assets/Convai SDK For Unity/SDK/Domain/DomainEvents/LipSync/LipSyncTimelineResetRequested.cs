using System;

namespace Convai.Domain.DomainEvents.LipSync
{
    public readonly struct LipSyncTimelineResetRequested
    {
        public LipSyncTimelineResetRequested(
            string characterId,
            string participantId,
            string responseId,
            int? neuroSyncTurnId,
            int? epoch,
            int? sequence,
            int? validThroughFrameIndex,
            string reason,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            NeuroSyncTurnId = neuroSyncTurnId;
            Epoch = epoch;
            Sequence = sequence;
            ValidThroughFrameIndex = validThroughFrameIndex.HasValue && validThroughFrameIndex.Value >= 0
                ? validThroughFrameIndex
                : null;
            Reason = reason ?? string.Empty;
            Timestamp = timestamp;
        }

        public string CharacterId { get; }
        public string ParticipantId { get; }
        public string ResponseId { get; }
        public int? NeuroSyncTurnId { get; }
        public int? Epoch { get; }
        public int? Sequence { get; }
        public int? ValidThroughFrameIndex { get; }
        public string Reason { get; }
        public DateTime Timestamp { get; }
        public bool HasOwnerMetadata =>
            !string.IsNullOrWhiteSpace(ResponseId) || NeuroSyncTurnId.HasValue || Epoch.HasValue;

        public static LipSyncTimelineResetRequested Create(
            string characterId,
            string participantId,
            string responseId,
            int? neuroSyncTurnId,
            int? epoch,
            int? sequence,
            int? validThroughFrameIndex,
            string reason) =>
            new(
                characterId,
                participantId,
                responseId,
                neuroSyncTurnId,
                epoch,
                sequence,
                validThroughFrameIndex,
                reason,
                DateTime.UtcNow);
    }
}
