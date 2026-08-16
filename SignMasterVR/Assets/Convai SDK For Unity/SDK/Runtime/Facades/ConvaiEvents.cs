using System;
using System.ComponentModel;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.DomainEvents.Participant;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Utilities;
using Convai.Runtime.Core;

namespace Convai.Runtime.Facades
{
    /// <summary>
    ///     Canonical typed reactive event facade exposed from <c>ConvaiManager.Events</c>.
    /// </summary>
    /// <remarks>
    ///     Use this for code-driven reactions to room, session, transcript, narrative, and runtime domain events.
    ///     For transcript history, turn state, or replayable room transcript data, use <c>ConvaiManager.Transcripts</c>.
    ///     For inspector-driven designer workflows, use the relay components in <c>Convai.Runtime.Presentation.Events</c>.
    /// </remarks>
    public sealed class ConvaiEvents : IDisposable
    {
        private SubscriptionToken _actionReceivedToken;
        private SubscriptionToken _blendshapeTurnStatsToken;
        private SubscriptionToken _characterEmotionToken;
        private SubscriptionToken _characterReadyToken;
        private SubscriptionToken _characterSpeechToken;
        private SubscriptionToken _characterTranscriptToken;
        private SubscriptionToken _characterTurnCompletedToken;
        private SubscriptionToken _dynamicContextUpdateResultToken;
        private SubscriptionToken _finalUserTranscriptionToken;
        private SubscriptionToken _interactionCreatedToken;
        private bool _isDisposed;
        private SubscriptionToken _llmNoResponseToken;
        private SubscriptionToken _micMuteToken;
        private SubscriptionToken _moderationResponseToken;
        private SubscriptionToken _narrativeSectionChangedToken;
        private SubscriptionToken _participantConnectedToken;
        private SubscriptionToken _participantDisconnectedToken;
        private SubscriptionToken _playerSpeakingToken;
        private SubscriptionToken _playerTranscriptToken;
        private SubscriptionToken _roomOwnershipRebindToken;
        private SubscriptionToken _runtimeBackgroundStateToken;
        private SubscriptionToken _sessionErrorToken;

        private SubscriptionToken _sessionStateToken;
        private SubscriptionToken _usageLimitReachedToken;
        private SubscriptionToken _userIdleWarningToken;
        private SubscriptionToken _userIdleTimeoutToken;
        private SubscriptionToken _vadSttStateToken;

        internal ConvaiEvents(IEventHub eventHub)
        {
            Raw = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            Subscribe();
        }

        /// <summary>Gets raw EventHub access for advanced scenarios.</summary>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public IEventHub Raw { get; }

        public void Dispose()
        {
            if (_isDisposed) return;

            Unsubscribe();
            _isDisposed = true;
        }

        /// <summary>Raised whenever session state changes.</summary>
        public event Action<SessionStateChanged> OnSessionStateChanged;

        /// <summary>Raised when a room connection succeeds.</summary>
        public event Action OnConnected;

        /// <summary>Raised when a room disconnects.</summary>
        public event Action OnDisconnected;

        /// <summary>Raised when a lifecycle/session error occurs.</summary>
        public event Action<SessionError> OnSessionError;

        /// <summary>
        ///     Raised when a character's transcript text is received from the backend.
        /// </summary>
        /// <remarks>
        ///     For reactive handling of individual messages.
        ///     <b>Note:</b> For building transcript UIs or turn-based history, prefer using
        ///     <c>ConvaiManager.Transcripts</c> which handles turn management and state.
        ///     <code>
        /// manager.Events.OnCharacterTranscriptReceived += HandleCharacterTranscriptReceived;
        /// 
        /// private void HandleCharacterTranscriptReceived(CharacterTranscriptReceived e)
        /// {
        ///     Debug.Log($"Character {e.CharacterId} said: {e.Text}");
        /// }
        /// </code>
        /// </remarks>
        public event Action<CharacterTranscriptReceived> OnCharacterTranscriptReceived;

