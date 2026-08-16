using System;
using System.Collections;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Room;
using UnityEngine;

namespace Convai.Runtime.Components
{
    [AddComponentMenu("Convai/Convai Push To Talk Controller")]
    public sealed class ConvaiPushToTalkController : MonoBehaviour
    {
        [SerializeField] [Tooltip("Optional explicit manager reference. If omitted, the active ConvaiManager is used.")]
        private ConvaiManager _manager;

        [SerializeField]
        [Tooltip(
            "Optional explicit character target. If omitted, the active conversation character is used when available.")]
        private ConvaiCharacter _targetCharacter;

        [SerializeField]
        [Tooltip(
            "When true and no explicit target is assigned, the controller uses ConvaiManager.ActiveConversationCharacter.")]
        private bool _useActiveConversationCharacter = true;

        private string _activeTargetCharacterId;
        private string _activeTargetParticipantId;
        private int _activeTurnGeneration;
        private IConvaiRoomAudioService _audioService;
        private SubscriptionToken _characterSpeechToken;
        private SubscriptionToken _characterTurnCompletedToken;
        private IConvaiRoomConnectionService _connectionService;

        private IEventHub _eventHub;
        private bool _hasOpenedMicrophoneThisSession;
        private SubscriptionToken _llmNoResponseToken;
        private SubscriptionToken _moderationResponseToken;
        private bool _forceStopSentForPendingRelease;
        private bool _pendingRelease;
        private int _pendingReleaseGeneration;
        private ResolvedTurnTakingOptions _pendingReleasePolicy;
        private Coroutine _releaseTailCoroutine;
        private Func<ResolvedTurnTakingOptions> _resolvedPolicyProviderOverride;
        private string _resolvedTargetCharacterIdOverride;
        private SubscriptionToken _sessionErrorToken;
        private SubscriptionToken _sessionStateToken;
        private bool _speechStartedForAwaitingTurn;
        private bool _targetSpeaking;
        private Coroutine _turnCompletionTimeoutCoroutine;
        private SubscriptionToken _usageLimitReachedToken;
        private SubscriptionToken _playerTranscriptToken;

        public bool IsPressed { get; private set; }
        public bool IsAwaitingTurnCompletion { get; private set; }
        public string ActiveTargetCharacterId => _activeTargetCharacterId ?? string.Empty;
        public string ActiveTargetParticipantId => _activeTargetParticipantId ?? string.Empty;
        public string BlockedReason { get; private set; } = string.Empty;

        private void OnEnable()
        {
            TryResolveDependencies();
            SubscribeToEvents();
        }

        private void OnDisable()
        {
            AbortActiveCaptureIfNeeded();
            ClearAwaitingTurn(string.Empty);
            UnsubscribeFromEvents();
            IsPressed = false;
            _targetSpeaking = false;
        }

        public bool SetPressed(bool pressed) => pressed ? Press() : Release();

        internal bool BelongsToManager(ConvaiManager manager)
        {
            if (manager == null)
                return false;

            return ReferenceEquals(ResolveManager(), manager);
        }

        internal void PrepareForConversationInputModeTransition(
            ResolvedTurnTakingOptions nextPolicy,
            string reason)
        {
            AbortActiveCaptureIfNeeded();
            _activeTurnGeneration++;
            IsPressed = false;
            _targetSpeaking = false;
            BlockedReason = string.Empty;
            ClearAwaitingTurn(reason);
            _hasOpenedMicrophoneThisSession = nextPolicy?.ShouldAutoStartMicrophoneAfterConnect ?? false;
        }

