using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Utilities;
using Convai.Shared.Types;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        /// <summary>
        ///     Whether <see cref="ReportUnrunActionsOnce" /> has already reported that this
        ///     character's action commands had nowhere to go. Once per character, per the SDK's
        ///     log-once rule for unusable setups.
        /// </summary>
        private bool _reportedUnrunActions;

        private void SubscribeToEvents()
        {
            if (EventHub != null)
            {
                _ttsTextToken = EventHub.Subscribe<CharacterTtsTextChunk>(OnCharacterTtsTextReceived);
                _speechStateToken = EventHub.Subscribe<CharacterSpeechStateChanged>(OnSpeechStateChanged);
                _characterReadyToken = EventHub.Subscribe<CharacterReady>(OnCharacterReadyReceived);
                _turnCompletedToken = EventHub.Subscribe<CharacterTurnCompleted>(OnCharacterTurnCompleted);
                _emotionToken = EventHub.Subscribe<CharacterEmotionChanged>(OnCharacterEmotionReceived);
                _actionReceivedToken = EventHub.Subscribe<CharacterActionReceived>(OnCharacterActionReceived);
                _dynamicContextUpdateResultToken =
                    EventHub.Subscribe<DynamicContextUpdateResultReceived>(OnDynamicContextUpdateResultReceived);
                _narrativeSectionChangedToken = EventHub.Subscribe<NarrativeSectionChanged>(HandleNarrativeSectionChanged);
            }
            else
                Logger?.Warning("EventHub is null - cannot subscribe to events");

            if (ConnectionService != null) ConnectionService.OnSessionStateChanged += OnSessionStateChangedInternal;
        }

        private void UnsubscribeFromEvents()
        {
            if (EventHub != null)
            {
                if (_ttsTextToken != default) EventHub.Unsubscribe(_ttsTextToken);
                if (_speechStateToken != default) EventHub.Unsubscribe(_speechStateToken);
                if (_characterReadyToken != default) EventHub.Unsubscribe(_characterReadyToken);
                if (_turnCompletedToken != default) EventHub.Unsubscribe(_turnCompletedToken);
                if (_emotionToken != default) EventHub.Unsubscribe(_emotionToken);
                if (_actionReceivedToken != default) EventHub.Unsubscribe(_actionReceivedToken);
                if (_dynamicContextUpdateResultToken != default)
                    EventHub.Unsubscribe(_dynamicContextUpdateResultToken);
                if (_narrativeSectionChangedToken != default) EventHub.Unsubscribe(_narrativeSectionChangedToken);
            }

            _ttsTextToken = default;
            _speechStateToken = default;
            _characterReadyToken = default;
            _turnCompletedToken = default;
            _emotionToken = default;
            _actionReceivedToken = default;
            _dynamicContextUpdateResultToken = default;
            _narrativeSectionChangedToken = default;

            if (ConnectionService != null) ConnectionService.OnSessionStateChanged -= OnSessionStateChangedInternal;
        }

        private void OnSessionStateChangedInternal(SessionStateChanged e)
        {
            Logger?.Debug($"[{_characterName}] Session state changed: {e.OldState} -> {e.NewState}");

            if (e.NewState == SessionState.Disconnected || e.NewState == SessionState.Error)
            {
                IsCharacterReady = false;
                _isSpeaking = false;
                ResetEmotionState();
                ClearResolvedSessionActionConfig();
                ClearResolvedSessionActionDefinitions();
                ClearResolvedSessionActionDefinitionCatalog();
                ClearPendingRuntimeActionStateUpdates();
                ClearEnvironmentSnapshotAtConnect();

                _dynamicContextTracker.StageCanonicalResync();

                MarkPendingNarrativeReplayAfterDisconnect();
            }

            SafeEventInvoker.Invoke(
                OnSessionStateChanged,
                e.NewState,
                Logger,
                "ConvaiCharacter.OnSessionStateChanged",
                LogCategory.Character);
        }

        private void OnCharacterReadyReceived(CharacterReady e)
        {
            if (!MatchesCharacterIdentity(e.CharacterId) && !MatchesCharacterIdentity(e.ParticipantId)) return;

            Logger?.Info($"[{_characterName}] Received character ready signal");
            IsCharacterReady = true;
            CaptureEnvironmentSnapshotAtConnect();
            SeedAllWorldObjectTrackedState();
            MarkPendingSceneMetadataSync();
            StageActionConfigSyncIfBackendIsMissingTargets();
            FlushPendingContextUpdates();
            FlushPendingNarrativeDesign();
        }

        private void OnCharacterTtsTextReceived(CharacterTtsTextChunk chunk)
        {
            if (!MatchesCharacterIdentity(chunk.ParticipantId)) return;

            SafeEventInvoker.Invoke(
                OnTranscriptReceived,
                chunk.Text,
                chunk.IsFinal,
                Logger,
                "ConvaiCharacter.OnTranscriptReceived",
                LogCategory.Character);
        }

        private void OnSpeechStateChanged(CharacterSpeechStateChanged e)
        {
            if (!MatchesCharacterIdentity(e.CharacterId)) return;

            _isSpeaking = e.IsSpeaking;

            if (e.IsSpeaking)
            {
                SafeEventInvoker.Invoke(
                    OnSpeechStarted,
                    Logger,
                    "ConvaiCharacter.OnSpeechStarted",
                    LogCategory.Character);
            }
            else
            {
                SafeEventInvoker.Invoke(
                    OnSpeechStopped,
                    Logger,
                    "ConvaiCharacter.OnSpeechStopped",
                    LogCategory.Character);
            }
        }

        private void OnCharacterEmotionReceived(CharacterEmotionChanged e)
        {
            if (!MatchesCharacterIdentity(e.CharacterId)) return;

            SetEmotionState(e.Emotion, e.Intensity);
            SafeEventInvoker.Invoke(
                OnEmotionChanged,
                e.Emotion,
                e.Intensity,
                Logger,
                "ConvaiCharacter.OnEmotionChanged",
                LogCategory.Character);
        }

        private void SetEmotionState(string emotion, int intensity)
        {
            lock (_emotionStateLock)
            {
                _currentEmotion = emotion;
                _currentEmotionIntensity = intensity;
            }
        }

        private void ResetEmotionState()
        {
            lock (_emotionStateLock)
            {
                _currentEmotion = null;
                _currentEmotionIntensity = 0;
            }
        }

        private void OnCharacterTurnCompleted(CharacterTurnCompleted e)
        {
            if (!MatchesCharacterIdentity(e.CharacterId) && !MatchesCharacterIdentity(e.ParticipantId)) return;

            Logger?.Debug($"[{_characterName}] Turn completed (interrupted={e.WasInterrupted})");
            SafeEventInvoker.Invoke(
                OnTurnCompleted,
                e.WasInterrupted,
                Logger,
                "ConvaiCharacter.OnTurnCompleted",
                LogCategory.Character);
        }

        private void OnCharacterActionReceived(CharacterActionReceived e)
        {
            if (!MatchesCharacterIdentity(e.CharacterId)) return;

            ReportUnrunActionsOnce(e.Actions);

            SafeEventInvoker.Invoke(
                OnActionsReceived,
                e.Actions,
                Logger,
                "ConvaiCharacter.OnActionsReceived",
                LogCategory.Character);
        }

        /// <summary>
        ///     Reports, once per character, the one action-setup mistake that otherwise produces no
        ///     symptom whatsoever: the Convai Character offered an action, the backend decided to
        ///     perform it, and nothing in the project is set up to run it — so the command is
        ///     received and silently dropped.
        /// </summary>
        /// <remarks>
        ///     This is the authoritative check, and the only one that can be made truthfully: unlike
        ///     the authoring-time <see cref="Convai.Runtime.Actions.ConvaiActionConfigValidator" />
        ///     pass, it does not guess from component presence but observes that no handler exists on
        ///     either supported path — this character's <see cref="OnActionsReceived" /> (a
        ///     <see cref="Convai.Runtime.Actions.ConvaiActionDispatcher" /> or a per-character custom
        ///     script subscribes here) or the room-wide
        ///     <c>ConvaiManager.Events.OnCharacterActionReceived</c>. An empty batch is skipped: the
        ///     backend uses it as an explicit no-op, so nothing was lost.
        /// </remarks>
        private void ReportUnrunActionsOnce(IReadOnlyList<ConvaiActionCommand> actions)
        {
            if (_reportedUnrunActions) return;
            if (actions == null || actions.Count == 0) return;
            if (OnActionsReceived != null) return;
            // EventsOrNull, never the public Events: that property builds the facades and throws
            // when initialization has not finished, and this check must never be able to disturb
            // delivery of the batch it is reporting on.
            if (ConvaiManager.ActiveManager?.EventsOrNull?.HasCharacterActionSubscribers == true) return;

            _reportedUnrunActions = true;
            Logger?.Warning(
                $"[{_characterName}] Received {actions.Count} action command(s) but nothing is set up " +
                "to run them, so they were ignored. Add the Convai Action Runner component to " +
                "this Convai Character, or handle ConvaiCharacter.OnActionsReceived (this character) " +
                "or ConvaiManager.Events.OnCharacterActionReceived (every character) in your own " +
                "code. Reported once per character.");
        }

        private bool MatchesCharacterIdentity(string participantIdOrCharacterId)
        {
            if (string.IsNullOrWhiteSpace(participantIdOrCharacterId) || string.IsNullOrWhiteSpace(CharacterId))
                return false;

            if (string.Equals(participantIdOrCharacterId, CharacterId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (AgentRegistry == null) return false;

            if (AgentRegistry.TryGetParticipantId(CharacterId, out string mappedParticipantId) &&
                !string.IsNullOrWhiteSpace(mappedParticipantId) &&
                string.Equals(mappedParticipantId, participantIdOrCharacterId, StringComparison.OrdinalIgnoreCase))
                return true;

            return AgentRegistry.TryGetCharacterByParticipantId(participantIdOrCharacterId,
                       out IConvaiCharacterAgent agent) &&
                   agent != null &&
                   string.Equals(agent.CharacterId, CharacterId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