        /// <summary>
        ///     Raised when the player's transcript text is received from the backend.
        /// </summary>
        /// <remarks>
        ///     For transcript UI, room history, or turn aggregation, prefer <c>ConvaiManager.Transcripts</c>.
        ///     <code>
        /// manager.Events.OnPlayerTranscriptReceived += HandlePlayerTranscriptReceived;
        /// 
        /// private void HandlePlayerTranscriptReceived(PlayerTranscriptReceived e)
        /// {
        ///     Debug.Log($"Player said: {e.Text} (isFinal: {e.IsFinal})");
        /// }
        /// </code>
        /// </remarks>
        public event Action<PlayerTranscriptReceived> OnPlayerTranscriptReceived;

        /// <summary>Raised when character speaking state changes.</summary>
        public event Action<CharacterSpeechStateChanged> OnCharacterSpeechStateChanged;

        /// <summary>Raised when character emotion changes.</summary>
        public event Action<CharacterEmotionChanged> OnCharacterEmotionChanged;

        /// <summary>Raised when a character is ready.</summary>
        public event Action<CharacterReady> OnCharacterReady;

        /// <summary>Raised when character turn completes.</summary>
        public event Action<CharacterTurnCompleted> OnCharacterTurnCompleted;

        /// <summary>Raised when player speaking state changes.</summary>
        public event Action<PlayerSpeakingStateChanged> OnPlayerSpeakingStateChanged;

        /// <summary>Raised when local microphone mute state changes.</summary>
        public event Action<MicMuteChanged> OnMicMuteChanged;

        /// <summary>Raised when participant joins the room.</summary>
        public event Action<ParticipantInfo> OnParticipantJoined;

        /// <summary>Raised when participant leaves the room.</summary>
        public event Action<ParticipantInfo> OnParticipantLeft;

        /// <summary>Raised when narrative section changes.</summary>
        public event Action<NarrativeSectionChanged> OnNarrativeSectionChanged;

        /// <summary>Raised when a usage quota is exhausted.</summary>
        public event Action<UsageLimitReached> OnUsageLimitReached;

        /// <summary>Raised when the backend warns user is idle.</summary>
        public event Action<UserIdleWarningReceived> OnUserIdleWarningReceived;

        /// <summary>
        ///     Raised when the local deadline from the latest server idle-warning countdown elapses.
        ///     This is not an authoritative transport-disconnected signal.
        /// </summary>
        public event Action<UserIdleTimeoutElapsed> OnUserIdleTimeoutElapsed;

        /// <summary>Raised after an application background policy is applied or removed.</summary>
        public event Action<RuntimeBackgroundStateChanged> OnRuntimeBackgroundStateChanged;

        /// <summary>Raised when the backend returns ordered structured actions for a character turn.</summary>
        public event Action<CharacterActionReceived> OnCharacterActionReceived;

        /// <summary>
        ///     Whether anything is subscribed to <see cref="OnCharacterActionReceived" />, i.e.
        ///     whether some project code handles action commands room-wide. Read by
        ///     <c>ConvaiCharacter</c> so it can tell the difference between "a script elsewhere is
        ///     running these commands" and "nothing anywhere is", and only report the second.
        /// </summary>
        internal bool HasCharacterActionSubscribers => OnCharacterActionReceived != null;

        /// <summary>Raised when content moderation evaluates user input.</summary>
        public event Action<ModerationResponseReceived> OnModerationResponseReceived;

        /// <summary>Raised when the backend LLM explicitly decides not to respond.</summary>
        public event Action<LlmNoResponseReceived> OnLlmNoResponseReceived;

        /// <summary>Raised when a processed final user transcription is received from the backend.</summary>
        public event Action<FinalUserTranscriptionReceived> OnFinalUserTranscriptionReceived;

        /// <summary>Raised when backend creates a new interaction for the active character conversation.</summary>
        public event Action<InteractionCreated> OnInteractionCreated;

        /// <summary>Raised when turn-level blendshape statistics are received from the backend.</summary>
        public event Action<BlendshapeTurnStatsReceived> OnBlendshapeTurnStatsReceived;

