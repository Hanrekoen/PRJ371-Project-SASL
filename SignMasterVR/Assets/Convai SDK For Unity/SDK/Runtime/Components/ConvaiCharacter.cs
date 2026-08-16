using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Emotion;
using Convai.Domain.EventSystem;
using Convai.Runtime.Emotion;
using Convai.Domain.Logging;
using Convai.Runtime.Actions;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Core.DependencyInjection;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Shared.Actions;
using Convai.Runtime.Utilities;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Serialization;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Unity-facing character component for conversation control, transcript callbacks, and character-scoped runtime
    ///     state.
    /// </summary>
    /// <remarks>
    ///     Add this to each NPC or agent you want to connect to Convai.
    ///     The component is configured either from inline values or from a Character Profile asset, depending on
    ///     <see cref="_configurationSource" />.
    /// </remarks>
    [AddComponentMenu("Convai/Convai Character")]
    public partial class ConvaiCharacter : MonoBehaviour, IConvaiCharacterAgent, IConvaiActionRuntimeSource,
        IConvaiActionDefinitionCatalogSource,
        IConvaiActionResolutionSource,
        IInjectable<IConvaiCharacterDependencies>, ICharacterIdentitySource
    {
        #region Dependency Injection

        /// <inheritdoc cref="IInjectable{TDependencies}.InjectDependencies" />
        public void InjectDependencies(IConvaiCharacterDependencies dependencies)
        {
            _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

            IsInjected = true;
            Logger?.Debug($"[{_characterName}] Dependencies injected (typed bundle)");

            PropagateCharacterDependencyConsumers();

            if (enabled && isActiveAndEnabled) InitializeAfterInjection();
        }

        private void PropagateCharacterDependencyConsumers()
        {
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            var consumers = new List<IInjectable<IConvaiCharacterDependencies>>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || ReferenceEquals(behaviour, this)) continue;
                if (behaviour.GetComponentInParent<ConvaiCharacter>(true) != this) continue;
                if (behaviour is IInjectable<IConvaiCharacterDependencies> consumer)
                    consumers.Add(consumer);
            }

            consumers.Sort((left, right) =>
            {
                int order = left.InjectionOrder.CompareTo(right.InjectionOrder);
                return order != 0
                    ? order
                    : string.Compare(left.GetType().FullName, right.GetType().FullName,
                        StringComparison.Ordinal);
            });

            for (int i = 0; i < consumers.Count; i++)
                consumers[i].InjectDependencies(_deps);
        }

        #endregion

        #region Serialized Fields

        [Header("Character Configuration")]
        [SerializeField]
        [Tooltip(
            "Choose whether this component uses inline character settings or a reusable Character Profile asset.")]
        private ConvaiConfigSourceMode _configurationSource = ConvaiConfigSourceMode.Inline;

        [SerializeField] [HideInInspector] private bool _configurationSourceInitialized;

        [SerializeField]
        [Tooltip(
            "Optional reusable Character Profile asset. Switch Character Setup Source to Character Profile Asset to make this the active source of truth.")]
        [FormerlySerializedAs("_agentDefinition")]
        private ConvaiCharacterProfile _characterConfigAsset;

        [SerializeField]
        [Tooltip("The unique Character ID from your Convai dashboard (https://convai.com). " +
                 "This is required for connecting to the character.")]
        private string _characterId;

        [SerializeField] [Tooltip("Display name used in transcripts and debug logs.")]
        private string _characterName;

        [SerializeField] private Color _nameTagColor = Color.white;

        [Header("Behavior")]
#pragma warning disable CS0649
        [SerializeField]
        [Tooltip("If enabled, the character will automatically start a conversation after initialization. " +
                 "The connection waits for ConvaiRoomManager.Start() to complete before connecting.")]
        private bool _autoConnect;
