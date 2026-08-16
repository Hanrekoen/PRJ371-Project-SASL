using System;
using Convai.Domain.Abstractions;
using Convai.Domain.Logging;
using Convai.RestAPI.Services;

namespace Convai.Infrastructure.Networking
{
    internal enum InvalidStoredSessionRecoveryPolicy
    {
        RetryWithoutStoredSessionAllowed,
        RetryWithoutStoredSessionDisallowed
    }

    internal enum RoomInitializationOutcomeStatus
    {
        RoomDetailsAccepted,
        InvalidStoredSession,
        Failed
    }

    internal readonly struct RoomInitializationOutcome
    {
        internal RoomInitializationOutcome(
            RoomInitializationOutcomeStatus status,
            bool attemptedWithStoredSession,
            bool retryWithoutStoredSessionAllowed,
            bool shouldPersistStoredSession,
            bool shouldClearStoredSession,
            string failureMessage)
        {
            Status = status;
            AttemptedWithStoredSession = attemptedWithStoredSession;
            RetryWithoutStoredSessionAllowed = retryWithoutStoredSessionAllowed;
            ShouldPersistStoredSession = shouldPersistStoredSession;
            ShouldClearStoredSession = shouldClearStoredSession;
            FailureMessage = failureMessage;
            DiagnosticsMetadata = CreateDiagnosticsMetadata(
                status,
                attemptedWithStoredSession,
                retryWithoutStoredSessionAllowed,
                shouldPersistStoredSession,
                shouldClearStoredSession,
                failureMessage);
        }

        public RoomInitializationOutcomeStatus Status { get; }
        public bool AttemptedWithStoredSession { get; }
        public bool RetryWithoutStoredSessionAllowed { get; }

        public bool ShouldRetryWithoutStoredSession =>
            Status == RoomInitializationOutcomeStatus.InvalidStoredSession && RetryWithoutStoredSessionAllowed;

        public bool ShouldPersistStoredSession { get; }
        public bool ShouldClearStoredSession { get; }
        public string FailureMessage { get; }
        public string DiagnosticsMetadata { get; }

        public string FormatDiagnosticsLogMessage(string prefix = null) => Prefix(
            prefix,
            $"Room init outcome: {DiagnosticsMetadata}");

        private static string CreateDiagnosticsMetadata(
            RoomInitializationOutcomeStatus status,
            bool attemptedWithStoredSession,
            bool retryWithoutStoredSessionAllowed,
            bool shouldPersistStoredSession,
            bool shouldClearStoredSession,
            string failureMessage)
        {
            string normalizedFailureMessage = string.IsNullOrWhiteSpace(failureMessage) ? "<none>" : failureMessage;
            return
                $"status={status}; attemptedStoredSession={attemptedWithStoredSession}; retryWithoutStoredSessionAllowed={retryWithoutStoredSessionAllowed}; shouldPersistStoredSession={shouldPersistStoredSession}; shouldClearStoredSession={shouldClearStoredSession}; failure={normalizedFailureMessage}";
        }

        private static string Prefix(string prefix, string message) => string.IsNullOrWhiteSpace(prefix)
            ? message
            : $"{prefix} {message}";
    }

    internal static class RoomInitializationRecoverySupport
    {
        private const string InvalidCharacterSessionIdError = "Invalid character_session_id";

        internal static RoomInitializationOutcome FromAccepted(
            string attemptedCharacterSessionId,
            bool enableSessionResume,
            string resolvedCharacterSessionId,
            InvalidStoredSessionRecoveryPolicy recoveryPolicy)
        {
            bool attemptedWithStoredSession = enableSessionResume && !string.IsNullOrEmpty(attemptedCharacterSessionId);
            bool shouldPersistStoredSession = enableSessionResume && !string.IsNullOrEmpty(resolvedCharacterSessionId);
            return new RoomInitializationOutcome(
                RoomInitializationOutcomeStatus.RoomDetailsAccepted,
                attemptedWithStoredSession,
                AllowsRetryWithoutStoredSession(recoveryPolicy),
                shouldPersistStoredSession,
                false,
                null);
        }

        internal static RoomInitializationOutcome FromFailure(
            string attemptedCharacterSessionId,
            bool enableSessionResume,
            string failureMessage,
            InvalidStoredSessionRecoveryPolicy recoveryPolicy)
        {
            bool allowsRetryWithoutStoredSession = AllowsRetryWithoutStoredSession(recoveryPolicy);
            bool attemptedWithStoredSession = enableSessionResume && !string.IsNullOrEmpty(attemptedCharacterSessionId);
            bool invalidStoredSession = attemptedWithStoredSession &&
                                        !string.IsNullOrEmpty(failureMessage) &&
                                        failureMessage.IndexOf(InvalidCharacterSessionIdError,
                                            StringComparison.Ordinal) >= 0;
            return new RoomInitializationOutcome(
                invalidStoredSession
                    ? RoomInitializationOutcomeStatus.InvalidStoredSession
                    : RoomInitializationOutcomeStatus.Failed,
                attemptedWithStoredSession,
                allowsRetryWithoutStoredSession,
                false,
                invalidStoredSession && allowsRetryWithoutStoredSession,
                failureMessage);
        }

        internal static bool TryPersistCharacterSession(
            ISessionPersistence sessionPersistence,
            ILogger logger,
            string characterId,
            string characterSessionId,
            in RoomInitializationOutcome outcome,
            string logPrefix = null)
        {
            if (sessionPersistence == null) throw new ArgumentNullException(nameof(sessionPersistence));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (!outcome.ShouldPersistStoredSession || string.IsNullOrEmpty(characterSessionId)) return false;

            sessionPersistence.SaveSession(characterId, characterSessionId);
            logger.Debug(
                Prefix(logPrefix, $"Stored character session ID for character {characterId}: {characterSessionId}"),
                LogCategory.Transport);
            return true;
        }

        internal static bool TryClearStoredSessionForRecovery(
            ISessionPersistence sessionPersistence,
            ILogger logger,
            string characterId,
            RoomConnectionRequest roomRequest,
            in RoomInitializationOutcome outcome,
            string logPrefix = null)
        {
            if (sessionPersistence == null) throw new ArgumentNullException(nameof(sessionPersistence));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (!outcome.ShouldClearStoredSession) return false;

            logger.Warning(Prefix(logPrefix, CreateRecoveryLogMessage(outcome)), LogCategory.Transport);
            sessionPersistence.ClearSession(characterId);
            if (roomRequest != null) roomRequest.CharacterSessionId = null;
            return true;
        }

        private static bool AllowsRetryWithoutStoredSession(InvalidStoredSessionRecoveryPolicy recoveryPolicy) =>
            recoveryPolicy == InvalidStoredSessionRecoveryPolicy.RetryWithoutStoredSessionAllowed;

        private static string CreateRecoveryLogMessage(in RoomInitializationOutcome outcome) =>
            outcome.ShouldRetryWithoutStoredSession
                ? "Invalid stored character_session_id. Clearing and retrying without session."
                : "Invalid stored character_session_id. Clearing stored session without retry.";

        private static string Prefix(string prefix, string message) => string.IsNullOrWhiteSpace(prefix)
            ? message
            : $"{prefix} {message}";
    }
}