        public bool Press()
        {
            if (!TryResolveDependencies())
                return Reject("Convai runtime services are not available.");

            ResolvedTurnTakingOptions policy = ResolveCurrentPolicy();
            if (policy.Mode != ConversationInputMode.PushToTalk)
                return Reject("Push-to-talk is not enabled for the current session.");

            if (_connectionService == null || !_connectionService.IsConnected)
                return Reject("Push-to-talk requires an active room connection.");

            if (_pendingRelease)
                return Reject("Push-to-talk is finishing the previous capture.");

            string targetCharacterId = ResolveTargetCharacterId();
            if (string.IsNullOrWhiteSpace(targetCharacterId))
                return Reject("Push-to-talk could not resolve a target character.");

            bool targetBusy = _targetSpeaking || IsAwaitingTurnCompletion;
            if (targetBusy)
            {
                if (policy.InterruptBotOnPress)
                {
                    _connectionService.InterruptBot();
                    ClearAwaitingTurn("interrupt-on-press");
                    _targetSpeaking = false;
                }
                else if (policy.RequireTurnCompletionBeforeNextPress)
                    return Reject("Push-to-talk is waiting for the current character turn to finish.");
            }

            BlockedReason = string.Empty;
            _activeTargetCharacterId = targetCharacterId;
            _activeTargetParticipantId = string.Empty;

            if (policy.EnableServerSttToggle)
                _connectionService.SetSttMuted(false);

            OpenLocalMicrophone(policy);

            IsPressed = true;
            return true;
        }

        public bool Release()
        {
            if (!TryResolveDependencies())
                return false;

            ResolvedTurnTakingOptions policy = ResolveCurrentPolicy();
            if (policy.Mode != ConversationInputMode.PushToTalk || !IsPressed)
                return false;

            IsPressed = false;
            _activeTurnGeneration++;
            _speechStartedForAwaitingTurn = false;

            IsAwaitingTurnCompletion = true;
            BeginPendingRelease(policy, _activeTurnGeneration);
            StartTurnCompletionTimeout(policy, _activeTurnGeneration);
            return true;
        }

        private bool TryResolveDependencies()
        {
            ConvaiManager manager = ResolveManager();
            if (manager == null)
                return false;

            if (_connectionService == null)
                manager.TryGetRoomConnectionService(out _connectionService);
            if (_audioService == null)
                manager.TryGetRoomAudioService(out _audioService);
            if (_eventHub == null)
                manager.TryGetEventHub(out _eventHub);

            SubscribeToEvents();

            return _connectionService != null && _audioService != null && _eventHub != null;
        }

        private ConvaiManager ResolveManager()
        {
            if (_manager != null)
                return _manager;

            _manager = ConvaiManager.ActiveManager ?? FindAnyObjectByType<ConvaiManager>();
            return _manager;
        }

        private ResolvedTurnTakingOptions ResolveCurrentPolicy()
        {
            if (_resolvedPolicyProviderOverride != null)
                return _resolvedPolicyProviderOverride();

            ConvaiManager manager = ResolveManager();
            if (manager != null && manager.TryGetRoomManager(out ConvaiRoomManager roomManager))
                return roomManager.CurrentResolvedTurnTakingOptions;

            return ResolvedTurnTakingOptions.DefaultHandsFree;
        }

        private ConvaiCharacter ResolveTargetCharacter()
        {
            if (_targetCharacter != null)
                return _targetCharacter;

            ConvaiManager manager = ResolveManager();
            if (manager == null)
                return null;

            if (_useActiveConversationCharacter && manager.ActiveConversationCharacter != null)
                return manager.ActiveConversationCharacter;

            return manager.Characters.Count == 1 ? manager.Characters[0] : null;
        }

        private string ResolveTargetCharacterId()
        {
            if (!string.IsNullOrWhiteSpace(_resolvedTargetCharacterIdOverride))
                return _resolvedTargetCharacterIdOverride;

            return ResolveTargetCharacter()?.CharacterId ?? string.Empty;
        }

        private void OpenLocalMicrophone(ResolvedTurnTakingOptions policy)
        {
            if (policy.PushToTalkStartupMode == PushToTalkMicStartupMode.OpenOnFirstPress &&
                !_hasOpenedMicrophoneThisSession)
            {
                _audioService.SetMicMuted(false);
                _audioService.StartListeningAsync();
                _hasOpenedMicrophoneThisSession = true;
                return;
            }

            _audioService.SetMicMuted(false);
        }

