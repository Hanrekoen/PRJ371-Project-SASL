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
    ///     WebGL implementation of <see cref="IRemoteAudioTrack" /> wrapping the LiveKit WebGL RemoteTrack.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Key differences from NativeRemoteAudioTrack:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Audio plays through browser HTML audio elements, not Unity AudioSource</description>
    ///             </item>
    ///             <item>
    ///                 <description>Requires user gesture to start audio playback</description>
    ///             </item>
    ///             <item>
    ///                 <description>CreateAudioStream returns a WebGL-specific implementation</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    internal sealed class WebGLRemoteAudioTrack : IRemoteAudioTrack
    {
        #region Private Fields

        private readonly WebGLRemoteParticipant _participant;

        #endregion

        #region Constructor

        /// <summary>
        ///     Creates a new WebGL remote audio track wrapper.
        /// </summary>
        /// <param name="track">The LiveKit remote track to wrap.</param>
        /// <param name="participant">The participant who published this track.</param>
        /// <param name="trackName">The track name from the publication.</param>
        public WebGLRemoteAudioTrack(RemoteTrack track, WebGLRemoteParticipant participant, string trackName = null)
        {
            UnderlyingTrack = track ?? throw new ArgumentNullException(nameof(track));
            _participant = participant ?? throw new ArgumentNullException(nameof(participant));
            Name = trackName ?? "audio";
        }

        #endregion

        #region ITrack Properties

        /// <inheritdoc />
        public string Sid => UnderlyingTrack.Sid;

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public TransportTrackKind Kind => TransportTrackKind.Audio;

        /// <inheritdoc />
        public bool IsMuted { get; }

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, mute change events are not currently forwarded from the underlying SDK.
        ///     This event is declared for interface compliance but will not fire.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used
        public event Action<bool> MuteChanged;
#pragma warning restore CS0067

        #endregion

        #region IRemoteTrack Properties

        /// <inheritdoc />
        public IRemoteParticipant Participant => _participant;

        /// <inheritdoc />
        public bool IsSubscribed { get; private set; } = true;

        #endregion

        #region IRemoteAudioTrack Methods

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, this creates a WebGLAudioStream that uses the browser's Web Audio API.
        /// </remarks>
        public IAudioStream CreateAudioStream() => new WebGLAudioStream(UnderlyingTrack);

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, audio cannot be attached directly to a Unity AudioSource.
        ///     Instead, the browser plays audio through HTML audio elements.
        ///     This method will attach the track to browser audio playback and log a warning.
        /// </remarks>
        public void AttachToAudioSource(AudioSource audioSource)
        {
            if (IsAttached)
            {
                ConvaiLogger.Warning("Track is already attached.", LogCategory.Audio);
                return;
            }

            // On WebGL, we use the track's Attach() method which creates a browser audio element
            UnderlyingTrack.Attach();
            IsAttached = true;

            ConvaiLogger.Info($"Attached audio track '{Name}' to browser audio. " +
                              "Note: Unity AudioSource parameter is ignored on WebGL.",
                LogCategory.Audio);
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
