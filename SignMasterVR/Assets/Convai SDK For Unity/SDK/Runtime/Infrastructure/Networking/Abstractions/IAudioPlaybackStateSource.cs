using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Exposes playback start/stop events from an audio stream (e.g. when actual audio signal is detected or silence
    ///     timeout).
    ///     Used to gate lip sync so animation starts only when remote audio is actually playing.
    /// </summary>
    public interface IAudioPlaybackStateSource
    {
        /// <summary>Raised when audio signal is first detected (playback started). Invoked on main thread when possible.</summary>
        public event Action PlaybackStarted;

        /// <summary>Raised when silence has exceeded the stream's stop timeout. Invoked on main thread when possible.</summary>
        public event Action PlaybackStopped;
    }
}
