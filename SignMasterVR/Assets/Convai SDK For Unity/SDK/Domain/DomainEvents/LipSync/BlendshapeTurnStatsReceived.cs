using System;

namespace Convai.Domain.DomainEvents.LipSync
{
    /// <summary>
    ///     Raised when the backend reports end-of-turn blendshape generation statistics.
    ///     Carries the response owner identity so consumers can scope end-of-stream to the
    ///     turn that actually finished instead of whichever stream is currently active.
    /// </summary>
    public readonly struct BlendshapeTurnStatsReceived
    {
        public BlendshapeTurnStatsReceived(
            string characterId,
            string participantId,
            int totalBlendshapes,
            int receivedBlendshapeFrames,
            int totalAudioBytes,
            double totalTurnDurationMs,
            double totalAudioDurationMs,
            double fps,
            string responseId,
            int? neuroSyncTurnId,
            int? epoch,
            int? sequence,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            TotalBlendshapes = totalBlendshapes;
            ReceivedBlendshapeFrames = receivedBlendshapeFrames;
            TotalAudioBytes = totalAudioBytes;
            TotalTurnDurationMs = totalTurnDurationMs;
            TotalAudioDurationMs = totalAudioDurationMs;
            Fps = fps;
            ResponseId = responseId ?? string.Empty;
            NeuroSyncTurnId = neuroSyncTurnId;
            Epoch = epoch;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        public string CharacterId { get; }
        public string ParticipantId { get; }
        public int TotalBlendshapes { get; }
        public int ReceivedBlendshapeFrames { get; }
        public int TotalAudioBytes { get; }
        public double TotalTurnDurationMs { get; }
        public double TotalAudioDurationMs { get; }
        public double Fps { get; }
        public string ResponseId { get; }
        public int? NeuroSyncTurnId { get; }
        public int? Epoch { get; }
        public int? Sequence { get; }
        public DateTime Timestamp { get; }
        public bool FrameCountMatches => TotalBlendshapes == ReceivedBlendshapeFrames;
        public bool HasOwnerMetadata =>
            !string.IsNullOrWhiteSpace(ResponseId) || NeuroSyncTurnId.HasValue || Epoch.HasValue;

        public static BlendshapeTurnStatsReceived Create(
            string characterId,
            string participantId,
            int totalBlendshapes,
            int receivedBlendshapeFrames,
            int totalAudioBytes,
            double totalTurnDurationMs,
            double totalAudioDurationMs,
            double fps,
            string responseId = null,
            int? neuroSyncTurnId = null,
            int? epoch = null,
            int? sequence = null)
        {
            return new BlendshapeTurnStatsReceived(
                characterId,
                participantId,
                totalBlendshapes,
                receivedBlendshapeFrames,
                totalAudioBytes,
                totalTurnDurationMs,
                totalAudioDurationMs,
                fps,
                responseId,
                neuroSyncTurnId,
                epoch,
                sequence,
                DateTime.UtcNow);
        }
    }
}
