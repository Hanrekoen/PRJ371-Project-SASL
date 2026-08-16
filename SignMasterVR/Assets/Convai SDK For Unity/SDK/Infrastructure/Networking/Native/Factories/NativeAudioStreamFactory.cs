using System;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native (LiveKit) implementation of <see cref="IAudioStreamFactory" />.
    ///     Creates audio streams that route LiveKit remote audio to Unity AudioSources.
    /// </summary>
    internal sealed class NativeAudioStreamFactory : IAudioStreamFactory
    {
        /// <inheritdoc />
        public IDisposable Create(IRemoteAudioTrack track, AudioSource audioSource)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));

            if (audioSource == null) throw new ArgumentNullException(nameof(audioSource));

            // Unwrap native track to get the underlying LiveKit RemoteAudioTrack
            if (track is NativeRemoteAudioTrack nativeTrack)
            {
                RemoteAudioTrack lkTrack = nativeTrack.UnderlyingTrack;
                var inner = new AudioStream(lkTrack, audioSource);
                return new NativeAudioStreamPlaybackAdapter(inner);
            }

            throw new ArgumentException(
                $"Expected NativeRemoteAudioTrack but got {track.GetType().Name}. " +
                "Cannot create audio stream for non-native track types.",
                nameof(track));
        }
    }
}
