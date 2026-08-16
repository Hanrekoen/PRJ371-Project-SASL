using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Application.Services.Transcript;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Services;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Persistence;
using Convai.Infrastructure.Services;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Adapters.Platform;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.DependencyInjection;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Facades;
using Convai.Runtime.Identity;
using Convai.Runtime.Logging;
using Convai.Runtime.Persistence;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Room;
using Convai.Runtime.Settings;
using Convai.Shared.Abstractions;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using ILogger = Convai.Domain.Logging.ILogger;
using ISessionPersistence = Convai.Domain.Abstractions.ISessionPersistence;

namespace Convai.Runtime.Core.Composition
{
    /// <summary>
    ///     Plain C# composition root that owns all SDK service construction and lifecycle.
    ///     Replaces the legacy container pattern with explicit, ordered construction.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Created and owned by <see cref="ConvaiManager" /> (the thin MonoBehaviour shell).
    ///         All services are singletons constructed eagerly in topological dependency order.
    ///     </para>
    ///     <para>
    ///         <b>No service locator</b>: services are stored as typed fields and exposed via
    ///         typed <c>TryGet*</c> accessors.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiRuntimeHost : IDisposable
    {
        private readonly List<ConvaiCharacter> _characters = new();

        // Configuration
        private readonly bool _debugLogging;
        private readonly IEndUserIdentityProvider _defaultIdentityProvider;
        private readonly ConvaiDiagnosticsDependencies _diagnosticsDependencies;

        // Diagnostics
        private readonly ConvaiRuntimeDiagnosticsService _diagnosticsService;
        private readonly IEventHub _eventHub;

        private readonly IKeyValueStore _keyValueStore;

        // Core services (no dependencies)
        private readonly ILogger _logger;
        private readonly ILogSink _logSink;
        private readonly IMicrophoneDeviceService _microphoneDeviceService;

        // Application services
        private readonly INarrativeDesignDataService _narrativeDataService;
        private readonly RuntimeSettingsNotificationApplier _notificationApplier;
        private readonly ConvaiNotificationEventBridge _notificationEventBridge;

        // Notification (optional)
        private readonly IConvaiNotificationService _notificationService;

        // Test overrides (internal, populated before construction)
        private readonly Dictionary<Type, object> _overrides = new();
        private readonly IConvaiPermissionService _permissionService;
        private readonly IPlayerInputService _playerInputService;

        private readonly List<IConvaiModule> _registeredModules = new();

        // Runtime reference
        private readonly ConvaiRuntime _runtime;
        private readonly ISessionMetrics _sessionMetrics;

        // Session services
        private readonly ISessionPersistence _sessionPersistence;
        private readonly IUnityScheduler _scheduler;
        private readonly ISessionService _sessionService;
        private readonly ISessionStateMachine _sessionStateMachine;
        private readonly IConvaiSettingsPanelController _settingsPanelController;
        private readonly IConvaiRuntimeSettingsService _settingsService;

        // Settings cluster
        private readonly IConvaiRuntimeSettingsStore _settingsStore;
        private readonly bool _strictMode;

        // Settings appliers
        private readonly RuntimeSettingsIdentityApplier _identityApplier;
        private readonly RuntimeSettingsTranscriptApplier _transcriptApplier;
        private readonly IRoomTranscriptEngine _transcriptEngine;
        private bool _transcriptPresentationEnabled = true;

        // Presentation services
        private readonly IVisibleCharacterService _visibleCharacterService;
        private RuntimeSettingsAudioApplier _audioApplier;
        private ICredentialProvider _credentialProvider;

        private bool _disposed;
        private IEndUserMetadataProvider _endUserMetadataProvider;
        private IEndUserIdentityProvider _identityProvider;
        private ConvaiRoomManager _roomManager;
        private IRoomOwnershipProvider _roomOwnershipProvider;
        private SubscriptionToken _roomOwnershipRebindToken;

        private bool _sdkEventsSubscribed;

        /// <summary>
        ///     Creates the composition root and constructs all services in dependency order.
        /// </summary>
        /// <param name="runtime">The built ConvaiRuntime (must have Events, Agents set).</param>
        /// <param name="debugLogging">Enable verbose bootstrap logging.</param>
        /// <param name="strictMode">Throw on missing required services during injection.</param>
        /// <param name="registerDefaultNotificationService">Register the default notification service.</param>
        /// <param name="eagerInitialization">Currently unused — all services are constructed eagerly.</param>
        /// <param name="scheduler">Scheduler used by runtime services. Defaults to UnityScheduler.</param>
        public ConvaiRuntimeHost(
            ConvaiRuntime runtime,
            bool debugLogging = false,
            bool strictMode = false,
            bool registerDefaultNotificationService = true,
            bool eagerInitialization = true,
            IUnityScheduler scheduler = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _debugLogging = debugLogging;
            _strictMode = strictMode;
            _scheduler = scheduler ?? UnityScheduler.Instance;

            IEventHub eventHub = runtime.Events
                                 ?? throw new ArgumentException(
                                     "ConvaiRuntime.Events must be set before creating host.");
            _eventHub = eventHub;

            // Layer 0: No-dependency services
            ConvaiLogger.Initialize();
            _logger = new ConvaiLogger();
            _logSink = new UnityConsoleSink();
            _defaultIdentityProvider = new DeviceEndUserIdProvider();
            _identityProvider = runtime.EndUserIdentityProvider ?? _defaultIdentityProvider;
            _endUserMetadataProvider = runtime.EndUserMetadataProvider;
            _credentialProvider = CredentialProviderFactory.Create();
            _keyValueStore = new PlayerPrefsKeyValueStore();
            _microphoneDeviceService = new MicrophoneDeviceService();
            _visibleCharacterService = new VisibleCharacterService();
            _playerInputService = new PlayerInputService();
            _settingsPanelController = new ConvaiSettingsPanelController();
            _permissionService = new ConvaiPermissionService();
            _transcriptEngine = new RoomTranscriptEngine(eventHub, _logger);

            if (_debugLogging)
                ConvaiLogger.Debug("Layer 0 services created", LogCategory.Bootstrap);

            // Layer 1: Single-dependency services
            _settingsStore = new ConvaiRuntimeSettingsStore(_keyValueStore);
            _settingsService = new ConvaiRuntimeSettingsService(
                ConvaiSettings.Instance, _settingsStore, _microphoneDeviceService);
            _sessionPersistence = new KeyValueStoreSessionPersistence(new PlayerPrefsKeyValueStore());

            if (_debugLogging)
                ConvaiLogger.Debug("Layer 1 services created", LogCategory.Bootstrap);

            // Layer 2: Multi-dependency services
            _sessionStateMachine = new SessionStateMachine(eventHub, _logger);
            _sessionService = new SessionService(_sessionPersistence, _logger);
            _sessionMetrics = new SessionMetrics(eventHub, _sessionStateMachine, _logger);

            _transcriptApplier = new RuntimeSettingsTranscriptApplier(
                _settingsService,
                SetTranscriptPresentationEnabled);
            _identityApplier = new RuntimeSettingsIdentityApplier(
                _settingsService,
                () => Player,
                name => _transcriptEngine?.UpdatePlayerDisplayName(name));
            // Audio applier created without room services (they're bound later)
            _audioApplier = new RuntimeSettingsAudioApplier(
                _settingsService, _microphoneDeviceService,
                null, null, _scheduler);

            _diagnosticsDependencies = new ConvaiDiagnosticsDependencies(
                _credentialProvider, _identityProvider, _sessionPersistence,
                _settingsService);
            _diagnosticsService = new ConvaiRuntimeDiagnosticsService(_diagnosticsDependencies);

            if (_debugLogging)
                ConvaiLogger.Debug("Layer 2 services created", LogCategory.Bootstrap);

            // Layer 3: Notification services (optional)
            if (registerDefaultNotificationService)
                _notificationService = new ConvaiNotificationService(_scheduler);

            _notificationEventBridge = new ConvaiNotificationEventBridge(
                () => _notificationService,
                eventHub,
                () => _settingsService.Current.NotificationsEnabled);

            if (_notificationService != null)
            {
                _notificationApplier = new RuntimeSettingsNotificationApplier(
                    _settingsService, _notificationService);
            }

            if (_debugLogging)
                ConvaiLogger.Debug("Layer 3 notification services created", LogCategory.Bootstrap);

            // Layer 4: Application services
            Func<string> apiKeyProvider = () =>
            {
                if (_credentialProvider == null || !_credentialProvider.HasValidCredentials)
                    return null;
                return _credentialProvider.GetApiKey();
            };
            _narrativeDataService = new NarrativeDesignDataService(apiKeyProvider);

            if (_debugLogging)
                ConvaiLogger.Debug("Layer 4 application services created", LogCategory.Bootstrap);

            IsBootstrapped = true;

            if (_debugLogging)
                ConvaiLogger.Debug("All services constructed successfully", LogCategory.Bootstrap);
        }

        /// <summary>Whether the host has completed service construction.</summary>
        public bool IsBootstrapped { get; private set; }

        // Ownership State Management

        public IReadOnlyList<ConvaiCharacter> Characters => _characters;
        public ConvaiPlayer Player { get; private set; }

        public ConvaiCharacter ActiveConversationCharacter { get; private set; }

        public IReadOnlyList<IConvaiModule> RegisteredModules => _registeredModules;

        // Event Subscriptions

        public ConvaiEvents Events { get; private set; }

        public ConvaiAudio Audio { get; private set; }

        public ConvaiTranscripts Transcripts { get; private set; }

        // Shutdown

        public void Dispose()
        {
            if (_disposed) return;

            Events?.Dispose();
            Events = null;

            Transcripts?.Dispose();
            Transcripts = null;

            _identityApplier?.Dispose();
            _transcriptApplier?.Dispose();
            _transcriptEngine?.Dispose();
            if (_eventHub != null && _roomOwnershipRebindToken != default)
            {
                _eventHub.Unsubscribe(_roomOwnershipRebindToken);
                _roomOwnershipRebindToken = default;
            }

            // Dispose disposable services
            _notificationEventBridge?.Dispose();
            (_sessionMetrics as IDisposable)?.Dispose();

            _disposed = true;

            if (_debugLogging)
                ConvaiLogger.Debug("Disposed", LogCategory.Bootstrap);
        }

        // Room Manager Binding

        /// <summary>
        ///     Binds the room manager and creates room-dependent services.
        ///     Called after ConvaiRoomManager is added to the scene.
        /// </summary>
        public void BindRoomManager(ConvaiRoomManager roomManager)
        {
            _roomManager = roomManager;
            if (roomManager == null) return;

            // Recreate audio applier with room services
            _audioApplier = new RuntimeSettingsAudioApplier(
                _settingsService, _microphoneDeviceService,
                roomManager, roomManager, _scheduler);

            if (_debugLogging)
            {
                ConvaiLogger.Debug("Room manager bound, audio services created",
                    LogCategory.Bootstrap);
            }
        }

        // Runtime Component Injection

        /// <summary>
        ///     Injects dependencies into room manager, characters, and player.
        /// </summary>
        public void InjectRuntimeComponents()
        {
            if (_roomManager != null)
            {
                try
                {
                    _roomManager.InjectDependencies(CreateRoomManagerBundle());
                    if (_debugLogging)
                        ConvaiLogger.Debug("Injected ConvaiRoomManager", LogCategory.Bootstrap);
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Failed to inject RoomManager: {ex.Message}",
                        LogCategory.Bootstrap);
                    if (_strictMode) throw;
                }
            }

            InjectOwnedAgents();
        }

