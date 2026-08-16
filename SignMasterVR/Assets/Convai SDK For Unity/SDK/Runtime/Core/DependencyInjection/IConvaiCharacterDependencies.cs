using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Room;

namespace Convai.Runtime.Core.DependencyInjection
{
    /// <summary>
    ///     Typed dependency bundle for <see cref="Convai.Runtime.Components.ConvaiCharacter" /> components.
    ///     Provides compile-time safety for dependency injection.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Components implement <c>IInjectable&lt;IConvaiCharacterDependencies&gt;</c>
    ///         for typed dependency injection.
    ///     </para>
    ///     <para>
    ///         Required dependencies throw if null; optional dependencies may be null.
    ///     </para>
    /// </remarks>
    public interface IConvaiCharacterDependencies
    {
        /// <summary>
        ///     Event hub for publishing and subscribing to domain events.
        /// </summary>
        /// <remarks>Required dependency. Must not be null.</remarks>
        public IEventHub EventHub { get; }

        /// <summary>
        ///     Room connection service for managing character connections.
        /// </summary>
        /// <remarks>Required dependency. Must not be null.</remarks>
        public IConvaiRoomConnectionService ConnectionService { get; }

        /// <summary>
        ///     Room audio service for managing audio playback and preferences.
        /// </summary>
        /// <remarks>Required dependency. Must not be null.</remarks>
        public IConvaiRoomAudioService AudioService { get; }

        /// <summary>
        ///     Agent registry for character and player tracking.
        /// </summary>
        /// <remarks>
        ///     Optional dependency. May be null if self-registration via
        ///     <see cref="IAgentRegistry" /> is the primary discovery mechanism.
        /// </remarks>
        public IAgentRegistry AgentRegistry { get; }

        /// <summary>
        ///     Logger for diagnostic output.
        /// </summary>
        /// <remarks>Optional dependency. May be null.</remarks>
        public ILogger Logger { get; }
    }
}
