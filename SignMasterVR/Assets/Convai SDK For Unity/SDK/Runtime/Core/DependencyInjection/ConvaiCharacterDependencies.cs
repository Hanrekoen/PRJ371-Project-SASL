using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Room;

namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Concrete implementation of <see cref="IConvaiCharacterDependencies" />.
    ///     Created from the manager-owned service graph during runtime binding.
    /// </summary>
    internal sealed class ConvaiCharacterDependencies : IConvaiCharacterDependencies
    {
        /// <summary>
        ///     Creates a new dependency bundle with all required and optional dependencies.
        /// </summary>
        /// <param name="eventHub">Required: Event hub for domain events.</param>
        /// <param name="connectionService">Required: Room connection service.</param>
        /// <param name="audioService">Required: Room audio service.</param>
        /// <param name="agentRegistry">Optional: Agent registry for tracking.</param>
        /// <param name="logger">Optional: Logger for diagnostics.</param>
        public ConvaiCharacterDependencies(
            IEventHub eventHub,
            IConvaiRoomConnectionService connectionService,
            IConvaiRoomAudioService audioService,
            IAgentRegistry agentRegistry = null,
            ILogger logger = null)
        {
            EventHub = eventHub;
            ConnectionService = connectionService;
            AudioService = audioService;
            AgentRegistry = agentRegistry;
            Logger = logger.WithTag("ConvaiCharacter");
        }

        /// <inheritdoc />
        public IEventHub EventHub { get; }

        /// <inheritdoc />
        public IConvaiRoomConnectionService ConnectionService { get; }

        /// <inheritdoc />
        public IConvaiRoomAudioService AudioService { get; }

        /// <inheritdoc />
        public IAgentRegistry AgentRegistry { get; }

        /// <inheritdoc />
        public ILogger Logger { get; }
    }
}
