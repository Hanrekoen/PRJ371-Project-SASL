using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Optional idle curiosity: while the character is idle (player target suppressed by
    ///     policy), occasionally schedules a short, soft glance at the player so the
    ///     character still feels aware of them. The controller turns each decision into a
    ///     low-priority scripted stack entry with the configured duration.
    /// </summary>
    internal sealed class CuriosityGlanceDirector
    {
        private float _countdown;
        private bool _armed;

        public void Reset()
        {
            _countdown = 0f;
            _armed = false;
        }

        /// <summary>
        ///     Returns <c>true</c> when a glance should fire this tick. Only counts down
        ///     while <paramref name="idleActive" /> (engaged states reset the timer).
        ///     <paramref name="intervalScale" /> shrinks the wait between glances (E8
        ///     reciprocation: a watched idle character glances back sooner); 1 is the
        ///     authored cadence.
        /// </summary>
        public bool Tick(
            ConvaiGazeProfile profile,
            float deltaTime,
            bool idleActive,
            ref DeterministicEmbodimentRandom random,
            float intervalScale = 1f)
        {
            if (profile == null || !profile.EnableCuriosityGlances || !idleActive)
            {
                _armed = false;
                return false;
            }

            float scale = Mathf.Clamp(intervalScale, 0.05f, 1f);

            if (!_armed)
            {
                _armed = true;
                _countdown = random.Range(profile.CuriosityGlanceIntervalMin, profile.CuriosityGlanceIntervalMax) * scale;
            }

            _countdown -= deltaTime;
            if (_countdown > 0f) return false;

            _countdown = random.Range(profile.CuriosityGlanceIntervalMin, profile.CuriosityGlanceIntervalMax) * scale;
            return true;
        }
    }
}
