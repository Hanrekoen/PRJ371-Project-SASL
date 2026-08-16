using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Core.Async;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Implementation of <see cref="IRoomConnectionCoordinator" /> for managing room connections.
    /// </summary>
    /// <remarks>
    ///     This coordinator wraps the existing connection infrastructure (ConvaiRoomController,
    ///     SessionStateMachine) and exposes a simplified interface for connection management.
    ///     Uses delegates to integrate with ConvaiRoomManager's connection methods without tight coupling.
    /// </remarks>
    internal sealed class RoomConnectionCoordinator : IRoomConnectionCoordinator, IDisposable
    {
        private readonly IRoomConnectionRuntimeAdapter _connectionRuntimeAdapter;
        private readonly IRoomDiagnostics _diagnostics;
        private readonly object _lock = new();
        private readonly ILogger _logger;
        private Task<RoomSession> _connectTask;
        private RoomSession _currentSession = RoomSession.Empty;
        private bool _disposed;

        public RoomConnectionCoordinator(
            IRoomConnectionRuntimeAdapter connectionRuntimeAdapter,
            IRoomDiagnostics diagnostics = null,
            ILogger logger = null)
        {
            _connectionRuntimeAdapter = connectionRuntimeAdapter ??
                                        throw new ArgumentNullException(nameof(connectionRuntimeAdapter));
            _diagnostics = diagnostics;
            _logger = logger.WithTag(nameof(RoomConnectionCoordinator));
            _connectionRuntimeAdapter.StateChanged += HandleRuntimeStateChanged;
        }

        /// <summary>
        ///     Disposes the coordinator.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connectionRuntimeAdapter.StateChanged -= HandleRuntimeStateChanged;
        }

        /// <inheritdoc />
        public SessionState CurrentState => _connectionRuntimeAdapter.CurrentState;

        /// <inheritdoc />
        public bool IsConnected => _connectionRuntimeAdapter.IsConnected;

        /// <inheritdoc />
        public RoomSession CurrentSession
        {
            get
            {
                lock (_lock)
                    return _currentSession;
            }
        }

        /// <inheritdoc />
        public string RoomCode => null; // Single-player mode has no room code

        /// <inheritdoc />
        public bool IsRoomOwner => true; // Single-player mode is always owner

        /// <inheritdoc />
        public event Action<SessionStateChanged> StateChanged;

        /// <inheritdoc />
        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken ct = default) =>
            ConvaiOperation<RoomSession>.FromTask(ConnectAsyncCore(ct));

        /// <inheritdoc />
        public IConvaiOperation<Unit> DisconnectAsync(CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(DisconnectAsyncCore(ct));

        /// <inheritdoc />
        /// <remarks>
        ///     Returns a local session in single-player mode. Multi-participant support planned for future release.
        /// </remarks>
        public IConvaiOperation<RoomSession> CreateRoomAsync(RoomCreateOptions options, CancellationToken ct = default)
        {
            _logger?.Info("CreateRoomAsync (single-player stub)");

            return ConnectAsync(ct);
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Returns failure in single-player mode. Multi-participant support planned for future release.
        /// </remarks>
        public IConvaiOperation<JoinResult> JoinRoomAsync(string roomCode, CancellationToken ct = default)
        {
            _logger?.Warning("JoinRoomAsync not available in single-player mode");

            return ConvaiOperation<JoinResult>.Succeeded(JoinResult.Failed(
                "Room joining is not available in single-player mode."));
        }

        private async Task<RoomSession> ConnectAsyncCore(CancellationToken ct)
        {
            if (!_connectionRuntimeAdapter.CanConnect)
            {
                _logger?.Warning("ConnectAsync rejected because the host is disabled.");
                throw new ConvaiOperationException(
                    SessionErrorCodes.ConnectionFailed,
                    "ConnectAsync rejected because the host is disabled.");
            }

            if (IsConnected)
                return EnsureConnectedSession();

            if (CurrentState == SessionState.Error)
            {
                _logger?.Warning("ConnectAsync called while the session is in error.");
                throw new ConvaiOperationException(
                    SessionErrorCodes.ConnectionFailed,
                    "ConnectAsync cannot proceed while the session is in the error state.");
            }

            if (CurrentState == SessionState.Disconnected)
            {
                if (!_connectionRuntimeAdapter.HasStarted)
                {
                    _logger?.Info("Waiting for host startup to complete before connect.");

                    if (!await _connectionRuntimeAdapter.WaitForStartCompletionAsync(ct))
                    {
                        _logger?.Error("Host startup did not complete before connect.");
                        throw new ConvaiOperationException(
                            SessionErrorCodes.ConnectionTimeout,
                            "Host startup did not complete before connect.");
                    }
                }

                if (!_connectionRuntimeAdapter.PrepareConnection())
                {
                    _logger?.Warning(
                        "ConnectAsync aborted because connection preparation failed.");
                    throw new ConvaiOperationException(
                        SessionErrorCodes.ConnectionFailed,
                        "ConnectAsync aborted because connection preparation failed.");
                }

                Task<RoomSession> connectTask;
                lock (_lock)
                {
                    if (_connectTask is { IsCompleted: false })
                        connectTask = _connectTask;
                    else
                        _connectTask = connectTask = ExecuteConnectAttemptAsync(ct);
                }

                return await connectTask;
            }

            if (CurrentState == SessionState.Connecting)
            {
                _logger?.Info("ConnectAsync waiting for the active connection attempt.");

                bool connected = await _connectionRuntimeAdapter.WaitForConnectionResolutionAsync(ct);
                if (connected)
                    return EnsureConnectedSession();

                throw new ConvaiOperationException(
                    SessionErrorCodes.ConnectionFailed,
                    "The active connection attempt did not resolve successfully.");
            }

            if (CurrentState == SessionState.Disconnecting)
            {
                _logger?.Warning("ConnectAsync called while disconnecting.");
                throw new ConvaiOperationException(
                    SessionErrorCodes.SessionInvalidState,
                    "ConnectAsync cannot proceed while disconnecting.");
            }

            return IsConnected
                ? EnsureConnectedSession()
                : throw new ConvaiOperationException(
                    SessionErrorCodes.ConnectionFailed,
                    "ConnectAsync could not establish a room session.");
        }

        private async Task<RoomSession> ExecuteConnectAttemptAsync(CancellationToken ct)
        {
            _logger?.Debug("ConnectAsync starting...");
            _diagnostics?.RecordConnectionAttempt(false);

            try
            {
                RoomConnectionAttemptResult attemptResult = await _connectionRuntimeAdapter.ConnectAsync(ct);

                if (attemptResult.Succeeded)
                {
                    lock (_lock)
                        _currentSession = CreateCurrentSession();

                    _diagnostics?.RecordConnectionAttempt(true);
                    _logger?.Info("Connection successful.");
                    return _currentSession;
                }

                ConnectionFailure failure = attemptResult.Failure;
                _diagnostics?.RecordError(failure.Code, failure.Message);
                _logger?.Warning($"Connection failed: {failure.Message}");
                throw failure.ToException();
            }
            catch (OperationCanceledException)
            {
                _logger?.Info("Connection cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _diagnostics?.RecordError("ConnectionException", ex.Message);
                _logger?.Error($"Connection error: {ex.Message}");
                throw;
            }
        }

        private async Task<Unit> DisconnectAsyncCore(CancellationToken ct)
        {
            _logger?.Debug("DisconnectAsync starting...");

            try
            {
                await _connectionRuntimeAdapter.DisconnectAsync(ct);

                lock (_lock) _currentSession = RoomSession.Empty;

                _logger?.Info("Disconnection complete.");
            }
            catch (Exception ex)
            {
                _diagnostics?.RecordError("DisconnectionException", ex.Message);
                _logger?.Error($"Disconnection error: {ex.Message}");
            }

            return Unit.Value;
        }

        private RoomSession CreateCurrentSession() =>
            new(
                _connectionRuntimeAdapter.CurrentSessionId ?? Guid.NewGuid().ToString("N"),
                _connectionRuntimeAdapter.CurrentRoomName,
                "local-player",
                DateTime.UtcNow);

        private RoomSession EnsureConnectedSession()
        {
            lock (_lock)
            {
                if (!_currentSession.IsValid)
                    _currentSession = CreateCurrentSession();

                return _currentSession;
            }
        }

        private void HandleRuntimeStateChanged(SessionStateChanged stateChanged) =>
            StateChanged?.Invoke(stateChanged);

        /// <summary>
        ///     Raises the StateChanged event.
        /// </summary>
        internal void OnStateChanged(SessionStateChanged stateChanged) => StateChanged?.Invoke(stateChanged);
    }
}
