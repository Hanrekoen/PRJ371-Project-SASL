using System;
using System.Collections.Generic;
using Convai.Domain.Logging;

namespace Convai.Infrastructure.Networking.Connection
{
    /// <summary>
    ///     Thread-safe implementation of IConnectionStateMachine for managing connection state transitions.
    /// </summary>
    /// <remarks>
    ///     Features: <br />
    ///     - Validated state transitions based on predefined rules
    ///     - Thread-safe state access and modification
    ///     - Event notification on state changes
    ///     - Force transition for recovery scenarios
    ///     State Transition Graph:
    ///     Disconnected ──> Connecting
    ///     Connecting ──> Connected
    ///     Connecting ──> Disconnected (on error or cancel)
    ///     Connected ──> Reconnecting
    ///     Connected ──> Disconnecting (graceful disconnect)
    ///     Reconnecting ──> Connected
    ///     Reconnecting ──> Disconnected (on reconnection failure)
    ///     Disconnecting ──> Disconnected
    /// </remarks>
    public sealed class ConnectionStateMachine : IConnectionStateMachine
    {
        private static readonly Dictionary<ConnectionState, HashSet<ConnectionState>> ValidTransitions = new()
        {
            [ConnectionState.Disconnected] = new HashSet<ConnectionState> { ConnectionState.Connecting },
            [ConnectionState.Connecting] =
                new HashSet<ConnectionState> { ConnectionState.Connected, ConnectionState.Disconnected },
            [ConnectionState.Connected] =
                new HashSet<ConnectionState> { ConnectionState.Reconnecting, ConnectionState.Disconnecting },
            [ConnectionState.Reconnecting] =
                new HashSet<ConnectionState> { ConnectionState.Connected, ConnectionState.Disconnected },
            [ConnectionState.Disconnecting] = new HashSet<ConnectionState> { ConnectionState.Disconnected }
        };

        private readonly object _lock = new();
        private readonly ILogger _logger;
        private ConnectionState _currentState = ConnectionState.Disconnected;

        public ConnectionStateMachine(ILogger logger = null)
        {
            _logger = logger.WithTag(nameof(ConnectionStateMachine));
        }

        /// <inheritdoc />
        public ConnectionState CurrentState
        {
            get
            {
                lock (_lock) return _currentState;
            }
        }

        /// <inheritdoc />
        public event Action<ConnectionState, ConnectionState, string> StateChanged;

        /// <inheritdoc />
        public bool TryTransition(ConnectionState newState, string errorMessage = null)
        {
            lock (_lock)
            {
                if (_currentState == newState) return false;

                if (!IsValidTransition(_currentState, newState))
                {
                    _logger?.Warning(
                        $"Invalid transition: {_currentState} -> {newState}",
                        LogCategory.Transport);
                    return false;
                }

                ConnectionState oldState = _currentState;
                _currentState = newState;

                _logger?.Debug(
                    $"State changed: {oldState} -> {newState}",
                    LogCategory.Transport);

                RaiseStateChanged(oldState, newState, errorMessage);

                return true;
            }
        }

        /// <inheritdoc />
        public void ForceTransition(ConnectionState newState, string errorMessage = null)
        {
            lock (_lock)
            {
                if (_currentState == newState) return;

                ConnectionState oldState = _currentState;
                _currentState = newState;

                _logger?.Warning(
                    $"Forced transition: {oldState} -> {newState}",
                    LogCategory.Transport);

                RaiseStateChanged(oldState, newState, errorMessage);
            }
        }

        /// <inheritdoc />
        public bool CanTransitionTo(ConnectionState targetState)
        {
            lock (_lock) return IsValidTransition(_currentState, targetState);
        }

        /// <inheritdoc />
        public void Reset()
        {
            lock (_lock)
            {
                if (_currentState == ConnectionState.Disconnected) return;

                ConnectionState oldState = _currentState;
                _currentState = ConnectionState.Disconnected;

                _logger?.Debug(
                    $"Reset: {oldState} -> Disconnected",
                    LogCategory.Transport);

                RaiseStateChanged(oldState, ConnectionState.Disconnected, null);
            }
        }

        /// <summary>
        ///     Checks if a transition from one state to another is valid.
        /// </summary>
        private static bool IsValidTransition(ConnectionState from, ConnectionState to)
        {
            if (!ValidTransitions.TryGetValue(from, out HashSet<ConnectionState> validTargets)) return false;

            return validTargets.Contains(to);
        }

        /// <summary>
        ///     Raises the StateChanged event.
        /// </summary>
        private void RaiseStateChanged(ConnectionState oldState, ConnectionState newState, string errorMessage)
        {
            try
            {
                StateChanged?.Invoke(oldState, newState, errorMessage);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"Exception in StateChanged handler: {ex.Message}",
                    LogCategory.Transport);
            }
        }
    }
}
