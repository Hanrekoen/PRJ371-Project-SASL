using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Core.Coordinators;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Core.Providers;
using Convai.Runtime.Room;

namespace Convai.Runtime.Core
{
    /// <summary>
    ///     The main runtime orchestrator for the Convai SDK.
    ///     Owns all services and manages module lifecycle.
    /// </summary>
    /// <remarks>
    ///     Created via <see cref="ConvaiRuntimeBuilder.Build" />, started with <see cref="StartAsync" />,
    ///     and disposed through <see cref="DisposeAsync" />.
    /// </remarks>
    public sealed class ConvaiRuntime : IConvaiRuntime, IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly List<IConvaiModule> _modules;
        private readonly object _stateLock = new();
        private RuntimePauseReason _lastPauseReason;
        private ModuleContext _moduleContext;
        private RuntimeState _state;

        /// <summary>
        ///     Internal constructor - use <see cref="ConvaiRuntimeBuilder" /> to create instances.
        /// </summary>
        internal ConvaiRuntime(
            IRoomRuntime room,
            IEventHub events,
            IAgentRegistry agents,
            IReadOnlyList<IConvaiModule> modules,
            ILogger logger = null,
            ITransportProvider transport = null,
            IConversationProvider conversation = null,
            ConvaiBootstrapConfigSnapshot config = null,
            IRuntimePreferences runtimePreferences = null,
            IFeatureVariantProvider featureVariants = null,
            IPersistenceProvider persistence = null,
            ITelemetryProvider telemetry = null,
            IEndUserIdentityProvider endUserIdentityProvider = null,
            IEndUserMetadataProvider endUserMetadataProvider = null)
        {
            Room = room ?? throw new ArgumentNullException(nameof(room));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Agents = agents ?? throw new ArgumentNullException(nameof(agents));
            _modules = new List<IConvaiModule>(modules ?? Array.Empty<IConvaiModule>());
            _logger = logger.WithTag(nameof(ConvaiRuntime));
            _state = RuntimeState.Created;

            Transport = transport;
            Conversation = conversation;
            Config = config;
            RuntimePreferences = runtimePreferences;
            FeatureVariants = featureVariants;
            Persistence = persistence;
            Telemetry = telemetry;
            EndUserIdentityProvider = endUserIdentityProvider;
            EndUserMetadataProvider = endUserMetadataProvider;
        }

        #region ModuleContext Implementation

        private sealed class ModuleContext : IModuleContext
        {
            private readonly Dictionary<Type, object> _services = new();

            public ModuleContext(ConvaiRuntime runtime)
            {
                Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            #region Runtime Reference

            public ConvaiRuntime Runtime { get; }

            #endregion

            #region Typed Properties (Preferred)

            /// <inheritdoc />
            public IEventHub Events => Runtime.Events;

            /// <inheritdoc />
            public IAgentRegistry Agents => Runtime.Agents;

            /// <inheritdoc />
            public ITransportProvider Transport => Runtime.Transport;

            /// <inheritdoc />
            public IRuntimePreferences Preferences => Runtime.RuntimePreferences;

            /// <inheritdoc />
            public ILogger Logger => Runtime._logger;

            /// <inheritdoc />
            public IConvaiRoomAudioService RoomAudio
            {
                get
                {
                    TryGetModuleService(out IConvaiRoomAudioService service);
                    return service;
                }
            }

            /// <inheritdoc />
            public ICredentialProvider Credentials
            {
                get
                {
                    TryGetModuleService(out ICredentialProvider service);
                    return service;
                }
            }

            #endregion

            #region Generic Service Access

            public bool TryGetModuleService<TService>(out TService service) where TService : class
            {
                if (_services.TryGetValue(typeof(TService), out object obj))
                {
                    service = (TService)obj;
                    return true;
                }

                service = default;
                return false;
            }

            public void ProvideModuleService<TService>(TService instance) where TService : class
            {
                if (instance == null)
                    throw new ArgumentNullException(nameof(instance));

                _services[typeof(TService)] = instance;
            }

            #endregion
        }

        #endregion

        #region Typed Dependencies (NO Service Locator)

