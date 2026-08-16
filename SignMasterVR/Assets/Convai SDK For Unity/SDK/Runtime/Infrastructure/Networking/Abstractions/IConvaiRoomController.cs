using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Platform-agnostic interface for room connection and management.
    /// </summary>
    public interface IConvaiRoomController
        : IRoomConnectionState,
            IRoomConnectionInitializer,
            IRoomAudioControl,
            IRoomControllerEvents,
            IDisposable
    {
    }
}
