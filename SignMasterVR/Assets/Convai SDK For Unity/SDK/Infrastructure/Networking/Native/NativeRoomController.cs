using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Participant;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Services;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime;
using ILogger = Convai.Domain.Logging.ILogger;
using LogCategory = Convai.Domain.Logging.LogCategory;
// Type aliases to disambiguate between LiveKit.Proto and Transport types
using TransportParticipantInfo = Convai.Infrastructure.Networking.Transport.TransportParticipantInfo;
using TransportTrackInfo = Convai.Infrastructure.Networking.Transport.TrackInfo;
using TransportTrackKind = Convai.Infrastructure.Networking.Transport.TrackKind;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native (non-WebGL) room controller that implements <see cref="IConvaiRoomController" /> directly.
    ///     Manages native room connections through the transport abstraction layer.
    /// </summary>
    internal sealed class NativeRoomController : IConvaiRoomController, IRoomDetailsStateTarget
    {
        private readonly IAgentRegistry _agentRegistry;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly NativeRoomEventBridge _eventBridge;
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;

        private readonly Dictionary<string, TransportTrackInfo> _pendingTransportAudioSubscriptions =
            new(StringComparer.Ordinal);

        private readonly IPlayerSession _playerSession;
        private readonly ProtocolGateway _protocolGateway;
        private readonly INarrativeSectionNameResolver _sectionNameResolver;
        private readonly ISessionPersistence _sessionPersistence;

        /// <summary>
        ///     Lock object for thread-safe access to public state properties.
        ///     Used to synchronize access from Unity main thread, EventHub background threads, and async tasks.
        /// </summary>
        private readonly object _stateLock = new();

        private readonly IRealtimeTransport _transport;
        private readonly ITransportConfiguration _transportConfiguration;

        private Func<string, bool> _audioSubscriptionPolicy;
        private string _characterSessionId;
        private bool _disposed;

        private bool _hasRoomDetails;
        private bool _isConnectedToRoom;
        private bool _isMicMuted;
        private IRemoteAudioControl _remoteAudioControl;
        private string _requestTraceId;
        private string _resolvedEndUserId;
        private IReadOnlyDictionary<string, object> _resolvedEndUserMetadata;
        private string _resolvedSpeakerId;
        private string _roomName;
        private string _roomUrl;
        private string _sessionId;
        private IRoomFacade _subscribedRoomFacade;
        private string _token;

        /// <summary>
        ///     Initializes a new instance of the <see cref="NativeRoomController" /> class.
        /// </summary>
        /// <param name="agentRegistry">Agent registry for resolving participants to characters.</param>
        /// <param name="playerSession">Player session abstraction.</param>
        /// <param name="transportConfiguration">Read-only transport/session configuration.</param>
        /// <param name="sessionPersistence">Character-session persistence adapter.</param>
        /// <param name="dispatcher">Dispatcher used to marshal work to the main thread.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="eventHub">Optional event hub used for domain events.</param>
        /// <param name="sectionNameResolver">Optional resolver for human-readable narrative section names.</param>
        /// <param name="transport">Realtime transport abstraction backing the native room controller.</param>
        public NativeRoomController(
            IAgentRegistry agentRegistry,
            IPlayerSession playerSession,
            ITransportConfiguration transportConfiguration,
            ISessionPersistence sessionPersistence,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub = null,
            INarrativeSectionNameResolver sectionNameResolver = null,
            IRealtimeTransport transport = null)
        {
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _playerSession = playerSession ?? throw new ArgumentNullException(nameof(playerSession));
            _transportConfiguration =
                transportConfiguration ?? throw new ArgumentNullException(nameof(transportConfiguration));
            _sessionPersistence = sessionPersistence ?? throw new ArgumentNullException(nameof(sessionPersistence));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = (logger ?? throw new ArgumentNullException(nameof(logger))).WithTag(nameof(NativeRoomController));
            _eventHub = eventHub;
            _sectionNameResolver = sectionNameResolver;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _eventBridge = new NativeRoomEventBridge(this);

            _transport.StateChanged += HandleConnectionStateChanged;
            _transport.ConnectionFailed += HandleConnectionFailed;
            _transport.DataReceived += HandleDataPacketReceived;
            _transport.ParticipantConnected += OnTransportParticipantConnected;
            _transport.ParticipantDisconnected += OnTransportParticipantDisconnected;
            _transport.TrackSubscribed += _eventBridge.HandleTransportTrackSubscribed;
            _transport.TrackUnsubscribed += _eventBridge.HandleTransportTrackUnsubscribed;
            _transport.Disconnected += HandleTransportDisconnected;
            _transport.Reconnecting += HandleTransportReconnecting;
            _transport.Reconnected += HandleTransportReconnected;

            _protocolGateway = new ProtocolGateway(
                logDebug: message => _logger.Debug(message, LogCategory.Transport),
                logError: message => _logger.Error(message, LogCategory.Transport));

            _eventBridge.RefreshRoomFacadeAudioBridge();
        }

        /// <inheritdoc />
        public IRoomFacade CurrentRoom => _transport.Room;

        /// <inheritdoc />
        public RTVIHandler RTVIHandler { get; private set; }

        /// <summary>
        ///     Indicates whether room connection details have been successfully retrieved.
        ///     Thread-safe property.
        /// </summary>
        public bool HasRoomDetails
        {
            get
            {
                lock (_stateLock) return _hasRoomDetails;
            }
            private set
            {
                lock (_stateLock) _hasRoomDetails = value;
            }
        }

        /// <summary>
        ///     Indicates whether currently connected to the LiveKit room.
        ///     Thread-safe property.
        /// </summary>
        public bool IsConnectedToRoom
        {
            get
            {
                lock (_stateLock) return _isConnectedToRoom;
            }
            private set
            {
                lock (_stateLock) _isConnectedToRoom = value;
            }
        }

        /// <summary>
        ///     Indicates whether the microphone is currently muted.
        ///     Thread-safe property.
        /// </summary>
        public bool IsMicMuted
        {
            get
            {
                lock (_stateLock) return _isMicMuted;
            }
            private set
            {
                lock (_stateLock) _isMicMuted = value;
            }
        }

        /// <summary>
        ///     The authentication token for the current room connection.
        ///     Thread-safe property.
        /// </summary>
        public string Token
        {
            get
            {
                lock (_stateLock) return _token;
            }
            private set
            {
                lock (_stateLock) _token = value;
            }
        }

        /// <summary>
        ///     The name of the current room.
        ///     Thread-safe property.
        /// </summary>
        public string RoomName
        {
            get
            {
                lock (_stateLock) return _roomName;
            }
            private set
            {
                lock (_stateLock) _roomName = value;
            }
        }

        /// <summary>
        ///     The session ID for the current connection.
        ///     Thread-safe property.
        /// </summary>
        public string SessionID
        {
            get
            {
                lock (_stateLock) return _sessionId;
            }
            private set
            {
                lock (_stateLock) _sessionId = value;
            }
        }

        /// <summary>
        ///     The URL of the current room.
        ///     Thread-safe property.
        /// </summary>
        public string RoomURL
        {
            get
            {
                lock (_stateLock) return _roomUrl;
            }
            private set
            {
                lock (_stateLock) _roomUrl = value;
            }
        }

        /// <summary>
        ///     The character-specific session ID for conversation continuity.
        ///     Thread-safe property.
        /// </summary>
        public string CharacterSessionID
        {
            get
            {
                lock (_stateLock) return _characterSessionId;
            }
            private set
            {
                lock (_stateLock) _characterSessionId = value;
            }
        }

        /// <summary>
        ///     Optional backend-resolved speaker ID for the local participant.
        ///     This is diagnostics/state carried through when the backend includes it.
        ///     Thread-safe property.
        /// </summary>
        /// <remarks>
        ///     This is NOT the same as the end_user_id sent during connection.
        ///     The backend may resolve <c>end_user_id</c> to an internal <c>speaker_id</c> for persistence and
        ///     memory storage, but current WebRTC connect responses may omit this field entirely.
        /// </remarks>
        public string ResolvedSpeakerId
        {
            get
            {
                lock (_stateLock) return _resolvedSpeakerId;
            }
            private set
            {
                lock (_stateLock) _resolvedSpeakerId = value;
            }
        }

        /// <inheritdoc />
        public string RequestTraceId
        {
            get
            {
                lock (_stateLock) return _requestTraceId;
            }
            private set
            {
                lock (_stateLock) _requestTraceId = value;
            }
        }

        /// <inheritdoc />
        public string ResolvedEndUserId
        {
            get
            {
                lock (_stateLock) return _resolvedEndUserId;
            }
            private set
            {
                lock (_stateLock) _resolvedEndUserId = value;
            }
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, object> ResolvedEndUserMetadata
        {
            get
            {
                lock (_stateLock) return _resolvedEndUserMetadata;
            }
            private set
            {
                lock (_stateLock) _resolvedEndUserMetadata = value;
            }
        }

        /// <inheritdoc />
        public event Action OnRoomConnectionSuccessful;

        /// <inheritdoc />
        public event Action OnRoomConnectionFailed;

        /// <inheritdoc />
        public event Action<bool> OnMicMuteChanged;

        /// <inheritdoc />
        public event Action OnRoomReconnecting;

        /// <inheritdoc />
        public event Action OnRoomReconnected;

        /// <inheritdoc />
        public event Action OnUnexpectedRoomDisconnected;

        /// <inheritdoc />
        public event Action<IRemoteAudioTrack, string, string> OnRemoteAudioTrackSubscribed;

        /// <inheritdoc />
        public event Action<string, string> OnRemoteAudioTrackUnsubscribed;

        /// <inheritdoc />
        public Task<RoomConnectionAttemptResult> InitializeAsync(
            string connectionType,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            string dynamicInfoText,
            bool keepDynamicInfoInContext) =>
            InitializeAsync(
                connectionType,
                coreServerUrl,
                characterId,
                storedSessionId,
                enableSessionResume,
                dynamicInfoText,
                keepDynamicInfoInContext,
                null,
                CancellationToken.None);

        /// <inheritdoc />
        public async Task<RoomConnectionAttemptResult> InitializeAsync(
            string connectionType,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            string dynamicInfoText,
            bool keepDynamicInfoInContext,
            RoomJoinOptions joinOptions,
            CancellationToken cancellationToken = default)
        {
            HasRoomDetails = false;
            RoomEmotionConfig emotionConfig = joinOptions?.ResolvedEmotionConfig;
            string requestEndUserId = joinOptions?.ResolvedEndUserId ?? _transportConfiguration.EndUserId;
            IReadOnlyDictionary<string, object> requestEndUserMetadata =
                joinOptions?.ResolvedEndUserMetadata ?? _transportConfiguration.EndUserMetadata;
            string playerNameForRequest = joinOptions?.ResolvedEndUserMetadata != null
                ? null
                : _playerSession?.PlayerName;

            RoomSessionStartupPlan startupPlan = RoomSessionStartupKernel.Prepare(
                characterId,
                connectionType,
                coreServerUrl,
                storedSessionId,
                enableSessionResume,
                joinOptions,
                requestEndUserId,
                requestEndUserMetadata,
                _transportConfiguration.VideoTrackName,
                emotionConfig,
                joinOptions?.ResolvedTurnTakingOptions ?? ResolvedTurnTakingOptions.DefaultHandsFree,
                _transportConfiguration.LipSyncTransportOptions,
                StoredSessionFallbackPolicy.NativeCompatibility,
                InvalidStoredSessionRecoveryPolicy.RetryWithoutStoredSessionAllowed,
                dynamicInfoText,
                keepDynamicInfoInContext,
                _transportConfiguration.Debug,
                playerNameForRequest,
                joinOptions?.ResolvedUserVadSettings,
                joinOptions?.ResolvedVisionInputConfig,
                joinOptions?.ResolvedRespondModes);

            _logger.Debug(startupPlan.FormatModeLogMessage(), LogCategory.Transport);

            RoomConnectionAttemptResult attemptResult = await ConnectToConvai(startupPlan, characterId);
            HasRoomDetails = attemptResult.Succeeded;
            IsConnectedToRoom = attemptResult.Succeeded;

            if (!attemptResult.Succeeded)
            {
                _logger.Error(
                    $"Failed to connect to Convai and LiveKit room: {attemptResult.Failure.Message}",
                    LogCategory.Transport);
                OnRoomConnectionFailed?.Invoke();
                return attemptResult;
            }

            _logger.Info("Connected to Convai and LiveKit room successfully", LogCategory.Transport);
            OnRoomConnectionSuccessful?.Invoke();
            return RoomConnectionAttemptResult.Success();
        }


        /// <summary>
        ///     Disconnects from the room synchronously (fire-and-forget).
        ///     For proper async disconnect that waits for completion, use <see cref="DisconnectFromRoomAsync" />.
        /// </summary>
        public void DisconnectFromRoom() => _ = DisconnectFromRoomAsync();

        /// <summary>
        ///     Disconnects from the room asynchronously, waiting for the underlying transport to complete cleanup.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A task that completes when the disconnect is finished.</returns>
        public async Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default)
        {
            _logger.Debug("Disconnecting from room...", LogCategory.Transport);

            await DisconnectFromRoomViaTransport(cancellationToken);

            ResetControllerOwnedDisconnectState();

            _logger.Info("Disconnected from room", LogCategory.Transport);
        }

        /// <summary>Sets whether the local microphone is muted.</summary>
        /// <param name="mute">True to mute; false to unmute.</param>
        public void SetMicMuted(bool mute)
        {
            IsMicMuted = mute;

            _playerSession.SetMicMuted(mute);

            ILocalParticipant localParticipant = CurrentRoom?.LocalParticipant;
            if (localParticipant != null)
            {
                try
                {
                    localParticipant.SetAudioMuted(mute);
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        $"Failed to apply local participant mic mute state: {ex.Message}",
                        LogCategory.Audio);
                }
            }

            OnMicMuteChanged?.Invoke(IsMicMuted);
        }

        /// <summary>Toggles the local microphone mute state.</summary>
        public void ToggleMicMute() => SetMicMuted(!IsMicMuted);

        /// <summary>Sets whether the given character's audio is muted.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <param name="mute">True to mute; false to unmute.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool SetCharacterAudioMuted(string characterId, bool mute)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                _logger.Debug("Attempted to set mute on a null Character ID", LogCategory.Character);
                return false;
            }

            if (!_agentRegistry.TryGetCharacter(characterId, out IConvaiCharacterAgent _))
            {
                _logger.Debug($"Character '{characterId}' is not registered; cannot update mute state.",
                    LogCategory.Character);
                return false;
            }

            _agentRegistry.SetCharacterMuted(characterId, mute);
            return true;
        }

        /// <summary>Mutes the given character's audio.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool MuteCharacter(string characterId) => SetCharacterAudioMuted(characterId, true);

        /// <summary>Unmutes the given character's audio.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool UnmuteCharacter(string characterId) => SetCharacterAudioMuted(characterId, false);

        /// <summary>Gets whether the given character's audio is muted.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <returns>True if muted; otherwise false.</returns>
        public bool IsCharacterAudioMuted(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;

            return _agentRegistry.TryGetCharacter(characterId, out IConvaiCharacterAgent _) &&
                   _agentRegistry.IsCharacterMuted(characterId);
        }

        /// <summary>
        ///     Sets the per-character audio subscription policy callback.
        ///     The callback is invoked when an audio track is published to determine if it should be subscribed.
        /// </summary>
        /// <param name="policy">A function that returns true if audio should be subscribed for the given participant identity.</param>
        public void SetAudioSubscriptionPolicy(Func<string, bool> policy)
        {
            _audioSubscriptionPolicy = policy;

            _logger.Debug("Audio subscription policy configured", LogCategory.Audio);
        }

        /// <summary>
        ///     Applies the remote audio preference for a character at runtime.
        ///     Call this when the preference changes after the track has already been subscribed/unsubscribed.
        /// </summary>
        /// <param name="characterId">The character identifier.</param>
        /// <param name="enabled">True to enable (subscribe) audio; false to disable (unsubscribe).</param>
        public void ApplyRemoteAudioPreference(string characterId, bool enabled)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            string participantIdentity = ResolveParticipantIdentity(characterId);
            _agentRegistry.TryGetParticipantId(characterId, out string participantSid);
            participantSid ??= string.Empty;

            bool applied = ResolveRemoteAudioControl()
                .Apply(characterId, participantIdentity, participantSid, enabled);
            if (!applied)
            {
                _logger.Debug(
                    $"No cached remote-audio control target for character: {characterId} (identity: {participantIdentity})",
                    LogCategory.Audio);
            }
        }

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            _transport.StateChanged -= HandleConnectionStateChanged;
            _transport.ConnectionFailed -= HandleConnectionFailed;
            _transport.DataReceived -= HandleDataPacketReceived;
            _transport.ParticipantConnected -= OnTransportParticipantConnected;
            _transport.ParticipantDisconnected -= OnTransportParticipantDisconnected;
            _transport.TrackSubscribed -= _eventBridge.HandleTransportTrackSubscribed;
            _transport.TrackUnsubscribed -= _eventBridge.HandleTransportTrackUnsubscribed;
            _transport.Disconnected -= HandleTransportDisconnected;
            _transport.Reconnecting -= HandleTransportReconnecting;
            _transport.Reconnected -= HandleTransportReconnected;
            _eventBridge.DetachRoomFacadeAudioBridge();
            _disposed = true;
        }

        #endregion

        string IRoomDetailsStateTarget.Token
        {
            get => Token;
            set => Token = value;
        }

        string IRoomDetailsStateTarget.RoomName
        {
            get => RoomName;
            set => RoomName = value;
        }

        string IRoomDetailsStateTarget.RoomURL
        {
            get => RoomURL;
            set => RoomURL = value;
        }

        string IRoomDetailsStateTarget.SessionID
        {
            get => SessionID;
            set => SessionID = value;
        }

        string IRoomDetailsStateTarget.CharacterSessionID
        {
            get => CharacterSessionID;
            set => CharacterSessionID = value;
        }

        string IRoomDetailsStateTarget.ResolvedSpeakerId
        {
            get => ResolvedSpeakerId;
            set => ResolvedSpeakerId = value;
        }

        string IRoomDetailsStateTarget.RequestTraceId
        {
            get => RequestTraceId;
            set => RequestTraceId = value;
        }

        string IRoomDetailsStateTarget.ResolvedEndUserId
        {
            get => ResolvedEndUserId;
            set => ResolvedEndUserId = value;
        }

        IReadOnlyDictionary<string, object> IRoomDetailsStateTarget.ResolvedEndUserMetadata
        {
            get => ResolvedEndUserMetadata;
            set => ResolvedEndUserMetadata = value;
        }

        bool IRoomDetailsStateTarget.HasRoomDetails
        {
            get => HasRoomDetails;
            set => HasRoomDetails = value;
        }

        /// <summary>
        ///     Connects to a Convai room using the specified room request and character ID, with optional session resume.
        /// </summary>
        public async Task<RoomConnectionAttemptResult> ConnectToConvai(RoomSessionStartupPlan startupPlan,
            string characterId)
            => await ConnectToConvai(new NativeRoomConnectionSession(startupPlan, characterId));

        private async Task<RoomConnectionAttemptResult> ConnectToConvai(NativeRoomConnectionSession session)
        {
            RoomConnectionRequest roomRequest = session.Request;
            Task<(bool success, RoomDetails details, Exception error)> roomDetailsTask =
                TryGetRoomDetailsAsync(roomRequest);

            (bool success, RoomDetails details, Exception error) attemptWithSessionId = await roomDetailsTask;
            if (attemptWithSessionId.success)
            {
                if (attemptWithSessionId.details == null || string.IsNullOrEmpty(attemptWithSessionId.details.Token))
                {
                    RoomSessionStartupDecision invalidDetailsDecision = RoomSessionStartupKernel.FromInvalidRoomDetails(
                        session.StartupPlan,
                        "Failed to get room details");
                    _logger.Debug(invalidDetailsDecision.FormatDiagnosticsLogMessage(), LogCategory.Transport);
                    return RoomConnectionAttemptResult.Fail(invalidDetailsDecision.FailureOutcome.Failure);
                }

                RoomSessionStartupDecision acceptedDecision = RoomSessionStartupKernel.AcceptRoomDetails(
                    session.StartupPlan,
                    attemptWithSessionId.details);
                _logger.Debug(acceptedDecision.FormatDiagnosticsLogMessage(), LogCategory.Transport);
                ApplyRoomDetails(acceptedDecision.AppliedRoomDetailsState);
                RoomInitializationRecoverySupport.TryPersistCharacterSession(
                    _sessionPersistence,
                    _logger,
                    session.CharacterId,
                    CharacterSessionID,
                    acceptedDecision.InitializationOutcome);

                return await ConnectToRoomAndInitialize();
            }

            RoomSessionStartupDecision failureDecision = RoomSessionStartupKernel.FromRequestException(
                session.StartupPlan,
                attemptWithSessionId.error);
            _logger.Debug(failureDecision.FormatDiagnosticsLogMessage(), LogCategory.Transport);

            if (failureDecision.InitializationOutcome.ShouldRetryWithoutStoredSession)
            {
                RoomInitializationRecoverySupport.TryClearStoredSessionForRecovery(
                    _sessionPersistence,
                    _logger,
                    session.CharacterId,
                    roomRequest,
                    failureDecision.InitializationOutcome);

                (bool success, RoomDetails details, Exception error) attemptWithoutSessionId =
                    await TryGetRoomDetailsAsync(roomRequest);
                if (attemptWithoutSessionId.success)
                {
                    if (attemptWithoutSessionId.details == null ||
                        string.IsNullOrEmpty(attemptWithoutSessionId.details.Token))
                    {
                        RoomSessionStartupDecision invalidDetailsDecision =
                            RoomSessionStartupKernel.FromInvalidRoomDetails(
                                session.StartupPlan,
                                "Failed to get room details");
                        _logger.Debug(invalidDetailsDecision.FormatDiagnosticsLogMessage(), LogCategory.Transport);
                        return RoomConnectionAttemptResult.Fail(invalidDetailsDecision.FailureOutcome.Failure);
                    }

                    RoomSessionStartupDecision retryAcceptedDecision = RoomSessionStartupKernel.AcceptRoomDetails(
                        session.StartupPlan,
                        attemptWithoutSessionId.details);
                    _logger.Debug(retryAcceptedDecision.FormatDiagnosticsLogMessage(), LogCategory.Transport);
                    ApplyRoomDetails(retryAcceptedDecision.AppliedRoomDetailsState);
                    RoomInitializationRecoverySupport.TryPersistCharacterSession(
                        _sessionPersistence,
                        _logger,
                        session.CharacterId,
                        CharacterSessionID,
                        retryAcceptedDecision.InitializationOutcome);
                    return await ConnectToRoomAndInitialize();
                }

                RoomSessionStartupDecision retryFailureDecision = RoomSessionStartupKernel.FromRequestException(
                    session.StartupPlan,
                    attemptWithoutSessionId.error);
                _logger.Debug(retryFailureDecision.FormatDiagnosticsLogMessage(), LogCategory.Transport);

                _logger.Error($"Error: {attemptWithoutSessionId.error.Message}", LogCategory.Transport);
                return RoomConnectionAttemptResult.Fail(retryFailureDecision.FailureOutcome.Failure);
            }

            _logger.Error($"Error: {attemptWithSessionId.error.Message}", LogCategory.Transport);
            return RoomConnectionAttemptResult.Fail(failureDecision.FailureOutcome.Failure);
        }

        private async Task<(bool success, RoomDetails details, Exception error)> TryGetRoomDetailsAsync(
            RoomConnectionRequest roomRequest)
        {
            try
            {
                // Capture the resolved credential to avoid repeated provider resolution.
                string credential = _transportConfiguration.ApiKey;
                bool usesAuthToken = TransportAuthenticationSupport.UsesAuthToken(_transportConfiguration);
                var options = ConvaiRestOptionsFactory.CreateForRuntimeCredential(credential, usesAuthToken);
                using var client = new ConvaiRestClient(options);
                RoomDetails details = await client.Rooms.ConnectAsync(roomRequest).ConfigureAwait(false);
                return (true, details, null);
            }
            catch (ConvaiRestException ex)
            {
                return (false, null,
                    new RoomInitializationFetchException(
                        ex.Message,
                        ex.Message,
                        ex.StatusCodeInt,
                        ex.ResponseBody,
                        ex));
            }
            catch (Exception ex)
            {
                return (false, null, ex);
            }
        }

        private void ApplyRoomDetails(in AppliedRoomDetailsState roomDetailsState) =>
            RoomDetailsStateApplier.Apply(this, roomDetailsState, _logger);

        /// <summary>
        ///     Connects to the LiveKit room and initializes the RTVI handler.
        /// </summary>
        private async Task<RoomConnectionAttemptResult> ConnectToRoomAndInitialize()
        {
            PreparedRtviHandlerDependencies rtviHandlerDependencies =
                RoomTransportConnectSupport.PrepareRtviHandlerDependencies(
                    _protocolGateway,
                    _transport,
                    _agentRegistry,
                    _playerSession,
                    _dispatcher,
                    _logger,
                    _eventHub,
                    _sectionNameResolver,
                    _transportConfiguration.LipSyncTransportOptions);

            RTVIHandler = rtviHandlerDependencies.CreateHandler();

            TransportConnectOutcome roomConnected = await ConnectToRoom();
            if (!roomConnected.Connected)
            {
                RTVIHandler = null;
                return RoomConnectionAttemptResult.Fail(ConnectionFailure.Create(
                    SessionErrorCodes.TransportLivekitError,
                    roomConnected.FailureMessage,
                    SessionErrorStage.Transport,
                    true));
            }

            _logger.Debug("Connected to Convai and LiveKit room successfully", LogCategory.Transport);
            return RoomConnectionAttemptResult.Success();
        }

        private async Task<TransportConnectOutcome> ConnectToRoom()
        {
            _logger.Debug("Connecting to Room...", LogCategory.Transport);

            return await ConnectToRoomViaTransport();
        }

        /// <summary>
        ///     Connects to room using the IRealtimeTransport abstraction layer.
        /// </summary>
        private async Task<TransportConnectOutcome> ConnectToRoomViaTransport()
        {
            _logger.Debug("Connecting to Room via Transport abstraction...", LogCategory.Transport);

            try
            {
                _pendingTransportAudioSubscriptions.Clear();
                var transportOptions = new TransportConnectOptions { AutoSubscribe = true };

                bool connected = await _transport.ConnectAsync(RoomURL, Token, transportOptions);
                TransportConnectOutcome connectOutcome = RoomTransportConnectSupport.FromConnectResult(
                    connected,
                    "Failed to connect via transport: Connection returned false");

                if (connectOutcome.Connected)
                {
                    _eventBridge.RefreshRoomFacadeAudioBridge();
                    _logger.Debug("Connected to room (via transport): " + RoomName, LogCategory.Transport);
                    _logger.Debug("Session ID: " + SessionID, LogCategory.Transport);
                    _logger.Debug($"Transport state: {_transport.State}", LogCategory.Transport);
                    return connectOutcome;
                }

                _logger.Error(connectOutcome.FailureLogMessage, LogCategory.Transport);
                return connectOutcome;
            }
            catch (Exception ex)
            {
                TransportConnectOutcome connectOutcome = RoomTransportConnectSupport.FromException(
                    ex,
                    "Failed to connect via transport");
                _logger.Error(connectOutcome.FailureLogMessage, LogCategory.Transport);
                return connectOutcome;
            }
        }

        /// <summary>
        ///     Disconnects from room using the IRealtimeTransport abstraction layer.
        /// </summary>
        private async Task DisconnectFromRoomViaTransport(CancellationToken cancellationToken)
        {
            _logger.Debug("Disconnecting via Transport...", LogCategory.Transport);

            _eventBridge.DetachRoomFacadeAudioBridge();

            await _transport.DisconnectAsync(DisconnectReason.ClientInitiated, cancellationToken);
        }


        private void ResetControllerOwnedDisconnectState()
        {
            RTVIHandler = null;
            Token = null;
            RoomName = null;
            RoomURL = null;
            SessionID = null;
            CharacterSessionID = null;
            ResolvedSpeakerId = null;
            RequestTraceId = null;
            ResolvedEndUserId = null;
            ResolvedEndUserMetadata = null;
            HasRoomDetails = false;
            IsConnectedToRoom = false;
            IsMicMuted = false;
            _playerSession.SetMicMuted(false);
            _agentRegistry.ClearTransportBindings();
        }

        private void HandleUnsolicitedDisconnectCleanup(string pathLabel)
        {
            _logger.Debug($"Running unsolicited disconnect cleanup via {pathLabel}",
                LogCategory.Transport);
            ResetControllerOwnedDisconnectState();
            OnUnexpectedRoomDisconnected?.Invoke();
        }

        private string ResolveParticipantIdentity(string characterId)
        {
            string participantIdentity = characterId;
            if (!_agentRegistry.TryGetParticipantId(characterId, out string participantId) ||
                string.IsNullOrEmpty(participantId)) return participantIdentity;

            IRoomFacade currentRoom = CurrentRoom;
            if (currentRoom != null &&
                currentRoom.TryGetParticipantBySid(participantId, out IRemoteParticipant participant) &&
                !string.IsNullOrEmpty(participant.Identity))
                participantIdentity = participant.Identity;

            return participantIdentity;
        }

        private IRemoteAudioControl ResolveRemoteAudioControl()
        {
            _remoteAudioControl ??= CreateRemoteAudioControl();
            return _remoteAudioControl;
        }

        private string ResolveCharacterIdFromParticipant(string participantSid, string participantIdentity = null)
        {
            if (!string.IsNullOrEmpty(participantSid) &&
                _agentRegistry.TryGetCharacterByParticipantId(participantSid, out IConvaiCharacterAgent byParticipant))
                return byParticipant.CharacterId;

            if (!string.IsNullOrEmpty(participantIdentity) &&
                _agentRegistry.TryGetCharacter(participantIdentity, out IConvaiCharacterAgent byIdentity))
                return byIdentity.CharacterId;

            IReadOnlyList<IConvaiCharacterAgent> allCharacters = _agentRegistry.Characters;
            return allCharacters != null && allCharacters.Count == 1 ? allCharacters[0].CharacterId : null;
        }

        private void RefreshRoomFacadeAudioBridgeCore()
        {
            IRoomFacade currentRoom = CurrentRoom;
            if (ReferenceEquals(_subscribedRoomFacade, currentRoom))
            {
                ReconcilePendingTransportAudioSubscriptionsCore();
                return;
            }

            bool hadSubscribedRoomFacade = _subscribedRoomFacade != null;
            DetachRoomFacadeAudioBridgeCore(clearPendingSubscriptions: hadSubscribedRoomFacade);
            if (currentRoom == null) return;

            currentRoom.AudioTrackSubscribed += _eventBridge.HandleRoomFacadeAudioTrackSubscribed;
            _subscribedRoomFacade = currentRoom;
            ReconcilePendingTransportAudioSubscriptionsCore();
        }

        private void DetachRoomFacadeAudioBridgeCore(bool clearPendingSubscriptions = true)
        {
            if (_subscribedRoomFacade != null)
                _subscribedRoomFacade.AudioTrackSubscribed -= _eventBridge.HandleRoomFacadeAudioTrackSubscribed;

            _subscribedRoomFacade = null;
            if (clearPendingSubscriptions)
                _pendingTransportAudioSubscriptions.Clear();
        }

        private bool QueuePendingTransportAudioSubscriptionCore(TransportTrackInfo track)
        {
            if (string.IsNullOrEmpty(track.TrackSid)) return false;

            _pendingTransportAudioSubscriptions[track.TrackSid] = track;
            return true;
        }

        private void ReconcilePendingTransportAudioSubscriptionsCore()
        {
            if (_pendingTransportAudioSubscriptions.Count == 0) return;

            foreach (TransportTrackInfo track in _pendingTransportAudioSubscriptions.Values.ToArray())
            {
                if (track.Kind != TransportTrackKind.Audio) continue;

                if (!TryResolveTransportRemoteAudioTrack(track.ParticipantIdentity, track.ParticipantId, track.TrackSid,
                        out IRemoteParticipant participant, out IRemoteAudioTrack audioTrack))
                    continue;

                _pendingTransportAudioSubscriptions.Remove(track.TrackSid);
                string participantIdentity = track.ParticipantIdentity ?? participant.Identity;
                bool shouldSubscribe = _audioSubscriptionPolicy?.Invoke(participantIdentity) ?? true;
                HandleResolvedTransportAudioSubscription(track, participant, audioTrack, shouldSubscribe,
                    "pending-transport");
            }
        }

        private void HandleRoomFacadeAudioTrackSubscribedCore(IRemoteAudioTrack audioTrack,
            IRemoteParticipant participant)
        {
            if (audioTrack == null || participant == null || string.IsNullOrEmpty(audioTrack.Sid)) return;
            if (!_pendingTransportAudioSubscriptions.TryGetValue(audioTrack.Sid, out TransportTrackInfo track)) return;

            _pendingTransportAudioSubscriptions.Remove(audioTrack.Sid);

            bool shouldSubscribe =
                _audioSubscriptionPolicy?.Invoke(track.ParticipantIdentity ?? participant.Identity) ?? true;
            HandleResolvedTransportAudioSubscription(track, participant, audioTrack, shouldSubscribe, "room-facade");
        }

        private void HandleResolvedTransportAudioSubscription(
            TransportTrackInfo track,
            IRemoteParticipant participant,
            IRemoteAudioTrack audioTrack,
            bool shouldSubscribe,
            string resolutionPath)
        {
            if (participant == null || audioTrack == null) return;

            string participantSid = !string.IsNullOrEmpty(participant.Sid) ? participant.Sid : track.ParticipantId;
            string participantIdentity = !string.IsNullOrEmpty(participant.Identity)
                ? participant.Identity
                : track.ParticipantIdentity;
            string characterId = ResolveCharacterIdFromParticipant(participantSid, participantIdentity);

            if (!shouldSubscribe)
            {
                _logger.Debug(
                    $"Remote audio disabled for participant: {participantIdentity}; disabling audio track via {resolutionPath} resolution seam.",
                    LogCategory.Audio);

                if (audioTrack is IRemoteAudioControlTrack controllableTrack)
                    controllableTrack.SetRemoteAudioEnabled(false);

                return;
            }

            _logger.Debug(
                $"Audio track detected via {resolutionPath}: Name={track.Name}, Sid={track.TrackSid}",
                LogCategory.Audio);
            RemoteTrackSessionNotificationSupport.NotifyAudioTrackSubscribed(
                OnRemoteAudioTrackSubscribed,
                audioTrack,
                participantSid,
                characterId,
                _logger,
                nameof(NativeRoomController));
        }

        private IRemoteAudioControl CreateRemoteAudioControl() => new RoomFacadeRemoteAudioControl(this);

        private void HandleConnectionStateChanged(TransportState state) =>
            IsConnectedToRoom = state == TransportState.Connected;

        private void HandleConnectionFailed(TransportError error) =>
            _logger.Error($"Transport connection failed: {error.Message}", LogCategory.Transport);

        private void HandleDataPacketReceived(DataPacket packet)
            => ProtocolPacketDispatchSupport.DispatchIncoming(packet, _protocolGateway, RTVIHandler);

        private sealed class NativeRoomConnectionSession
        {
            public NativeRoomConnectionSession(RoomSessionStartupPlan startupPlan, string characterId)
            {
                StartupPlan = startupPlan;
                CharacterId = characterId ?? string.Empty;
            }

            public RoomSessionStartupPlan StartupPlan { get; }
            public string CharacterId { get; }
            public RoomConnectionRequest Request => StartupPlan.Request;
        }

        private sealed class NativeRoomEventBridge
        {
            private readonly NativeRoomController _owner;

            public NativeRoomEventBridge(NativeRoomController owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public void RefreshRoomFacadeAudioBridge() => _owner.RefreshRoomFacadeAudioBridgeCore();

            public void DetachRoomFacadeAudioBridge() => _owner.DetachRoomFacadeAudioBridgeCore();

            public bool QueuePendingTransportAudioSubscription(TransportTrackInfo track) =>
                _owner.QueuePendingTransportAudioSubscriptionCore(track);

            public void HandleRoomFacadeAudioTrackSubscribed(IRemoteAudioTrack audioTrack,
                IRemoteParticipant participant) =>
                _owner.HandleRoomFacadeAudioTrackSubscribedCore(audioTrack, participant);

            public void HandleTransportTrackSubscribed(TransportTrackInfo track) =>
                _owner.HandleTransportTrackSubscribedCore(track);

            public void HandleTransportTrackUnsubscribed(TransportTrackInfo track) =>
                _owner.HandleTransportTrackUnsubscribedCore(track);
        }

        #region Transport Event Handlers

        private void HandleTransportDisconnected(DisconnectReason reason)
        {
            _logger.Debug($"Transport disconnected with reason: {reason}",
                LogCategory.Transport);

            if (reason == DisconnectReason.ClientInitiated) return;

            HandleUnsolicitedDisconnectCleanup("transport-disconnected-event");
        }

        private void OnTransportParticipantConnected(TransportParticipantInfo participant)
        {
            _logger.Debug($"Participant connected (via transport): {participant.Identity}", LogCategory.Transport);
            _logger.Debug($"Participant SID: {participant.ParticipantId}", LogCategory.Transport);

            bool matchedRegistry =
                _agentRegistry.TryGetCharacter(participant.Identity, out IConvaiCharacterAgent agent);
            string characterId;
            if (!matchedRegistry)
            {
                IReadOnlyList<IConvaiCharacterAgent> allCharacters = _agentRegistry.Characters;
                if (allCharacters.Count == 0)
                {
                    _logger.Debug("Cannot map participant: No Characters in registry", LogCategory.Character);
                    return;
                }

                agent = allCharacters[0];
                characterId = agent.CharacterId;
                _logger.Debug(
                    $"No Character matched identity '{participant.Identity}'. Using default Character: {characterId}",
                    LogCategory.Character);
            }
            else
                characterId = agent.CharacterId;

            _agentRegistry.SetParticipantId(characterId, participant.ParticipantId);
            _logger.Debug(
                $"Mapped participant {participant.Identity} (SID: {participant.ParticipantId}) to Character: {characterId}",
                LogCategory.Character);

            ParticipantEventPublicationSupport.PublishConnected(
                _eventHub,
                _logger,
                ParticipantInfo.ForCharacter(participant.ParticipantId, participant.Identity, participant.Identity),
                nameof(NativeRoomController));
        }

        private void OnTransportParticipantDisconnected(TransportParticipantInfo participant)
        {
            _logger.Debug($"Participant disconnected (via transport): {participant.Identity}", LogCategory.Transport);

            if (_agentRegistry.TryGetCharacterByParticipantId(participant.ParticipantId,
                    out IConvaiCharacterAgent agent))
            {
                _agentRegistry.SetParticipantId(agent.CharacterId, null);
                _logger.Debug($"Cleared participant mapping for Character: {agent.CharacterId}",
                    LogCategory.Character);
            }

            ParticipantEventPublicationSupport.PublishDisconnected(
                _eventHub,
                _logger,
                ParticipantInfo.ForCharacter(participant.ParticipantId, participant.Identity, participant.Identity),
                nameof(NativeRoomController));
        }

        private void HandleTransportTrackSubscribedCore(TransportTrackInfo track)
        {
            _logger.Debug(
                $"Track subscribed (via transport): {track.Name} from participant: {track.ParticipantIdentity}",
                LogCategory.Transport);

            if (track.Kind == TransportTrackKind.Audio)
            {
                bool shouldSubscribe = _audioSubscriptionPolicy?.Invoke(track.ParticipantIdentity) ?? true;

                if (!TryResolveTransportRemoteAudioTrack(track.ParticipantIdentity, track.ParticipantId, track.TrackSid,
                        out IRemoteParticipant participant, out IRemoteAudioTrack audioTrack))
                {
                    if (_eventBridge.QueuePendingTransportAudioSubscription(track))
                    {
                        _logger.Debug(
                            $"Transport audio track not yet available in room facade; awaiting track wrapper for participant: {track.ParticipantIdentity} ({track.ParticipantId})",
                            LogCategory.Audio);
                        return;
                    }

                    _logger.Debug(
                        $"Unable to resolve transport audio track for participant: {track.ParticipantIdentity} ({track.ParticipantId})",
                        LogCategory.Audio);
                    return;
                }

                HandleResolvedTransportAudioSubscription(track, participant, audioTrack, shouldSubscribe, "transport");
            }
        }

        private void HandleTransportTrackUnsubscribedCore(TransportTrackInfo track)
        {
            _logger.Debug(
                $"Track unsubscribed (via transport): {track.Name} from participant: {track.ParticipantIdentity}",
                LogCategory.Transport);

            if (track.Kind == TransportTrackKind.Audio)
            {
                if (!string.IsNullOrEmpty(track.TrackSid))
                    _pendingTransportAudioSubscriptions.Remove(track.TrackSid);

                _logger.Debug($"Audio track unsubscribed for participant: {track.ParticipantIdentity}",
                    LogCategory.Audio);
                RemoteTrackSessionNotificationSupport.NotifyAudioTrackUnsubscribed(
                    OnRemoteAudioTrackUnsubscribed,
                    track.ParticipantId,
                    null,
                    _logger,
                    nameof(NativeRoomController));
            }
        }

        private bool TryResolveTransportRemoteAudioTrack(
            string participantIdentity,
            string participantSid,
            string trackSid,
            out IRemoteParticipant participant,
            out IRemoteAudioTrack audioTrack)
        {
            participant = null;
            audioTrack = null;

            IRoomFacade room = CurrentRoom;
            if (room == null) return false;

            bool foundParticipant =
                (!string.IsNullOrEmpty(participantSid) &&
                 room.TryGetParticipantBySid(participantSid, out participant)) ||
                (!string.IsNullOrEmpty(participantIdentity) &&
                 room.TryGetParticipantByIdentity(participantIdentity, out participant));

            if (!foundParticipant || participant == null) return false;

            foreach (IRemoteAudioTrack candidate in participant.AudioTracks)
            {
                if (string.IsNullOrEmpty(trackSid) || string.Equals(candidate.Sid, trackSid, StringComparison.Ordinal))
                {
                    audioTrack = candidate;
                    break;
                }
            }

            return audioTrack != null;
        }

        private void HandleTransportReconnecting()
        {
            _logger.Debug("Room reconnecting (via transport)", LogCategory.Transport);
            OnRoomReconnecting?.Invoke();
        }

        private void HandleTransportReconnected()
        {
            _eventBridge.RefreshRoomFacadeAudioBridge();
            _logger.Debug("Room reconnected (via transport)", LogCategory.Transport);
            OnRoomReconnected?.Invoke();
        }

        #endregion

        #region Event Helpers

        private interface IRemoteAudioControl
        {
            public string PathName { get; }

            public bool Apply(string characterId, string participantIdentity, string participantSid, bool enabled);
        }

        private sealed class RoomFacadeRemoteAudioControl : IRemoteAudioControl
        {
            private readonly NativeRoomController _owner;

            public RoomFacadeRemoteAudioControl(NativeRoomController owner)
            {
                _owner = owner;
            }

            public string PathName => "room-facade";

            public bool Apply(string characterId, string participantIdentity, string participantSid, bool enabled)
            {
                if (!_owner.TryResolveTransportRemoteAudioTrack(participantIdentity, participantSid, null,
                        out IRemoteParticipant participant, out IRemoteAudioTrack audioTrack) ||
                    !(audioTrack is IRemoteAudioControlTrack controllableTrack))
                    return false;

                _owner._logger.Debug(
                    $"{(enabled ? "Enabling" : "Disabling")} remote audio for character: {characterId} via room facade control seam.",
                    LogCategory.Audio);

                controllableTrack.SetRemoteAudioEnabled(enabled);

                string resolvedParticipantSid = !string.IsNullOrEmpty(participant?.Sid)
                    ? participant.Sid
                    : participantSid;
                string resolvedParticipantIdentity = !string.IsNullOrEmpty(participant?.Identity)
                    ? participant.Identity
                    : participantIdentity;

                if (enabled)
                {
                    RemoteTrackSessionNotificationSupport.NotifyAudioTrackSubscribed(
                        _owner.OnRemoteAudioTrackSubscribed,
                        audioTrack,
                        resolvedParticipantSid,
                        characterId,
                        _owner._logger,
                        nameof(NativeRoomController));
                }
                else if (!string.IsNullOrEmpty(resolvedParticipantSid))
                {
                    RemoteTrackSessionNotificationSupport.NotifyAudioTrackUnsubscribed(
                        _owner.OnRemoteAudioTrackUnsubscribed,
                        resolvedParticipantSid,
                        characterId,
                        _owner._logger,
                        nameof(NativeRoomController));
                }

                return true;
            }
        }

        #endregion
    }
}