        private void AbortActiveCaptureIfNeeded()
        {
            if ((!IsPressed && !_pendingRelease) || !TryResolveDependencies())
                return;

            ResolvedTurnTakingOptions policy = _pendingReleasePolicy ?? ResolveCurrentPolicy();
            if (policy.Mode != ConversationInputMode.PushToTalk)
                return;

            _activeTurnGeneration++;
            CancelReleaseTailCoroutine();
            bool forceStopAlreadySent = _pendingRelease && _forceStopSentForPendingRelease;
            _pendingRelease = false;
            _pendingReleasePolicy = null;
            _forceStopSentForPendingRelease = false;

            if (!forceStopAlreadySent)
                _connectionService.ForceUserStoppedSpeaking();

            CloseCapture(policy);
        }

        private void BeginPendingRelease(ResolvedTurnTakingOptions policy, int generation)
        {
            _forceStopSentForPendingRelease = false;
            _pendingRelease = true;
            _pendingReleaseGeneration = generation;
            _pendingReleasePolicy = policy;

            if (policy.ReleaseTailMs <= 0 || !isActiveAndEnabled)
            {
                CompletePendingRelease(generation);
                return;
            }

            _releaseTailCoroutine =
                StartCoroutine(ReleaseTailCoroutine(policy.ReleaseTailMs, generation, postStopWindow: false));
        }

        private IEnumerator ReleaseTailCoroutine(int releaseTailMs, int generation, bool postStopWindow)
        {
            yield return new WaitForSecondsRealtime(releaseTailMs / 1000f);
            _releaseTailCoroutine = null;

            if (postStopWindow)
                CompletePendingRelease(generation);
            else
                BeginPostStopFinalization(generation);
        }

        private void BeginPostStopFinalization(int generation)
        {
            if (!_pendingRelease || generation != _pendingReleaseGeneration)
                return;

            SendForceStopForPendingRelease();

            int releaseTailMs = _pendingReleasePolicy.ReleaseTailMs;
            if (releaseTailMs <= 0 || !isActiveAndEnabled)
            {
                CompletePendingRelease(generation);
                return;
            }

            _releaseTailCoroutine =
                StartCoroutine(ReleaseTailCoroutine(releaseTailMs, generation, postStopWindow: true));
        }

        private void CompletePendingRelease(int generation)
        {
            if (!_pendingRelease || generation != _pendingReleaseGeneration)
                return;

            ResolvedTurnTakingOptions policy = _pendingReleasePolicy;
            CancelReleaseTailCoroutine();
            SendForceStopForPendingRelease();
            _pendingRelease = false;
            _pendingReleasePolicy = null;
            _forceStopSentForPendingRelease = false;
            CloseCapture(policy);
        }

        private void CancelReleaseTailCoroutine()
        {
            if (_releaseTailCoroutine == null)
                return;

            StopCoroutine(_releaseTailCoroutine);
            _releaseTailCoroutine = null;
        }

        private void SendForceStopForPendingRelease()
        {
            if (_forceStopSentForPendingRelease)
                return;

            _forceStopSentForPendingRelease = true;
            _connectionService.ForceUserStoppedSpeaking();
        }

        private void CloseCapture(ResolvedTurnTakingOptions policy)
        {
            _audioService.SetMicMuted(true);
            if (policy.EnableServerSttToggle)
                _connectionService.SetSttMuted(true);
        }

        private void SubscribeToEvents()
        {
            if (_eventHub == null)
                return;

            if (_characterSpeechToken == default)
                _characterSpeechToken =
                    _eventHub.Subscribe<CharacterSpeechStateChanged>(HandleCharacterSpeechStateChanged);
            if (_characterTurnCompletedToken == default)
                _characterTurnCompletedToken =
                    _eventHub.Subscribe<CharacterTurnCompleted>(HandleCharacterTurnCompleted);
            if (_llmNoResponseToken == default)
                _llmNoResponseToken = _eventHub.Subscribe<LlmNoResponseReceived>(HandleLlmNoResponse);
            if (_moderationResponseToken == default)
                _moderationResponseToken = _eventHub.Subscribe<ModerationResponseReceived>(HandleModerationResponse);
            if (_playerTranscriptToken == default)
                _playerTranscriptToken = _eventHub.Subscribe<PlayerTranscriptReceived>(HandlePlayerTranscriptReceived);
            if (_usageLimitReachedToken == default)
                _usageLimitReachedToken = _eventHub.Subscribe<UsageLimitReached>(HandleUsageLimitReached);
            if (_sessionErrorToken == default)
                _sessionErrorToken = _eventHub.Subscribe<SessionError>(HandleSessionError);
            if (_sessionStateToken == default)
                _sessionStateToken = _eventHub.Subscribe<SessionStateChanged>(HandleSessionStateChanged);
        }

