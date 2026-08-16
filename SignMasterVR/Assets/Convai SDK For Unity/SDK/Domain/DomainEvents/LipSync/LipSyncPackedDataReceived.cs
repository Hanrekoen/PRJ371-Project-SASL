using System;
using Convai.Domain.Models.LipSync;

namespace Convai.Domain.DomainEvents.LipSync
{
    /// <summary>
    ///     Raised when packed lip sync data is received and ready for direct playback buffering.
    /// </summary>
    public readonly struct LipSyncPackedDataReceived
    {
        public LipSyncPackedDataReceived(
            string characterId,
            string participantId,
            LipSyncPackedChunk chunk,
            DateTime timestamp)
        {
            CharacterId = characterId ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            Chunk = chunk;
            Timestamp = timestamp;
        }

        public string CharacterId { get; }
        public string ParticipantId { get; }
        public LipSyncPackedChunk Chunk { get; }
        public DateTime Timestamp { get; }

        public LipSyncProfileId ProfileId => Chunk?.ProfileId ?? LipSyncProfileId.ARKit;
        public bool IsValid => Chunk != null && Chunk.IsValid;
        public int FrameCount => Chunk?.FrameCount ?? 0;
        public float Duration => Chunk?.Duration ?? 0f;

        public static LipSyncPackedDataReceived Create(
            string characterId,
            string participantId,
            LipSyncPackedChunk chunk)
        {
            return new LipSyncPackedDataReceived(
                characterId,
                participantId,
                chunk,
                DateTime.UtcNow);
        }
    }
}
