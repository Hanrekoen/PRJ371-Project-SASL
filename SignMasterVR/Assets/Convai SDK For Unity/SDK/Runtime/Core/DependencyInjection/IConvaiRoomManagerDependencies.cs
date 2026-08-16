using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Services;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Core.Configuration;
using Convai.Shared.Abstractions;
using ISessionPersistence = Convai.Domain.Abstractions.ISessionPersistence;

namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Typed dependency bundle for <see cref="Convai.Runtime.Adapters.Networking.ConvaiRoomManager" />.
    ///     Provides compile-time safety for dependency injection without handing a raw
    ///     locator or container into the component.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Provides the coordinator architecture to ConvaiRoomManager, enabling it to
    ///         function as a thin facade delegating to coordinators.
    ///     </para>
    ///     <para>
    ///         <b>Target State:</b> ConvaiRoomManager becomes a thin adapter that delegates
    ///         all business logic to these coordinators. The coordinators are instantiated
    ///         by <c>ConvaiRuntimeBuilder</c> and composed into <c>RoomRuntime</c>.
    ///     </para>
    /// </remarks>
    internal interface IConvaiRoomManagerDependencies
    {
        #region Platform Providers

        /// <summary>
        ///     Transport provider for platform-specific factories.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Provides access to <see cref="ITransportProvider.CreateMicrophoneFactory" />,
        ///         <see cref="ITransportProvider.CreateRoomControllerFactory" />, and other
        ///         platform-specific factory methods.
        ///     </para>
        ///     <para>
        ///         Optional. When present, the room manager uses it as the authoritative source
        ///         for platform-specific factories.
        ///     </para>
        /// </remarks>
        public ITransportProvider TransportProvider { get; }

        #endregion

        #region Core Services

        /// <summary>
        ///     Event hub for publishing and subscribing to domain events.
        /// </summary>
        public IEventHub EventHub { get; }

        /// <summary>
        ///     Agent registry shared with the runtime.
        /// </summary>
        public IAgentRegistry AgentRegistry { get; }

        /// <summary>
        ///     Logger for diagnostic output.
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        ///     Session persistence service.
        /// </summary>
        public ISessionPersistence SessionPersistence { get; }

        /// <summary>
        ///     Credential provider for runtime authentication.
        /// </summary>
        public ICredentialProvider CredentialProvider { get; }

        /// <summary>
        ///     End-user identity provider for transport/session metadata.
        /// </summary>
        public IEndUserIdentityProvider EndUserIdentityProvider { get; }

        /// <summary>
        ///     Optional end-user metadata provider for transport/session metadata.
        /// </summary>
        public IEndUserMetadataProvider EndUserMetadataProvider { get; }

        /// <summary>
        ///     Session state machine for connection state tracking.
        /// </summary>
        public ISessionStateMachine SessionStateMachine { get; }

        /// <summary>
        ///     Session service for persistence/recovery coordination.
        /// </summary>
        public ISessionService SessionService { get; }

        /// <summary>
        ///     Optional narrative section resolver.
        /// </summary>
        public INarrativeSectionNameResolver SectionNameResolver { get; }

        /// <summary>
        ///     Ownership provider used for startup composition and rebind logic.
        /// </summary>
        public IRoomOwnershipProvider RoomOwnershipProvider { get; }

        /// <summary>
        ///     Runtime diagnostics bridge.
        /// </summary>
        public ConvaiRuntimeDiagnosticsService RuntimeDiagnosticsService { get; }

        /// <summary>
        ///     Runtime settings service.
        /// </summary>
        public IConvaiRuntimeSettingsService RuntimeSettingsService { get; }

        /// <summary>
        ///     Microphone device service used by runtime settings.
        /// </summary>
        public IMicrophoneDeviceService MicrophoneDeviceService { get; }

        #endregion
    }
}
