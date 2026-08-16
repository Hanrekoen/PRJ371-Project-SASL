using System;
using LiveKit;
using TransportTrackKind = Convai.Infrastructure.Networking.Transport.TrackKind;

// ReSharper disable once CheckNamespace
namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="ILocalVideoTrack" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, video tracks are managed through the browser's getUserMedia or getDisplayMedia APIs.
    ///         This wrapper provides the abstraction interface but actual video capture is handled by the browser.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLLocalVideoTrack : ILocalVideoTrack
    {
        #region Constructor

        /// <summary>
        ///     Creates a new WebGL local video track wrapper.
        /// </summary>
        /// <param name="source">The video source associated with this track.</param>
        /// <param name="isScreenShare">Whether this is a screen share track.</param>
        /// <param name="mediaStreamTrack">Optional browser media track used for publish/unpublish routing.</param>
        /// <param name="sid">Optional transport SID when available.</param>
        public WebGLLocalVideoTrack(
            IVideoSource source,
            bool isScreenShare = false,
            MediaStreamTrack mediaStreamTrack = null,
            string sid = null)
        {
            Source = source;
            IsScreenShare = isScreenShare;
            MediaStreamTrack = mediaStreamTrack;
            Sid = string.IsNullOrWhiteSpace(sid) ? $"webgl-video-{Guid.NewGuid():N}" : sid;
        }

        #endregion

        #region ILocalVideoTrack Properties

        /// <inheritdoc />
        public IVideoSource Source { get; }

        #endregion

        #region ITrack Properties

        /// <inheritdoc />
        public string Sid { get; }

        /// <inheritdoc />
        public string Name => Source?.Name ?? (IsScreenShare ? "screen" : "camera");

        /// <inheritdoc />
        public TransportTrackKind Kind => TransportTrackKind.Video;

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

        #region Internal Properties & Methods

        /// <summary>
        ///     Gets whether this is a screen share track.
        /// </summary>
        internal bool IsScreenShare { get; }

        /// <summary>
        ///     Gets the underlying browser media track when this track was published from canvas capture.
        /// </summary>
        internal MediaStreamTrack MediaStreamTrack { get; }

        /// <summary>
        ///     Marks this track as unpublished.
        /// </summary>
        internal void MarkUnpublished() => IsPublished = false;

        #endregion
    }
}
