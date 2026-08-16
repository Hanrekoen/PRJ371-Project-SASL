namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Selects the appropriate <see cref="IPlaybackClock" /> implementation for the current platform.
    ///     On non-WebGL platforms, <see cref="DspTimePlaybackClock" /> is used to read
    ///     <c>AudioSettings.dspTime</c> for audio-hardware-locked timing.
    ///     On WebGL, <see cref="RealtimePlaybackClock" /> is used because DSP-time semantics
    ///     differ from native platforms.
    /// </summary>
    internal static class LipSyncClockResolver
    {
        public static IPlaybackClock Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new RealtimePlaybackClock();
#else
            return new DspTimePlaybackClock();
#endif
        }
    }
}
