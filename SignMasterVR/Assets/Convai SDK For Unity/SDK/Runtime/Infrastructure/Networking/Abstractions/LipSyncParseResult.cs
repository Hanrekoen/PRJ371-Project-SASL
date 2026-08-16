using Convai.Domain.Models.LipSync;

namespace Convai.Infrastructure.Networking
{
    internal readonly struct LipSyncParseResult
    {
        public LipSyncParseResult(
            bool handled,
            bool parsed,
            string payloadType,
            LipSyncPackedChunk chunk,
            string dropReasonCode)
        {
            Handled = handled;
            Parsed = parsed;
            PayloadType = payloadType ?? string.Empty;
            Chunk = chunk;
            DropReasonCode = dropReasonCode ?? string.Empty;
        }

        public bool Handled { get; }
        public bool Parsed { get; }
        public string PayloadType { get; }
        public LipSyncPackedChunk Chunk { get; }
        public string DropReasonCode { get; }
    }
}