        /// <summary>
        ///     Re-injects only the currently resolved owned agents without rebuilding room-manager runtime state.
        /// </summary>
        public void InjectOwnedAgents()
        {
            foreach (ConvaiCharacter character in _characters)
            {
                try
                {
                    character.InjectDependencies(CreateCharacterBundle());
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Failed to inject character: {ex.Message}",
                        LogCategory.Bootstrap);
                    if (_strictMode) throw;
                }
            }

            if (Player != null)
            {
                try
                {
                    Player.InjectDependencies(CreatePlayerBundle());
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error($"Failed to inject player: {ex.Message}",
                        LogCategory.Bootstrap);
                    if (_strictMode) throw;
                }
            }
        }

        /// <summary>
        ///     Pre-populates the runtime's module context with services that modules need.
        /// </summary>
        public void PopulateModuleServices()
        {
            if (_runtime == null) return;

            if (_roomManager is IConvaiRoomConnectionService roomConnectionService)
                _runtime.RegisterModuleService(roomConnectionService);

            if (_roomManager is IConvaiRoomAudioService roomAudioService)
                _runtime.RegisterModuleService(roomAudioService);

            if (_credentialProvider != null)
                _runtime.RegisterModuleService(_credentialProvider);

            if (_debugLogging)
                ConvaiLogger.Debug("Module services pre-populated", LogCategory.Bootstrap);
        }

        private void RebuildDiagnosticsService()
        {
            _diagnosticsDependencies.SetCredentialProvider(_credentialProvider);
            _diagnosticsDependencies.SetIdentityProvider(_identityProvider);
        }

        // Dependency Bundle Creation

        private IConvaiCharacterDependencies CreateCharacterBundle()
        {
            return new ConvaiCharacterDependencies(
                _eventHub,
                _roomManager,
                _roomManager,
                _runtime?.Agents,
                _logger);
        }

        private IConvaiPlayerDependencies CreatePlayerBundle()
        {
            return new ConvaiPlayerDependencies(
                _playerInputService,
                _settingsService,
                _logger);
        }

        private IConvaiRoomManagerDependencies CreateRoomManagerBundle()
        {
            return new ConvaiRoomManagerDependencies(
                _eventHub,
                _logger,
                _runtime?.Agents,
                _sessionPersistence,
                _credentialProvider,
                _identityProvider,
                _endUserMetadataProvider,
                _sessionStateMachine,
                _sessionService,
                null, // sectionNameResolver — resolved by module context
                _roomOwnershipProvider,
                _diagnosticsService,
                _settingsService,
                _microphoneDeviceService,
                _runtime?.Transport);
        }

        // Typed Service Accessors

        public bool TryGetEventHub(out IEventHub eventHub)
        {
            eventHub = _eventHub;
            return eventHub != null;
        }

        public bool TryGetRoomConnectionService(out IConvaiRoomConnectionService service)
        {
            if (_roomManager != null)
            {
                service = _roomManager;
                return true;
            }

            service = null;
            return false;
        }

        public bool TryGetRoomAudioService(out IConvaiRoomAudioService service)
        {
            if (_roomManager != null)
            {
                service = _roomManager;
                return true;
            }

            service = null;
            return false;
        }

        public bool TryGetAgentRegistry(out IAgentRegistry registry)
        {
            if (_runtime?.Agents != null)
            {
                registry = _runtime.Agents;
                return true;
            }

            registry = null;
            return false;
        }

        public bool TryGetSettingsPanelController(out IConvaiSettingsPanelController controller)
        {
            controller = _settingsPanelController;
            return controller != null;
        }

        public bool TryGetRuntimeSettingsService(out IConvaiRuntimeSettingsService service)
        {
            service = _settingsService;
            return service != null;
        }

        public bool TryGetMicrophoneDeviceService(out IMicrophoneDeviceService service)
        {
            service = _microphoneDeviceService;
            return service != null;
        }

        public bool TryGetPermissionService(out IConvaiPermissionService service)
        {
            service = _permissionService;
            return service != null;
        }

        public bool TryGetNotificationService(out IConvaiNotificationService service)
        {
            service = _notificationService;
            return service != null;
        }

        public bool TryGetPlayerInputService(out IPlayerInputService service)
        {
            service = _playerInputService;
            return service != null;
        }

        public bool TryGetVisibleCharacterService(out IVisibleCharacterService service)
        {
            service = _visibleCharacterService;
            return service != null;
        }

        public bool TryGetTransportProvider(out ITransportProvider provider)
        {
            if (_runtime?.Transport != null)
            {
                provider = _runtime.Transport;
                return true;
            }

            provider = null;
            return false;
        }

        public bool TryGetDiagnosticsService(out IConvaiRuntimeDiagnosticsService service)
        {
            service = _diagnosticsService;
            return service != null;
        }

        /// <summary>
        ///     Refreshes ownership from the provided explicit references and scene installer.
        /// </summary>
        public void RefreshOwnedAgentState(
            ConvaiSceneInstaller sceneInstaller,
            ConvaiPlayer explicitPlayer,
            List<ConvaiCharacter> explicitCharacters,
            ConvaiCharacter explicitConversationTarget,
            IRoomOwnershipProvider ownershipProvider,
            IReadOnlyList<ConvaiCharacter> additionalOwnedCharacters = null,
            bool injectOwnedAgentsBeforeNotification = false)
        {
            _roomOwnershipProvider = ownershipProvider;
            ConvaiPlayer previousPlayer = Player;
            ConvaiCharacter previousTarget = ActiveConversationCharacter;
            ConvaiCharacter[] previousCharacters = _characters.ToArray();
            int previousCharCount = _characters.Count;

            // Resolve player: explicit > installer > scene-wide single player fallback.
            Player = ResolveOwnedPlayer(sceneInstaller, explicitPlayer);
            _identityApplier.ApplyCurrent();

            // Resolve characters: explicit > installer-owned > installer-all > explicit target > scene-wide fallback.
            _characters.Clear();
            IReadOnlyList<ConvaiCharacter> sourceChars =
                ResolveOwnedCharacters(sceneInstaller, explicitCharacters, explicitConversationTarget);

            if (sourceChars != null)
            {
                foreach (ConvaiCharacter c in sourceChars)
                {
                    if (c != null && !_characters.Contains(c))
                        _characters.Add(c);
                }
            }

            // Runtime-created module owners are additive. Installer or explicit ownership remains
            // authoritative for its configured characters without hiding late-created characters.
            if (additionalOwnedCharacters != null)
            {
                foreach (ConvaiCharacter c in additionalOwnedCharacters)
                {
                    if (c != null && !_characters.Contains(c))
                        _characters.Add(c);
                }
            }

            // Resolve conversation target
            ActiveConversationCharacter = ResolveConversationTarget(explicitConversationTarget);

            // Notify room manager if ownership changed
            bool changed = !ReferenceEquals(previousPlayer, Player) ||
                           !ReferenceEquals(previousTarget, ActiveConversationCharacter) ||
                           previousCharCount != _characters.Count ||
                           !HaveSameCharacterReferences(previousCharacters, _characters);

            // Runtime-created character modules register after the initial room startup. Inject a
            // newly discovered owner before notifying the room manager so room composition observes
            // an initialized character that is already present in the agent registry.
            if (changed && injectOwnedAgentsBeforeNotification)
                InjectOwnedAgents();

            if (changed && _roomManager != null) _roomManager.HandleOwnedAgentStateChanged();

            // Update diagnostics
            _diagnosticsService?.AttachManager(null); // will be set via ConvaiManager
        }

        private ConvaiCharacter ResolveConversationTarget(ConvaiCharacter explicitTarget)
        {
            if (explicitTarget != null) return explicitTarget;
            return _characters.Count == 1 ? _characters[0] : null;
        }

        private static ConvaiPlayer ResolveOwnedPlayer(
            ConvaiSceneInstaller sceneInstaller,
            ConvaiPlayer explicitPlayer)
        {
            if (explicitPlayer != null)
                return explicitPlayer;

            if (sceneInstaller != null)
            {
                IReadOnlyList<ConvaiPlayer> players = sceneInstaller.Players;
                if (players != null && players.Count > 0)
                    return players[0];
            }

            ConvaiPlayer[] scenePlayers = GetLiveSceneObjects<ConvaiPlayer>();

            ConvaiPlayer resolvedPlayer = null;
            for (int i = 0; i < scenePlayers.Length; i++)
            {
                ConvaiPlayer candidate = scenePlayers[i];
                if (candidate == null)
                    continue;

                if (resolvedPlayer != null)
                    return null;

                resolvedPlayer = candidate;
            }

            return resolvedPlayer;
        }

        private static IReadOnlyList<ConvaiCharacter> ResolveOwnedCharacters(
            ConvaiSceneInstaller sceneInstaller,
            IReadOnlyList<ConvaiCharacter> explicitCharacters,
            ConvaiCharacter explicitConversationTarget)
        {
            if (explicitCharacters != null && explicitCharacters.Count > 0)
                return explicitCharacters;

            if (sceneInstaller != null)
            {
                IReadOnlyList<ConvaiCharacter> ownedCharacters = sceneInstaller.OwnedCharacters;
                if (ownedCharacters != null && ownedCharacters.Count > 0)
                    return ownedCharacters;

                IReadOnlyList<ConvaiCharacter> configuredCharacters = sceneInstaller.Characters;
                if (configuredCharacters != null && configuredCharacters.Count > 0)
                    return configuredCharacters;
            }

            if (explicitConversationTarget != null)
                return new[] { explicitConversationTarget };

            ConvaiCharacter[] sceneCharacters = GetLiveSceneObjects<ConvaiCharacter>();
            if (sceneCharacters != null && sceneCharacters.Length > 0)
                return sceneCharacters;

            return Array.Empty<ConvaiCharacter>();
        }

        private static T[] GetLiveSceneObjects<T>() where T : UnityEngine.Component
        {
            var candidates = new List<T>();

            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int sceneIndex = 0; sceneIndex < sceneCount; sceneIndex++)
            {
                UnityEngine.SceneManagement.Scene scene =
                    UnityEngine.SceneManagement.SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                UnityEngine.GameObject[] roots = scene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    UnityEngine.GameObject root = roots[rootIndex];
                    if (root == null)
                        continue;

                    T[] components = root.GetComponentsInChildren<T>(true);
                    for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                    {
                        T candidate = components[componentIndex];
                        if (candidate != null)
                            candidates.Add(candidate);
                    }
                }
            }

            if (candidates.Count == 0)
                return Array.Empty<T>();

            return candidates
                .OrderBy(GetStableSceneObjectOrderKey, StringComparer.Ordinal)
                .ToArray();
        }

        private static string GetStableSceneObjectOrderKey(UnityEngine.Component candidate)
        {
            UnityEngine.Transform transform = candidate.transform;
            var segments = new List<string>();

            while (transform != null)
            {
                segments.Add(transform.GetSiblingIndex().ToString("D6"));
                transform = transform.parent;
            }

            segments.Reverse();
            return $"{candidate.gameObject.scene.handle:D6}:{string.Join("/", segments)}:{candidate.name}";
        }

        private static bool HaveSameCharacterReferences(
            IReadOnlyList<ConvaiCharacter> previousCharacters,
            IReadOnlyList<ConvaiCharacter> currentCharacters)
        {
            int previousCount = previousCharacters?.Count ?? 0;
            int currentCount = currentCharacters?.Count ?? 0;
            if (previousCount != currentCount)
                return false;

            for (int i = 0; i < previousCount; i++)
            {
                if (!ReferenceEquals(previousCharacters[i], currentCharacters[i]))
                    return false;
            }

            return true;
        }

        /// <summary>Builds the ownership snapshot for IRoomOwnershipProvider.</summary>
        public RoomOwnershipSnapshot CaptureOwnership()
        {
            var ownedCharacters = new List<IConvaiCharacterAgent>();
            foreach (ConvaiCharacter c in _characters)
                if (c != null)
                    ownedCharacters.Add(c);

            ActiveConversationTarget target = ActiveConversationCharacter != null
                ? new ActiveConversationTarget(ActiveConversationCharacter)
                : null;

            return new RoomOwnershipSnapshot(Player, ownedCharacters, target);
        }

        /// <summary>
        ///     Ensures the IRoomOwnershipProvider is registered with the diagnostics service.
        /// </summary>
        public void UpdateDiagnosticsRegistration(ConvaiManager manager, ConvaiRoomManager roomManager)
        {
            _diagnosticsService?.AttachManager(manager);
            _diagnosticsService?.AttachRoomManager(roomManager);
        }

        internal void SetEndUserIdentityProvider(IEndUserIdentityProvider provider)
        {
            _identityProvider = provider ?? _defaultIdentityProvider;
            RebuildDiagnosticsService();
            _roomManager?.UpdateEndUserContextProviders(_identityProvider, _endUserMetadataProvider);
        }

        internal void SetEndUserMetadataProvider(IEndUserMetadataProvider provider)
        {
            _endUserMetadataProvider = provider;
            _roomManager?.UpdateEndUserContextProviders(_identityProvider, _endUserMetadataProvider);
        }

        // Module Registration

        public void RegisterModule(IConvaiModule module)
        {
            if (module != null && !_registeredModules.Contains(module))
                _registeredModules.Add(module);
        }

        public void UnregisterModule(IConvaiModule module)
        {
            if (module != null) _registeredModules.Remove(module);
        }

        /// <summary>Creates facades and subscribes to SDK events.</summary>
        public void EnsureFacades(ConvaiRoomManager roomManager)
        {
            if (Audio == null && roomManager != null)
                Audio = new ConvaiAudio(roomManager);

            if (Transcripts == null && _transcriptEngine != null)
            {
                Transcripts = new ConvaiTranscripts(_transcriptEngine);
                Transcripts.SetPresentationEnabled(_transcriptPresentationEnabled);
            }

            if (Events == null && _eventHub != null)
                Events = new ConvaiEvents(_eventHub);
        }

        private void SetTranscriptPresentationEnabled(bool enabled)
        {
            _transcriptPresentationEnabled = enabled;
            Transcripts?.SetPresentationEnabled(enabled);
        }

        // Private Helpers

#if UNITY_INCLUDE_TESTS
        /// <summary>
        ///     Registers a test override for a service type. Must be called before
        ///     the service is accessed. Used by PlayMode tests to inject test doubles.
        /// </summary>
        internal void OverrideService<TService>(TService instance) where TService : class
        {
            _overrides[typeof(TService)] = instance;

            if (instance is ICredentialProvider credentialProvider)
            {
                _credentialProvider = credentialProvider;
                RebuildDiagnosticsService();
            }
        }

        /// <summary>
        ///     Gets a registered override, or null if none.
        /// </summary>
        internal TService GetOverride<TService>() where TService : class =>
            _overrides.TryGetValue(typeof(TService), out object obj) ? obj as TService : null;
#endif
    }
}
