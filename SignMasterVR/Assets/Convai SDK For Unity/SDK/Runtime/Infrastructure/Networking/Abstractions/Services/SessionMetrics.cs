using System;
using System.Threading;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;

namespace Convai.Infrastructure.Networking.Services
{
    /// <summary>
    ///     Thread-safe implementation of ISessionMetrics that tracks session telemetry
    ///     by subscribing to SessionStateChanged events via EventHub.
    /// </summary>
    /// <remarks>
    ///     Features:
    ///     - Automatic metric collection via SessionStateChanged events
    ///     - Thread-safe counter updates using Interlocked
    ///     - Session duration tracking with pause/resume during reconnection
    ///     - Summary logging on session end
    ///     The metrics are reset on application restart and are not persisted.
    /// </remarks>
    public sealed class SessionMetrics : ISessionMetrics, IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly object _lock = new();
        private readonly ILogger _logger;
        private readonly ISessionStateMachine _stateMachine;
        private readonly SubscriptionToken _stateToken;
        private int _cleanDisconnections;
        private DateTime? _connectedStartTime;
        private long _currentSessionConnectedTimeMs;
        private bool _disposed;
        private int _errorDisconnections;
        private int _failedReconnections;

        private string _lastErrorCode;
        private DateTime? _lastErrorTime;

        private SessionState _previousState = SessionState.Disconnected;

        private int _reconnectionAttempts;
        private DateTime? _sessionEndTime;

        private DateTime? _sessionStartTime;
        private int _successfulReconnections;
        private long _totalConnectedTimeMs;

        private int _totalSessionsStarted;

        /// <summary>
        ///     Creates a new SessionMetrics instance that subscribes to SessionStateChanged events.
        /// </summary>
        /// <param name="eventHub">The EventHub to subscribe to.</param>
        /// <param name="stateMachine">The session state machine to observe for current state.</param>
        /// <param name="logger">Optional logger for diagnostic messages.</param>
        public SessionMetrics(IEventHub eventHub, ISessionStateMachine stateMachine, ILogger logger = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _logger = logger.WithTag(nameof(SessionMetrics));

            _stateToken = _eventHub.Subscribe<SessionStateChanged>(OnStateChanged);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            if (_stateToken != default) _eventHub.Unsubscribe(_stateToken);

            _disposed = true;
        }