        private void UnsubscribeFromEvents()
        {
            if (_eventHub == null)
                return;

            if (_characterSpeechToken != default) _eventHub.Unsubscribe(_characterSpeechToken);
            if (_characterTurnCompletedToken != default) _eventHub.Unsubscribe(_characterTurnCompletedToken);
            if (_llmNoResponseToken != default) _eventHub.Unsubscribe(_llmNoResponseToken);
            if (_moderationResponseToken != default) _eventHub.Unsubscribe(_moderationResponseToken);
            if (_playerTranscriptToken != default) _eventHub.Unsubscribe(_playerTranscriptToken);
            if (_usageLimitReachedToken != default) _eventHub.Unsubscribe(_usageLimitReachedToken);
            if (_sessionErrorToken != default) _eventHub.Unsubscribe(_sessionErrorToken);
            if (_sessionStateToken != default) _eventHub.Unsubscribe(_sessionStateToken);

            _characterSpeechToken = default;
            _characterTurnCompletedToken = default;
            _llmNoResponseToken = default;
            _moderationResponseToken = default;
            _playerTranscriptToken = default;
            _usageLimitReachedToken = default;
            _sessionErrorToken = default;
            _sessionStateToken = default;
        }

        private void HandleCharacterSpeechStateChanged(CharacterSpeechStateChanged e)
        {
            if (!MatchesCurrentTarget(e.CharacterId, null))
                return;

            _targetSpeaking = e.IsSpeaking;
            if (IsAwaitingTurnCompletion && !_speechStartedForAwaitingTurn && e.IsSpeaking)
                _speechStartedForAwaitingTurn = true;

            if (IsAwaitingTurnCompletion &&
                !e.IsSpeaking &&
                _speechStartedForAwaitingTurn &&
                ResolveCurrentPolicy().AllowSpeechStoppedFallbackAfterSpeechStart)
                ClearAwaitingTurn("speech-stopped-fallback");
        }

        private void HandleCharacterTurnCompleted(CharacterTurnCompleted e)
        {
            if (!MatchesCurrentTarget(e.CharacterId, e.ParticipantId))
                return;

            ClearAwaitingTurn("character-turn-completed", e.ParticipantId);
            _targetSpeaking = false;
        }

        private void HandleLlmNoResponse(LlmNoResponseReceived e)
        {
            if (!MatchesCurrentTarget(e.CharacterId, e.ParticipantId))
                return;

            ClearAwaitingTurn("llm-no-response", e.ParticipantId);
            _targetSpeaking = false;
        }

        private void HandleModerationResponse(ModerationResponseReceived e)
        {
            if (IsAwaitingTurnCompletion && e.WasFlagged)
                ClearAwaitingTurn("moderation-blocked");
        }

        private void HandlePlayerTranscriptReceived(PlayerTranscriptReceived e)
        {
            if (!_pendingRelease || e.Phase != TranscriptionPhase.AsrFinal)
                return;

            string localParticipantId = _connectionService?.CurrentRoom?.LocalParticipant?.Sid;
            if (!string.IsNullOrWhiteSpace(e.ParticipantId) &&
                !string.IsNullOrWhiteSpace(localParticipantId) &&
                !string.Equals(e.ParticipantId, localParticipantId, StringComparison.OrdinalIgnoreCase))
                return;

            CompletePendingRelease(_pendingReleaseGeneration);
        }

        private void HandleUsageLimitReached(UsageLimitReached e)
        {
            if (IsAwaitingTurnCompletion)
                ClearAwaitingTurn($"usage-limit:{e.QuotaType}");
        }

        private void HandleSessionError(SessionError e)
        {
            if (IsAwaitingTurnCompletion)
                ClearAwaitingTurn($"session-error:{e.ErrorCode}");
        }

