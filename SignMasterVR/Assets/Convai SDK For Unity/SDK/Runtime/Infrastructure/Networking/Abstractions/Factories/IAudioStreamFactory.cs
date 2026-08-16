using System;
using UnityEngine;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Factory interface for creating platform-specific audio streams.
    ///     Audio streams route remote audio track data to Unity AudioSources.
    ///     Implementations are provided by platform-specific assemblies (Native, WebGL).
    /// </summary>
    public interface IAudioStreamFactory
    {
        /// <summary>
        ///     Creates an audio stream that routes audio from a remote track to an AudioSource.
        /// </summary>
        /// <param name="track">The remote audio track to stream from.</param>
        /// <param name="audioSource">The Unity AudioSource to play audio through.</param>
        /// <returns>A disposable audio stream. Dispose to stop streaming and release resources.</returns>
        public IDisposable Create(IRemoteAudioTrack track, AudioSource audioSource);
    }
}
