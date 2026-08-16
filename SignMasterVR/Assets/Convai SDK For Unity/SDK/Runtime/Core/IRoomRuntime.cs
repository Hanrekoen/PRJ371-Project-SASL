using System.Threading;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;

namespace Convai.Runtime.Core
{
    /// <summary>
    ///     Runtime contract for a single room session.
    /// </summary>
    /// <remarks>
    ///     Owns room-scoped coordinators for connection, audio, ownership, and diagnostics.
    ///     Use the coordinator properties for advanced control or the convenience connect/disconnect methods for a simpler
    ///     API.
    /// </remarks>
    public interface IRoomRuntime
    {
        #region State

        /// <summary>
        ///     Whether the room runtime is currently active.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>
        ///     The current room session information.
        /// </summary>
        public RoomSession Session { get; }

        #endregion

        #region Coordinators

        /// <summary>
        ///     Connection coordinator for managing room connection lifecycle.
        /// </summary>
        public IRoomConnectionCoordinator Connection { get; }

        /// <summary>
        ///     Audio coordinator for managing microphone and remote audio.
        /// </summary>
        public IRoomAudioCoordinator Audio { get; }

        /// <summary>
        ///     Ownership coordinator for managing character ownership and focus.
        /// </summary>
        public IRoomOwnershipCoordinator Ownership { get; }

        /// <summary>
        ///     Diagnostics for monitoring room health and metrics.
        /// </summary>
        public IRoomDiagnostics Diagnostics { get; }

        #endregion

        #region Connection Convenience

        /// <summary>
        ///     Connects to the room and establishes a session.
        /// </summary>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation resolving with the established session.</returns>
        /// <remarks>
        ///     Equivalent to calling <c>Connection.ConnectAsync(ct)</c>.
        /// </remarks>
        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken ct = default);

        /// <summary>
        ///     Disconnects from the room and terminates the session.
        /// </summary>
        /// <param name="ct">Cancellation token for the operation.</param>
        /// <returns>Operation completing when disconnection is complete.</returns>
        /// <remarks>
        ///     Equivalent to calling <c>Connection.DisconnectAsync(ct)</c>.
        /// </remarks>
        public IConvaiOperation<Unit> DisconnectAsync(CancellationToken ct = default);

        #endregion

        #region Lifecycle

        /// <summary>
        ///     Initializes the room runtime with the given session.
        /// </summary>
        /// <param name="session">The session to initialize with.</param>
        /// <remarks>
        ///     Updates room-runtime state; it does not initiate network I/O.
        /// </remarks>
        public void Initialize(RoomSession session);

        /// <summary>
        ///     Shuts down the room runtime and releases resources.
        /// </summary>
        /// <remarks>
        ///     Updates room-runtime state; it does not initiate network I/O.
        /// </remarks>
        public void Shutdown();

        #endregion
    }
}
