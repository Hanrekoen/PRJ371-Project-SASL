using System;
using System.Collections.Generic;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Owns protocol identity correlation for one room's character responses.
    /// </summary>
    internal sealed class CharacterResponseState
    {
        private readonly Dictionary<string, string> _interactionIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _responseIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LipSyncResponseOwner> _speechOwners = new(StringComparer.Ordinal);
        private readonly object _stateLock = new();
        private long _nextResponseId = 1;

        public string BeginResponse(string participantId)
        {
            string safeParticipantId = participantId ?? string.Empty;
            lock (_stateLock)
            {
                string responseId = CreateResponseIdLocked(safeParticipantId);
                _responseIds[safeParticipantId] = responseId;
                return responseId;
            }
        }

        public string EnsureResponseId(string participantId)
        {
            string safeParticipantId = participantId ?? string.Empty;
            lock (_stateLock)
            {
                if (_responseIds.TryGetValue(safeParticipantId, out string responseId) &&
                    !string.IsNullOrWhiteSpace(responseId))
                    return responseId;

                responseId = CreateResponseIdLocked(safeParticipantId);
                _responseIds[safeParticipantId] = responseId;
                return responseId;
            }
        }

        public void RegisterInteraction(string participantId, string interactionId)
        {
            if (string.IsNullOrWhiteSpace(interactionId)) return;

            lock (_stateLock)
            {
                _interactionIds[participantId ?? string.Empty] = interactionId;
            }
        }

        public void PromoteAnonymousParticipant(string participantId)
        {
            if (string.IsNullOrWhiteSpace(participantId)) return;

            lock (_stateLock)
            {
                MoveAnonymousStateIfTargetMissing(_responseIds, participantId);
                MoveAnonymousStateIfTargetMissing(_interactionIds, participantId);
                MoveAnonymousStateIfTargetMissing(_speechOwners, participantId);
            }
        }

        public LipSyncResponseOwner ResolveSpeechOwner(
            string participantId,
            in LipSyncResponseOwner incoming,
            bool isSpeaking)
        {
            string safeParticipantId = participantId ?? string.Empty;
            lock (_stateLock)
            {
                if (incoming.HasIdentity)
                {
                    _speechOwners[safeParticipantId] = incoming;
                    if (incoming.ResponseId.Length > 0)
                        _responseIds[safeParticipantId] = incoming.ResponseId;
                    return incoming;
                }

                if (!isSpeaking && _speechOwners.TryGetValue(safeParticipantId, out LipSyncResponseOwner current))
                    return current;

                _speechOwners.Remove(safeParticipantId);
                return default;
            }
        }

        public void CompleteSpeech(string participantId)
        {
            lock (_stateLock)
            {
                _speechOwners.Remove(participantId ?? string.Empty);
            }
        }

        public string ResolveProjectionResponseId(string participantId, in LipSyncResponseOwner owner)
        {
            if (owner.ResponseId.Length > 0) return owner.ResponseId;

            lock (_stateLock)
            {
                return _responseIds.TryGetValue(participantId ?? string.Empty, out string responseId)
                    ? responseId
                    : string.Empty;
            }
        }

        public TranscriptIdentity ResolveTranscriptIdentity(
            string participantId,
            string responseId,
            string messageId,
            string turnId,
            string envelopeId)
        {
            string safeParticipantId = participantId ?? string.Empty;
            lock (_stateLock)
            {
                string explicitResponseId = responseId ?? string.Empty;
                string currentResponseId = _responseIds.TryGetValue(safeParticipantId, out string existingResponseId)
                    ? existingResponseId
                    : string.Empty;

                // Explicit protocol identity is authoritative. This repairs state when a
                // completion packet for an interrupted response arrives late or not at all.
                if (!string.IsNullOrWhiteSpace(explicitResponseId) &&
                    !string.Equals(currentResponseId, explicitResponseId, StringComparison.Ordinal))
                {
                    currentResponseId = explicitResponseId;
                    _responseIds[safeParticipantId] = currentResponseId;
                }

                if (string.IsNullOrWhiteSpace(currentResponseId))
                {
                    currentResponseId = ResolveFirstNonEmpty(
                        explicitResponseId,
                        envelopeId,
                        _interactionIds.TryGetValue(safeParticipantId, out string interactionId)
                            ? interactionId
                            : string.Empty);

                    if (string.IsNullOrWhiteSpace(currentResponseId))
                        currentResponseId = CreateResponseIdLocked(safeParticipantId);

                    _responseIds[safeParticipantId] = currentResponseId;
                }

                string resolvedTurnId = ResolveFirstNonEmpty(turnId, responseId, currentResponseId, messageId);
                string resolvedMessageId = ResolveFirstNonEmpty(messageId, turnId, responseId, currentResponseId);
                string resolvedResponseId = ResolveFirstNonEmpty(responseId, currentResponseId, messageId, turnId);

                return new TranscriptIdentity(resolvedTurnId, resolvedMessageId, resolvedResponseId);
            }
        }

        public void Clear(string participantId)
        {
            string safeParticipantId = participantId ?? string.Empty;
            lock (_stateLock)
            {
                _responseIds.Remove(safeParticipantId);
                _speechOwners.Remove(safeParticipantId);
                _interactionIds.Remove(safeParticipantId);
            }
        }

        private string CreateResponseIdLocked(string participantId) =>
            string.IsNullOrWhiteSpace(participantId)
                ? $"character-response-{_nextResponseId++:D6}"
                : $"{participantId}:character-response-{_nextResponseId++:D6}";

        private static void MoveAnonymousStateIfTargetMissing<T>(
            Dictionary<string, T> state,
            string participantId)
        {
            if (state.ContainsKey(participantId) || !state.TryGetValue(string.Empty, out T anonymousValue))
                return;

            state[participantId] = anonymousValue;
            state.Remove(string.Empty);
        }

        private static string ResolveFirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;

            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

            return string.Empty;
        }

        internal readonly struct TranscriptIdentity
        {
            public TranscriptIdentity(string turnId, string messageId, string responseId)
            {
                TurnId = turnId ?? string.Empty;
                MessageId = messageId ?? string.Empty;
                ResponseId = responseId ?? string.Empty;
            }

            public string TurnId { get; }
            public string MessageId { get; }
            public string ResponseId { get; }
        }
    }
}