        /// <summary>
        ///     Raised when the backend Voice Activity Detection (VAD) or Speech-to-Text (STT)
        ///     gating state changes, indicating if the pipeline is currently listening.
        /// </summary>
        public event Action<VadSttStateChanged> OnVadSttStateChanged;

        /// <summary>Raised when the backend pipeline reports an error.</summary>
        public event Action<SessionError> OnPipelineError;

        /// <summary>Raised when room ownership rebinding is evaluated or consumed by room lifecycle logic.</summary>
        public event Action<RoomOwnershipRebindStateChanged> OnRoomOwnershipRebindStateChanged;

        /// <summary>Raised when the backend acknowledges a dynamic context update.</summary>
        public event Action<DynamicContextUpdateResultReceived> OnDynamicContextUpdateResultReceived;

        private void Subscribe()
        {
            _sessionStateToken = Raw.Subscribe<SessionStateChanged>(HandleSessionStateChanged);
            _participantConnectedToken = Raw.Subscribe<ParticipantConnected>(HandleParticipantConnected);
            _participantDisconnectedToken = Raw.Subscribe<ParticipantDisconnected>(HandleParticipantDisconnected);

            _characterTranscriptToken = Raw.Subscribe<CharacterTranscriptReceived>(HandleCharacterTranscript);
            _playerTranscriptToken = Raw.Subscribe<PlayerTranscriptReceived>(HandlePlayerTranscript);

            _characterSpeechToken = Raw.Subscribe<CharacterSpeechStateChanged>(HandleCharacterSpeechStateChanged);
            _characterEmotionToken = Raw.Subscribe<CharacterEmotionChanged>(HandleCharacterEmotionChanged);
            _characterReadyToken = Raw.Subscribe<CharacterReady>(HandleCharacterReady);
            _characterTurnCompletedToken = Raw.Subscribe<CharacterTurnCompleted>(HandleCharacterTurnCompleted);
            _playerSpeakingToken = Raw.Subscribe<PlayerSpeakingStateChanged>(HandlePlayerSpeakingStateChanged);
            _micMuteToken = Raw.Subscribe<MicMuteChanged>(HandleMicMuteChanged);
            _narrativeSectionChangedToken = Raw.Subscribe<NarrativeSectionChanged>(HandleNarrativeSectionChanged);
            _usageLimitReachedToken = Raw.Subscribe<UsageLimitReached>(HandleUsageLimitReached);
            _userIdleWarningToken = Raw.Subscribe<UserIdleWarningReceived>(HandleUserIdleWarningReceived);
            _userIdleTimeoutToken = Raw.Subscribe<UserIdleTimeoutElapsed>(HandleUserIdleTimeoutElapsed);
            _runtimeBackgroundStateToken =
                Raw.Subscribe<RuntimeBackgroundStateChanged>(HandleRuntimeBackgroundStateChanged);
            _actionReceivedToken = Raw.Subscribe<CharacterActionReceived>(HandleActionReceived);
            _moderationResponseToken = Raw.Subscribe<ModerationResponseReceived>(HandleModerationResponse);
            _llmNoResponseToken = Raw.Subscribe<LlmNoResponseReceived>(HandleLlmNoResponseReceived);
            _finalUserTranscriptionToken =
                Raw.Subscribe<FinalUserTranscriptionReceived>(HandleFinalUserTranscriptionReceived);
            _interactionCreatedToken = Raw.Subscribe<InteractionCreated>(HandleInteractionCreated);
            _blendshapeTurnStatsToken = Raw.Subscribe<BlendshapeTurnStatsReceived>(HandleBlendshapeTurnStatsReceived);
            _vadSttStateToken = Raw.Subscribe<VadSttStateChanged>(HandleVadSttStateChanged);
            _roomOwnershipRebindToken =
                Raw.Subscribe<RoomOwnershipRebindStateChanged>(HandleRoomOwnershipRebindStateChanged);
            _sessionErrorToken = Raw.Subscribe<SessionError>(HandleSessionError);
            _dynamicContextUpdateResultToken =
                Raw.Subscribe<DynamicContextUpdateResultReceived>(HandleDynamicContextUpdateResultReceived);
        }

