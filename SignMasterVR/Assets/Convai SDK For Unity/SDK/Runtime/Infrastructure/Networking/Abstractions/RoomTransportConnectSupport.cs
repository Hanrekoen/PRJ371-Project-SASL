using System;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.Runtime.Behaviors;
using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Prepared collaborator bundle for constructing an <see cref="RTVIHandler" /> while preserving
    ///     controller-local timing of when the handler is actually created.
    /// </summary>
    internal readonly struct PreparedRtviHandlerDependencies
    {
        private readonly IAgentRegistry _agentRegistry;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly IEventHub _eventHub;
        private readonly ProtocolGateway _gateway;
        private readonly LipSyncTransportOptions _lipSyncTransportOptions;
        private readonly ILogger _logger;
        private readonly IPlayerSession _playerSession;
        private readonly INarrativeSectionNameResolver _sectionNameResolver;
        private readonly IRealtimeTransport _transport;

        internal PreparedRtviHandlerDependencies(
            ProtocolGateway gateway,
            IRealtimeTransport transport,
            IAgentRegistry agentRegistry,
            IPlayerSession playerSession,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub = null,
            INarrativeSectionNameResolver sectionNameResolver = null,
            LipSyncTransportOptions lipSyncTransportOptions = default)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _playerSession = playerSession ?? throw new ArgumentNullException(nameof(playerSession));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventHub = eventHub;
            _sectionNameResolver = sectionNameResolver;
            _lipSyncTransportOptions = lipSyncTransportOptions;
        }

        public RTVIHandler CreateHandler() => new(
            _gateway,
            _transport,
            _agentRegistry,
            _playerSession,
            _dispatcher,
            _logger,
            _eventHub,
            _sectionNameResolver,
            _lipSyncTransportOptions);
    }

    /// <summary>
    ///     Normalized result of the immediate transport connect attempt.
    ///     Keeps publishable failure message text separate from controller-specific log prefixes.
    /// </summary>
    internal readonly struct TransportConnectOutcome
    {
        internal TransportConnectOutcome(bool connected, string failureMessage, string failureLogMessage)
        {
            if (!connected && string.IsNullOrWhiteSpace(failureMessage))
            {
                throw new ArgumentException(
                    "Failure message is required when the transport connect outcome is unsuccessful.",
                    nameof(failureMessage));
            }

            Connected = connected;
            FailureMessage = connected ? null : failureMessage;
            FailureLogMessage = connected
                ? null
                : string.IsNullOrWhiteSpace(failureLogMessage)
                    ? failureMessage
                    : failureLogMessage;
        }

        public bool Connected { get; }
        public string FailureMessage { get; }
        public string FailureLogMessage { get; }

        public string FormatFailureLogMessage(string prefix = null) => string.IsNullOrWhiteSpace(prefix) ||
                                                                       string.IsNullOrWhiteSpace(FailureLogMessage)
            ? FailureLogMessage
            : $"{prefix} {FailureLogMessage}";
    }

    /// <summary>
    ///     Shared helper for the post-room-details transport connect path used by both Native and WebGL controllers.
    ///     Keeps controller-local connect options, event emission, and user-gesture messaging outside this seam.
    /// </summary>
    internal static class RoomTransportConnectSupport
    {
        internal static PreparedRtviHandlerDependencies PrepareRtviHandlerDependencies(
            ProtocolGateway gateway,
            IRealtimeTransport transport,
            IAgentRegistry agentRegistry,
            IPlayerSession playerSession,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub = null,
            INarrativeSectionNameResolver sectionNameResolver = null,
            LipSyncTransportOptions lipSyncTransportOptions = default) => new(
            gateway,
            transport,
            agentRegistry,
            playerSession,
            dispatcher,
            logger,
            eventHub,
            sectionNameResolver,
            lipSyncTransportOptions);

        internal static TransportConnectOutcome FromConnectResult(
            bool connected,
            string failureMessage,
            string failureLogMessage = null) => connected
            ? new TransportConnectOutcome(true, null, null)
            : new TransportConnectOutcome(false, failureMessage, failureLogMessage);

        internal static TransportConnectOutcome FromException(
            Exception exception,
            string failureLogMessagePrefix = null)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            string failureMessage = exception.Message;
            string failureLogMessage = string.IsNullOrWhiteSpace(failureLogMessagePrefix)
                ? failureMessage
                : $"{failureLogMessagePrefix}: {failureMessage}";
            return new TransportConnectOutcome(false, failureMessage, failureLogMessage);
        }
    }
}
