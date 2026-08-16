using System;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Playback clock driven by Unity's audio hardware timer (<see cref="AudioSettings.dspTime" />).
    ///     Pure C# with no component attachment.
    ///     To keep lip sync aligned after focus loss or backgrounding on platforms where DSP time can
    ///     stall while remote audio continues, this clock tracks both DSP time and realtime startup
    ///     time and uses the larger elapsed value.
    /// </summary>
    internal sealed class DspTimePlaybackClock : IPlaybackClock
    {
        private PlaybackClockCore _dspCore;
        private PlaybackClockCore _realtimeCore;

        public double ElapsedSeconds
        {
            get
            {
                double dspElapsed = _dspCore.GetElapsed(AudioSettings.dspTime);
                double realtimeElapsed = _realtimeCore.GetElapsed(Time.realtimeSinceStartupAsDouble);
                return Math.Max(dspElapsed, realtimeElapsed);
            }
        }

        /// <summary>
        ///     Always valid after Unity's audio system is initialized.
        /// </summary>
        public bool IsValid => true;

        public void StartClock()
        {
            _dspCore.Start(AudioSettings.dspTime);
            _realtimeCore.Start(Time.realtimeSinceStartupAsDouble);
        }

        public void StartClock(double initialElapsedSeconds)
        {
            _dspCore.Start(AudioSettings.dspTime, initialElapsedSeconds);
            _realtimeCore.Start(Time.realtimeSinceStartupAsDouble, initialElapsedSeconds);
        }

        public void Rebase(double elapsedSeconds)
        {
            _dspCore.Rebase(AudioSettings.dspTime, elapsedSeconds);
            _realtimeCore.Rebase(Time.realtimeSinceStartupAsDouble, elapsedSeconds);
        }

        public void Pause()
        {
            _dspCore.Pause(AudioSettings.dspTime);
            _realtimeCore.Pause(Time.realtimeSinceStartupAsDouble);
        }

        public void Resume()
        {
            _dspCore.Resume(AudioSettings.dspTime);
            _realtimeCore.Resume(Time.realtimeSinceStartupAsDouble);
        }

        public void Reset()
        {
            _dspCore.Reset();
            _realtimeCore.Reset();
        }
    }
}
