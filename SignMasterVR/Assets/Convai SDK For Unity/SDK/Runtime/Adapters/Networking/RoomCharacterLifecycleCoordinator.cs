using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Networking.Media;

namespace Convai.Runtime.Adapters.Networking
{
    internal sealed class RoomCharacterLifecycleCoordinator
    {
        private readonly Func<IAgentRegistry> _agentRegistryProvider;
        private readonly Func<AudioTrackManager> _audioTrackManagerProvider;
        private readonly Func<IReadOnlyList<IConvaiCharacterAgent>> _characterListProvider;
        private readonly Func<SessionState> _currentStateProvider;
        private readonly Action<SessionState> _updateSessionState;
        private bool _hasRecoveredCharacterReady;

        public RoomCharacterLifecycleCoordinator(
            Func<SessionState> currentStateProvider,
            Func<IReadOnlyList<IConvaiCharacterAgent>> characterListProvider,
            Func<IAgentRegistry> agentRegistryProvider,
            Func<AudioTrackManager> audioTrackManagerProvider,
            Action<SessionState> updateSessionState)
        {
            _currentStateProvider =
                currentStateProvider ?? throw new ArgumentNullException(nameof(currentStateProvider));
            _characterListProvider =
                characterListProvider ?? throw new ArgumentNullException(nameof(characterListProvider));
            _agentRegistryProvider = agentRegistryProvider ??
                                     throw new ArgumentNullException(nameof(agentRegistryProvider));
            _audioTrackManagerProvider = audioTrackManagerProvider ??
                                         throw new ArgumentNullException(nameof(audioTrackManagerProvider));
            _updateSessionState = updateSessionState ?? throw new ArgumentNullException(nameof(updateSessionState));
        }

        public void HandleCharacterReady(CharacterReady readyEvent)
        {
            SessionState currentState = _currentStateProvider();
            if (currentState != SessionState.Connecting && currentState != SessionState.Reconnecting) return;

            IReadOnlyList<IConvaiCharacterAgent> characterList = _characterListProvider();
            string activeCharacterId = characterList?.Count > 0 ? characterList[0]?.CharacterId : null;

            if (!string.IsNullOrEmpty(activeCharacterId))
            {
                bool matchesByCharacterId = !string.IsNullOrEmpty(readyEvent.CharacterId) &&
                                            string.Equals(activeCharacterId, readyEvent.CharacterId,
                                                StringComparison.OrdinalIgnoreCase);

                bool matchesByParticipantId = false;
                IAgentRegistry agentRegistry = _agentRegistryProvider();
                if (!string.IsNullOrEmpty(readyEvent.ParticipantId) &&
                    agentRegistry != null &&
                    agentRegistry.TryGetCharacter(activeCharacterId, out IConvaiCharacterAgent _))
                {
                    if (agentRegistry.TryGetParticipantId(activeCharacterId, out string existingParticipantId) &&
                        !string.IsNullOrEmpty(existingParticipantId))
                    {
                        matchesByParticipantId = string.Equals(existingParticipantId, readyEvent.ParticipantId,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    else if (characterList != null && characterList.Count == 1)
                    {
                        agentRegistry.SetParticipantId(activeCharacterId, readyEvent.ParticipantId);
                        matchesByParticipantId = true;
                    }
                }

                if (!matchesByCharacterId && !matchesByParticipantId) return;
            }

            _hasRecoveredCharacterReady = true;
            _updateSessionState(SessionState.Connected);
        }

        public void ResetRecoveredReadinessState() => _hasRecoveredCharacterReady = false;

        public CharacterReady? TryRecoverCharacterReadyFromSpeech(CharacterSpeechStateChanged speechEvent)
        {
            if (!speechEvent.IsSpeaking)
                return null;

            return TryRecoverCharacterReady(speechEvent.CharacterId, null);
        }

        public CharacterReady? TryRecoverCharacterReadyFromTts(CharacterTtsTextChunk ttsEvent)
        {
            if (ttsEvent.IsEmpty)
                return null;

            return TryRecoverCharacterReady(null, ttsEvent.ParticipantId);
        }

        public CharacterReady? TryRecoverCharacterReadyFromLipSync(LipSyncPackedDataReceived lipSyncEvent)
        {
            if (!lipSyncEvent.IsValid)
                return null;

            return TryRecoverCharacterReady(lipSyncEvent.CharacterId, lipSyncEvent.ParticipantId);
        }

        public void HandleRemoteAudioTrackSubscribed(IRemoteAudioTrack audioTrack, string participantSid,
            string characterId) =>
            _audioTrackManagerProvider()?.HandleRemoteAudioTrackSubscribed(audioTrack, participantSid, characterId);

        public void HandleRemoteAudioTrackUnsubscribed(string participantSid) =>
            _audioTrackManagerProvider()?.HandleRemoteAudioTrackUnsubscribed(participantSid);

        private CharacterReady? TryRecoverCharacterReady(string observedCharacterId, string observedParticipantId)
        {
            if (_hasRecoveredCharacterReady)
                return null;

            SessionState currentState = _currentStateProvider();
            if (currentState != SessionState.Connecting && currentState != SessionState.Reconnecting)
                return null;

            if (!TryResolveActiveCharacter(out string activeCharacterId, out bool singleCharacterSession))
                return null;

            bool matchesByCharacterId = !string.IsNullOrWhiteSpace(observedCharacterId) &&
                                        string.Equals(activeCharacterId, observedCharacterId,
                                            StringComparison.OrdinalIgnoreCase);

            string participantId = NormalizeParticipantForActiveCharacter(activeCharacterId, observedParticipantId,
                singleCharacterSession, out bool matchesByParticipantId);

            if (!matchesByCharacterId && !matchesByParticipantId)
                return null;

            _hasRecoveredCharacterReady = true;
            return CharacterReady.Create(activeCharacterId, participantId ?? string.Empty);
        }

        private bool TryResolveActiveCharacter(out string activeCharacterId, out bool singleCharacterSession)
        {
            IReadOnlyList<IConvaiCharacterAgent> characterList = _characterListProvider();
            activeCharacterId = characterList?.Count > 0 ? characterList[0]?.CharacterId : null;
            singleCharacterSession = characterList != null && characterList.Count == 1;
            return !string.IsNullOrWhiteSpace(activeCharacterId);
        }

        private string NormalizeParticipantForActiveCharacter(string activeCharacterId, string observedParticipantId,
            bool singleCharacterSession, out bool matchesByParticipantId)
        {
            matchesByParticipantId = false;

            if (string.IsNullOrWhiteSpace(activeCharacterId))
                return observedParticipantId;

            IAgentRegistry agentRegistry = _agentRegistryProvider();
            if (agentRegistry == null || !agentRegistry.TryGetCharacter(activeCharacterId, out IConvaiCharacterAgent _))
                return observedParticipantId;

            if (agentRegistry.TryGetParticipantId(activeCharacterId, out string existingParticipantId) &&
                !string.IsNullOrWhiteSpace(existingParticipantId))
            {
                matchesByParticipantId = !string.IsNullOrWhiteSpace(observedParticipantId) &&
                                         string.Equals(existingParticipantId, observedParticipantId,
                                             StringComparison.OrdinalIgnoreCase);
                return existingParticipantId;
            }

            if (!string.IsNullOrWhiteSpace(observedParticipantId) && singleCharacterSession)
            {
                agentRegistry.SetParticipantId(activeCharacterId, observedParticipantId);
                matchesByParticipantId = true;
                return observedParticipantId;
            }

            return observedParticipantId;
        }
    }
}
