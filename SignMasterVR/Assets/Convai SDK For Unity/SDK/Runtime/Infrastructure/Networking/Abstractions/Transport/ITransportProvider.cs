namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Provider for platform-specific transport and all related factories.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Usage</b>: Injected into <c>ConvaiRuntimeBuilder</c> via <c>UseTransport(provider)</c>.
    ///         Platform-specific implementations (NativeTransportProvider, WebGLTransportProvider) are
    ///         selected at build time or runtime.
    ///     </para>
    ///     <para>
    ///         <b>Implementation Note</b>: Implementations should be singletons for the lifetime
    ///         of the application. Factories create new instances per request.
    ///     </para>
    ///     <para>
    ///         This interface is the single source of truth for all platform factories.
    ///         ConvaiManager and ConvaiRoomManager should get all factories from this
    ///         provider rather than constructing platform services ad hoc.
    ///     </para>
    /// </remarks>
    public interface ITransportProvider
    {
        /// <summary>
        ///     Gets the static capabilities of this transport platform.
        /// </summary>
        public TransportCapabilities Capabilities { get; }

        /// <summary>
        ///     Creates a new transport instance for real-time communication.
        /// </summary>
        /// <returns>A new transport instance.</returns>
        public IRealtimeTransport CreateTransport();

        /// <summary>
        ///     Creates a factory for microphone sources.
        /// </summary>
        /// <returns>A microphone source factory.</returns>
        public IMicrophoneSourceFactory CreateMicrophoneFactory();

        /// <summary>
        ///     Creates a factory for audio streams (remote playback).
        /// </summary>
        /// <returns>An audio stream factory.</returns>
        public IAudioStreamFactory CreateAudioStreamFactory();

        /// <summary>
        ///     Creates a factory for video sources (camera capture, texture sources).
        /// </summary>
        /// <returns>A video source factory.</returns>
        public IVideoSourceFactory CreateVideoSourceFactory();

        /// <summary>
        ///     Creates a factory for room controllers.
        /// </summary>
        /// <returns>A room controller factory.</returns>
        public IConvaiRoomControllerFactory CreateRoomControllerFactory();
    }
}
