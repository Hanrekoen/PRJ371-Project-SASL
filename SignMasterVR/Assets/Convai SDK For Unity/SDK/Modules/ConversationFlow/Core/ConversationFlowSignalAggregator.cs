using System;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;

namespace Convai.Modules.ConversationFlow.Core
{
    /// <summary>
    ///     Folds <see cref="IEventHub" /> events and LipSync phase signals into the per-frame
    ///     <see cref="ConversationFlowInputs" /> consumed by
    ///     <see cref="ConversationFlowStateMachine" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Pure POCO so it is edit-mode testable. Subscription lifetime is managed by the
    ///         caller via <see cref="Attach" /> / <see cref="Detach" /> ; never in the
    ///         constructor, because the aggregator is created once and reused.
    ///     </para>
    ///     <para>
    ///         The aggregator is scoped to a single character identified by
    ///         <see cref="CharacterId" />. Events for other characters are ignored.
    ///     </para>
    /// </remarks>
    internal sealed class ConversationFlowSignalAggregator
    {
        private string _characterId;
        private IDialoguePhaseProvider _dialoguePhaseProvider;
        private IEventHub _eventHub;

        private SubscriptionToken _playerSpeakingToken;
        private SubscriptionToken _playerTranscriptToken;
        private SubscriptionToken _characterSpeechToken;
        private SubscriptionToken _characterTurnToken;
        private SubscriptionToken _characterReadyToken;

        private bool _isAttached;
        private bool _acceptUnscopedPlayerSignals = true;

        private bool _isPlayerSpeaking;
        private bool _hasPendingPlayerTurn;
        private bool _isCharacterSpeaking;
        private bool _isCharacterReady;
        private bool _wasInterrupted;
        private bool _turnJustCompletedLatch;

        public ConversationFlowSignalAggregator(string characterId)
        {
            _characterId = characterId ?? string.Empty;
        }

        /// <summary>Character this aggregator listens for.</summary>
        public string CharacterId => _characterId;

        /// <summary>Updates the scoped character id when ConvaiCharacter finishes initialization.</summary>
        public void SetCharacterId(string characterId)
        {
            _characterId = characterId ?? string.Empty;
        }

        public void SetAcceptUnscopedPlayerSignals(bool accept)
        {
            _acceptUnscopedPlayerSignals = accept;
            if (!accept)
            {
                _isPlayerSpeaking = false;
                _hasPendingPlayerTurn = false;
            }
        }

        /// <summary>
        ///     Hooks the aggregator up to the live event hub and LipSync phase provider. Safe
        ///     to call multiple times ; additional calls are no-ops.
        /// </summary>
        public void Attach(IEventHub eventHub, IDialoguePhaseProvider dialoguePhaseProvider)
        {
            if (_isAttached &&
                ReferenceEquals(_eventHub, eventHub) &&
                ReferenceEquals(_dialoguePhaseProvider, dialoguePhaseProvider))
            {
                return;
            }

            if (_isAttached)
                Detach();

            _eventHub = eventHub;
            _dialoguePhaseProvider = dialoguePhaseProvider;

            if (_eventHub != null)
            {
                _playerSpeakingToken = _eventHub.Subscribe<PlayerSpeakingStateChanged>(OnPlayerSpeakingChanged);
                _playerTranscriptToken = _eventHub.Subscribe<PlayerTranscriptReceived>(OnPlayerTranscript);
                _characterSpeechToken = _eventHub.Subscribe<CharacterSpeechStateChanged>(OnCharacterSpeech);
                _characterTurnToken = _eventHub.Subscribe<CharacterTurnCompleted>(OnCharacterTurnCompleted);
                _characterReadyToken = _eventHub.Subscribe<CharacterReady>(OnCharacterReady);
            }

            _isAttached = true;
        }

