using System;
using Convai.Runtime.Behaviors;

namespace Convai.Runtime.Core.Coordinators
{
    /// <summary>
    ///     Implementation of <see cref="IRoomDiagnostics" /> for tracking room session metrics.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This implementation collects connection attempts, errors, and warnings for
    ///         monitoring and debugging purposes. All operations are thread-safe.
    ///     </para>
    ///     <para>
    ///         <b>Integration</b>: Receives state updates from <see cref="IRoomConnectionCoordinator" />
    ///         and agent counts from <see cref="IAgentRegistry" />.
    ///     </para>
    /// </remarks>
    internal sealed class RoomDiagnostics : IRoomDiagnostics
    {
        private readonly IAgentRegistry _agentRegistry;
        private readonly object _lock = new();

        // Current state
        private string _currentState = "Disconnected";
        private int _failedConnections;
        private DateTime? _lastConnectedAt;
        private DateTime? _lastErrorAt;
        private string _lastErrorCode = string.Empty;
        private string _lastErrorMessage = string.Empty;
        private DateTime? _sessionStartedAt;
        private int _successfulConnections;

        // Connection metrics
        private int _totalConnectionAttempts;

        // Error metrics
        private int _totalErrors;
        private int _totalWarnings;

        /// <summary>
        ///     Creates a new diagnostics instance.
        /// </summary>
        /// <param name="agentRegistry">Registry for counting agents (optional).</param>
        public RoomDiagnostics(IAgentRegistry agentRegistry = null)
        {
            _agentRegistry = agentRegistry;
        }

        /// <inheritdoc />
        public RoomDiagnosticsSnapshot GetDiagnostics()
        {
            lock (_lock)
            {
                TimeSpan? uptime = null;
                if (_sessionStartedAt.HasValue && _currentState != "Disconnected")
                    uptime = DateTime.UtcNow - _sessionStartedAt.Value;

                int characterCount = 0;
                int playerCount = 0;

                // Count active agents from registry
                if (_agentRegistry != null)
                {
                    characterCount = _agentRegistry.Characters.Count;
                    playerCount = _agentRegistry.Players.Count;
                }

                return new RoomDiagnosticsSnapshot(
                    _currentState,
                    _totalConnectionAttempts,
                    _successfulConnections,
                    _failedConnections,
                    _totalErrors,
                    _lastConnectedAt,
                    _lastErrorAt,
                    _lastErrorCode,
                    _lastErrorMessage,
                    uptime,
                    characterCount,
                    playerCount);
            }
        }

        /// <inheritdoc />
        public void RecordConnectionAttempt(bool success)
        {
            lock (_lock)
            {
                _totalConnectionAttempts++;

                if (success)
                {
                    _successfulConnections++;
                    _lastConnectedAt = DateTime.UtcNow;
                    _sessionStartedAt = DateTime.UtcNow;
                    _currentState = "Connected";
                }
                else
                    _failedConnections++;
            }
        }

        /// <inheritdoc />
        public void RecordError(string errorCode, string message)
        {
            lock (_lock)
            {
                _totalErrors++;
                _lastErrorAt = DateTime.UtcNow;
                _lastErrorCode = errorCode ?? string.Empty;
                _lastErrorMessage = message ?? string.Empty;
            }
        }

        /// <inheritdoc />
        public void RecordWarning(string warningCode, string message)
        {
            lock (_lock) _totalWarnings++;
            // Warnings don't update last error fields
        }

        /// <inheritdoc />
        public void Reset()
        {
            lock (_lock)
            {
                _totalConnectionAttempts = 0;
                _successfulConnections = 0;
                _failedConnections = 0;
                _totalErrors = 0;
                _totalWarnings = 0;
                _lastConnectedAt = null;
                _lastErrorAt = null;
                _sessionStartedAt = null;
                _lastErrorCode = string.Empty;
                _lastErrorMessage = string.Empty;
                _currentState = "Disconnected";
            }
        }

        /// <summary>
        ///     Updates the current session state.
        /// </summary>
        /// <param name="stateName">New state name.</param>
        public void UpdateState(string stateName)
        {
            lock (_lock)
            {
                _currentState = stateName ?? "Unknown";

                if (stateName == "Disconnected" || stateName == "Disconnecting") _sessionStartedAt = null;
            }
        }
    }
}