        private void Unsubscribe()
        {
            if (_sessionStateToken != default) Raw.Unsubscribe(_sessionStateToken);
            if (_participantConnectedToken != default) Raw.Unsubscribe(_participantConnectedToken);
            if (_participantDisconnectedToken != default) Raw.Unsubscribe(_participantDisconnectedToken);
            if (_characterTranscriptToken != default) Raw.Unsubscribe(_characterTranscriptToken);
            if (_playerTranscriptToken != default) Raw.Unsubscribe(_playerTranscriptToken);
            if (_characterSpeechToken != default) Raw.Unsubscribe(_characterSpeechToken);
            if (_characterEmotionToken != default) Raw.Unsubscribe(_characterEmotionToken);
            if (_characterReadyToken != default) Raw.Unsubscribe(_characterReadyToken);
            if (_characterTurnCompletedToken != default) Raw.Unsubscribe(_characterTurnCompletedToken);
            if (_playerSpeakingToken != default) Raw.Unsubscribe(_playerSpeakingToken);
            if (_micMuteToken != default) Raw.Unsubscribe(_micMuteToken);
            if (_narrativeSectionChangedToken != default) Raw.Unsubscribe(_narrativeSectionChangedToken);
            if (_usageLimitReachedToken != default) Raw.Unsubscribe(_usageLimitReachedToken);
            if (_userIdleWarningToken != default) Raw.Unsubscribe(_userIdleWarningToken);
            if (_userIdleTimeoutToken != default) Raw.Unsubscribe(_userIdleTimeoutToken);
            if (_runtimeBackgroundStateToken != default) Raw.Unsubscribe(_runtimeBackgroundStateToken);
            if (_actionReceivedToken != default) Raw.Unsubscribe(_actionReceivedToken);
            if (_moderationResponseToken != default) Raw.Unsubscribe(_moderationResponseToken);
            if (_llmNoResponseToken != default) Raw.Unsubscribe(_llmNoResponseToken);
            if (_finalUserTranscriptionToken != default) Raw.Unsubscribe(_finalUserTranscriptionToken);
            if (_interactionCreatedToken != default) Raw.Unsubscribe(_interactionCreatedToken);
            if (_blendshapeTurnStatsToken != default) Raw.Unsubscribe(_blendshapeTurnStatsToken);
            if (_vadSttStateToken != default) Raw.Unsubscribe(_vadSttStateToken);
            if (_roomOwnershipRebindToken != default) Raw.Unsubscribe(_roomOwnershipRebindToken);
            if (_sessionErrorToken != default) Raw.Unsubscribe(_sessionErrorToken);
            if (_dynamicContextUpdateResultToken != default) Raw.Unsubscribe(_dynamicContextUpdateResultToken);

            _sessionStateToken = default;
            _participantConnectedToken = default;
            _participantDisconnectedToken = default;
            _characterTranscriptToken = default;
            _playerTranscriptToken = default;
            _characterSpeechToken = default;
            _characterEmotionToken = default;
            _characterReadyToken = default;
            _characterTurnCompletedToken = default;
            _playerSpeakingToken = default;
            _micMuteToken = default;
            _narrativeSectionChangedToken = default;
            _usageLimitReachedToken = default;
            _userIdleWarningToken = default;
            _userIdleTimeoutToken = default;
            _runtimeBackgroundStateToken = default;
            _actionReceivedToken = default;
            _moderationResponseToken = default;
            _llmNoResponseToken = default;
            _finalUserTranscriptionToken = default;
            _interactionCreatedToken = default;
            _blendshapeTurnStatsToken = default;
            _vadSttStateToken = default;
            _roomOwnershipRebindToken = default;
            _sessionErrorToken = default;
            _dynamicContextUpdateResultToken = default;
        }

