using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Audio;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Services;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Persistence;
using Convai.Infrastructure.Protocol.Messages;
using Convai.RestAPI.Services;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Core.DependencyInjection;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Core.Registry;
using Convai.Runtime.Logging;
using Convai.Runtime.Networking.Media;
using Convai.Runtime.NarrativeDesign;
using Convai.Runtime.Persistence;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Context;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Serialization;
using RtviSceneMetadata = Convai.Infrastructure.Protocol.Messages.SceneMetadata;
using ISessionPersistence = Convai.Domain.Abstractions.ISessionPersistence;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Adapters.Networking
{
    internal enum RoomOwnershipRebindOutcome
    {
        DeferredUntilStartup = 0,
        AppliedImmediately,
        PendingReconnect,
        RejectedTransitionState,
        RejectedInvalidOwnership
    }

    /// <summary>
    ///     Unity-facing room and audio component managed by <see cref="ConvaiManager" />.
    ///     Implements the scene-level room connection and room audio service contracts.
    /// </summary>
    public partial class ConvaiRoomManager : MonoBehaviour, IConvaiRoomConnectionService, IConvaiRoomAudioService,
        IConvaiRoomAudioTimelineService, IConvaiRoomAudioMediaTimelineService,
        IConvaiDynamicContextTransport, IInjectable<IConvaiRoomManagerDependencies>
    {
        #region IConvaiRoomAudioService Properties

        /// <inheritdoc />
        public bool IsMicMuted => _roomAudioCoordinator?.IsMicMuted ?? false;

        #endregion

        /// <inheritdoc />
        public bool RequiresUserGestureForAudio
        {
            get
            {
                IRealtimeTransportAccessor accessor = TryGetTransportAccessor();
                if (accessor != null)
                    return accessor.Capabilities.RequiresUserGestureForAudio;

                return _transportProvider?.Capabilities.RequiresUserGestureForAudio ?? false;
            }
        }

        /// <inheritdoc />
        public bool IsAudioPlaybackActive
        {
            get
            {
                IRealtimeTransport transport = TryGetTransport();
                return transport == null || transport.AudioState.IsAudioPlaybackActive;
            }
        }

        /// <inheritdoc />
        public bool CanEnableAudioPlayback
        {
            get
            {
                IRealtimeTransport transport = TryGetTransport();
                return transport == null || transport.CanEnableAudio();
            }
        }

        /// <inheritdoc />
        public void EnableAudioPlayback()
        {
            IRealtimeTransport transport = TryGetTransport();
            transport?.EnableAudio();
        }

        /// <summary>
        ///     Enables audio playback and immediately attempts to start microphone capture.
        ///     Useful for WebGL permission flows driven by a single user gesture.
        /// </summary>
        public void EnableAudioAndStartListening()
        {
            EnableAudioPlayback();
            StartListeningAsync().AsTask().ContinueWith(
                static t => ConvaiLogger.Error(
                    $"StartListeningAsync failed: {t.Exception?.GetBaseException().Message}",
                    LogCategory.SDK),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        ///     Starts microphone capture/publication using the default microphone index.
        ///     This wrapper exists so Unity UI events can trigger mic start without async signatures.
        /// </summary>
        public void StartListening()
        {
            if (RequiresUserGestureForAudio && !IsAudioPlaybackActive)
            {
                EnableAudioAndStartListening();
                return;
            }

            StartListeningAsync().AsTask().ContinueWith(
                static t => ConvaiLogger.Error(
                    $"StartListeningAsync failed: {t.Exception?.GetBaseException().Message}",
                    LogCategory.SDK),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        #region Serialized Fields

        [Header("Configuration")]
        [SerializeField]
        [Tooltip(
            "Choose whether this component uses scene-specific room settings or a reusable Room Manager Profile asset.")]
        private ConvaiConfigSourceMode _configurationSource = ConvaiConfigSourceMode.Inline;

        [SerializeField] [HideInInspector] private bool _configurationSourceInitialized;

        [SerializeField] [HideInInspector] private bool _legacyConversationSettingsMigrated;

        [SerializeField]
        [Tooltip(
            "Optional reusable Room Manager Profile asset. Switch Room Setup Source to Room Manager Profile Asset to make this the active source of truth.")]
        [FormerlySerializedAs("_runtimeProfile")]
        private ConvaiRoomManagerProfile _roomConfigAsset;

        /// <summary>
        ///     The connection type used for character sessions when Room Setup Source is set to Scene Defaults.
        ///     This private field is exposed publicly via the <see cref="ConnectionType" /> property and
        ///     <see cref="EffectiveConnectionType" /> property, which resolves between asset and inline configuration.
        /// </summary>
        [SerializeField] [Tooltip("Connection type used when Room Setup Source is set to Scene Defaults.")]
        private ConvaiConnectionType _connectionType = ConvaiConnectionType.Audio;

        /// <summary>Gets the outgoing video track name used for room sessions in Inline mode.</summary>
        [field: SerializeField]
        [field:
            Tooltip(
                "Optional outgoing video track name used when Room Setup Source is set to Scene Defaults. Leave blank to use publisher defaults.")]
        public string VideoTrackName { get; private set; } = string.Empty;

        /// <summary>Gets the base URL of the Convai core server (without endpoint path).</summary>
        [field: SerializeField]
        [field:
            Tooltip(
                "The base URL of the Convai core server (without endpoint path). Leave blank to use ConvaiSettings.ServerUrl. Example: https://api.convai.com")]
        public string CoreServerBaseURL { get; private set; }

        [field: SerializeField]
        [field:
            Tooltip(
                "Room endpoint used when Room Setup Source is set to Scene Defaults.")]
        public ConvaiServerEndpoint ServerEndpoint { get; private set; } = ConvaiServerEndpoint.Connect;

        /// <summary>Gets a value indicating whether the manager connects automatically on <see cref="Start" />.</summary>
        [field: SerializeField]
        [field:
            Tooltip(
                "Whether the manager connects automatically on Start when Room Setup Source is set to Scene Defaults.")]
        public bool ConnectOnStart { get; private set; } = true;

        [SerializeField]
        [Tooltip(
            "Advanced room-level turn-taking, microphone startup, and initial STT defaults used when Room Setup Source is set to Scene Defaults.")]
        private TurnTakingOptions _turnTakingOptions = TurnTakingOptions.CreateHandsFreeDefault();

        [SerializeField]
        [Tooltip(
            "Advanced backend user voice activity detection defaults used when Room Setup Source is set to Scene Defaults.")]
        private UserVadSettings _userVadSettings = UserVadSettings.CreateDefault();

        [Header("Dynamic Vision Context")]
        [SerializeField]
        [Tooltip("Controls backend dynamic context vision. Auto follows the configured Connection Type (Video enables it); Enabled always uses video; Disabled never sends vision config.")]
        private ConvaiVisionContextMode _visionContextMode = ConvaiVisionContextMode.Auto;

        [SerializeField] [Tooltip("Backend frame sampling settings for dynamic context vision.")]
        private ConvaiVisionInputSettings _visionInputSettings = ConvaiVisionInputSettings.CreateDefault();

        [SerializeField] [Tooltip("Backend response mode policy for dynamic context vision updates and triggers.")]
        private ConvaiVisionRespondModeSettings _visionRespondModes = ConvaiVisionRespondModeSettings.CreateDefault();

        [SerializeField] [Tooltip("Keyboard key used by the built-in push-to-talk flow for this scene.")]
        private KeyCode _pushToTalkKey = KeyCode.T;

        [Header("Inline Reconnect Defaults")]
        [SerializeField]
        [Min(0f)]
        [Tooltip(
            "How long a room can be rejoined before forcing a fresh room when Room Setup Source is set to Scene Defaults.")]
        private float _roomRejoinTtlSeconds = 60f;

        [SerializeField]
        [Tooltip("How session resume behaves on reconnect when Room Setup Source is set to Scene Defaults.")]
        private ResumePolicy _resumePolicy = ResumePolicy.ResumeIfPossible;

        [SerializeField]
        [Min(0)]
        [Tooltip("Maximum reconnect attempts before giving up when Room Setup Source is set to Scene Defaults.")]
        private int _maxReconnectAttempts = 3;

        [SerializeField]
        [Tooltip("Whether the agent should spawn again when rejoining an existing room in Inline mode.")]
        private bool _spawnAgentOnRejoin = true;

        [SerializeField] [Min(0)] [Tooltip("How long ConnectAsync waits for Start() before timing out in Inline mode.")]
        private int _startWaitTimeoutMs = 5000;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Delay before starting the microphone after a successful connection in Inline mode.")]
        private float _autoMicStartDelaySeconds = 0.5f;

        /// <summary>
        ///     When true, the connect request sends debug=true so the backend enables RTVI metrics for this session
        ///     (debug/analytics).
        /// </summary>
        [field: SerializeField]
        [field:
            Tooltip(
                "Enable debug mode: backend will enable RTVI metrics for this session. Use for debugging or analytics; leave off in production.")]
        public bool Debug { get; private set; }

        // These fields must always be serialized to maintain consistent serialization layout across platforms.
        // They are read/written only in ConvaiRoomManager.Editor.cs (editor-only partial).
#pragma warning disable CS0414 // assigned but never read — used in editor partial class
        [SerializeField] [HideInInspector]
        private ConvaiConnectionType _lastValidatedConnectionType = ConvaiConnectionType.Audio;
#pragma warning restore CS0414

        [SerializeField] [HideInInspector] private bool _visionSetupPrompted;

        // _visionSetupQueued is declared in ConvaiRoomManager.Editor.cs (editor-only partial)

        #endregion

        #region Public Properties

        private string ResolvedCoreServerBaseURL
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CoreServerBaseURL))
                    return CoreServerBaseURL;

                return TryGetRuntimeCredentials(out RuntimeCredentials credentials)
                    ? credentials.CoreServerUrl
                    : ConvaiSettings.Instance?.ServerUrl;
            }
        }

        /// <summary>
        ///     Gets the full Core Server URL with the endpoint path appended.
        /// </summary>
        public string CoreServerURL => EffectiveServerEndpoint.BuildUrl(ResolvedCoreServerBaseURL);

        public ConvaiConfigSourceMode ConfigurationSource => ResolveConfigurationSourceMode();

        public ConvaiRoomManagerProfile RoomConfigAsset => _roomConfigAsset;

        /// <summary>
        ///     Gets the connection type for this room after resolving dynamic vision capability:
        ///     the configured value, upgraded to Video only when the vision mode is explicitly Enabled.
        /// </summary>
        public ConvaiConnectionType EffectiveConnectionType => ResolveEffectiveConnectionType();

        /// <summary>Gets the dynamic vision context mode, resolving from asset or inline configuration.</summary>
        public ConvaiVisionContextMode EffectiveVisionContextMode =>
            UsesRoomConfigAsset ? _roomConfigAsset.VisionContextMode : _visionContextMode;

        /// <summary>
        ///     Gets whether this room will send dynamic-vision config on connect: true when the mode is
        ///     Enabled, or Auto with the connection type configured to Video.
        /// </summary>
        public bool EffectiveVisionContextEnabled => ResolveVisionContextEnabled();

        /// <summary>Gets a copy of the vision frame-sampling settings, resolving from asset or inline configuration.</summary>
        public ConvaiVisionInputSettings EffectiveVisionInputSettings =>
            UsesRoomConfigAsset
                ? _roomConfigAsset.VisionInputSettings
                : _visionInputSettings?.Clone() ?? ConvaiVisionInputSettings.CreateDefault();

        /// <summary>Gets a copy of the per-lane respond-mode defaults, resolving from asset or inline configuration.</summary>
        public ConvaiVisionRespondModeSettings EffectiveVisionRespondModes =>
            UsesRoomConfigAsset
                ? _roomConfigAsset.VisionRespondModes
                : _visionRespondModes?.Clone() ?? ConvaiVisionRespondModeSettings.CreateDefault();

        public ConvaiServerEndpoint EffectiveServerEndpoint =>
            UsesRoomConfigAsset ? _roomConfigAsset.ServerEndpoint : ServerEndpoint;

        public bool EffectiveConnectOnStart =>
            UsesRoomConfigAsset ? _roomConfigAsset.ConnectOnStart : ConnectOnStart;

        public bool EffectiveDebug => Debug;

        public KeyCode PushToTalkKey => _pushToTalkKey;

        public TurnTakingOptions EffectiveTurnTakingOptions =>
            UsesRoomConfigAsset
                ? _roomConfigAsset.TurnTakingOptions
                : _turnTakingOptions?.Clone() ?? TurnTakingOptions.CreateHandsFreeDefault();

        public UserVadSettings EffectiveUserVadSettings =>
            UsesRoomConfigAsset
                ? _roomConfigAsset.UserVadSettings
                : _userVadSettings?.Clone() ?? UserVadSettings.CreateDefault();

        public string EffectiveVideoTrackName => ResolveVideoTrackName();

        public string EffectiveReconnectPolicyDescription
        {
            get
            {
                if (!_isInjected)
                    return CreateConfiguredReconnectPolicy().ToString();

                return (_reconnectPolicy ?? ReconnectPolicy.Default).ToString();
            }
        }

        private bool UsesRoomConfigAsset =>
            ResolveConfigurationSourceMode() == ConvaiConfigSourceMode.Asset && _roomConfigAsset != null;

        private ConvaiConfigSourceMode ResolveConfigurationSourceMode() =>
            !_configurationSourceInitialized
                ? _roomConfigAsset != null ? ConvaiConfigSourceMode.Asset : ConvaiConfigSourceMode.Inline
                : _configurationSource;

        private ConvaiConnectionType ConfiguredConnectionType =>
            UsesRoomConfigAsset ? _roomConfigAsset.ConnectionType : _connectionType;

        // Deliberately conservative: only an explicit Enabled changes the configured connection
        // type. Disabled must NOT force Audio — it only stops the dynamic-vision connect config
        // from being sent, so legacy native-video paths (e.g. Gemini Live) keep their video track.
        private ConvaiConnectionType ResolveEffectiveConnectionType() =>
            EffectiveVisionContextMode == ConvaiVisionContextMode.Enabled
                ? ConvaiConnectionType.Video
                : ConfiguredConnectionType;

        private bool ResolveVisionContextEnabled()
        {
            switch (EffectiveVisionContextMode)
            {
                case ConvaiVisionContextMode.Enabled:
                    return true;
                case ConvaiVisionContextMode.Auto:
                    // Auto never upgrades an Audio room: enabling vision changes what the backend
                    // bills per turn, so it must follow an explicit Video configuration (or an
                    // explicit Enabled), never the mere presence of a publisher component.
                    return ConfiguredConnectionType == ConvaiConnectionType.Video;
                default:
                    return false;
            }
        }

        internal RoomVisionInputConfig CreateEffectiveVisionInputConfig() =>
            EffectiveVisionContextEnabled ? EffectiveVisionInputSettings.ToRoomVisionInputConfig() : null;

        // Respond modes are NOT vision-gated: the context_update, trigger, and scene_metadata
        // lanes govern plain dynamic-context features, so the lane policy must reach the backend
        // even when dynamic vision is disabled for the room.
        internal RoomRespondModesConfig CreateEffectiveVisionRespondModes() =>
            EffectiveVisionRespondModes.ToRoomRespondModesConfig();

        private void EnsureConfigurationSourceInitialized()
        {
            if (_configurationSourceInitialized) return;

            _configurationSource =
                _roomConfigAsset != null ? ConvaiConfigSourceMode.Asset : ConvaiConfigSourceMode.Inline;
            _configurationSourceInitialized = true;
        }

        private ReconnectPolicy CreateInlineReconnectPolicy() =>
            new(
                _roomRejoinTtlSeconds,
                _resumePolicy,
                _maxReconnectAttempts,
                _spawnAgentOnRejoin,
                _startWaitTimeoutMs,
                _autoMicStartDelaySeconds);

        private ReconnectPolicy CreateConfiguredReconnectPolicy() =>
            UsesRoomConfigAsset ? _roomConfigAsset.CreateReconnectPolicy() : CreateInlineReconnectPolicy();

        internal void AdoptLegacyManagerConversationSettings(
            ConvaiManagerConversationMode legacyConversationMode,
            KeyCode legacyPushToTalkKey,
            bool interruptBotOnPress,
            bool requireTurnCompletionBeforeNextPress,
            int pushToTalkTurnCompletionTimeoutMs)
        {
            if (_legacyConversationSettingsMigrated)
                return;

            bool hasExplicitRoomConfigAsset = _roomConfigAsset != null;
            bool roomManagerAlreadyOwnsConversationSettings =
                _configurationSourceInitialized && !HasDefaultInlineConversationSetup();

            if (hasExplicitRoomConfigAsset || roomManagerAlreadyOwnsConversationSettings)
            {
                _legacyConversationSettingsMigrated = true;
                return;
            }

            if (legacyPushToTalkKey != KeyCode.T && _pushToTalkKey == KeyCode.T)
                _pushToTalkKey = legacyPushToTalkKey;

            if (legacyConversationMode == ConvaiManagerConversationMode.UseRoomDefaults)
            {
                _legacyConversationSettingsMigrated = true;
                return;
            }

            _configurationSource = ConvaiConfigSourceMode.Inline;
            _configurationSourceInitialized = true;
            _turnTakingOptions = ConvaiManager.CreateConversationModeTurnTakingOverride(
                                     legacyConversationMode,
                                     interruptBotOnPress,
                                     requireTurnCompletionBeforeNextPress,
                                     pushToTalkTurnCompletionTimeoutMs)
                                 ?? _turnTakingOptions?.Clone()
                                 ?? TurnTakingOptions.CreateHandsFreeDefault();
            _legacyConversationSettingsMigrated = true;
        }

        private bool HasDefaultInlineConversationSetup()
        {
            if (_pushToTalkKey != KeyCode.T)
                return false;

            TurnTakingOptions options = _turnTakingOptions ?? TurnTakingOptions.CreateHandsFreeDefault();
            PushToTalkPolicy pushToTalkPolicy = options.PushToTalkPolicy ?? PushToTalkPolicy.CreateDefault();
            LocalAudioPolicy localAudioPolicy = options.LocalAudioPolicy ?? LocalAudioPolicy.CreateDefault();
            SmartTurnSettings smartTurn = options.CustomTurnDetection ?? SmartTurnSettings.CreateDefault();

            return options.Mode == ConversationInputMode.HandsFree &&
                   options.TurnDetection == TurnDetectionMode.UseDefault &&
                   options.InitialServerStt == ServerSttInitialState.UseDefault &&
                   localAudioPolicy.StartMutedInPushToTalk &&
                   !localAudioPolicy.EnableAcousticEchoCancellation &&
                   localAudioPolicy.PushToTalkStartupMode == PushToTalkMicStartupMode.PrewarmMuted &&
                   pushToTalkPolicy.EnableServerSttToggle &&
                   pushToTalkPolicy.InterruptBotOnPress &&
                   pushToTalkPolicy.RequireTurnCompletionBeforeNextPress &&
                   pushToTalkPolicy.TurnCompletionTimeoutMs == PushToTalkPolicy.DefaultTurnCompletionTimeoutMs &&
                   !pushToTalkPolicy.AllowSpeechStoppedFallbackAfterSpeechStart &&
                   Mathf.Approximately(smartTurn.StopSecs, SmartTurnSettings.DefaultStopSecs) &&
                   smartTurn.PreSpeechMs == SmartTurnSettings.DefaultPreSpeechMs &&
                   Mathf.Approximately(smartTurn.MaxDurationSecs, SmartTurnSettings.DefaultMaxDurationSecs);
        }

        private bool TryGetRuntimeCredentials(out RuntimeCredentials credentials)
        {
            credentials = default;
            if (_credentialProvider == null)
                return false;

            string apiKey = _credentialProvider.GetApiKey();
            string serverUrl = _credentialProvider.GetServerUrl();
            if (string.IsNullOrEmpty(apiKey))
                return false;

            credentials = new RuntimeCredentials(apiKey, serverUrl);
            return true;
        }

        /// <summary>Gets the current player agent, when available.</summary>
        public IConvaiPlayerAgent Player { get; private set; }

        /// <summary>Gets the list of discovered scene character agents.</summary>
        public IReadOnlyList<IConvaiCharacterAgent> CharacterList => _characterList;

        public int ConnectAttemptCount { get; private set; }

        public int ReconnectCount { get; private set; }

        public string LastSessionErrorCode => _lastSessionErrorCode ?? string.Empty;
        public string LastSessionErrorMessage => _lastSessionErrorMessage ?? string.Empty;
        public DateTime? LastSuccessfulConnectionUtc { get; private set; }

        public string LastOwnershipRebindOutcome => _lastOwnershipRebindOutcome ?? string.Empty;
        public string CurrentRoomName => _convaiRoomController?.RoomName ?? string.Empty;
        public string CurrentSessionId => _convaiRoomController?.SessionID ?? string.Empty;
        public string CurrentCharacterSessionId => _convaiRoomController?.CharacterSessionID ?? string.Empty;
        public string RoomControllerTypeName => _convaiRoomController?.GetType().Name ?? string.Empty;
        public string TransportAccessorTypeName => TryGetTransportAccessor()?.GetType().Name ?? string.Empty;

        private IConvaiCharacterAgent _activeCharacter;
        private List<IConvaiCharacterAgent> _characterList;

        #endregion

        #region IConvaiRoomConnectionService Events

        /// <inheritdoc />
        public event Action Connected;

        /// <inheritdoc />
        public event Action<SessionError> OnSessionError;

        /// <inheritdoc />
        public event Action<SessionStateChanged> OnSessionStateChanged;

        /// <inheritdoc />
        public event Action<ConversationInputMode> ConversationInputModeChanged;

        /// <summary>
        ///     Raised when an RTVI metrics message is received (only when Debug is enabled on this manager).
        ///     Use for troubleshooting latency (ttfb, processing) or custom metrics (e.g. NeuroSync).
        /// </summary>
        public event Action<RTVIMetricsPayload> OnRtvMetricsReceived;

        #endregion

        #region IConvaiRoomAudioService Events

        /// <inheritdoc />
        public event Action<bool> MicMuteChanged;

        /// <inheritdoc />
        public event Action<string, bool> RemoteAudioEnabledChanged;

        #endregion

        #region IConvaiRoomConnectionService Properties

        /// <inheritdoc />
        public ConvaiConnectionType ConnectionType => EffectiveConnectionType;

        /// <inheritdoc />
        public SessionState CurrentState => _sessionStateMachine?.CurrentState ?? SessionState.Disconnected;

        /// <inheritdoc />
        public bool IsConnected => _convaiRoomController?.IsConnectedToRoom ?? false;

        /// <inheritdoc />
        public bool HasRoomDetails => _convaiRoomController?.HasRoomDetails ?? false;

        /// <inheritdoc />
        public bool HasPendingOwnershipReconnect { get; private set; }

        /// <inheritdoc />
        public ConversationInputMode ActiveConversationInputMode =>
            _hasConnectedSessionTurnTakingState
                ? CurrentResolvedTurnTakingOptions.Mode
                : EffectiveTurnTakingOptions.Mode;

        /// <inheritdoc />
        /// <remarks>
        ///     Returns the platform-agnostic room facade. On native platforms, this wraps LiveKit.Room.
        ///     On WebGL, this wraps the browser-based room implementation.
        ///     Use this property instead of accessing LiveKit.Room directly for cross-platform compatibility.
        /// </remarks>
        public IRoomFacade CurrentRoom
        {
            get
            {
                if (_convaiRoomController?.CurrentRoom != null) return _convaiRoomController.CurrentRoom;
                return null;
            }
        }

        /// <inheritdoc />
        public RTVIHandler RtvHandler => _convaiRoomController?.RTVIHandler;

        /// <inheritdoc />
        public bool SendNarrativeTrigger(ConvaiNarrativeTriggerRequest request)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVITriggerMessage(request));
            return true;
        }

        /// <inheritdoc />
        public bool UpdateSceneMetadata(IReadOnlyList<RtviSceneMetadata> sceneMetadata)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null || sceneMetadata == null) return false;

            List<RtviSceneMetadata> payload =
                sceneMetadata as List<RtviSceneMetadata> ?? new List<RtviSceneMetadata>(sceneMetadata);
            handler.SendData(new RTVIUpdateSceneMetadata(payload));
            return true;
        }

        bool IConvaiDynamicContextTransport.SendDynamicContext(ConvaiDynamicContextUpdate update)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null || update == null) return false;

            handler.SendData(new RTVIUpdateDynamicContext(update));
            return true;
        }

        /// <inheritdoc />
        public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIUpdateTemplateKeys(templateKeys));
            return true;
        }

        /// <inheritdoc />
        public bool SetTtsEnabled(bool ttsEnabled)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVITtsToggle(ttsEnabled));
            return true;
        }

        /// <inheritdoc />
        public bool SetSttMuted(bool muted)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVISttToggle(muted));
            return true;
        }

        /// <inheritdoc />
        public bool InterruptBot()
            => SendInterruption(BargeInTrigger.Manual, requireActivePlayback: false);

        private bool SendInterruption(BargeInTrigger trigger, bool requireActivePlayback)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;
            if (requireActivePlayback && !(_bargeInCoordinator?.HasActivePlayback ?? false))
                return false;

            _clientLatencyMetricsCollector?.RecordBargeInMarker(
                BargeInMarker.Create(BargeInMarkerStage.InterruptRequested, trigger));
            _bargeInCoordinator?.Commit(trigger);
            handler.SendData(new RTVIInterruptBot());
            _clientLatencyMetricsCollector?.RecordBargeInMarker(
                BargeInMarker.Create(BargeInMarkerStage.InterruptSent, trigger));
            return true;
        }

        /// <inheritdoc />
        public bool KillPipeline()
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIKillPipeline());
            return true;
        }

        /// <inheritdoc />
        public bool ForceUserStoppedSpeaking()
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIForceUserStoppedSpeaking());
            return true;
        }

        /// <inheritdoc />
        public bool ResetIdleTimer()
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIResetIdleTimer());
            return true;
        }

        /// <inheritdoc />
        public bool RequestVisionStatus(string updateId = null)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIVisionStatus(updateId));
            return true;
        }

        /// <inheritdoc />
        public bool TriggerVision(ConvaiVisionTriggerRequest request)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIVisionTrigger(request));
            return true;
        }

        /// <inheritdoc />
        public bool UpdateRespondMode(ConvaiRespondModeLane lane, ConvaiRespondMode mode, string updateId = null)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIRespondModeUpdate(lane, mode, updateId));
            return true;
        }

        #endregion

        #region Private Fields

        private RemoteAudioPreferenceManager _remoteAudioPreferences;

        private AudioTrackManager _audioTrackManager;

        private IConvaiRoomController _convaiRoomController;
        private ILogger _logger;
        private IConvaiRoomControllerFactory _controllerFactory;
        private IMicrophoneSourceFactory _microphoneSourceFactory;
        private ITransportProvider _transportProvider;

        private IRealtimeTransportAccessor TryGetTransportAccessor() => null;

        private IRealtimeTransport TryGetTransport()
        {
            IRealtimeTransportAccessor accessor = TryGetTransportAccessor();
            return accessor?.Transport;
        }

        private PlayerSessionAdapter _playerSession;
        private ICredentialProvider _credentialProvider;
        private IEndUserIdentityProvider _endUserIdentityProvider;
        private IEndUserMetadataProvider _endUserMetadataProvider;
        private readonly MutableEndUserContextProvider _mutableEndUserContextProvider = new();

        private IEventHub _eventHub;
        private IRoomOwnershipProvider _roomOwnershipProvider;
        private SubscriptionToken _characterReadyToken;
        private SubscriptionToken _characterSpeechStateToken;
        private SubscriptionToken _characterTtsTextToken;
        private SubscriptionToken _lipSyncPackedDataToken;
        private SubscriptionToken _clientVoiceActivityStateToken;
        private SubscriptionToken _playerSpeakingStateToken;
        private SubscriptionToken _sessionStateToken;
        private ISessionPersistence _sessionPersistence;

        private ConnectionContext _connectionContext = ConnectionContext.Empty;
        private string _pendingOwnershipRequestedCharacterId;
        private ReconnectPolicy _reconnectPolicy = ReconnectPolicy.Default;
        private ISessionStateMachine _sessionStateMachine;
        private ISessionService _sessionService;
        private RoomSessionDiagnostics _sessionDiagnostics;
        private RoomCharacterLifecycleCoordinator _characterLifecycleCoordinator;
        private RoomControllerEventBinder _roomControllerEventBinder;
        private RTVIHandler _metricsRtviHandler;
        private ClientLatencyMetricsCollector _clientLatencyMetricsCollector;
        private BargeInCoordinator _bargeInCoordinator;
        private IDebugMetricsFileWriter _debugMetricsFileWriter;
        private INarrativeSectionNameResolver _sectionNameResolver;
        private IConvaiRuntimeSettingsService _runtimeSettingsService;
        private IMicrophoneDeviceService _microphoneDeviceService;
        private ConvaiRuntimeDiagnosticsService _runtimeDiagnosticsService;
        private string _lastSessionErrorCode;
        private string _lastSessionErrorMessage;
        private string _lastOwnershipRebindOutcome;

        private bool _isInjected;

        private static ConvaiRoomManager _instance;

        private RoomAudioCoordinator _roomAudioCoordinator;
        private RoomDisconnectRuntimeAdapter _roomDisconnectRuntimeAdapter;
        private RoomConnectionRuntimeAdapter _roomConnectionRuntimeAdapter;
        private RoomAudioRuntimeAdapter _roomAudioRuntimeAdapter;
        private RoomRuntimeAssembly _roomRuntimeAssembly;
        private readonly RoomRuntimeAssemblyFactory _roomRuntimeAssemblyFactory = new();
        private readonly RoomCompositionService _roomCompositionService = new();
        private readonly object _connectOptionsLock = new();
        private readonly SemaphoreSlim _conversationInputModeTransitionGate = new(1, 1);
        private Coroutine _autoStartMicrophoneCoroutine;
        private bool _autoStartMicrophoneCompleted;
        private bool _autoStartMicrophonePending;
        private int _autoStartMicrophoneRequestId;
        private RoomSessionConnectOptions _pendingConnectOptions;
        private TurnTakingOptions _sessionTurnTakingSourceOptions;
        private bool _hasConnectedSessionTurnTakingState;
        private bool _conversationInputModeTransitionInProgress;

        private ResolvedTurnTakingOptions
            _currentResolvedTurnTakingOptions = ResolvedTurnTakingOptions.DefaultHandsFree;

        /// <summary>
        ///     The room runtime instance created by the runtime assembly factory.
        /// </summary>
        /// <remarks>
        ///     RC-2: Stored for access by <see cref="DeferredRoomRuntime" /> via <see cref="GetRoomRuntime" />.
        /// </remarks>
        private RoomRuntime _roomRuntime;

        #endregion

        #region Coordinators

        public IRoomDiagnostics DiagnosticsCoordinator { get; private set; }

        public IRoomAudioCoordinator AudioCoordinator => _roomAudioCoordinator;

        public IRoomOwnershipCoordinator OwnershipCoordinator { get; private set; }

        public IRoomConnectionCoordinator ConnectionCoordinator { get; private set; }

        public IAgentRegistry AgentRegistry { get; private set; }

        #endregion

        #region Public Methods

        /// <remarks>
        ///     Typed injection entrypoint used by the runtime host.
        /// </remarks>
        void IInjectable<IConvaiRoomManagerDependencies>.
            InjectDependencies(IConvaiRoomManagerDependencies dependencies) =>
            InjectDependencies(dependencies);

        internal void InjectDependencies(IConvaiRoomManagerDependencies dependencies)
        {
            if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));

            _transportProvider = dependencies.TransportProvider;
            AgentRegistry = dependencies.AgentRegistry;
            _eventHub = dependencies.EventHub;
            _logger = (dependencies.Logger ?? new ConvaiLogger()).WithTag(nameof(ConvaiRoomManager));
            _runtimeDiagnosticsService = dependencies.RuntimeDiagnosticsService;
            _runtimeSettingsService = dependencies.RuntimeSettingsService;
            _microphoneDeviceService = dependencies.MicrophoneDeviceService;
            _roomOwnershipProvider = dependencies.RoomOwnershipProvider;
            _sessionPersistence =
                dependencies.SessionPersistence ?? new KeyValueStoreSessionPersistence(new PlayerPrefsKeyValueStore());
            _credentialProvider = dependencies.CredentialProvider ?? CredentialProviderFactory.Create();
            UpdateEndUserContextProviders(
                dependencies.EndUserIdentityProvider,
                dependencies.EndUserMetadataProvider);
            _sectionNameResolver = dependencies.SectionNameResolver;
            _sessionService = dependencies.SessionService;

            if (_transportProvider != null)
            {
                _controllerFactory = _transportProvider.CreateRoomControllerFactory();
                _microphoneSourceFactory = _transportProvider.CreateMicrophoneFactory();
            }

            _logger ??= new ConvaiLogger();

            if (_sessionStateToken != default)
                _eventHub.Unsubscribe(_sessionStateToken);

            _sessionStateMachine = dependencies.SessionStateMachine ?? new SessionStateMachine(_eventHub, _logger);
            _sessionStateToken = _eventHub.Subscribe<SessionStateChanged>(OnSessionStateMachineStateChanged);
            _reconnectPolicy = ResolveConfiguredReconnectPolicy();
            _connectionContext = ConnectionContext.Empty;
            ConnectAttemptCount = 0;
            ReconnectCount = 0;
            _lastSessionErrorCode = null;
            _lastSessionErrorMessage = null;
            _lastOwnershipRebindOutcome = null;
            LastSuccessfulConnectionUtc = null;

            _roomRuntimeAssembly = _roomRuntimeAssemblyFactory.Create(CreateRoomManagerRuntimeContext());
            ApplyRuntimeAssembly(_roomRuntimeAssembly);
            _runtimeDiagnosticsService?.AttachRoomManager(this);

            AttachRuntimeEventBridges();
            _isInjected = true;
            ConvaiLogger.Debug("Dependencies injected via typed bundle", LogCategory.SDK);
        }

        internal void UpdateEndUserContextProviders(
            IEndUserIdentityProvider identityProvider,
            IEndUserMetadataProvider metadataProvider)
        {
            _mutableEndUserContextProvider.SetProviders(identityProvider, metadataProvider);
            _endUserIdentityProvider = _mutableEndUserContextProvider;
            _endUserMetadataProvider = _mutableEndUserContextProvider;
        }

        /// <summary>
        ///     Configures the reconnect policy. Call before any connection attempts.
        /// </summary>
        public void SetReconnectPolicy(ReconnectPolicy policy)
        {
            _reconnectPolicy = policy ?? ReconnectPolicy.Default;
            ConvaiLogger.Debug($"Reconnect policy set: {policy}", LogCategory.SDK);
        }

        /// <summary>
        ///     Gets the room runtime instance created by the runtime assembly factory.
        /// </summary>
        /// <returns>The room runtime, or null if not yet initialized.</returns>
        /// <remarks>
        ///     RC-2: Used by <see cref="DeferredRoomRuntime" /> to delegate room operations.
        /// </remarks>
        internal IRoomRuntime GetRoomRuntime() => _roomRuntime;

        private RoomManagerRuntimeContext CreateRoomManagerRuntimeContext()
        {
            AgentRegistry ??= new AgentRegistry();

            return new RoomManagerRuntimeContext
            {
                AgentRegistry = AgentRegistry,
                Logger = _logger,
                EventHub = _eventHub,
                SessionStateMachine = _sessionStateMachine,
                SessionService = _sessionService,
                SessionIdProvider = () => _convaiRoomController?.SessionID,
                CurrentStateProvider = () => CurrentState,
                IsConnectedProvider = () => IsConnected,
                CanConnectProvider = () => isActiveAndEnabled,
                StartWaitTimeoutProvider = () =>
                    Math.Max(500,
                        (_reconnectPolicy?.StartWaitTimeoutMs ?? ReconnectPolicy.Default.StartWaitTimeoutMs) + 500),
                PrepareConnection = PrepareOwnershipCompositionForNextConnect,
                ActiveCharacterProvider = () => _activeCharacter,
                ConnectionContextProvider = () => _connectionContext,
                SetConnectionContext = context => _connectionContext = context,
                ResolveReconnectPolicy = ResolveConfiguredReconnectPolicy,
                SetReconnectPolicy = policy => _reconnectPolicy = policy ?? ReconnectPolicy.Default,
                OnConnectAttemptStarted = hasValidRoom =>
                {
                    _bargeInCoordinator?.ResetForConnectionBoundary();
                    ConnectAttemptCount++;
                    ReconnectCount = hasValidRoom ? ReconnectCount + 1 : 0;
                },
                ControllerProvider = () => _convaiRoomController,
                ConnectionTypeProvider = () => EffectiveConnectionType,
                CredentialProvider = () => _credentialProvider,
                CoreServerUrlProvider = () => CoreServerURL,
                ConfiguredTurnTakingOptionsProvider = () => EffectiveTurnTakingOptions,
                ConfiguredUserVadSettingsProvider = () => EffectiveUserVadSettings,
                VisionInputConfigProvider = CreateEffectiveVisionInputConfig,
                RespondModesProvider = CreateEffectiveVisionRespondModes,
                ConsumePendingConnectOptions = ConsumePendingConnectOptions,
                PrepareSessionTurnTakingState = PrepareSessionTurnTakingState,
                SetCurrentResolvedTurnTakingOptions = SetCurrentResolvedTurnTakingOptions,
                CurrentResolvedTurnTakingOptionsProvider = () => CurrentResolvedTurnTakingOptions,
                SessionPersistenceProvider = () => _sessionPersistence,
                AudioTrackManagerProvider = () => _audioTrackManager,
                UpdateSessionState = UpdateSessionState,
                RecordConnectionSuccess = RecordConnectionSuccess,
                RecordConnectionFailure = RecordConnectionFailure,
                CompleteDisconnectionTracking = CompleteDisconnectionTracking,
                ResetBargeInCoordinatorState =
                    () => _bargeInCoordinator?.ResetForConnectionBoundary(),
                RequiresUserGestureForAudioProvider = () => RequiresUserGestureForAudio,
                IsAudioPlaybackActiveProvider = () => IsAudioPlaybackActive,
                MicrophoneSourceFactoryProvider = () => _microphoneSourceFactory,
                SetMicrophoneSourceFactory = factory => _microphoneSourceFactory = factory,
                TransportProviderProvider = () => _transportProvider,
                PreferredMicrophoneDeviceIdProvider = () => _runtimeSettingsService?.Current.PreferredMicrophoneDeviceId,
                MicrophoneDeviceServiceProvider = () => _microphoneDeviceService,
                GameObjectProvider = () => gameObject,
                CharacterListProvider = () => CharacterList,
                HandleMicMuteChanged = HandleMicMuteChanged,
                HandleUnexpectedRoomDisconnected = HandleUnexpectedRoomDisconnected,
                HandleRemoteAudioTrackSubscribed = HandleRemoteAudioTrackSubscribed,
                HandleRemoteAudioTrackUnsubscribed = HandleRemoteAudioTrackUnsubscribed
            };
        }

        private void ApplyRuntimeAssembly(RoomRuntimeAssembly assembly)
        {
            _roomRuntimeAssembly = assembly;
            _sessionDiagnostics = assembly?.SessionDiagnostics;
            _characterLifecycleCoordinator = assembly?.CharacterLifecycleCoordinator;
            _roomDisconnectRuntimeAdapter = assembly?.DisconnectRuntimeAdapter;
            _roomControllerEventBinder = assembly?.RoomControllerEventBinder;
            _remoteAudioPreferences = assembly?.RemoteAudioPreferences;
            _roomConnectionRuntimeAdapter = assembly?.ConnectionRuntimeAdapter;
            _roomAudioRuntimeAdapter = assembly?.AudioRuntimeAdapter;
            _roomRuntime = assembly?.RoomRuntime;
            DiagnosticsCoordinator = assembly?.DiagnosticsCoordinator;
            ConnectionCoordinator = assembly?.ConnectionCoordinator;
            _roomAudioCoordinator = assembly?.AudioCoordinator;
            OwnershipCoordinator = assembly?.OwnershipCoordinator;
        }

        private void AttachRuntimeEventBridges()
        {
            if (_remoteAudioPreferences != null)
                _remoteAudioPreferences.RemoteAudioEnabledChanged += OnRemoteAudioPreferenceChanged;

            if (ConnectionCoordinator != null)
                ConnectionCoordinator.StateChanged += HandleCoordinatorStateChanged;

            if (_characterReadyToken == default && _eventHub != null)
                _characterReadyToken = _eventHub.Subscribe<CharacterReady>(HandleCharacterReadyEvent);

            if (_characterSpeechStateToken == default && _eventHub != null)
            {
                _characterSpeechStateToken =
                    _eventHub.Subscribe<CharacterSpeechStateChanged>(HandleCharacterSpeechEvidence);
            }

            if (_characterTtsTextToken == default && _eventHub != null)
                _characterTtsTextToken = _eventHub.Subscribe<CharacterTtsTextChunk>(HandleCharacterTtsEvidence);

            if (_lipSyncPackedDataToken == default && _eventHub != null)
                _lipSyncPackedDataToken = _eventHub.Subscribe<LipSyncPackedDataReceived>(HandleLipSyncEvidence);

            if (_playerSpeakingStateToken == default && _eventHub != null)
            {
                _playerSpeakingStateToken =
                    _eventHub.Subscribe<PlayerSpeakingStateChanged>(HandlePlayerSpeakingEvidence);
            }

            if (_clientVoiceActivityStateToken == default && _eventHub != null)
            {
                _clientVoiceActivityStateToken =
                    _eventHub.Subscribe<ClientVoiceActivityStateChanged>(HandleClientVoiceActivity);
            }
        }

        private void DetachRuntimeEventBridges()
        {
            if (_remoteAudioPreferences != null)
                _remoteAudioPreferences.RemoteAudioEnabledChanged -= OnRemoteAudioPreferenceChanged;

            if (ConnectionCoordinator != null)
                ConnectionCoordinator.StateChanged -= HandleCoordinatorStateChanged;

            if (_characterReadyToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_characterReadyToken);
                _characterReadyToken = default;
            }

            if (_characterSpeechStateToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_characterSpeechStateToken);
                _characterSpeechStateToken = default;
            }

            if (_characterTtsTextToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_characterTtsTextToken);
                _characterTtsTextToken = default;
            }

            if (_lipSyncPackedDataToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_lipSyncPackedDataToken);
                _lipSyncPackedDataToken = default;
            }

            if (_playerSpeakingStateToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_playerSpeakingStateToken);
                _playerSpeakingStateToken = default;
            }

            if (_clientVoiceActivityStateToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_clientVoiceActivityStateToken);
                _clientVoiceActivityStateToken = default;
            }
        }

        #endregion
    }
}
