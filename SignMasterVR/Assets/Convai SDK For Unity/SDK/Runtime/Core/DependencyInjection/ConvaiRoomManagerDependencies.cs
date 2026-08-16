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
    ///     Concrete implementation of <see cref="IConvaiRoomManagerDependencies" />.
    ///     Created from the manager-owned service graph during runtime binding.
    /// </summary>
    internal sealed class ConvaiRoomManagerDependencies : IConvaiRoomManagerDependencies
    {
        /// <summary>
        ///     Creates a new dependency bundle with the runtime services required by
        ///     <see cref="Convai.Runtime.Adapters.Networking.ConvaiRoomManager" />.
        /// </summary>
        /// <param name="eventHub">Required: Event hub for domain events.</param>
        /// <param name="agentRegistry">Required: Shared agent registry.</param>
        /// <param name="logger">Optional: Logger for diagnostics.</param>
        /// <param name="sessionPersistence">Optional: Session persistence service.</param>
        /// <param name="credentialProvider">Optional: Runtime credential provider.</param>
        /// <param name="endUserIdentityProvider">Optional: End-user identity provider.</param>
        /// <param name="sessionStateMachine">Optional: Session state machine.</param>
        /// <param name="sessionService">Optional: Session service.</param>
        /// <param name="sectionNameResolver">Optional: Narrative section resolver.</param>
        /// <param name="roomOwnershipProvider">Optional: Room ownership provider.</param>
        /// <param name="runtimeDiagnosticsService">Optional: Runtime diagnostics service.</param>
        /// <param name="runtimeSettingsService">Optional: Runtime settings service.</param>
        /// <param name="microphoneDeviceService">Optional: Microphone device service.</param>
        /// <param name="transportProvider">Optional: Transport provider for platform factories.</param>
        public ConvaiRoomManagerDependencies(
            IEventHub eventHub,
            ILogger logger = null,
            IAgentRegistry agentRegistry = null,
            ISessionPersistence sessionPersistence = null,
            ICredentialProvider credentialProvider = null,
            IEndUserIdentityProvider endUserIdentityProvider = null,
            IEndUserMetadataProvider endUserMetadataProvider = null,
            ISessionStateMachine sessionStateMachine = null,
            ISessionService sessionService = null,
            INarrativeSectionNameResolver sectionNameResolver = null,
            IRoomOwnershipProvider roomOwnershipProvider = null,
            ConvaiRuntimeDiagnosticsService runtimeDiagnosticsService = null,
            IConvaiRuntimeSettingsService runtimeSettingsService = null,
            IMicrophoneDeviceService microphoneDeviceService = null,
            ITransportProvider transportProvider = null)
        {
            EventHub = eventHub;
            AgentRegistry = agentRegistry;
            Logger = logger;
            SessionPersistence = sessionPersistence;
            CredentialProvider = credentialProvider;
            EndUserIdentityProvider = endUserIdentityProvider;
            EndUserMetadataProvider = endUserMetadataProvider;
            SessionStateMachine = sessionStateMachine;
            SessionService = sessionService;
            SectionNameResolver = sectionNameResolver;
            RoomOwnershipProvider = roomOwnershipProvider;
            RuntimeDiagnosticsService = runtimeDiagnosticsService;
            RuntimeSettingsService = runtimeSettingsService;
            MicrophoneDeviceService = microphoneDeviceService;
            TransportProvider = transportProvider;
        }

        /// <inheritdoc />
        public IEventHub EventHub { get; }

        /// <inheritdoc />
        public IAgentRegistry AgentRegistry { get; }

        /// <inheritdoc />
        public ILogger Logger { get; }

        /// <inheritdoc />
        public ISessionPersistence SessionPersistence { get; }

        /// <inheritdoc />
        public ICredentialProvider CredentialProvider { get; }

        /// <inheritdoc />
        public IEndUserIdentityProvider EndUserIdentityProvider { get; }

        /// <inheritdoc />
        public IEndUserMetadataProvider EndUserMetadataProvider { get; }

        /// <inheritdoc />
        public ISessionStateMachine SessionStateMachine { get; }

        /// <inheritdoc />
        public ISessionService SessionService { get; }

        /// <inheritdoc />
        public INarrativeSectionNameResolver SectionNameResolver { get; }

        /// <inheritdoc />
        public IRoomOwnershipProvider RoomOwnershipProvider { get; }

        /// <inheritdoc />
        public ConvaiRuntimeDiagnosticsService RuntimeDiagnosticsService { get; }

        /// <inheritdoc />
        public IConvaiRuntimeSettingsService RuntimeSettingsService { get; }

        /// <inheritdoc />
        public IMicrophoneDeviceService MicrophoneDeviceService { get; }

        /// <inheritdoc />
        public ITransportProvider TransportProvider { get; }
    }
}