        /// <summary>
        ///     Current runtime state.
        /// </summary>
        public RuntimeState State
        {
            get
            {
                lock (_stateLock)
                    return _state;
            }
        }

        /// <summary>
        ///     Room runtime for connection, audio, and ownership management.
        /// </summary>
        public IRoomRuntime Room { get; }

        /// <summary>
        ///     Event hub for decoupled communication.
        /// </summary>
        public IEventHub Events { get; }

        /// <summary>
        ///     Agent registry for character and player management.
        /// </summary>
        public IAgentRegistry Agents { get; }

        /// <summary>
        ///     Registered modules.
        /// </summary>
        public IReadOnlyList<IConvaiModule> Modules => _modules.AsReadOnly();

        #endregion

        #region Providers (RC-2: All builder fields consumed)

        /// <summary>
        ///     Transport provider for platform-specific communication.
        /// </summary>
        public ITransportProvider Transport { get; }

        /// <summary>
        ///     Conversation provider for AI backend communication.
        /// </summary>
        public IConversationProvider Conversation { get; }

        /// <summary>
        ///     Bootstrap configuration snapshot (immutable).
        /// </summary>
        public ConvaiBootstrapConfigSnapshot Config { get; }

        /// <summary>
        ///     Mutable runtime preferences.
        /// </summary>
        public IRuntimePreferences RuntimePreferences { get; }

        /// <summary>
        ///     Feature variant provider for A/B testing.
        /// </summary>
        public IFeatureVariantProvider FeatureVariants { get; }

        /// <summary>
        ///     Persistence provider for data storage.
        /// </summary>
        public IPersistenceProvider Persistence { get; }

        /// <summary>
        ///     Telemetry provider for analytics.
        /// </summary>
        public ITelemetryProvider Telemetry { get; }

        /// <summary>
        ///     Optional end-user identity provider configured for this runtime.
        /// </summary>
        public IEndUserIdentityProvider EndUserIdentityProvider { get; }

        /// <summary>
        ///     Optional end-user metadata provider configured for this runtime.
        /// </summary>
        public IEndUserMetadataProvider EndUserMetadataProvider { get; }

        #endregion

        #region Lifecycle Operations

