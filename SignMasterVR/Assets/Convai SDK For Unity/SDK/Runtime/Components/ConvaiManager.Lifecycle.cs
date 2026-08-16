/// <summary>
///     ConvaiManager partial: Unity lifecycle callbacks and core initialization.
/// </summary>

using Convai.Domain.Logging;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Composition;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Components
{
    public partial class ConvaiManager
    {
        private void Awake()
        {
            if (ActiveManager != null && ActiveManager != this)
            {
                // In scene-specific mode, promote the newly loaded scene manager and retire the previous one.
                if (!ActiveManager.ShouldPersistAcrossScenes && !ShouldPersistAcrossScenes)
                {
                    ConvaiManager previousManager = ActiveManager;
                    ActiveManager = this;
                    if (previousManager != null)
                        DestroyImmediate(previousManager.gameObject);
                }
                else
                {
                    if (_debugLogging)
                        ConvaiLogger.Debug("Duplicate manager detected, destroying copy.", LogCategory.SDK);
                    DestroyImmediate(gameObject);
                    return;
                }
            }

            ActiveManager = this;

            if (ShouldPersistAcrossScenes)
            {
                if (transform.parent != null) transform.SetParent(null);
                if (UnityEngine.Application.isPlaying) DontDestroyOnLoad(gameObject);
            }

            EnsureRequiredCoreComponents();
        }

        private void Start()
        {
            // Refresh ownership again on Start so scene-discovered player/character components added or
            // finalized during other Awake callbacks are available before ConvaiRoomManager.Start auto-connects.
            RefreshOwnedAgentState();

            // Re-run injection after the Start-time ownership refresh so newly discovered scene characters/player
            // are registered with the agent registry before room startup and remote track events arrive.
            if (_autoInject)
            {
                _host?.InjectOwnedAgents();
            }

            // RC-1: Discover modules and start the runtime.
            // Module discovery is deferred to Start() because modules self-register in their Awake(),
            // which runs AFTER ConvaiManager.Awake() (execution order -1100 vs default 0).
            if (ConvaiRuntime != null && ConvaiRuntime.State == RuntimeState.Created)
            {
                DiscoverAndAddModules();

                if (_debugLogging)
                    ConvaiLogger.Debug("Starting runtime in Start()", LogCategory.Bootstrap);

                StartRuntimeAsync();
            }

            _host?.EnsureFacades(_roomManager);
            SubscribeToFacadeEvents();
            UpdateWebGLVoiceStartArmState();
            EnsureConversationModeComponents();
        }

        private void Update()
        {
            TryConsumeWebGLVoiceStartGesture();
            HandleConversationModeInput();
            TickIdleDeadline();

            if (_isApplicationBackgrounded &&
                _effectiveBackgroundPolicy == RuntimeBackgroundPolicy.MuteButCatchUp)
                ApplyBackgroundCharacterMute();

            if (_timelinePauseReasons.Count > 0)
                PauseCharacterAudioSources();
        }

        private void OnEnable()
        {
            // Event subscriptions handled by host/facades
        }

        private void OnDisable()
        {
            _webGLVoiceStartArmed = false;
            ReleaseManagedPushToTalkIfNeeded();
        }

        private void OnDestroy()
        {
            if (ActiveManager == this) ActiveManager = null;

            RestoreSessionLifecycleState();
            _host?.Dispose();
            _host = null;

            DisposeBuilderRuntime();
        }

        private void OnApplicationQuit() => _host?.Dispose();

        /// <summary>
        ///     Builds the runtime, creates the host, ensures room manager, and binds components.
        /// </summary>
        private void EnsureRequiredCoreComponents()
        {
            // Phase 1: Build runtime via ConvaiRuntimeBuilder
            if (ConvaiRuntime == null)
            {
                if (_debugLogging)
                    ConvaiLogger.Debug("Building runtime via ConvaiRuntimeBuilder",
                        LogCategory.Bootstrap);

                BuildRuntime();
                InitializeUnityAdapter();
            }

            // Phase 2: Create the composition root host
            if (_host == null && ConvaiRuntime != null)
            {
                _host = new ConvaiRuntimeHost(
                    ConvaiRuntime,
                    _debugLogging,
                    _strictMode,
                    _registerDefaultNotificationService,
                    _eagerInitialization);

                if (_debugLogging)
                    ConvaiLogger.Debug("ConvaiRuntimeHost created", LogCategory.Bootstrap);
            }

            // Phase 3: Ensure room manager
            _roomManager = gameObject.GetComponent<ConvaiRoomManager>()
                           ?? gameObject.AddComponent<ConvaiRoomManager>();
            _roomManager.AdoptLegacyManagerConversationSettings(
                _conversationMode,
                _legacyPushToTalkKey,
                _interruptBotOnPress,
                _requireTurnCompletionBeforeNextPress,
                _pushToTalkTurnCompletionTimeoutMs);
            EnsureConversationModeComponents();

            // Phase 4: Bind room manager to host and inject
            _host?.BindRoomManager(_roomManager);
            RefreshOwnedAgentState();

            if (_autoInject)
            {
                _host?.PopulateModuleServices();
                _host?.InjectRuntimeComponents();
            }

            if (_debugLogging)
                ConvaiLogger.Debug("Core components initialized", LogCategory.Bootstrap);
        }

        /// <summary>
        ///     Attempts to get the room manager reference.
        /// </summary>
        internal bool TryGetRoomManager(out ConvaiRoomManager roomManager)
        {
            if (_roomManager == null)
                _roomManager = gameObject.GetComponent<ConvaiRoomManager>();

            roomManager = _roomManager;
            if (roomManager != null) return true;

            InvokeError("ConvaiRoomManager is not available.");
            return false;
        }
    }
}
