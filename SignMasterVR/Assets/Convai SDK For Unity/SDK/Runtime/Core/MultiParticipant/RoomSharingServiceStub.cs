using System;
using System.Threading;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Stub implementation of <see cref="IRoomSharingService" /> for single-player mode.
    /// </summary>
    /// <remarks>
    ///     In single-player mode, room sharing is not supported. All methods return
    ///     appropriate stub responses indicating the feature is unavailable.
    /// </remarks>
    internal sealed class RoomSharingServiceStub : IRoomSharingService
    {
        /// <inheritdoc />
        public bool IsInShareableRoom => false;

        /// <inheritdoc />
        public IConvaiOperation<RoomSession> CreateRoomAsync(RoomCreateOptions options, CancellationToken ct = default)
        {
            // In single-player stub, just return a local session
            var session = new RoomSession(
                Guid.NewGuid().ToString("N"),
                null, // No sharing in stub
                "local-player",
                DateTime.UtcNow);

            return ConvaiOperation<RoomSession>.Succeeded(session);
        }

        /// <inheritdoc />
        public string GetRoomCode()
        {
            // No room code in single-player mode
            return null;
        }

        /// <inheritdoc />
        public IConvaiOperation<JoinResult> JoinRoomAsync(string roomCode, CancellationToken ct = default)
        {
            // Cannot join rooms in single-player mode
            return ConvaiOperation<JoinResult>.Succeeded(
                JoinResult.Failed("Room sharing is not available in single-player mode."));
        }

        /// <inheritdoc />
        public IConvaiOperation<bool> ValidateRoomCodeAsync(string roomCode, CancellationToken ct = default)
        {
            // No valid room codes in single-player mode
            return ConvaiOperation<bool>.Succeeded(false);
        }

        /// <inheritdoc />
        public IConvaiOperation<Unit> LeaveRoomAsync(CancellationToken ct = default)
        {
            // No-op in single-player mode
            return ConvaiOperation<Unit>.Succeeded(Unit.Value);
        }
    }
}
