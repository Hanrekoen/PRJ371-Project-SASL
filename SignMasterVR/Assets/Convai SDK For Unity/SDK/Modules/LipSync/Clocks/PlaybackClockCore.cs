using System;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Shared pause/resume/reset accounting for playback clock implementations.
    ///     Computes elapsed time from an external time source while subtracting
    ///     accumulated paused duration.
    ///     Pure C# struct -- no Unity dependency.
    /// </summary>
    internal struct PlaybackClockCore
    {
        private double _startTime;
        private double _totalPausedDuration;
        private double _pauseStartTime;

        public bool IsStarted { get; private set; }

        public bool IsPaused { get; private set; }

        /// <summary>
        ///     Computes elapsed seconds using the current value from the external time source.
        ///     Returns 0 when not started.
        /// </summary>
        public double GetElapsed(double now)
        {
            if (!IsStarted) return 0d;

            double reference = IsPaused ? _pauseStartTime : now;
            return Math.Max(0d, reference - _startTime - _totalPausedDuration);
        }

        public void Start(double now) => Start(now, 0d);

        /// <summary>Starts with the elapsed value already at <paramref name="initialElapsed" />.</summary>
        public void Start(double now, double initialElapsed)
        {
            _startTime = now - Math.Max(0d, initialElapsed);
            _totalPausedDuration = 0d;
            _pauseStartTime = 0d;
            IsStarted = true;
            IsPaused = false;
        }

        /// <summary>
        ///     Re-bases a running clock so GetElapsed(now) returns <paramref name="elapsed" />,
        ///     preserving pause state. No-op when not started.
        /// </summary>
        public void Rebase(double now, double elapsed)
        {
            if (!IsStarted) return;

            double reference = IsPaused ? _pauseStartTime : now;
            _startTime = reference - Math.Max(0d, elapsed);
            _totalPausedDuration = 0d;
        }

        public void Pause(double now)
        {
            if (!IsStarted || IsPaused) return;

            IsPaused = true;
            _pauseStartTime = now;
        }

        public void Resume(double now)
        {
            if (!IsPaused) return;

            _totalPausedDuration += now - _pauseStartTime;
            IsPaused = false;
        }

        public void Reset()
        {
            IsStarted = false;
            IsPaused = false;
            _startTime = 0d;
            _totalPausedDuration = 0d;
            _pauseStartTime = 0d;
        }
    }
}
