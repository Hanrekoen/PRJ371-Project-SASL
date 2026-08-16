using System;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IAudioStreamFactory" />.
    ///     Creates audio streams that route remote audio through browser audio elements.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, audio playback is handled by the browser through HTML audio elements,
    ///         not Unity's AudioSource. The AudioSource parameter is used only to attach playback
    ///         lifecycle listeners so higher layers can gate lip sync from actual browser playback.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLAudioStreamFactory : IAudioStreamFactory
    {
        internal static Func<RemoteTrack, AudioSource, IDisposable> StreamCreator = CreateAttachedStream;

        /// <inheritdoc />
        /// <remarks>
        ///     Creates a WebGL audio stream. Audio is routed through browser audio elements; the
        ///     AudioSource identifies the subscription path that owns playback state listeners.
        /// </remarks>
        public IDisposable Create(IRemoteAudioTrack track, AudioSource audioSource)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));

            // Unwrap WebGL track to get the underlying LiveKit RemoteTrack
            if (track is WebGLRemoteAudioTrack webglTrack)
                return StreamCreator(webglTrack.UnderlyingTrack, audioSource);

            throw new ArgumentException(
                $"Expected WebGLRemoteAudioTrack but got {track.GetType().Name}. " +
                "Cannot create audio stream for non-WebGL track types.",
                nameof(track));
        }

        internal static void ResetTestHooks() => StreamCreator = CreateAttachedStream;

        private static IDisposable CreateAttachedStream(RemoteTrack track, AudioSource audioSource)
        {
            var stream = new WebGLAudioStream(track);
            stream.AttachToAudioSource(audioSource);
            return stream;
        }
    }
}
