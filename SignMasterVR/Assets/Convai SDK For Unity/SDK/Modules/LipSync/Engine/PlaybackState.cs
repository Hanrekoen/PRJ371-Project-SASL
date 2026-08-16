namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Runtime state machine for lip sync playback progression.
    /// </summary>
    public enum PlaybackState
    {
        /// <summary>No active stream and output is reset.</summary>
        Idle = 0,

        /// <summary>Frames are buffered while waiting for playback to begin.</summary>
        Buffering = 1,

        /// <summary>Playback is advancing normally.</summary>
        Playing = 2,

        /// <summary>Playback has passed buffer end. Holding last values, waiting for more data or stream end.</summary>
        Starving = 3,

        /// <summary>Output is smoothly fading to neutral before returning to idle.</summary>
        FadingOut = 4
    }
}
