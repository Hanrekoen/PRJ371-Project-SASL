namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Optional playback capability used to make character interruption transitions
    ///     perceptually smooth without exposing platform-specific audio implementation details.
    /// </summary>
    internal interface IBargeInPlaybackControl
    {
        /// <summary>
        ///     Reduces the current playback level without discarding the active response.
        /// </summary>
        void Duck(float targetGain, float durationSeconds);

        /// <summary>
        ///     Fades the current response to silence and suppresses its remaining audio.
        /// </summary>
        void CommitInterruption(float durationSeconds);

        /// <summary>
        ///     Allows the next response to play and restores its configured level.
        /// </summary>
        /// <returns>
        ///     True when playback is already active after the restore and no new
        ///     <see cref="IAudioPlaybackStateSource.PlaybackStarted" /> signal is expected;
        ///     otherwise false so the caller can wait for the fresh playback signal.
        /// </returns>
        bool Restore(float durationSeconds);
    }
}
