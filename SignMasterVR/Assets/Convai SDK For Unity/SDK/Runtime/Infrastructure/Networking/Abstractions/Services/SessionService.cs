using System;
using Convai.Domain.Abstractions;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Models;

namespace Convai.Infrastructure.Networking.Services
{
    /// <summary>
    ///     Implementation of ISessionService that wraps ISessionPersistence with business logic.
    /// </summary>
    public sealed class SessionService : ISessionService
    {
        private readonly object _lock = new();
        private readonly ILogger _logger;
        private readonly ISessionPersistence _persistence;
        private string _activeCharacterId;

        private string _activeSessionId;

        public SessionService(ISessionPersistence persistence, ILogger logger = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _logger = logger.WithTag(nameof(SessionService));
        }

        /// <inheritdoc />
        public string ActiveSessionId
        {
            get
            {
                lock (_lock) return _activeSessionId;
            }
        }

        /// <inheritdoc />
        public string ActiveCharacterId
        {
            get
            {
                lock (_lock) return _activeCharacterId;
            }
        }

        /// <inheritdoc />
        public string LoadStoredSession(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            return _persistence.LoadSession(characterId);
        }

        /// <inheritdoc />
        public void StoreSession(string characterId, string sessionId)
        {
            if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(sessionId)) return;

            _persistence.SaveSession(characterId, sessionId);
            _logger?.Debug($"Stored session for character {characterId}: {sessionId}");
        }

        /// <inheritdoc />
        public void ClearStoredSession(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            _persistence.ClearSession(characterId);
            _logger?.Debug($"Cleared stored session for character {characterId}");
        }

        /// <inheritdoc />
        public void ClearAllStoredSessions()
        {
            _persistence.ClearAllSessions();
            _logger?.Debug("Cleared all stored sessions");
        }

        /// <inheritdoc />
        public bool HasStoredSession(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;

            return _persistence.HasSession(characterId);
        }

        /// <inheritdoc />
        public bool ShouldResumeSession(string characterId, ResumePolicy policy)
        {
            return policy switch
            {
                ResumePolicy.AlwaysFresh => false,
                ResumePolicy.AlwaysResume => true,
                ResumePolicy.ResumeIfPossible => HasStoredSession(characterId),
                _ => false
            };
        }

        /// <inheritdoc />
        public string GetSessionIdForConnection(string characterId, ResumePolicy policy,
            string explicitSessionId = null)
        {
            if (!string.IsNullOrEmpty(explicitSessionId))
            {
                _logger?.Debug($"Using explicit session ID: {explicitSessionId}");
                return explicitSessionId;
            }

            if (policy == ResumePolicy.AlwaysFresh)
            {
                _logger?.Debug("AlwaysFresh policy - starting fresh session");
                return null;
            }

            string storedSession = LoadStoredSession(characterId);
            if (!string.IsNullOrEmpty(storedSession))
            {
                _logger?.Debug($"Using stored session: {storedSession}");
                return storedSession;
            }

            if (policy == ResumePolicy.AlwaysResume)
                _logger?.Warning("AlwaysResume policy but no stored session found");

            return null;
        }

        /// <inheritdoc />
        public void SetActiveSession(string characterId, string sessionId)
        {
            lock (_lock)
            {
                _activeCharacterId = characterId;
                _activeSessionId = sessionId;
            }

            _logger?.Debug($"Active session set: character={characterId}, session={sessionId}");
        }

        /// <inheritdoc />
        public void ClearActiveSession()
        {
            lock (_lock)
            {
                _activeCharacterId = null;
                _activeSessionId = null;
            }

            _logger?.Debug("Active session cleared");
        }
    }
}
