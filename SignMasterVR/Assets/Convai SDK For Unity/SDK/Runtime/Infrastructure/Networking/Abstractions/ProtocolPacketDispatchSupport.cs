using System;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Shared helper for dispatching incoming transport data packets into the protocol layer.
    ///     Keeps transport event wiring and platform-specific exception/log handling local to controllers.
    /// </summary>
    internal static class ProtocolPacketDispatchSupport
    {
        internal static void DispatchIncoming(
            DataPacket packet,
            ProtocolGateway gateway,
            RTVIHandler rtviHandler = null)
        {
            if (gateway == null) throw new ArgumentNullException(nameof(gateway));

            ProtocolPacket protocolPacket = new(
                packet.Payload,
                packet.ParticipantId,
                packet.Topic,
                packet.Kind == DataPacketKind.Reliable);

            // Fast-path: intercept LipSync packets before JSON deserialization in the gateway.
            if (rtviHandler != null && rtviHandler.TryHandleLipSyncServerMessage(in protocolPacket)) return;

            gateway.ProcessIncoming(protocolPacket);
        }
    }
}