        private void HandleSessionStateChanged(SessionStateChanged e)
        {
            if (e.NewState == SessionState.Connected)
            {
                _hasOpenedMicrophoneThisSession = ResolveCurrentPolicy().ShouldAutoStartMicrophoneAfterConnect;
                return;
            }

            if (e.NewState is SessionState.Disconnected or SessionState.Error)
            {
                CancelReleaseTailCoroutine();
                _pendingRelease = false;
                _pendingReleasePolicy = null;
                _forceStopSentForPendingRelease = false;
                _hasOpenedMicrophoneThisSession = false;
                _targetSpeaking = false;
                IsPressed = false;
                BlockedReason = string.Empty;
                ClearAwaitingTurn($"session-state:{e.NewState}");
            }
        }

        private bool MatchesCurrentTarget(string characterId, string participantId)
        {
            if (!IsAwaitingTurnCompletion && string.IsNullOrWhiteSpace(_activeTargetCharacterId))
                return MatchesResolvedTarget(characterId);

            if (!string.IsNullOrWhiteSpace(_activeTargetParticipantId) &&
                string.Equals(_activeTargetParticipantId, participantId, StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrWhiteSpace(_activeTargetCharacterId) &&
                   string.Equals(_activeTargetCharacterId, characterId, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesResolvedTarget(string characterId)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedTargetCharacterIdOverride))
            {
                return string.Equals(
                    _resolvedTargetCharacterIdOverride,
                    characterId,
                    StringComparison.OrdinalIgnoreCase);
            }

            ConvaiCharacter targetCharacter = ResolveTargetCharacter();
            return targetCharacter != null &&
                   string.Equals(targetCharacter.CharacterId, characterId, StringComparison.OrdinalIgnoreCase);
        }

        private void StartTurnCompletionTimeout(ResolvedTurnTakingOptions policy, int generation)
        {
            StopTurnCompletionTimeout();
            if (policy.TurnCompletionTimeoutMs <= 0)
                return;

            _turnCompletionTimeoutCoroutine =
                StartCoroutine(TurnCompletionTimeoutCoroutine(policy.TurnCompletionTimeoutMs, generation));
        }

        private IEnumerator TurnCompletionTimeoutCoroutine(int timeoutMs, int generation)
        {
            yield return new WaitForSecondsRealtime(timeoutMs / 1000f);

            if (!IsAwaitingTurnCompletion || generation != _activeTurnGeneration)
                yield break;

            ClearAwaitingTurn("timeout");
        }

        private void StopTurnCompletionTimeout()
        {
            if (_turnCompletionTimeoutCoroutine == null)
                return;

            StopCoroutine(_turnCompletionTimeoutCoroutine);
            _turnCompletionTimeoutCoroutine = null;
        }

        private void ClearAwaitingTurn(string reason, string participantId = null)
        {
            StopTurnCompletionTimeout();
            IsAwaitingTurnCompletion = false;
            _speechStartedForAwaitingTurn = false;
            _activeTargetCharacterId = string.Empty;
            _activeTargetParticipantId = string.Empty;
            BlockedReason = reason ?? string.Empty;
        }

        private bool Reject(string reason)
        {
            BlockedReason = reason ?? string.Empty;
            return false;
        }

        internal void InjectForTests(
            ConvaiManager manager,
            IEventHub eventHub,
            IConvaiRoomConnectionService connectionService,
            IConvaiRoomAudioService audioService,
            Func<ResolvedTurnTakingOptions> resolvedPolicyProvider)
        {
            _manager = manager;
            _eventHub = eventHub;
            _connectionService = connectionService;
            _audioService = audioService;
            _resolvedPolicyProviderOverride = resolvedPolicyProvider;
            SubscribeToEvents();
        }

        internal void SetTargetCharacterIdForTests(string characterId) =>
            _resolvedTargetCharacterIdOverride = characterId;

        internal void ExpireReleaseTailForTests()
        {
            if (!_pendingRelease)
                return;

            CancelReleaseTailCoroutine();
            if (_forceStopSentForPendingRelease)
                CompletePendingRelease(_pendingReleaseGeneration);
            else
                BeginPostStopFinalization(_pendingReleaseGeneration);
        }
    }
}