        /// <inheritdoc />
        public SessionMetricsSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new SessionMetricsSnapshot(
                    TotalConnectedTime,
                    ReconnectionAttempts,
                    SuccessfulReconnections,
                    FailedReconnections,
                    ReconnectionSuccessRate,
                    TotalSessionsStarted,
                    CleanDisconnections,
                    ErrorDisconnections,
                    _lastErrorCode);
            }
        }

        /// <inheritdoc />
        public void Reset()
        {
            lock (_lock)
            {
                _sessionStartTime = null;
                _sessionEndTime = null;
                _connectedStartTime = null;
                _totalConnectedTimeMs = 0;
                _currentSessionConnectedTimeMs = 0;
                _reconnectionAttempts = 0;
                _successfulReconnections = 0;
                _failedReconnections = 0;
                _totalSessionsStarted = 0;
                _cleanDisconnections = 0;
                _errorDisconnections = 0;
                _lastErrorCode = null;
                _lastErrorTime = null;
                _previousState = SessionState.Disconnected;
            }

            _logger?.Debug("Metrics reset");
        }

        private void OnStateChanged(SessionStateChanged stateChanged)
        {
            SessionState oldState = stateChanged.OldState;
            SessionState newState = stateChanged.NewState;

            lock (_lock)
            {
                _previousState = oldState;

                switch (newState)
                {
                    case SessionState.Connecting:
                        HandleConnecting(oldState);
                        break;

                    case SessionState.Connected:
                        HandleConnected(oldState);
                        break;

                    case SessionState.Reconnecting:
                        HandleReconnecting(oldState);
                        break;

                    case SessionState.Disconnecting:
                        HandleDisconnecting(oldState);
                        break;

                    case SessionState.Disconnected:
                        HandleDisconnected(oldState);
                        break;

                    case SessionState.Error:
                        HandleError(oldState, stateChanged.ErrorCode);
                        break;
                }
            }
        }

        private void HandleConnecting(SessionState oldState)
        {
            if (oldState == SessionState.Disconnected)
            {
                _sessionStartTime = DateTime.UtcNow;
                _sessionEndTime = null;
                _currentSessionConnectedTimeMs = 0;
                Interlocked.Increment(ref _totalSessionsStarted);

                _logger?.Debug("Session started");
            }
        }

        private void HandleConnected(SessionState oldState)
        {
            DateTime now = DateTime.UtcNow;

            if (oldState == SessionState.Connecting)
            {
                _connectedStartTime = now;
                _logger?.Debug("Connected - starting duration timer");
            }
            else if (oldState == SessionState.Reconnecting)
            {
                Interlocked.Increment(ref _successfulReconnections);
                _connectedStartTime = now;

                _logger?.Info($"Reconnection successful (attempt #{ReconnectionAttempts}, " +
                              $"success rate: {ReconnectionSuccessRate:P0})");
            }
        }

        private void HandleReconnecting(SessionState oldState)
        {
            if (_connectedStartTime != null)
            {
                long elapsedMs = (long)(DateTime.UtcNow - _connectedStartTime.Value).TotalMilliseconds;
                _currentSessionConnectedTimeMs += elapsedMs;
                Interlocked.Add(ref _totalConnectedTimeMs, elapsedMs);
                _connectedStartTime = null;
            }

            Interlocked.Increment(ref _reconnectionAttempts);

            _logger?.Debug($"Reconnection attempt #{ReconnectionAttempts}");
        }

        private void HandleDisconnecting(SessionState oldState)
        {
            if (_connectedStartTime != null)
            {
                long elapsedMs = (long)(DateTime.UtcNow - _connectedStartTime.Value).TotalMilliseconds;
                _currentSessionConnectedTimeMs += elapsedMs;
                Interlocked.Add(ref _totalConnectedTimeMs, elapsedMs);
                _connectedStartTime = null;
            }
        }

        private void HandleDisconnected(SessionState oldState)
        {
            DateTime now = DateTime.UtcNow;
            _sessionEndTime = now;

            if (_connectedStartTime != null)
            {
                long elapsedMs = (long)(now - _connectedStartTime.Value).TotalMilliseconds;
                _currentSessionConnectedTimeMs += elapsedMs;
                Interlocked.Add(ref _totalConnectedTimeMs, elapsedMs);
                _connectedStartTime = null;
            }

            if (oldState == SessionState.Disconnecting)
            {
                Interlocked.Increment(ref _cleanDisconnections);
                LogSessionSummary("clean disconnect");
            }
            else if (oldState == SessionState.Error)
            {
                Interlocked.Increment(ref _errorDisconnections);
                LogSessionSummary("error recovery");
            }
        }

        private void HandleError(SessionState oldState, string errorCode)
        {
            _lastErrorCode = errorCode;
            _lastErrorTime = DateTime.UtcNow;

            if (_connectedStartTime != null)
            {
                long elapsedMs = (long)(DateTime.UtcNow - _connectedStartTime.Value).TotalMilliseconds;
                _currentSessionConnectedTimeMs += elapsedMs;
                Interlocked.Add(ref _totalConnectedTimeMs, elapsedMs);
                _connectedStartTime = null;
            }

            if (oldState == SessionState.Reconnecting)
            {
                Interlocked.Increment(ref _failedReconnections);
                _logger?.Warning($"Reconnection failed (error: {errorCode})");
            }
            else
                _logger?.Warning($"Session error: {errorCode}");
        }

        private void LogSessionSummary(string reason)
        {
            if (_logger == null) return;

            SessionMetricsSnapshot snapshot = GetSnapshot();
            _logger.Info($"Session ended ({reason}): {snapshot}");
        }

        #region ISessionMetrics Properties

        /// <inheritdoc />
        public TimeSpan CurrentSessionDuration
        {
            get
            {
                lock (_lock)
                {
                    if (_connectedStartTime == null) return TimeSpan.FromMilliseconds(_currentSessionConnectedTimeMs);

                    long elapsedMs = (long)(DateTime.UtcNow - _connectedStartTime.Value).TotalMilliseconds;
                    return TimeSpan.FromMilliseconds(_currentSessionConnectedTimeMs + elapsedMs);
                }
            }
        }

        /// <inheritdoc />
        public TimeSpan TotalConnectedTime
        {
            get
            {
                lock (_lock)
                {
                    long currentMs = 0;
                    if (_connectedStartTime != null)
                        currentMs = (long)(DateTime.UtcNow - _connectedStartTime.Value).TotalMilliseconds;

                    return TimeSpan.FromMilliseconds(Volatile.Read(ref _totalConnectedTimeMs) + currentMs);
                }
            }
        }

        /// <inheritdoc />
        public DateTime? SessionStartTime
        {
            get
            {
                lock (_lock) return _sessionStartTime;
            }
        }

        /// <inheritdoc />
        public DateTime? SessionEndTime
        {
            get
            {
                lock (_lock) return _sessionEndTime;
            }
        }

        /// <inheritdoc />
        public int ReconnectionAttempts => Volatile.Read(ref _reconnectionAttempts);

        /// <inheritdoc />
        public int SuccessfulReconnections => Volatile.Read(ref _successfulReconnections);

        /// <inheritdoc />
        public int FailedReconnections => Volatile.Read(ref _failedReconnections);

        /// <inheritdoc />
        public float ReconnectionSuccessRate
        {
            get
            {
                int attempts = ReconnectionAttempts;
                if (attempts == 0) return 1.0f;

                return (float)SuccessfulReconnections / attempts;
            }
        }

        /// <inheritdoc />
        public int TotalSessionsStarted => Volatile.Read(ref _totalSessionsStarted);

        /// <inheritdoc />
        public int CleanDisconnections => Volatile.Read(ref _cleanDisconnections);

        /// <inheritdoc />
        public int ErrorDisconnections => Volatile.Read(ref _errorDisconnections);

        /// <inheritdoc />
        public string LastErrorCode
        {
            get
            {
                lock (_lock) return _lastErrorCode;
            }
        }

        /// <inheritdoc />
        public DateTime? LastErrorTime
        {
            get
            {
                lock (_lock) return _lastErrorTime;
            }
        }

        #endregion
    }
}
