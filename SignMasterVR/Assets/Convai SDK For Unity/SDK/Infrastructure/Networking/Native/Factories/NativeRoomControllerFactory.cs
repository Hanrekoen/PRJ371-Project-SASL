using System;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Behaviors;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Factory for creating <see cref="IConvaiRoomController" /> instances on native platforms.
    ///     Creates a <see cref="NativeRoomController" /> directly.
    /// </summary>
    internal sealed class NativeRoomControllerFactory : IConvaiRoomControllerFactory
    {
        private readonly Func<IRealtimeTransport> _createTransport;

        /// <summary>
        ///     Creates a new factory instance with the specified transport factory.
        /// </summary>
        /// <param name="createTransport">
        ///     Factory function that creates IRealtimeTransport instances.
        ///     Must be provided by the caller (typically an ITransportProvider).
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown if createTransport is null.</exception>
        internal NativeRoomControllerFactory(Func<IRealtimeTransport> createTransport)
        {
            _createTransport = createTransport ?? throw new ArgumentNullException(nameof(createTransport));
        }

        /// <inheritdoc />
        public IConvaiRoomController Create(
            IAgentRegistry agentRegistry,
            IPlayerSession playerSession,
            ITransportConfiguration transportConfiguration,
            ISessionPersistence sessionPersistence,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub,
            INarrativeSectionNameResolver sectionNameResolver = null)
        {
            logger = logger.WithTag(nameof(NativeRoomControllerFactory));
            IRealtimeTransport transport = _createTransport.Invoke();
            LogTransportOnlyPath(logger);

            return new NativeRoomController(
                agentRegistry,
                playerSession,
                transportConfiguration,
                sessionPersistence,
                dispatcher,
                logger,
                eventHub,
                sectionNameResolver,
                transport);
        }

        private static void LogTransportOnlyPath(ILogger logger)
        {
            if (logger == null) return;

            logger.Info(
                "Native runtime uses the transport-backed room controller.",
                LogCategory.Transport);
        }
    }
}
