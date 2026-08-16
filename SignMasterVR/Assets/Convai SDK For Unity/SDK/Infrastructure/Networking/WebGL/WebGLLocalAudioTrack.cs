using System;
using Convai.Infrastructure.Networking.Transport;

// ReSharper disable once CheckNamespace
namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="ILocalAudioTrack" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, audio tracks are managed internally by the LiveKit SDK through the browser's
    ///         getUserMedia API. This wrapper provides the abstraction interface but the actual
    ///         audio capture is handled by the browser.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLLocalAudioTrack : ILocalAudioTrack
    {
        #region Constructor

        /// <summary>
        ///     Creates a new WebGL local audio track wrapper.
        /// </summary>
        /// <param name="source">The audio source associated with this track.</param>
        public WebGLLocalAudioTrack(IAudioSource source)
        {
            Source = source;
            Sid = $"webgl-audio-{Guid.NewGuid():N}";
        }

        #endregion

        #region ILocalAudioTrack Properties

        /// <inheritdoc />
        public IAudioSource Source { get; }

        #endregion

        #region Internal Methods

        /// <summary>
        ///     Marks this track as unpublished.
        /// </summary>
        internal void MarkUnpublished() => IsPublished = false;

        #endregion

        #region ITrack Properties

        /// <inheritdoc />
        public string Sid { get; }

        /// <inheritdoc />
        public string Name => Source?.Name ?? "microphone";

        /// <inheritdoc />
        public TrackKind Kind => TrackKind.Audio;

        /// <inheritdoc />
        public bool IsMuted { get; private set; }

        /// <inheritdoc />
        public event Action<bool> MuteChanged;

        #endregion

        #region ILocalTrack Properties & Methods

        /// <inheritdoc />
        public bool IsPublished { get; private set; } = true;

        /// <inheritdoc />
        public void SetMuted(bool muted)
        {
            if (IsMuted == muted) return;
            IsMuted = muted;
            MuteChanged?.Invoke(muted);
        }

        #endregion
    }
}