        /// <summary>Releases event hub subscriptions. Safe to call multiple times.</summary>
        public void Detach()
        {
            if (!_isAttached) return;

            if (_eventHub != null)
            {
                _eventHub.Unsubscribe(_playerSpeakingToken);
                _eventHub.Unsubscribe(_playerTranscriptToken);
                _eventHub.Unsubscribe(_characterSpeechToken);
                _eventHub.Unsubscribe(_characterTurnToken);
                _eventHub.Unsubscribe(_characterReadyToken);
            }

            _playerSpeakingToken = default;
            _playerTranscriptToken = default;
            _characterSpeechToken = default;
            _characterTurnToken = default;
            _characterReadyToken = default;
            _eventHub = null;
            _dialoguePhaseProvider = null;
            _isAttached = false;
        }

        /// <summary>Forces the ready flag (e.g. when the agent is wired up before the event arrives).</summary>
        public void SetCharacterReady(bool isReady) => _isCharacterReady = isReady;

        /// <summary>Resets all signals. Used when the character is shut down.</summary>
        public void Reset()
        {
            _isCharacterReady = false;
            _isPlayerSpeaking = false;
            _hasPendingPlayerTurn = false;
            _isCharacterSpeaking = false;
            _wasInterrupted = false;
            _turnJustCompletedLatch = false;
        }

        /// <summary>Produces a per-frame input struct and clears one-shot latches.</summary>
        /// <param name="isPerformingAction">
        ///     Whether the character is carrying out work it was given. Passed in rather than
        ///     subscribed to because it is a polled state on a peer seam, not an event this
        ///     aggregator can latch — everything else here arrives on the event hub.
        /// </param>
        public ConversationFlowInputs Sample(bool isPerformingAction = false)
        {
            bool turnCompleted = _turnJustCompletedLatch;
            bool wasInterrupted = _wasInterrupted;
            _turnJustCompletedLatch = false;
            _wasInterrupted = false;

            bool lipSyncSpeaking = _dialoguePhaseProvider?.IsSpeechActive ?? false;

            return new ConversationFlowInputs(
                isCharacterReady: _isCharacterReady,
                isPlayerSpeaking: _isPlayerSpeaking,
                hasPendingPlayerTurn: _hasPendingPlayerTurn,
                isCharacterSpeaking: _isCharacterSpeaking,
                isLipSyncSpeaking: lipSyncSpeaking,
                wasRecentlyInterrupted: wasInterrupted,
                turnJustCompleted: turnCompleted,
                isPerformingAction: isPerformingAction);
        }

        /// <summary>Notifies the aggregator of an inbound speech-state event (used by tests).</summary>
        internal void FeedCharacterSpeaking(bool isSpeaking) => _isCharacterSpeaking = isSpeaking;

        private bool IsOurCharacter(string characterId) =>
            !string.IsNullOrWhiteSpace(_characterId) &&
            string.Equals(_characterId, characterId, StringComparison.OrdinalIgnoreCase);

        private void OnPlayerSpeakingChanged(PlayerSpeakingStateChanged e)
        {
            if (!_acceptUnscopedPlayerSignals) return;
            _isPlayerSpeaking = e.IsSpeaking;
        }

        private void OnPlayerTranscript(PlayerTranscriptReceived e)
        {
            if (!_acceptUnscopedPlayerSignals) return;
            if (e.Phase == TranscriptionPhase.ProcessedFinal ||
                e.Phase == TranscriptionPhase.Completed ||
                e.Phase == TranscriptionPhase.AsrFinal)
            {
                _hasPendingPlayerTurn = true;
            }
        }

        private void OnCharacterSpeech(CharacterSpeechStateChanged e)
        {
            if (!IsOurCharacter(e.CharacterId)) return;
            _isCharacterSpeaking = e.IsSpeaking;
            if (e.IsSpeaking) _hasPendingPlayerTurn = false;
        }

        private void OnCharacterTurnCompleted(CharacterTurnCompleted e)
        {
            if (!IsOurCharacter(e.CharacterId)) return;
            _turnJustCompletedLatch = true;
            _wasInterrupted = e.WasInterrupted;
            _isCharacterSpeaking = false;
            _hasPendingPlayerTurn = false;
        }

        private void OnCharacterReady(CharacterReady e)
        {
            if (!IsOurCharacter(e.CharacterId)) return;
            _isCharacterReady = true;
        }
    }
}
