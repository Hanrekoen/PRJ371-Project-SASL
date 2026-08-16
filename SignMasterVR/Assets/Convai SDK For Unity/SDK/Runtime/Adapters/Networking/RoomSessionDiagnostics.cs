using System;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Services;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class RoomSessionDiagnostics
    {
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;
        private readonly Action _onConnected;
        private readonly Action<SessionState> _onConnectionStateChanged;
        private readonly Action<RoomOwnershipRebindStateChanged> _onOwnershipRebindStateChanged;
        private readonly Action<SessionError> _onSessionError;
        private readonly Action<SessionStateChanged> _onSessionStateChanged;
        private readonly Func<string> _sessionIdProvider;
        private readonly ISessionService _sessionService;
        private readonly ISessionStateMachine _sessionStateMachine;

        public RoomSessionDiagnostics(
            ISessionStateMachine sessionStateMachine,
            ISessionService sessionService,
            IEventHub eventHub,
            ILogger logger,
            Func<string> sessionIdProvider,
            Action onConnected = null,
            Action<SessionError> onSessionError = null,
            Action<SessionStateChanged> onSessionStateChanged = null,
            Action<SessionState> onConnectionStateChanged = null,
            Action<RoomOwnershipRebindStateChanged> onOwnershipRebindStateChanged = null)
        {
            _sessionStateMachine = sessionStateMachine;
            _sessionService = sessionService;
            _eventHub = eventHub;
            _logger = logger.WithTag("ConvaiRoomManager");
            _sessionIdProvider = sessionIdProvider ?? throw new ArgumentNullException(nameof(sessionIdProvider));
            _onConnected = onConnected;
            _onSessionError = onSessionError;
            _onSessionStateChanged = onSessionStateChanged;
            _onConnectionStateChanged = onConnectionStateChanged;
            _onOwnershipRebindStateChanged = onOwnershipRebindStateChanged;
        }

        public void UpdateSessionState(SessionState newState, SessionError? error = null)
        {
            if (_sessionStateMachine == null)
            {
                _logger?.Warning("Session state machine not initialized; skipping state update.");
                return;
            }

            string sessionId = _sessionIdProvider();
            if (_sessionStateMachine.TryTransition(newState, sessionId, error))
                return;

            _logger?.Warning($"Invalid transition to {newState}; forcing transition.");
            _sessionStateMachine.ForceTransition(newState, sessionId, error);
        }

        public ConnectionContext RecordConnectionSuccess(
            string roomName,
            string characterSessionId,
            string sessionId,
            string characterId,
            bool enableSessionResume)
        {
            var connectionContext = new ConnectionContext(
                roomName,
                characterSessionId,
                sessionId,
                characterId,
                DateTime.UtcNow);

            if (_sessionService != null)
            {
                _sessionService.SetActiveSession(characterId, sessionId);

                if (enableSessionResume && !string.IsNullOrEmpty(characterSessionId))
                    _sessionService.StoreSession(characterId, characterSessionId);
            }

            _onConnected?.Invoke();
            return connectionContext;
        }

        public SessionError RecordConnectionFailure(ConnectionFailure failure)
        {
            var sessionError = failure.ToSessionError(_sessionIdProvider());
            UpdateSessionState(SessionState.Error, sessionError);
            _eventHub?.Publish(sessionError);
            _onSessionError?.Invoke(sessionError);
            return sessionError;
        }

        public RoomOwnershipRebindStateChanged PublishOwnershipRebindState(
            RoomOwnershipRebindStatus outcome,
            bool hasPendingReconnect,
            SessionState sessionState,
            string activeCharacterId,
            string requestedCharacterId)
        {
            var stateChanged = RoomOwnershipRebindStateChanged.Create(
                outcome,
                hasPendingReconnect,
                sessionState,
                activeCharacterId,
                requestedCharacterId);
            _eventHub?.Publish(stateChanged);
            _onOwnershipRebindStateChanged?.Invoke(stateChanged);
            return stateChanged;
        }

        public ConnectionContext CompleteDisconnectionTracking(
            ConnectionContext connectionContext,
            bool updateSessionState,
            string completionMessage)
        {
            if (updateSessionState)
                UpdateSessionState(SessionState.Disconnected);

            ConnectionContext updatedContext = connectionContext ?? ConnectionContext.Empty;
            if (updatedContext.HasValidRoom)
                updatedContext = updatedContext.WithDisconnection(DateTime.UtcNow);

            _sessionService?.ClearActiveSession();
            _logger?.Info($"{completionMessage}");
            return updatedContext;
        }

        public void HandleStateChanged(SessionStateChanged stateChanged)
        {
            _onSessionStateChanged?.Invoke(stateChanged);
            _onConnectionStateChanged?.Invoke(stateChanged.NewState);
        }
    }
}
