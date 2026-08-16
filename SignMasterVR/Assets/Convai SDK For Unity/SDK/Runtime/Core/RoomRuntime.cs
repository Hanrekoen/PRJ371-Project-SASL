using System;
using System.Threading;
using Convai.Domain.Logging;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;

namespace Convai.Runtime.Core
{
    /// <summary>
    ///     Implementation of <see cref="IRoomRuntime" /> that composes room coordinators.
    /// </summary>
    /// <remarks>
    ///     <b>Creation</b>: Use <see cref="RoomRuntimeBuilder" /> to create instances
    ///     with proper coordinator wiring.
    /// </remarks>
    internal sealed class RoomRuntime : IRoomRuntime, IDisposable
    {
        private readonly object _lock = new();
        private readonly ILogger _logger;
        private bool _disposed;

        internal RoomRuntime(
            IRoomConnectionCoordinator connection,
            IRoomAudioCoordinator audio,
            IRoomOwnershipCoordinator ownership,
            IRoomDiagnostics diagnostics,
            ILogger logger = null)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Audio = audio ?? throw new ArgumentNullException(nameof(audio));
            Ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _logger = logger.WithTag(nameof(RoomRuntime));
        }

        #region IDisposable

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                Shutdown();

                // Dispose coordinators if they implement IDisposable
                (Connection as IDisposable)?.Dispose();
                (Audio as IDisposable)?.Dispose();
                (Ownership as IDisposable)?.Dispose();
                (Diagnostics as IDisposable)?.Dispose();

                _disposed = true;
                _logger?.Debug("Disposed.");
            }
        }

        #endregion

        #region IRoomRuntime - State

        /// <inheritdoc />
        public bool IsActive { get; private set; }

        /// <inheritdoc />
        public RoomSession Session { get; private set; }

        #endregion

        #region IRoomRuntime - Coordinators

        /// <inheritdoc />
        public IRoomConnectionCoordinator Connection { get; }

        /// <inheritdoc />
        public IRoomAudioCoordinator Audio { get; }

        /// <inheritdoc />
        public IRoomOwnershipCoordinator Ownership { get; }

        /// <inheritdoc />
        public IRoomDiagnostics Diagnostics { get; }

        #endregion

        #region IRoomRuntime - Connection Convenience

        /// <inheritdoc />
        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken ct = default) =>
            Connection.ConnectAsync(ct);

        /// <inheritdoc />
        public IConvaiOperation<Unit> DisconnectAsync(CancellationToken ct = default) => Connection.DisconnectAsync(ct);

        #endregion

        #region IRoomRuntime - Lifecycle

        /// <inheritdoc />
        public void Initialize(RoomSession session)
        {
            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(RoomRuntime));

                if (IsActive)
                    throw new InvalidOperationException("Room runtime is already initialized.");

                Session = session;
                IsActive = true;

                _logger?.Debug($"Initialized with session: {session.RoomId}");
            }
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            lock (_lock)
            {
                if (!IsActive)
                    return;

                _logger?.Debug("Shutting down...");

                IsActive = false;
                Session = RoomSession.Empty;

                _logger?.Info("Shutdown complete.");
            }
        }

        #endregion
    }
}
