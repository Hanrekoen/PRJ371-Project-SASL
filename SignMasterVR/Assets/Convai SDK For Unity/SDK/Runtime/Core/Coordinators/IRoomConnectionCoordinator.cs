using System;
using System.Threading;
using Convai.Domain.DomainEvents.Session;
using Convai.Runtime.Core.Async;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Coordinates room connection lifecycle including connect, disconnect, and reconnection.
    ///     Extracted from ConvaiRoomManager to enable focused testing and clear responsibility boundaries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This coordinator owns all connection state transitions and retry logic.
    ///         It delegates actual transport operations to injected dependencies.
    ///     </para>
    ///     <para>
    ///         <b>Multi-Participant Support</b>: The <c>CreateRoomAsync</c> and <c>JoinRoomAsync</c> methods
    ///         are stubs for future multi-participant functionality.
    ///     </para>
    /// </remarks>
    public interface IRoomConnectionCoordinator
    {
        #region Events

        /// <summary>
        ///     Fired when the session state changes.
        /// </summary>
        public event Action<SessionStateChanged> StateChanged;

        #endregion

        #region State

        /// <summary>
        ///     Current session connection state.
        /// </summary>
        public SessionState CurrentState { get; }

        /// <summary>
        ///     Whether the session is currently connected (Connected or Reconnecting states).
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        ///     The current active session, or <see cref="RoomSession.Empty" /> if not connected.
        /// </summary>
        public RoomSession CurrentSession { get; }

        #endregion

        #region Basic Connection

        /// <summary>
        ///     Initiates a connection to the Convai backend.
        /// </summary>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation resolving with the established session.</returns>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken ct = default);

        /// <summary>
        ///     Gracefully disconnects from the current session.
        /// </summary>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation completing when disconnection is complete.</returns>
        public IConvaiOperation<Unit> DisconnectAsync(CancellationToken ct = default);

        #endregion

        #region Multi-Participant

        /// <summary>
        ///     Creates a new shareable room for multi-participant sessions.
        /// </summary>
        /// <param name="options">Room creation options.</param>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation resolving with the created session.</returns>
        /// <remarks>
        ///     Returns a stub result in single-player mode.
        /// </remarks>
        public IConvaiOperation<RoomSession> CreateRoomAsync(RoomCreateOptions options, CancellationToken ct = default);

        /// <summary>
        ///     Joins an existing room by room code.
        /// </summary>
        /// <param name="roomCode">The room code to join.</param>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation resolving with the join result.</returns>
        /// <remarks>
        ///     Returns a stub failure result in single-player mode.
        /// </remarks>
        public IConvaiOperation<JoinResult> JoinRoomAsync(string roomCode, CancellationToken ct = default);

        /// <summary>
        ///     Room code of the current session (multi-participant only).
        ///     Null for single-player sessions.
        /// </summary>
        public string RoomCode { get; }

        /// <summary>
        ///     Whether the local player is the room owner (creator).
        ///     Always true for single-player sessions.
        /// </summary>
        public bool IsRoomOwner { get; }

        #endregion
    }
}
