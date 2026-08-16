using System;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using LiveKit;
using UnityEngine;
// Type alias to disambiguate LiveKit types from abstraction interfaces
using TransportTrackKind = Convai.Infrastructure.Networking.Transport.TrackKind;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IRemoteVideoTrack" /> wrapping the LiveKit WebGL RemoteTrack.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Key differences from NativeRemoteVideoTrack:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Video is rendered through browser HTML video elements</description>
    ///             </item>
    ///             <item>
    ///                 <description>AttachToRenderTexture is limited on WebGL due to browser restrictions</description>
    ///             </item>
    ///             <item>
    ///                 <description>Requires user gesture to start video playback in some browsers</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    internal sealed class WebGLRemoteVideoTrack : IRemoteVideoTrack
    {
        #region Private Fields

        private readonly WebGLRemoteParticipant _participant;

        #endregion

        #region Constructor

        /// <summary>
        ///     Creates a new WebGL remote video track wrapper.
        /// </summary>
        /// <param name="track">The LiveKit remote track to wrap.</param>
        /// <param name="participant">The participant who published this track.</param>
        /// <param name="trackName">The track name from the publication.</param>
        public WebGLRemoteVideoTrack(RemoteTrack track, WebGLRemoteParticipant participant, string trackName = null)
        {
            UnderlyingTrack = track ?? throw new ArgumentNullException(nameof(track));
            _participant = participant ?? throw new ArgumentNullException(nameof(participant));
            Name = trackName ?? "video";

            // Note: WebGL SDK uses a different event signature for mute changes
            // We'll track mute state when explicitly notified or through other means
        }

        #endregion

        #region ITrack Properties

        /// <inheritdoc />
        public string Sid => UnderlyingTrack.Sid;

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public TransportTrackKind Kind => TransportTrackKind.Video;

        /// <inheritdoc />
        public bool IsMuted { get; }

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, mute state changes are not propagated from the browser's video element.
        ///     This event will not fire. Use the native platform for mute change tracking.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used - required by interface but WebGL doesn't support mute tracking
        public event Action<bool> MuteChanged;
#pragma warning restore CS0067

        #endregion

        #region IRemoteTrack Properties

        /// <inheritdoc />
        public IRemoteParticipant Participant => _participant;

        /// <inheritdoc />
        public bool IsSubscribed { get; private set; } = true;

        #endregion

        #region IRemoteVideoTrack Properties & Methods

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, video cannot be directly rendered to a Unity RenderTexture from browser video elements.
        ///     This method attaches the video to a browser video element. Use browser-based rendering approaches
        ///     or consider using canvas-based solutions for WebGL video display.
        /// </remarks>
        public void AttachToRenderTexture(RenderTexture target)
        {
            if (IsAttached)
            {
                ConvaiLogger.Warning("Track is already attached.", LogCategory.Transport);
                return;
            }

            // On WebGL, we use the track's Attach() method which creates a browser video element
            UnderlyingTrack.Attach();
            IsAttached = true;

            ConvaiLogger.Info($"Attached video track '{Name}' to browser video element. " +
                              "Note: RenderTexture parameter is not directly usable on WebGL. " +
                              "Consider using HTML overlay or WebGL canvas integration for video display.",
                LogCategory.Transport);
        }

        /// <inheritdoc />
        public void Detach()
        {
            if (!IsAttached) return;

            UnderlyingTrack.Detach();
            IsAttached = false;
        }

        /// <inheritdoc />
        public bool IsAttached { get; private set; }

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, video dimensions may not be immediately available and depend on the browser's video element.
        ///     Returns null if dimensions cannot be determined.
        /// </remarks>
        public (int width, int height)? Dimensions => null; // WebGL doesn't expose this easily

        #endregion

        #region Internal Methods

        internal void SetSubscribed(bool subscribed)
        {
            IsSubscribed = subscribed;
            if (!subscribed) Detach();
        }

        internal RemoteTrack UnderlyingTrack { get; }

        #endregion
    }
}
