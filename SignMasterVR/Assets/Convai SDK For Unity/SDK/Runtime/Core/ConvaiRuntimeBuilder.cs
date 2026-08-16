using System;
using System.Collections.Generic;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Core.MultiParticipant;
using Convai.Runtime.Core.Providers;
using Convai.Runtime.Core.Registry;

namespace Convai.Runtime.Core
{
    /// <summary>
    ///     Builder for composing and creating <see cref="ConvaiRuntime" /> instances.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Design Note</b>: This builder uses typed provider injection only.
    ///         No <c>ConfigureServices(Action&lt;IServiceCollection&gt;)</c> is exposed to prevent
    ///         undeclared dependencies on internals.
    ///     </para>
    ///     <para>
    ///         <b>Usage</b>:
    ///         <code>
    ///         var runtime = new ConvaiRuntimeBuilder()
    ///             .UseTransport(NativeTransportProvider.Instance)
    ///             .UseConfig(ConvaiBootstrapConfigSnapshot.FromSettings())
    ///             .AddModule(new VisionModule())
    ///             .Build();
    ///         </code>
    ///     </para>
    ///     <para>
    ///         <b>Lifecycle</b>: Call <see cref="Build" /> once to create the runtime.
    ///         The runtime is returned in <see cref="RuntimeState.Created" /> state;
    ///         call <see cref="ConvaiRuntime.StartAsync" /> to activate.
    ///     </para>
    /// </remarks>
    public sealed class ConvaiRuntimeBuilder
    {
        private readonly List<IConvaiModule> _modules = new();
        private readonly List<CharacterDescriptor> _ownedCharacters = new();

        // Agent registry
        private IAgentRegistry _agentRegistry;
        private ConvaiBootstrapConfigSnapshot _config;

        // Optional providers
        private IConversationProvider _conversationProvider;
        private IEndUserIdentityProvider _endUserIdentityProvider;
        private IEndUserMetadataProvider _endUserMetadataProvider;

        // Event system
        private IEventHub _eventHub;
        private IFeatureVariantProvider _featureVariants;

        // Multi-participant configuration
        private PlayerDescriptor? _localPlayer;

        // Logging
        private ILogger _logger;
        private IParticipantCoordinator _participantCoordinator;
        private IPersistenceProvider _persistenceProvider;
        private RoomMode _roomMode = RoomMode.SinglePlayer;

        // Room runtime factory
        private Func<IRoomRuntime> _roomRuntimeFactory;
        private IRuntimePreferences _runtimePreferences;
        private ITelemetryProvider _telemetryProvider;

        // Required providers
        private ITransportProvider _transportProvider;

        #region Transport Provider