#pragma warning restore CS0649

        [Header("Remote Audio (Default ON)")]
        [SerializeField]
        [Tooltip("Whether this character should start with remote audio playback enabled. Disable for text-only mode.")]
        private bool _enableRemoteAudio = true;

        [SerializeField] [Tooltip("If enabled, this character attempts to resume previous sessions when reconnecting.")]
        private bool _enableSessionResume = false;

        [SerializeField]
        [Tooltip("Character session ID used when Session Resume is enabled. Leave empty to start a fresh session.")]
        private string _characterSessionId = string.Empty;

        [Header("Ownership")]
        [SerializeField]
        [Tooltip("Optional owner id for multi-character scenarios. Leave empty to use the local player.")]
        private string _ownerId = "";

        [Header("Dynamic Info (Connection Request)")]
        [SerializeField]
        [Tooltip("Initial dynamic info text sent in the room connection request.")]
        private string _initialDynamicInfoText = string.Empty;

        [SerializeField] [Tooltip("When true, dynamic info is kept in context by the server.")]
        private bool _initialDynamicInfoKeepInContext;

        [Header("Connection Settings")]
        [SerializeField]
        [Tooltip("How long to wait for the character ready signal after connection. Set to 0 to wait indefinitely.")]
        [Range(0, 120)]
        private float _characterReadyTimeoutSeconds = 30f;

        #endregion

        #region Dependencies

        /// <summary>
        ///     Typed dependency bundle owned by the manager/runtime binding path.
        /// </summary>
        private IConvaiCharacterDependencies _deps;

        private IEventHub EventHub => _deps?.EventHub;
        private IConvaiRoomConnectionService ConnectionService => _deps?.ConnectionService;
        private IConvaiRoomAudioService AudioService => _deps?.AudioService;
        private IAgentRegistry AgentRegistry => _deps?.AgentRegistry;
        private ILogger Logger => _deps?.Logger;

        private SubscriptionToken _ttsTextToken;
        private SubscriptionToken _speechStateToken;
        private SubscriptionToken _characterReadyToken;
        private SubscriptionToken _turnCompletedToken;
        private SubscriptionToken _emotionToken;
        private SubscriptionToken _actionReceivedToken;
        private SubscriptionToken _dynamicContextUpdateResultToken;
        private SubscriptionToken _narrativeSectionChangedToken;

        private volatile bool _isSpeaking;
        private readonly object _emotionStateLock = new();
        private string _currentEmotion;
        private int _currentEmotionIntensity;

        private Task _toggleTask;
        private readonly object _toggleLock = new();

        private bool _isCharacterReady;
        private readonly object _characterReadyLock = new();
        private ConvaiActionConfig _resolvedSessionActionConfig;
        private bool _hasResolvedSessionActionConfig;
        private ConvaiActionConfig _sessionLocalResolutionConfig;
        private bool _hasSessionLocalResolutionConfig;
        private List<ConvaiActionDefinition> _resolvedSessionActionDefinitions;
        private List<ConvaiActionDefinition> _resolvedSessionActionDefinitionCatalog;

        private CancellationTokenSource _destroyCts;

        private TaskCompletionSource<bool> _characterReadyTcs;
        private readonly object _characterReadyTcsLock = new();
        private bool _isInitializedAfterInjection;

        #endregion

        #region Public Properties

        /// <summary>Character ID configured in the Convai dashboard.</summary>
        public ConvaiConfigSourceMode ConfigurationSource => ResolveConfigurationSourceMode();

        public ConvaiCharacterProfile CharacterConfigAsset => _characterConfigAsset;

        public string CharacterId =>
            UsesCharacterConfigAsset && !string.IsNullOrWhiteSpace(_characterConfigAsset.CharacterId)
                ? _characterConfigAsset.CharacterId
                : _characterId;

        /// <summary>Display name for the character.</summary>
        public string CharacterName =>
            UsesCharacterConfigAsset && !string.IsNullOrWhiteSpace(_characterConfigAsset.CharacterName)
                ? _characterConfigAsset.CharacterName
                : _characterName;

        /// <summary>Name tag color for transcript display.</summary>
        public Color NameTagColor => UsesCharacterConfigAsset ? _characterConfigAsset.NameTagColor : _nameTagColor;

        /// <summary>
        ///     Owner ID for multi-character support.
        ///     Empty string indicates the local player owns this character.
        /// </summary>
        public string OwnerId => _ownerId ?? string.Empty;

        /// <summary>
        ///     Current session state from the connection service.
        /// </summary>
        public SessionState SessionState => ConnectionService?.CurrentState ?? SessionState.Disconnected;

        /// <summary>
        ///     Whether the character has received the bot-ready signal from the server.
        ///     This is set only by the CharacterReady domain event, not by room connection.
        /// </summary>
        public bool IsCharacterReady
        {
            get
            {
                lock (_characterReadyLock) return _isCharacterReady;
            }
            private set
            {
                bool changed;
                lock (_characterReadyLock)
                {
                    changed = _isCharacterReady != value;
                    _isCharacterReady = value;
                }

                if (changed)
                {
                    if (value)
                    {
                        lock (_characterReadyTcsLock) _characterReadyTcs?.TrySetResult(true);
                        SafeEventInvoker.Invoke(
                            OnCharacterReady,
                            Logger,
                            "ConvaiCharacter.OnCharacterReady",
                            LogCategory.Character);
                    }
                }
            }
        }

        /// <summary>
        ///     Whether the character is connected to the room. Does not imply character-ready state.
        /// </summary>
        public bool IsSessionConnected => SessionState == SessionState.Connected;

        /// <summary>Whether the character is in an active conversation (connected and character ready).</summary>
        public bool IsInConversation => IsSessionConnected && IsCharacterReady;

        /// <summary>Whether dependencies have been injected.</summary>
        public bool IsInjected { get; private set; }

        /// <summary>
        ///     Inspector-configured remote audio preference for this character.
        ///     This is applied after injection; default is true (audio enabled).
        /// </summary>
        public bool EnableRemoteAudioOnStart =>
            UsesCharacterConfigAsset ? _characterConfigAsset.EnableRemoteAudioOnStart : _enableRemoteAudio;

        /// <summary>
        ///     Whether session resume should be attempted for this character.
        /// </summary>
        public bool EnableSessionResume =>
            UsesCharacterConfigAsset ? _characterConfigAsset.EnableSessionResume : _enableSessionResume;

        /// <summary>
        ///     Character session ID used for the next room connection when session resume is enabled.
        ///     Successful connections write the server-returned character_session_id back to this value.
        /// </summary>
        public string CharacterSessionId => NormalizeCharacterSessionId(_characterSessionId);

        /// <summary>
        ///     Initial dynamic info text included in room connection request payload.
        /// </summary>
        public string InitialDynamicInfoText => _initialDynamicInfoText;

        /// <summary>
        ///     Whether initial dynamic info should be kept in context by the server.
        /// </summary>
        public bool InitialDynamicInfoKeepInContext => _initialDynamicInfoKeepInContext;

        /// <summary>
        ///     Timeout in seconds to wait for the character ready signal after connection.
        ///     Set to 0 to disable timeout (wait indefinitely). Default: 30 seconds.
        /// </summary>
        public float CharacterReadyTimeoutSeconds
        {
            get => _characterReadyTimeoutSeconds;
            set => _characterReadyTimeoutSeconds = Mathf.Max(0f, value);
        }

        /// <summary>Whether the character is currently speaking.</summary>
        public bool IsSpeaking => _isSpeaking;

        /// <summary>The most recently received character emotion label.</summary>
        public string CurrentEmotion
        {
            get
            {
                lock (_emotionStateLock) return _currentEmotion;
            }
        }

        /// <summary>The most recently received character emotion intensity (1-3).</summary>
        public int CurrentEmotionIntensity
        {
            get
            {
                lock (_emotionStateLock) return _currentEmotionIntensity;
            }
        }

        #endregion

        /// <summary>
        ///     Sets the character_session_id used by the next connection when session resume is enabled.
        ///     Passing null, empty, or whitespace clears the value so the next connection starts fresh.
        /// </summary>
        public void SetCharacterSessionId(string characterSessionId) =>
            _characterSessionId = NormalizeCharacterSessionId(characterSessionId);

        /// <summary>Clears the configured character_session_id so the next connection starts fresh.</summary>
        public void ClearCharacterSessionId() => _characterSessionId = string.Empty;

        internal void SetCurrentCharacterSessionId(string characterSessionId) =>
            SetCharacterSessionId(characterSessionId);

        private static string NormalizeCharacterSessionId(string characterSessionId) =>
            string.IsNullOrWhiteSpace(characterSessionId) ? string.Empty : characterSessionId.Trim();

        #region Events

        /// <summary>Raised when transcript text is received from this character's local TTS stream.</summary>
        public event Action<string, bool> OnTranscriptReceived;

        /// <summary>Raised when this character begins speaking.</summary>
        public event Action OnSpeechStarted;

        /// <summary>Raised when this character stops speaking.</summary>
        public event Action OnSpeechStopped;

        /// <summary>Raised when this character completes its full turn.</summary>
        public event Action<bool> OnTurnCompleted;

        /// <summary>Raised when this character is ready to interact.</summary>
        public event Action OnCharacterReady;

        /// <summary>Raised when the session state changes.</summary>
        public event Action<SessionState> OnSessionStateChanged;

        /// <summary>Raised when the remote audio enabled state changes for this character.</summary>
        public event Action<bool> OnRemoteAudioEnabledChanged;

        /// <summary>Raised when this character's emotion changes. Parameters: (emotion, intensity).</summary>
        public event Action<string, int> OnEmotionChanged;

        /// <summary>Raised when this character receives backend structured actions for the current turn.</summary>
        public event Action<IReadOnlyList<ConvaiActionCommand>> OnActionsReceived;

        #endregion

        #region IConvaiCharacterAgent Implementation

        string IConvaiCharacterAgent.CharacterId => CharacterId;
        string IConvaiCharacterAgent.CharacterName => CharacterName;
        bool IConvaiCharacterAgent.EnableSessionResume => EnableSessionResume;
        string IConvaiCharacterAgent.InitialDynamicInfoText => InitialDynamicInfoText;
        bool IConvaiCharacterAgent.InitialDynamicInfoKeepInContext => InitialDynamicInfoKeepInContext;
        IConvaiDynamicContext IConvaiCharacterAgent.DynamicContext => DynamicContext;

        public EmotionDetectionMode EmotionDetectionMode
        {
            // Resolved on demand at connect time (rare). The emotion controller lives in a module
            // assembly Runtime cannot reference, so it is discovered via the IEmotionDetectionModeSource
            // interface. No source attached => emotions off.
            get
            {
                IEmotionDetectionModeSource source = GetCharacterScopedEmotionDetectionModeSource();
                return source?.EmotionDetectionMode ?? EmotionDetectionMode.Off;
            }
        }

        private IEmotionDetectionModeSource GetCharacterScopedEmotionDetectionModeSource()
        {
            IEmotionDetectionModeSource[] sources = GetComponentsInChildren<IEmotionDetectionModeSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] is not Component component) continue;
                if (component.GetComponentInParent<ConvaiCharacter>(true) == this)
                    return sources[i];
            }

            return null;
        }

        void IConvaiCharacterAgent.SendTrigger(string triggerName) =>
            SendTrigger(triggerName);

        void IConvaiCharacterAgent.SendNarrativeEvent(string eventMessage) =>
            SendNarrativeEvent(eventMessage);

        void IConvaiCharacterAgent.SendNarrativeSpeech(string speechText) =>
            SendNarrativeSpeech(speechText);

        void IConvaiCharacterAgent.UpdateTemplateKeys(Dictionary<string, string> templateKeys) =>
            UpdateTemplateKeys(templateKeys);

        #endregion

        /// <summary>
        ///     Wire-facing action config for the current session — what the backend has been told
        ///     this character can do. Advanced only by the connect payload and by
        ///     <b>acknowledged</b> runtime action-config patches, so an unacknowledged local
        ///     mutation never appears here. For the local name-to-scene-object view that also folds
        ///     in <see cref="ConvaiActionTarget" /> components, target groups and runtime target
        ///     registrations, use <see cref="IConvaiActionResolutionSource.ResolutionActionConfig" />.
        /// </summary>
        public ConvaiActionConfig ActionConfig => GetServerSharedActionConfig()?.Clone();

        ConvaiActionConfig IConvaiActionResolutionSource.ResolutionActionConfig => GetRuntimeActionConfig();

        Vector3? IConvaiActionResolutionSource.ResolutionOrigin => transform.position;

        public IReadOnlyList<ConvaiActionDefinition> ActionDefinitions => GetRuntimeActionDefinitions();

        IReadOnlyList<ConvaiActionDefinition> IConvaiActionDefinitionCatalogSource.ActionDefinitionCatalog =>
            GetRuntimeActionDefinitionCatalog();

        /// <summary>
        ///     Gets the authoring component that defines connect-time action affordances for this character.
        /// </summary>
        public ConvaiActionConfigSource GetActionConfigSource() => GetComponent<ConvaiActionConfigSource>();

        /// <summary>
        ///     Local name-to-scene-object resolution config: the server-shared session config
        ///     (<see cref="GetServerSharedActionConfig" />) plus this scene's local-only lookup aids
        ///     — explicitly registered runtime targets, enabled <see cref="ConvaiActionTarget" />
        ///     components, <see cref="ConvaiActionTargetGroup" /> groups, aliases, interaction
        ///     points and availability overrides. None of those aids are serialized to the backend;
        ///     they only translate a name the backend already used into a scene object, so keeping
        ///     them live cannot desynchronize this character from the backend's own view.
        /// </summary>
        internal ConvaiActionConfig GetRuntimeActionConfig() => BuildMergedRuntimeActionConfig();

        /// <summary>
        ///     Action state this character and the backend both agree on: the config resolved for
        ///     the current connection, advanced only by acknowledged runtime action-config patches.
        ///     Runtime patch prediction and ACK commit read this — never the locally merged
        ///     <see cref="GetRuntimeActionConfig" /> — so an unacknowledged local mutation can never
        ///     be folded into what we believe the backend knows.
        /// </summary>
        internal ConvaiActionConfig GetServerSharedActionConfig() =>
            _hasResolvedSessionActionConfig
                ? _resolvedSessionActionConfig
                : GetActionConfigSource()?.BuildActionConfig(_actions);

        internal IReadOnlyList<ConvaiActionDefinition> GetRuntimeActionDefinitions() =>
            _resolvedSessionActionDefinitions ?? GetActionConfigSource()?.GetEffectiveDefinitions() ??
            Array.Empty<ConvaiActionDefinition>();

        internal IReadOnlyList<ConvaiActionDefinition> GetRuntimeActionDefinitionCatalog() =>
            _resolvedSessionActionDefinitionCatalog ??
            GetActionConfigSource()?.GetEffectiveDefinitions(requireExecutable: true) ??
            Array.Empty<ConvaiActionDefinition>();

        internal void SetResolvedSessionActionConfig(ConvaiActionConfig actionConfig)
        {
            _resolvedSessionActionConfig = actionConfig?.Clone();
            _hasResolvedSessionActionConfig = true;
        }

        /// <summary>
        ///     Stores the wider local-resolution base for this session (see
        ///     <see cref="GetRuntimeActionConfig" />). Unlike
        ///     <see cref="SetResolvedSessionActionConfig" /> this is never sent anywhere and keeps
        ///     disabled actions, so a stale backend command for a disabled action is still
        ///     classified instead of vanishing as unmatched.
        /// </summary>
        internal void SetSessionLocalResolutionConfig(ConvaiActionConfig actionConfig)
        {
            _sessionLocalResolutionConfig = actionConfig?.Clone();
            _hasSessionLocalResolutionConfig = true;
        }

        internal void ClearSessionLocalResolutionConfig()
        {
            _sessionLocalResolutionConfig = null;
            _hasSessionLocalResolutionConfig = false;
        }

        internal void SetResolvedSessionActionDefinitions(IReadOnlyList<ConvaiActionDefinition> definitions) =>
            _resolvedSessionActionDefinitions = definitions == null
                ? null
                : ConvaiActionDefinition.CloneList(definitions);

        internal void SetResolvedSessionActionDefinitionCatalog(IReadOnlyList<ConvaiActionDefinition> definitions) =>
            _resolvedSessionActionDefinitionCatalog = definitions == null
                ? null
                : ConvaiActionDefinition.CloneList(definitions);

        internal void ClearResolvedSessionActionConfig()
        {
            _resolvedSessionActionConfig = null;
            _hasResolvedSessionActionConfig = false;
        }

        internal void ClearResolvedSessionActionDefinitions() => _resolvedSessionActionDefinitions = null;

        internal void ClearResolvedSessionActionDefinitionCatalog() =>
            _resolvedSessionActionDefinitionCatalog = null;

        #region Unity Lifecycle

        private void Awake() => ValidateSDKSetup();

        private void Start() => ValidateRuntimeBootstrapSetup();

        private void OnEnable()
        {
            _destroyCts?.Dispose();
            _destroyCts = new CancellationTokenSource();

            if (!IsInjected) return;

            InitializeAfterInjection();
        }

        /// <summary>
        ///     Called after dependency injection is complete. Handles registration and event subscription.
        ///     This is called from OnEnable or immediately after typed dependency injection when already enabled.
        /// </summary>
        private void InitializeAfterInjection()
        {
            if (_isInitializedAfterInjection) return;

            RegisterWithLocator();
            SubscribeToEvents();
            _isInitializedAfterInjection = true;

            SetRemoteAudioEnabled(EnableRemoteAudioOnStart);

            Logger?.Debug(
                $"[{_characterName}] Initialized after injection, autoConnect={_autoConnect}");
            if (_autoConnect && SessionState is SessionState.Disconnected or SessionState.Error)
            {
                Logger?.Debug(
                    $"[{_characterName}] Starting conversation automatically (deferred one frame so room manager startup can complete)");
                StartCoroutine(DelayedAutoConnectCoroutine());
            }
        }

        /// <summary>
        ///     Defers auto-connect by one frame so that ConvaiRoomManager.Start() (and SignalStartCompleted) runs first,
        ///     avoiding "ConnectAsync timed out waiting for startup" when the character is initialized during injection.
        /// </summary>
        private IEnumerator DelayedAutoConnectCoroutine()
        {
            yield return null;

            if (!this || !isActiveAndEnabled || SessionState is not (SessionState.Disconnected or SessionState.Error))
                yield break;

            StartConversationAsync().AsTask().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Exception ex = task.Exception?.GetBaseException();
                    ConvaiLogger.Error($"[{_characterName}] Auto-connect failed: {ex?.Message}",
                        LogCategory.Character);
                    Logger?.Error($"[{_characterName}] Auto-connect failed: {ex?.Message}");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnDisable()
        {
            CancelPendingOperations();
            ClearPendingRuntimeActionStateUpdates();

            UnsubscribeFromEvents();
            UnregisterFromLocator();
            _isInitializedAfterInjection = false;
        }

        private void OnDestroy()
        {
            CancelPendingOperations();
            ClearPendingRuntimeActionStateUpdates();
            _destroyCts?.Dispose();
            _destroyCts = null;

            UnsubscribeFromEvents();
            UnregisterFromLocator();
            _isInitializedAfterInjection = false;
        }

        private void CancelPendingOperations()
        {
            if (_destroyCts != null && !_destroyCts.IsCancellationRequested)
            {
                try
                {
                    _destroyCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            lock (_characterReadyTcsLock)
            {
                _characterReadyTcs?.TrySetCanceled();
                _characterReadyTcs = null;
            }
        }

        private void OnValidate()
        {
            EnsureConfigurationSourceInitialized();
            if (!UnityEngine.Application.isPlaying) ValidateEditorSetup();
        }

        /// <summary>
        ///     Validates SDK setup in the editor and shows warnings for missing components.
        /// </summary>
        private void ValidateEditorSetup()
        {
            // During domain reloads, ActiveManager may be null even if a manager exists in the scene
            // because Awake() hasn't run yet. Use FindAnyObjectByType to check for component existence.
            if (ConvaiManager.ActiveManager == null && FindAnyObjectByType<ConvaiManager>() == null)
            {
                ConvaiLogger.Warning(
                    $"[Convai SDK] {gameObject.name}: ConvaiManager not found in scene.\n" +
                    "Add it via: GameObject > Convai > Setup Required Components\n" +
                    "Or manually add the ConvaiManager component to a GameObject.",
                    LogCategory.Character);
            }
        }

        /// <summary>
        ///     Validates that the Convai SDK is properly set up at runtime.
        ///     Logs actionable error messages if required components are missing.
        /// </summary>
        private void ValidateSDKSetup()
        {
            List<string> errors = new();

            if (ConvaiManager.ActiveManager == null)
            {
                errors.Add(
                    "ConvaiManager not found in scene.\n" +
                    "   → Add ConvaiManager to your scene (GameObject > Convai > Setup Required Components)\n" +
                    "   → It auto-creates required bootstrap and room components");
            }

            if (errors.Count > 0)
            {
                string fullError = $"[Convai SDK Setup Error] {gameObject.name} ({_characterName}):\n\n" +
                                   string.Join("\n\n", errors.Select((e, i) => $"{i + 1}. {e}")) +
                                   "\n\n📖 For setup instructions, see: https://docs.convai.com/api-docs/plugins-and-integrations/convai-unity-sdk/getting-started/installation" +
                                   "\n💡 Quick fix: Use 'GameObject > Convai > Setup Required Components' menu";

                ConvaiLogger.Error(fullError, LogCategory.Character);
            }
        }

        private void ValidateRuntimeBootstrapSetup()
        {
            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            if (manager.IsInitialized) return;

            string fullError = $"[Convai SDK Setup Error] {gameObject.name} ({_characterName}):\n\n" +
                               "1. Convai runtime bootstrap is not initialized after startup.\n" +
                               "   → Ensure ConvaiManager is active and enabled in the scene\n" +
                               "   → Check console logs for bootstrap or room-session initialization errors\n\n" +
                               "📖 For setup instructions, see: https://docs.convai.com/api-docs/plugins-and-integrations/convai-unity-sdk/getting-started/installation" +
                               "\n💡 Quick fix: Use 'GameObject > Convai > Setup Required Components' menu";

            ConvaiLogger.Error(fullError, LogCategory.Character);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Starts a conversation with this character.
        ///     Connects to the room if not already connected.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation. Combined with component lifetime token.</param>
        /// <returns>The completed lifecycle operation.</returns>
        public IConvaiOperation<Unit> StartConversationAsync(CancellationToken cancellationToken = default) =>
            ConvaiOperation<Unit>.FromTask(StartConversationAsyncCore(cancellationToken));

        /// <summary>
        ///     Waits for the character to become ready. Returns immediately if already ready.
        /// </summary>
        /// <param name="timeoutSeconds">
        ///     Optional timeout in seconds. If null, uses the configured CharacterReadyTimeoutSeconds.
        ///     Use 0 or negative to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">Token to cancel the wait operation.</param>
        /// <returns>The completed lifecycle operation.</returns>
        public IConvaiOperation<Unit> WaitForCharacterReadyAsync(float? timeoutSeconds = null,
            CancellationToken cancellationToken = default) =>
            ConvaiOperation<Unit>.FromTask(WaitForCharacterReadyAsyncCore(timeoutSeconds, cancellationToken));

        /// <summary>
        ///     Ends the current conversation gracefully.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation. Combined with component lifetime token.</param>
        public IConvaiOperation<Unit> StopConversationAsync(CancellationToken cancellationToken = default) =>
            ConvaiOperation<Unit>.FromTask(StopConversationAsyncCore(cancellationToken));

        /// <summary>
        ///     Resets the character from Error state and immediately attempts to reconnect.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The completed lifecycle operation.</returns>
        public IConvaiOperation<Unit> ResetAndRetryAsync(CancellationToken cancellationToken = default) =>
            ConvaiOperation<Unit>.FromTask(ResetAndRetryAsyncCore(cancellationToken));

        /// <summary>
        ///     Toggles the conversation state. Starts if disconnected, ends if connected.
        ///     This convenience wrapper exists for Unity UI button bindings.
        ///     Note: This does NOT toggle remote audio playback. Use EnableRemoteAudio/DisableRemoteAudio/ToggleRemoteAudio for
        ///     that.
        ///     Toggle operations are serialized to prevent overlapping Start/Stop calls.
        /// </summary>
        public void ToggleSpeech()
        {
            lock (_toggleLock)
            {
                if (_toggleTask != null && !_toggleTask.IsCompleted)
                {
                    Logger?.Warning(
                        $"[{_characterName}] Toggle operation already in progress, ignoring duplicate call");
                    return;
                }

                if (SessionState is SessionState.Disconnected or SessionState.Error)
                {
                    Logger?.Debug($"[{_characterName}] Starting conversation after toggle");
                    _toggleTask = ToggleStartAsync();
                }
                else if (IsSessionConnected)
                {
                    Logger?.Debug($"[{_characterName}] Stopping conversation after toggle");
                    _toggleTask = ToggleStopAsync();
                }
                else
                {
                    Logger?.Warning(
                        $"[{_characterName}] Toggle called in transitional state {SessionState}, ignoring");
                }
            }
        }

        /// <summary>
        ///     Awaitable toggle variant for callers that need completion feedback.
        /// </summary>
        public IConvaiOperation<Unit> ToggleSpeechAsync()
        {
            ToggleSpeech();
            lock (_toggleLock)
            {
                return _toggleTask == null
                    ? ConvaiOperation<Unit>.Succeeded(Unit.Value)
                    : ConvaiOperation<Unit>.FromTask(WaitForTaskCompletionAsync(_toggleTask));
            }
        }

        /// <summary>
        ///     Resets the character ready state and clears internal tracking.
        ///     Call this when you want to allow a fresh connection attempt after an error.
        /// </summary>
        /// <returns>True if the reset was performed; false if already disconnected.</returns>
        public bool Reset()
        {
            if (SessionState == SessionState.Error || SessionState == SessionState.Disconnected)
            {
                Logger?.Debug(
                    $"[{_characterName}] Resetting character ready state from {SessionState}");
                IsCharacterReady = false;
                return true;
            }

            Logger?.Debug(
                $"[{_characterName}] Reset called but state is {SessionState}, not Error or Disconnected");
            return false;
        }

        #endregion

        #region Private Helpers

        private static async Task<Unit> WaitForTaskCompletionAsync(Task task)
        {
            await task;
            return Unit.Value;
        }

        private void RegisterWithLocator()
        {
            AgentRegistry?.RegisterCharacter(this, OwnerId);
            Logger?.Debug($"[{_characterName}] Registered with agent registry");
        }

        private void UnregisterFromLocator() => AgentRegistry?.Unregister(this);

        #endregion
    }
}
