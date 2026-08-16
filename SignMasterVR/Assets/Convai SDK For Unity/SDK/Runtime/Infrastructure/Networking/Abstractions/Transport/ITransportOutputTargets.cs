namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Unity-free abstraction for an audio output target.
    ///     Allows transport code to work with audio outputs without depending on
    ///     <c>UnityEngine.AudioSource</c> directly.
    /// </summary>
    /// <remarks>
    ///     Consumers that already have an <c>AudioSource</c> can continue using the
    ///     existing Unity-typed convenience methods on <see cref="IRemoteAudioTrack" />
    ///     and <see cref="IAudioStreamFactory" />.
    /// </remarks>
    public interface IAudioOutputTarget
    {
        /// <summary>
        ///     Gets a unique identifier for this output target (e.g. instance ID).
        /// </summary>
        public string TargetId { get; }

        /// <summary>
        ///     Gets whether this target is still valid and can receive audio.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        ///     Gets or sets the playback volume (0.0 to 1.0).
        /// </summary>
        public float Volume { get; set; }

        /// <summary>
        ///     Gets or sets the muted state of the output target.
        /// </summary>
        public bool IsMuted { get; set; }
    }

    /// <summary>
    ///     Unity-free abstraction for a render output target.
    ///     Allows frame delivery without depending on <c>UnityEngine.RenderTexture</c> directly.
    ///     </remarks>
    public interface IRenderOutputTarget
    {
        /// <summary>
        ///     Gets a unique identifier for this render target.
        /// </summary>
        public string TargetId { get; }

        /// <summary>
        ///     Gets whether this target is still valid and can receive frames.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        ///     Gets the width of the render target in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        ///     Gets the height of the render target in pixels.
        /// </summary>
        public int Height { get; }
    }

    /// <summary>
    ///     Unity-free abstraction for an object that can host runtime components
    ///     (e.g. microphone source, audio stream).
    ///     Allows transport code to work without referencing <c>UnityEngine.GameObject</c> directly.
    /// </summary>
    /// <remarks>
    ///     Typically backed by a host object capable of creating or attaching runtime components.
    /// </remarks>
    public interface IComponentHost
    {
        /// <summary>
        ///     Gets a unique identifier for this host (e.g. instance ID).
        /// </summary>
        public string HostId { get; }

        /// <summary>
        ///     Gets the display name of the host.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets whether this host is still valid and active.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        ///     Gets whether this host is currently active in the scene hierarchy.
        /// </summary>
        public bool IsActive { get; }
    }
}
