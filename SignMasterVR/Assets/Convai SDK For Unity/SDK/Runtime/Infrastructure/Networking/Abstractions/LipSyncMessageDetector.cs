using System;
using System.Text;

namespace Convai.Infrastructure.Networking
{
    internal static class LipSyncMessageDetector
    {
        private static readonly byte[] ServerMessageNeedle = Encoding.UTF8.GetBytes("\"server-message\"");
        private static readonly byte[] SinglePayloadNeedle = Encoding.UTF8.GetBytes("\"neurosync-blendshapes\"");

        private static readonly byte[] ChunkedPayloadNeedle =
            Encoding.UTF8.GetBytes("\"chunked-neurosync-blendshapes\"");

        public static bool MayContainLipSyncServerMessage(ReadOnlySpan<byte> payloadBytes)
        {
            if (payloadBytes.IsEmpty) return false;

            return payloadBytes.IndexOf(ServerMessageNeedle) >= 0 &&
                   (payloadBytes.IndexOf(SinglePayloadNeedle) >= 0 ||
                    payloadBytes.IndexOf(ChunkedPayloadNeedle) >= 0);
        }
    }
}