        private void HandleSessionStateChanged(SessionStateChanged e)
        {
            SafeEventInvoker.Invoke(
                OnSessionStateChanged,
                e,
                null,
                "ConvaiEvents.OnSessionStateChanged",
                LogCategory.Events);

            switch (e.NewState)
            {
                case SessionState.Connected
                    when e.OldState == SessionState.Connecting || e.OldState == SessionState.Reconnecting:
                    SafeEventInvoker.Invoke(OnConnected, null, "ConvaiEvents.OnConnected", LogCategory.Events);
                    break;
                case SessionState.Disconnected when e.OldState != SessionState.Disconnected:
                    SafeEventInvoker.Invoke(OnDisconnected, null, "ConvaiEvents.OnDisconnected", LogCategory.Events);
                    break;
            }
        }

        private void HandleParticipantConnected(ParticipantConnected e) =>
            SafeEventInvoker.Invoke(
                OnParticipantJoined,
                e.Participant,
                null,
                "ConvaiEvents.OnParticipantJoined",
                LogCategory.Events);

        private void HandleParticipantDisconnected(ParticipantDisconnected e) =>
            SafeEventInvoker.Invoke(
                OnParticipantLeft,
                e.Participant,
                null,
                "ConvaiEvents.OnParticipantLeft",
                LogCategory.Events);

        private void HandleCharacterTranscript(CharacterTranscriptReceived e) =>
            SafeEventInvoker.Invoke(
                OnCharacterTranscriptReceived,
                e,
                null,
                "ConvaiEvents.OnCharacterTranscriptReceived",
                LogCategory.Events);

        private void HandlePlayerTranscript(PlayerTranscriptReceived e) =>
            SafeEventInvoker.Invoke(
                OnPlayerTranscriptReceived,
                e,
                null,
                "ConvaiEvents.OnPlayerTranscriptReceived",
                LogCategory.Events);

        private void HandleCharacterSpeechStateChanged(CharacterSpeechStateChanged e) =>
            SafeEventInvoker.Invoke(
                OnCharacterSpeechStateChanged,
                e,
                null,
                "ConvaiEvents.OnCharacterSpeechStateChanged",
                LogCategory.Events);

        private void HandleCharacterEmotionChanged(CharacterEmotionChanged e) =>
            SafeEventInvoker.Invoke(
                OnCharacterEmotionChanged,
                e,
                null,
                "ConvaiEvents.OnCharacterEmotionChanged",
                LogCategory.Events);

        private void HandleCharacterReady(CharacterReady e) =>
            SafeEventInvoker.Invoke(
                OnCharacterReady,
                e,
                null,
                "ConvaiEvents.OnCharacterReady",
                LogCategory.Events);

        private void HandleCharacterTurnCompleted(CharacterTurnCompleted e) =>
            SafeEventInvoker.Invoke(
                OnCharacterTurnCompleted,
                e,
                null,
                "ConvaiEvents.OnCharacterTurnCompleted",
                LogCategory.Events);

        private void HandlePlayerSpeakingStateChanged(PlayerSpeakingStateChanged e) =>
            SafeEventInvoker.Invoke(
                OnPlayerSpeakingStateChanged,
                e,
                null,
                "ConvaiEvents.OnPlayerSpeakingStateChanged",
                LogCategory.Events);

        private void HandleMicMuteChanged(MicMuteChanged e) =>
            SafeEventInvoker.Invoke(
                OnMicMuteChanged,
                e,
                null,
                "ConvaiEvents.OnMicMuteChanged",
                LogCategory.Events);

        private void HandleVadSttStateChanged(VadSttStateChanged e) =>
            SafeEventInvoker.Invoke(
                OnVadSttStateChanged,
                e,
                null,
                "ConvaiEvents.OnVadSttStateChanged",
                LogCategory.Events);

        private void HandleNarrativeSectionChanged(NarrativeSectionChanged e) =>
            SafeEventInvoker.Invoke(
                OnNarrativeSectionChanged,
                e,
                null,
                "ConvaiEvents.OnNarrativeSectionChanged",
                LogCategory.Events);

