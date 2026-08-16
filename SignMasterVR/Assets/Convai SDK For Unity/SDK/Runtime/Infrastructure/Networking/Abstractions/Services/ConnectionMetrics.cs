using System;
using System.Threading;

namespace Convai.Infrastructure.Networking.Services
{
    /// <summary>
    ///     Thread-safe implementation of IConnectionMetrics for tracking connection statistics.
    /// </summary>
    /// <remarks>
    ///     All operations use Interlocked for atomic counter updates.
    ///     Uses lock for complex operations involving multiple fields.
    ///     Metrics are in-memory only and not persisted.
    /// </remarks>
    public sealed class ConnectionMetrics
    {
        private readonly object _lock = new();
        private int _disconnectionCount;
        private int _failedConnections;

        private TimeSpan _lastConnectionDuration;
        private string _lastErrorCode;
        private DateTime? _lastErrorTime;
        private int _reconnectionAttempts;

        private DateTime? _sessionStartTime;
        private int _successfulConnections;
        private int _successfulReconnections;
        private long _totalConnectedTimeMs;

        private int _totalConnectionAttempts;
        private long _totalConnectionTimeMs;

        #region IConnectionMetrics Properties

        /// <inheritdoc />
        public int TotalConnectionAttempts => Volatile.Read(ref _totalConnectionAttempts);

        /// <inheritdoc />
        public int SuccessfulConnections => Volatile.Read(ref _successfulConnections);

        /// <inheritdoc />
        public int FailedConnections => Volatile.Read(ref _failedConnections);

        /// <inheritdoc />
        public int ReconnectionAttempts => Volatile.Read(ref _reconnectionAttempts);

        /// <inheritdoc />
        public int SuccessfulReconnections => Volatile.Read(ref _successfulReconnections);

        /// <inheritdoc />
        public int DisconnectionCount => Volatile.Read(ref _disconnectionCount);

        /// <inheritdoc />
        public TimeSpan LastConnectionDuration
        {
            get
            {
                lock (_lock) return _lastConnectionDuration;
            }
        }

        /// <inheritdoc />
        public TimeSpan AverageConnectionTime
        {
            get
            {
                lock (_lock)
                {
                    int count = _successfulConnections;
                    if (count == 0)
                        return TimeSpan.Zero;
                    return TimeSpan.FromMilliseconds(_totalConnectionTimeMs / count);
                }
            }
        }

        /// <inheritdoc />
        public TimeSpan TotalConnectedTime
        {
            get
            {
                lock (_lock) return TimeSpan.FromMilliseconds(_totalConnectedTimeMs);
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
        public TimeSpan CurrentSessionDuration
        {
            get
            {
                lock (_lock)
                {
                    if (!_sessionStartTime.HasValue)
                        return TimeSpan.Zero;
                    return DateTime.UtcNow - _sessionStartTime.Value;
                }
            }
        }

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

        #region IConnectionMetrics Methods

        /// <inheritdoc />
        public void RecordConnectionAttempt() => Interlocked.Increment(ref _totalConnectionAttempts);

        /// <inheritdoc />
        public void RecordConnectionSuccess(TimeSpan duration)
        {
            Interlocked.Increment(ref _successfulConnections);

            lock (_lock)
            {
                _lastConnectionDuration = duration;
                _totalConnectionTimeMs += (long)duration.TotalMilliseconds;
                _sessionStartTime = DateTime.UtcNow;
            }
        }

        /// <inheritdoc />
        public void RecordConnectionFailure(string errorCode)
        {
            Interlocked.Increment(ref _failedConnections);

            lock (_lock)
            {
                _lastErrorCode = errorCode;
                _lastErrorTime = DateTime.UtcNow;
            }
        }

        /// <inheritdoc />
        public void RecordReconnectionAttempt() => Interlocked.Increment(ref _reconnectionAttempts);

        /// <inheritdoc />
        public void RecordReconnectionSuccess()
        {
            Interlocked.Increment(ref _successfulReconnections);

            lock (_lock) _sessionStartTime = DateTime.UtcNow;
        }

        /// <inheritdoc />
        public void RecordDisconnection()
        {
            Interlocked.Increment(ref _disconnectionCount);

            lock (_lock)
            {
                if (_sessionStartTime.HasValue)
                {
                    TimeSpan sessionDuration = DateTime.UtcNow - _sessionStartTime.Value;
                    _totalConnectedTimeMs += (long)sessionDuration.TotalMilliseconds;
                    _sessionStartTime = null;
                }
            }
        }

        /// <inheritdoc />
        public void Reset()
        {
            lock (_lock)
            {
                _totalConnectionAttempts = 0;
                _successfulConnections = 0;
                _failedConnections = 0;
                _reconnectionAttempts = 0;
                _successfulReconnections = 0;
                _disconnectionCount = 0;
                _lastConnectionDuration = TimeSpan.Zero;
                _totalConnectionTimeMs = 0;
                _totalConnectedTimeMs = 0;
                _sessionStartTime = null;
                _lastErrorCode = null;
                _lastErrorTime = null;
            }
        }

        #endregion
    }
}
