using System.Threading;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;

namespace Convai.Runtime.Core.MultiParticipant
{
    /// <summary>
    ///     Service for creating and joining shareable rooms.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This interface is scaffolded with stubs for future multi-participant support.
    ///     </para>
    ///     <para>
    ///         <b>Room Codes</b>: Rooms can be shared via short alphanumeric codes
    ///         that other players can use to join.
    ///     </para>
    /// </remarks>
    public interface IRoomSharingService
    {
        #region Room Creation

        /// <summary>
        ///     Creates a new shareable room.
        /// </summary>
        /// <param name="options">Room creation options.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation resolving with the created room session.</returns>
        public IConvaiOperation<RoomSession> CreateRoomAsync(RoomCreateOptions options, CancellationToken ct = default);

        /// <summary>
        ///     Generates a shareable room code for the current session.
        /// </summary>
        /// <returns>Shareable room code, or null if not in a shareable room.</returns>
        public string GetRoomCode();

        #endregion

        #region Room Joining

        /// <summary>
        ///     Joins an existing room by room code.
        /// </summary>
        /// <param name="roomCode">The room code to join.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation resolving with the join result.</returns>
        public IConvaiOperation<JoinResult> JoinRoomAsync(string roomCode, CancellationToken ct = default);

        /// <summary>
        ///     Validates a room code without joining.
        /// </summary>
        /// <param name="roomCode">The room code to validate.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Operation resolving with true if valid, false otherwise.</returns>
        public IConvaiOperation<bool> ValidateRoomCodeAsync(string roomCode, CancellationToken ct = default);

        #endregion

        #region Room Management

        /// <summary>
        ///     Leaves the current room.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public IConvaiOperation<Unit> LeaveRoomAsync(CancellationToken ct = default);

        /// <summary>
        ///     Whether the local player is currently in a shareable room.
        /// </summary>
        public bool IsInShareableRoom { get; }

        #endregion
    }
}