        private void HandleUsageLimitReached(UsageLimitReached e) =>
            SafeEventInvoker.Invoke(
                OnUsageLimitReached,
                e,
                null,
                "ConvaiEvents.OnUsageLimitReached",
                LogCategory.Events);

        private void HandleUserIdleWarningReceived(UserIdleWarningReceived e) =>
            SafeEventInvoker.Invoke(
                OnUserIdleWarningReceived,
                e,
                null,
                "ConvaiEvents.OnUserIdleWarningReceived",
                LogCategory.Events);

        private void HandleUserIdleTimeoutElapsed(UserIdleTimeoutElapsed e) =>
            SafeEventInvoker.Invoke(
                OnUserIdleTimeoutElapsed,
                e,
                null,
                "ConvaiEvents.OnUserIdleTimeoutElapsed",
                LogCategory.Events);

        private void HandleRuntimeBackgroundStateChanged(RuntimeBackgroundStateChanged e) =>
            SafeEventInvoker.Invoke(
                OnRuntimeBackgroundStateChanged,
                e,
                null,
                "ConvaiEvents.OnRuntimeBackgroundStateChanged",
                LogCategory.Events);

        private void HandleActionReceived(CharacterActionReceived e) =>
            SafeEventInvoker.Invoke(
                OnCharacterActionReceived,
                e,
                null,
                "ConvaiEvents.OnCharacterActionReceived",
                LogCategory.Events);

        private void HandleModerationResponse(ModerationResponseReceived e) =>
            SafeEventInvoker.Invoke(
                OnModerationResponseReceived,
                e,
                null,
                "ConvaiEvents.OnModerationResponseReceived",
                LogCategory.Events);

        private void HandleLlmNoResponseReceived(LlmNoResponseReceived e) =>
            SafeEventInvoker.Invoke(
                OnLlmNoResponseReceived,
                e,
                null,
                "ConvaiEvents.OnLlmNoResponseReceived",
                LogCategory.Events);

        private void HandleFinalUserTranscriptionReceived(FinalUserTranscriptionReceived e) =>
            SafeEventInvoker.Invoke(
                OnFinalUserTranscriptionReceived,
                e,
                null,
                "ConvaiEvents.OnFinalUserTranscriptionReceived",
                LogCategory.Events);

        private void HandleInteractionCreated(InteractionCreated e) =>
            SafeEventInvoker.Invoke(
                OnInteractionCreated,
                e,
                null,
                "ConvaiEvents.OnInteractionCreated",
                LogCategory.Events);

        private void HandleBlendshapeTurnStatsReceived(BlendshapeTurnStatsReceived e) =>
            SafeEventInvoker.Invoke(
                OnBlendshapeTurnStatsReceived,
                e,
                null,
                "ConvaiEvents.OnBlendshapeTurnStatsReceived",
                LogCategory.Events);

        private void HandleRoomOwnershipRebindStateChanged(RoomOwnershipRebindStateChanged e) =>
            SafeEventInvoker.Invoke(
                OnRoomOwnershipRebindStateChanged,
                e,
                null,
                "ConvaiEvents.OnRoomOwnershipRebindStateChanged",
                LogCategory.Events);

        private void HandleDynamicContextUpdateResultReceived(DynamicContextUpdateResultReceived e) =>
            SafeEventInvoker.Invoke(
                OnDynamicContextUpdateResultReceived,
                e,
                null,
                "ConvaiEvents.OnDynamicContextUpdateResultReceived",
                LogCategory.Events);

        private void HandleSessionError(SessionError e)
        {
            SafeEventInvoker.Invoke(OnSessionError, e, null, "ConvaiEvents.OnSessionError", LogCategory.Events);

            if (e.IsServerError)
                SafeEventInvoker.Invoke(OnPipelineError, e, null, "ConvaiEvents.OnPipelineError", LogCategory.Events);
        }
    }
}