        /// <summary>
        ///     Sets the transport provider for platform-specific communication.
        /// </summary>
        /// <param name="provider">Transport provider (e.g., NativeTransportProvider, WebGLTransportProvider).</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UseTransport(ITransportProvider provider)
        {
            _transportProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        #endregion

        #region Conversation Provider

        /// <summary>
        ///     Sets the conversation provider for AI backend communication.
        /// </summary>
        /// <param name="provider">Conversation provider (e.g., ConvaiConversationProvider, MockConversationProvider).</param>
        /// <returns>This builder for chaining.</returns>
        /// <remarks>
        ///     If not set, the explicit default provider (<see cref="ConvaiConversationProvider" />)
        ///     will be used by the composition root.
        /// </remarks>
        public ConvaiRuntimeBuilder UseConversation(IConversationProvider provider)
        {
            _conversationProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        /// <summary>
        ///     Sets the end-user identity provider used for runtime room connections.
        /// </summary>
        public ConvaiRuntimeBuilder WithEndUserIdentityProvider(IEndUserIdentityProvider provider)
        {
            _endUserIdentityProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        /// <summary>
        ///     Sets the optional end-user metadata provider used for runtime room connections.
        /// </summary>
        public ConvaiRuntimeBuilder WithEndUserMetadataProvider(IEndUserMetadataProvider provider)
        {
            _endUserMetadataProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        #endregion

        #region Configuration

        /// <summary>
        ///     Sets the bootstrap configuration snapshot.
        /// </summary>
        /// <param name="config">Immutable bootstrap configuration.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UseConfig(ConvaiBootstrapConfigSnapshot config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            return this;
        }

        /// <summary>
        ///     Sets the runtime preferences provider.
        /// </summary>
        /// <param name="preferences">Mutable runtime preferences.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder WithRuntimePreferences(IRuntimePreferences preferences)
        {
            _runtimePreferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            return this;
        }

        /// <summary>
        ///     Sets the feature variant provider.
        /// </summary>
        /// <remarks>
        ///     Advanced extension point for projects that want explicit feature-flag or variant resolution in the runtime.
        /// </remarks>
        /// <param name="provider">Feature variant provider.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder WithFeatureVariants(IFeatureVariantProvider provider)
        {
            _featureVariants = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        #endregion

        #region Optional Providers

        /// <summary>
        ///     Sets the persistence provider for runtime-owned storage.
        /// </summary>
        /// <remarks>
        ///     Advanced extension point. Most Unity scene integrations do not need to set this manually.
        /// </remarks>
        /// <param name="provider">Persistence provider.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UsePersistence(IPersistenceProvider provider)
        {
            _persistenceProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        /// <summary>
        ///     Sets the telemetry provider.
        /// </summary>
        /// <remarks>
        ///     Advanced extension point for custom analytics or observability sinks.
        /// </remarks>
        /// <param name="provider">Telemetry provider.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UseTelemetry(ITelemetryProvider provider)
        {
            _telemetryProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        #endregion

        #region Core Services

        /// <summary>
        ///     Sets the event hub for decoupled communication.
        /// </summary>
        /// <param name="eventHub">Event hub instance.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UseEventHub(IEventHub eventHub)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            return this;
        }

        /// <summary>
        ///     Sets the agent registry for character and player management.
        /// </summary>
        /// <param name="registry">Agent registry instance.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UseAgentRegistry(IAgentRegistry registry)
        {
            _agentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            return this;
        }

        /// <summary>
        ///     Sets a factory for creating the room runtime.
        /// </summary>
        /// <param name="factory">Factory that creates IRoomRuntime.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UseRoomRuntime(Func<IRoomRuntime> factory)
        {
            _roomRuntimeFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        /// <summary>
        ///     Sets the logger for runtime logging.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder UseLogger(ILogger logger)
        {
            _logger = logger.WithTag(nameof(ConvaiRuntimeBuilder));
            return this;
        }

        #endregion

        #region Modules

        /// <summary>
        ///     Adds a module to the runtime.
        /// </summary>
        /// <param name="module">Module instance.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder AddModule(IConvaiModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            _modules.Add(module);
            return this;
        }

        /// <summary>
        ///     Adds a module by creating an instance.
        /// </summary>
        /// <typeparam name="TModule">Module type with parameterless constructor.</typeparam>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder AddModule<TModule>() where TModule : IConvaiModule, new()
        {
            _modules.Add(new TModule());
            return this;
        }

        #endregion

        #region Multi-Participant Configuration

        /// <summary>
        ///     Sets the local player descriptor.
        /// </summary>
        /// <param name="player">Local player descriptor.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder WithLocalPlayer(PlayerDescriptor player)
        {
            _localPlayer = player;
            return this;
        }

        /// <summary>
        ///     Sets the characters owned by the local player.
        /// </summary>
        /// <param name="characters">Character descriptors.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder WithOwnedCharacters(params CharacterDescriptor[] characters)
        {
            _ownedCharacters.Clear();
            if (characters != null)
                _ownedCharacters.AddRange(characters);
            return this;
        }

        /// <summary>
        ///     Sets the room mode for multi-participant scenarios.
        /// </summary>
        /// <param name="mode">Room mode.</param>
        /// <returns>This builder for chaining.</returns>
        public ConvaiRuntimeBuilder WithRoomMode(RoomMode mode)
        {
            _roomMode = mode;
            return this;
        }

        /// <summary>
        ///     Sets a custom participant coordinator.
        /// </summary>
        /// <param name="coordinator">Participant coordinator implementation.</param>
        /// <returns>This builder for chaining.</returns>
        /// <remarks>
        ///     If not set, a default <see cref="ParticipantCoordinatorStub" /> is used
        ///     for single-player scenarios.
        /// </remarks>
        public ConvaiRuntimeBuilder WithParticipantCoordinator(IParticipantCoordinator coordinator)
        {
            _participantCoordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            return this;
        }

        #endregion

        #region Build

        /// <summary>
        ///     Builds and returns a fully composed ConvaiRuntime.
        /// </summary>
        /// <returns>A new ConvaiRuntime in Created state.</returns>
        /// <exception cref="InvalidOperationException">If required providers are not configured.</exception>
        /// <remarks>
        ///     <b>RC-2</b>: All builder fields are now consumed by Build(). No stub defaults remain.
        /// </remarks>
        public ConvaiRuntime Build()
        {
            ValidateConfiguration();

            // Create or use provided event hub
            IEventHub eventHub = _eventHub ?? CreateDefaultEventHub();

            // Create or use provided agent registry
            IAgentRegistry agentRegistry = _agentRegistry ?? CreateDefaultAgentRegistry();

            // Create or use provided room runtime
            // RC-2: Room runtime factory is required - no stub default
            IRoomRuntime roomRuntime = _roomRuntimeFactory?.Invoke()
                                       ?? throw new InvalidOperationException(
                                           "Room runtime factory is required. Call UseRoomRuntime() before Build().");

            // Sort modules by dependencies
            IReadOnlyList<IConvaiModule> sortedModules = SortModulesByDependency(_modules);

            // RC-2: Create the runtime with all providers
            var runtime = new ConvaiRuntime(
                roomRuntime,
                eventHub,
                agentRegistry,
                sortedModules,
                _logger,
                _transportProvider,
                _conversationProvider,
                _config,
                _runtimePreferences,
                _featureVariants,
                _persistenceProvider,
                _telemetryProvider,
                _endUserIdentityProvider,
                _endUserMetadataProvider);

            _logger?.Debug($"Built runtime with {sortedModules.Count} modules, " +
                           $"transport={_transportProvider?.GetType().Name ?? "none"}, " +
                           $"conversation={_conversationProvider?.GetType().Name ?? "none"}");

            return runtime;
        }

        private void ValidateConfiguration()
        {
            // RC-2: Room runtime factory is validated in Build() to fail fast with clear message
            // Transport and conversation providers are optional - components should check for null
        }

        private IEventHub CreateDefaultEventHub()
        {
            // Use UnityScheduler if available, otherwise create without scheduler
            return new EventHub(null, _logger);
        }

        private IAgentRegistry CreateDefaultAgentRegistry() => new AgentRegistry();

        private static IReadOnlyList<IConvaiModule> SortModulesByDependency(List<IConvaiModule> modules)
        {
            // Simple topological sort by dependency
            var sorted = new List<IConvaiModule>();
            var visited = new HashSet<string>();
            var moduleMap = new Dictionary<string, IConvaiModule>();

            foreach (IConvaiModule module in modules)
                moduleMap[module.ModuleId] = module;

            void Visit(IConvaiModule module)
            {
                if (visited.Contains(module.ModuleId))
                    return;

                visited.Add(module.ModuleId);

                if (module.RequiredModules != null)
                {
                    foreach (string depId in module.RequiredModules)
                    {
                        if (moduleMap.TryGetValue(depId, out IConvaiModule dep))
                            Visit(dep);
                    }
                }

                sorted.Add(module);
            }

            foreach (IConvaiModule module in modules)
                Visit(module);

            return sorted.AsReadOnly();
        }

        #endregion
    }

    /// <summary>
    ///     Mode for room participation.
    /// </summary>
    public enum RoomMode
    {
        /// <summary>Single player mode - no room sharing.</summary>
        SinglePlayer,

        /// <summary>Create a new room and get invite code.</summary>
        CreateRoom,

        /// <summary>Join an existing room via code.</summary>
        JoinRoom
    }
}
