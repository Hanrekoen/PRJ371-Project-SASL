using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Configuration;
using Convai.Runtime.Room;

namespace Convai.Runtime.Core.Modules
{
    /// <summary>
    ///     Extension point for Convai SDK modules.
    ///     Modules provide optional functionality (vision, lip-sync, narrative) with explicit lifecycle.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Modules are registered with <see cref="ConvaiRuntimeBuilder" /> and participate
    ///         in the runtime lifecycle. They can depend on other modules and services.
    ///     </para>
    ///     <para>
    ///         <b>Lifecycle</b>: Modules are started in dependency order and stopped in reverse order.
    ///         Each lifecycle method receives an <see cref="IModuleContext" /> with typed dependencies.
    ///     </para>
    /// </remarks>
    public interface IConvaiModule
    {
        /// <summary>
        ///     Unique identifier for this module.
        /// </summary>
        public string ModuleId { get; }

        /// <summary>
        ///     Human-readable name for display purposes.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        ///     Module IDs that this module depends on (must be started first).
        /// </summary>
        public IReadOnlyList<string> RequiredModules { get; }

        /// <summary>
        ///     Service types that this module requires from the runtime.
        /// </summary>
        public IReadOnlyList<Type> RequiredServices { get; }

        /// <summary>
        ///     Service types that this module provides to other modules.
        /// </summary>
        public IReadOnlyList<Type> ProvidedServices { get; }

        /// <summary>
        ///     Whether this module is currently active.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>
        ///     Called during runtime build to register module services.
        /// </summary>
        public ValueTask RegisterAsync(IModuleContext context, CancellationToken ct = default);

        /// <summary>
        ///     Called when the runtime starts to activate the module.
        /// </summary>
        public ValueTask StartAsync(IModuleContext context, CancellationToken ct = default);

        /// <summary>
        ///     Called when the runtime pauses.
        /// </summary>
        public ValueTask PauseAsync(RuntimePauseReason reason, CancellationToken ct = default);

        /// <summary>
        ///     Called when the runtime resumes from pause.
        /// </summary>
        public ValueTask ResumeAsync(CancellationToken ct = default);

        /// <summary>
        ///     Called when the runtime stops to deactivate the module.
        /// </summary>
        public ValueTask StopAsync(CancellationToken ct = default);
    }

    /// <summary>
    ///     Context provided to modules during lifecycle operations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Prefer typed properties such as <see cref="Events" />, <see cref="Agents" />, and <see cref="Transport" />
    ///         for core runtime dependencies.
    ///     </para>
    ///     <para>
    ///         Use <see cref="TryGetModuleService{TService}" /> only for optional services shared by other modules
    ///         or pre-populated by the composition root.
    ///     </para>
    /// </remarks>
    public interface IModuleContext
    {
        #region Runtime Reference

        /// <summary>
        ///     The runtime this module belongs to.
        /// </summary>
        public ConvaiRuntime Runtime { get; }

        #endregion

        #region Typed Properties (Preferred)

        /// <summary>
        ///     Event hub for module and runtime event flow.
        /// </summary>
        /// <remarks>
        ///     Always available. Use for publishing and subscribing to domain events.
        /// </remarks>
        public IEventHub Events { get; }

        /// <summary>
        ///     Agent registry for character and player management.
        /// </summary>
        /// <remarks>
        ///     Always available. Use for querying registered characters and players.
        /// </remarks>
        public IAgentRegistry Agents { get; }

        /// <summary>
        ///     Transport provider for platform-specific communication.
        /// </summary>
        /// <remarks>
        ///     May be null if no transport is configured. Check before use.
        /// </remarks>
        public ITransportProvider Transport { get; }

        /// <summary>
        ///     Mutable runtime preferences.
        /// </summary>
        /// <remarks>
        ///     May be null if no preferences are configured. Check before use.
        /// </remarks>
        public IRuntimePreferences Preferences { get; }

        /// <summary>
        ///     Logger for module diagnostics.
        /// </summary>
        /// <remarks>
        ///     May be null if no logger is configured. Always null-check before use.
        /// </remarks>
        public ILogger Logger { get; }

        /// <summary>
        ///     Room audio service for microphone and playback management.
        /// </summary>
        /// <remarks>
        ///     May be null if no room manager is configured. Check before use.
        ///     Pre-populated by the composition root from the active room manager.
        /// </remarks>
        public IConvaiRoomAudioService RoomAudio { get; }

        /// <summary>
        ///     Credential provider for API key and server URL resolution.
        /// </summary>
        /// <remarks>
        ///     May be null if no credential provider is registered. Check before use.
        ///     Pre-populated by the composition root from the active runtime host.
        /// </remarks>
        public ICredentialProvider Credentials { get; }

        #endregion

        #region Module Service Access (For Inter-Module/Optional Services)

        /// <summary>
        ///     Tries to get an optional module service of the specified type.
        /// </summary>
        /// <typeparam name="TService">Type of service to get.</typeparam>
        /// <param name="service">The service instance if found.</param>
        /// <returns>True if the service was found, false otherwise.</returns>
        /// <remarks>
        ///     Prefer typed properties for core runtime services. Use this method for optional cross-module services only.
        /// </remarks>
        public bool TryGetModuleService<TService>(out TService service) where TService : class;

        /// <summary>
        ///     Registers a module service that this module provides.
        /// </summary>
        /// <typeparam name="TService">Type of service to register.</typeparam>
        /// <param name="instance">The service instance.</param>
        public void ProvideModuleService<TService>(TService instance) where TService : class;

        #endregion
    }
}
