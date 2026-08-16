namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Contract for the clock that drives lip sync playback timing.
    ///     Implementations can be audio-locked (DSP), realtime, or manually driven.
    /// </summary>
    public interface IPlaybackClock
    {
        /// <summary>Elapsed playback time in seconds since Start was called, accounting for pauses.</summary>
        public double ElapsedSeconds { get; }

        /// <summary>Whether the clock source is actively providing valid time data.</summary>
        public bool IsValid { get; }

        public void StartClock();

        /// <summary>
        ///     Starts the clock at a non-zero timeline position. Used when playback joins a stream
        ///     whose audio began earlier (for example, gate-open latency after audio onset), so
        ///     sampling lands on the frame that matches what is currently audible.
        /// </summary>
        public void StartClock(double initialElapsedSeconds);

        /// <summary>
        ///     Re-bases a running clock to the given elapsed value without a state transition.
        ///     Used by drift correction to snap the visual timeline back onto the audio playhead.
        /// </summary>
        public void Rebase(double elapsedSeconds);

        public void Pause();
        public void Resume();
        public void Reset();
    }
}