        /// <summary>
        ///     Starts the runtime and all registered modules.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Task that completes when startup is finished.</returns>
        public IConvaiOperation<Unit> StartAsync(CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(StartAsyncCore(ct));

        private async Task<Unit> StartAsyncCore(CancellationToken ct)
        {
            RuntimeState previousState;
            lock (_stateLock)
            {
                if (_state != RuntimeState.Created)
                {
                    throw new InvalidOperationException(
                        $"Cannot start runtime in state {_state}. Runtime must be in Created state.");
                }

                previousState = _state;
                _state = RuntimeState.Starting;
            }

            PublishStateChanged(previousState, RuntimeState.Starting);
            _logger?.Info("Starting...");

            try
            {
                IModuleContext context = GetOrCreateModuleContext();

                // Register module services in dependency order
                foreach (IConvaiModule module in _modules)
                {
                    ct.ThrowIfCancellationRequested();
                    _logger?.Debug($"Registering module services: {module.ModuleId}");
                    await module.RegisterAsync(context, ct);
                }

                // Start modules in dependency order
                foreach (IConvaiModule module in _modules)
                {
                    ct.ThrowIfCancellationRequested();
                    _logger?.Debug($"Starting module: {module.ModuleId}");
                    await module.StartAsync(context, ct);
                }

                lock (_stateLock)
                {
                    previousState = _state;
                    _state = RuntimeState.Running;
                }

                PublishStateChanged(previousState, RuntimeState.Running);
                _logger?.Info("Started successfully");
                return Unit.Value;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Start failed: {ex.Message}");
                lock (_stateLock)
                    _state = RuntimeState.Stopped;
                throw;
            }
        }

        /// <summary>
        ///     Pauses the runtime and all modules.
        /// </summary>
        public IConvaiOperation<Unit> PauseAsync(RuntimePauseReason reason, CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(PauseAsyncCore(reason, ct));

        private async Task<Unit> PauseAsyncCore(RuntimePauseReason reason, CancellationToken ct)
        {
            RuntimeState previousState;
            lock (_stateLock)
            {
                if (_state != RuntimeState.Running)
                {
                    throw new InvalidOperationException(
                        $"Cannot pause runtime in state {_state}. Runtime must be Running.");
                }

                previousState = _state;
                _state = RuntimeState.Pausing;
                _lastPauseReason = reason;
            }

            PublishStateChanged(previousState, RuntimeState.Pausing, reason);
            _logger?.Info($"Pausing (reason: {reason})...");

            try
            {
                // Pause modules in reverse order
                for (int i = _modules.Count - 1; i >= 0; i--)
                {
                    ct.ThrowIfCancellationRequested();
                    await _modules[i].PauseAsync(reason, ct);
                }

                lock (_stateLock)
                {
                    previousState = _state;
                    _state = RuntimeState.Paused;
                }

                PublishStateChanged(previousState, RuntimeState.Paused, reason);
                _logger?.Info("Paused");
                return Unit.Value;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Pause failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        ///     Resumes the runtime from pause.
        /// </summary>
        public IConvaiOperation<Unit> ResumeAsync(CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(ResumeAsyncCore(ct));

        private async Task<Unit> ResumeAsyncCore(CancellationToken ct)
        {
            RuntimeState previousState;
            lock (_stateLock)
            {
                if (_state != RuntimeState.Paused)
                {
                    throw new InvalidOperationException(
                        $"Cannot resume runtime in state {_state}. Runtime must be Paused.");
                }

                previousState = _state;
                _state = RuntimeState.Resuming;
            }

            PublishStateChanged(previousState, RuntimeState.Resuming);
            _logger?.Info("Resuming...");

            try
            {
                // Resume modules in original order
                foreach (IConvaiModule module in _modules)
                {
                    ct.ThrowIfCancellationRequested();
                    await module.ResumeAsync(ct);
                }

                lock (_stateLock)
                {
                    previousState = _state;
                    _state = RuntimeState.Running;
                    _lastPauseReason = RuntimePauseReason.None;
                }

                PublishStateChanged(previousState, RuntimeState.Running);
                _logger?.Info("Resumed");
                return Unit.Value;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Resume failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        ///     Stops the runtime and all modules.
        /// </summary>
        public IConvaiOperation<Unit> StopAsync(CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(StopAsyncCore(ct));

        private async Task<Unit> StopAsyncCore(CancellationToken ct)
        {
            RuntimeState previousState;
            lock (_stateLock)
            {
                if (_state == RuntimeState.Stopped || _state == RuntimeState.Disposed)
                    return Unit.Value;

                if (_state == RuntimeState.Created)
                {
                    _state = RuntimeState.Stopped;
                    return Unit.Value;
                }

                previousState = _state;
                _state = RuntimeState.Stopping;
            }

            PublishStateChanged(previousState, RuntimeState.Stopping);
            _logger?.Info("Stopping...");

            try
            {
                // Stop modules in reverse order
                for (int i = _modules.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        await _modules[i].StopAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Error stopping module {_modules[i].ModuleId}: {ex.Message}");
                    }
                }

                lock (_stateLock)
                {
                    previousState = _state;
                    _state = RuntimeState.Stopped;
                }

                PublishStateChanged(previousState, RuntimeState.Stopped);
                _logger?.Info("Stopped");
                return Unit.Value;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Stop failed: {ex.Message}");
                lock (_stateLock)
                    _state = RuntimeState.Stopped;
                throw;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            lock (_stateLock)
            {
                if (_state == RuntimeState.Disposed)
                    return;
            }

            // Stop if not already stopped
            if (State != RuntimeState.Stopped && State != RuntimeState.Created)
            {
                try
                {
                    await StopAsync();
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Error during stop in dispose: {ex.Message}");
                }
            }

            // Dispose room runtime if disposable
            Room?.Shutdown();

            lock (_stateLock)
                _state = RuntimeState.Disposed;

            _logger?.Debug("Disposed");
        }

        #endregion

        #region Internal Helpers

        private void PublishStateChanged(RuntimeState previous, RuntimeState current,
            RuntimePauseReason reason = RuntimePauseReason.None) =>
            Events?.Publish(new RuntimeStateChanged(previous, current, reason));

        private IModuleContext GetOrCreateModuleContext()
        {
            _moduleContext ??= new ModuleContext(this);
            return _moduleContext;
        }

        /// <summary>
        ///     Pre-registers a service into the shared module context.
        ///     Called by the composition root (ConvaiManager) to make container-owned services
        ///     available to modules before <see cref="StartAsync" /> runs.
        /// </summary>
        /// <typeparam name="TService">Service type to register.</typeparam>
        /// <param name="instance">Service instance.</param>
        internal void RegisterModuleService<TService>(TService instance) where TService : class
        {
            if (instance == null) return;
            GetOrCreateModuleContext().ProvideModuleService(instance);
        }

        /// <summary>
        ///     Adds late-discovered modules to the runtime before it starts.
        ///     Called by <see cref="ConvaiManager" /> in Start() after all Awake() calls
        ///     have completed, ensuring modules that self-register at default execution order
        ///     are captured before <see cref="StartAsync" /> runs.
        /// </summary>
        /// <param name="modules">Modules to add. Duplicates (by ModuleId) are skipped.</param>
        /// <exception cref="InvalidOperationException">If runtime is not in Created state.</exception>
        internal void AddModules(IReadOnlyList<IConvaiModule> modules)
        {
            lock (_stateLock)
            {
                if (_state != RuntimeState.Created)
                {
                    throw new InvalidOperationException(
                        $"Cannot add modules in state {_state}. Runtime must be in Created state.");
                }
            }

            if (modules == null || modules.Count == 0) return;

            var existingIds = new HashSet<string>();
            foreach (IConvaiModule existing in _modules)
                existingIds.Add(existing.ModuleId);

            foreach (IConvaiModule module in modules)
            {
                if (module != null && existingIds.Add(module.ModuleId))
                {
                    _modules.Add(module);
                    _logger?.Debug($"Late-discovered module added: {module.ModuleId}");
                }
            }
        }

        /// <summary>
        ///     Adds and starts a module discovered after the runtime has already started.
        ///     Runtime-instantiated character features such as lip sync use this path.
        /// </summary>
        internal IConvaiOperation<Unit> AddAndStartModuleAsync(
            IConvaiModule module,
            CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(AddAndStartModuleAsyncCore(module, ct));

        private async Task<Unit> AddAndStartModuleAsyncCore(
            IConvaiModule module,
            CancellationToken ct)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));

            lock (_stateLock)
            {
                if (_state != RuntimeState.Running)
                {
                    throw new InvalidOperationException(
                        $"Cannot dynamically start module '{module.ModuleId}' in state {_state}. " +
                        "Runtime must be running.");
                }

                if (_modules.Contains(module)) return Unit.Value;
                _modules.Add(module);
            }

            try
            {
                IModuleContext context = GetOrCreateModuleContext();
                ct.ThrowIfCancellationRequested();
                await module.RegisterAsync(context, ct);
                ct.ThrowIfCancellationRequested();
                await module.StartAsync(context, ct);
                _logger?.Debug($"Runtime module started dynamically: {module.ModuleId}");
                return Unit.Value;
            }
            catch
            {
                lock (_stateLock)
                    _modules.Remove(module);
                throw;
            }
        }

        /// <summary>Stops and removes a module that was destroyed while the runtime is active.</summary>
        internal IConvaiOperation<Unit> StopAndRemoveModuleAsync(
            IConvaiModule module,
            CancellationToken ct = default) =>
            ConvaiOperation<Unit>.FromTask(StopAndRemoveModuleAsyncCore(module, ct));

        private async Task<Unit> StopAndRemoveModuleAsyncCore(
            IConvaiModule module,
            CancellationToken ct)
        {
            if (module == null) return Unit.Value;

            bool shouldStop;
            lock (_stateLock)
            {
                if (!_modules.Remove(module)) return Unit.Value;
                shouldStop = _state is RuntimeState.Running or RuntimeState.Paused;
            }

            if (shouldStop)
                await module.StopAsync(ct);

            return Unit.Value;
        }

        #endregion
    }
}
