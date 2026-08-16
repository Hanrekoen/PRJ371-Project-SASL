using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core
{
    /// <summary>
    ///     Distance + visibility level-of-detail decisions for the gaze controller, so a crowd
    ///     of gaze-enabled characters stays in budget: far characters think less often and
    ///     off-screen characters skip actuation entirely. A pure, stateful decision table (the
    ///     only state is the far-band hysteresis latch and the skipped-tick time accumulator),
    ///     fully unit-testable without a scene.
    /// </summary>
    internal sealed class GazeLodGovernor
    {
        /// <summary>Hysteresis half-width (meters) around the far distance → a 1 m band, no thrash at the boundary.</summary>
        private const float DistanceHysteresis = 0.5f;

        /// <summary>
        ///     Tolerance (seconds) on the interval comparison. Accumulating frame deltas in
        ///     float undershoots the exact interval (five 0.02 steps sum to 0.099999994, not
        ///     0.1), which would silently defer every executed tick by one extra frame.
        /// </summary>
        private const float IntervalToleranceSeconds = 1e-4f;

        private bool _far;
        private float _accumulatedDeltaTime;

        /// <summary>Whether the character is currently in the reduced-rate far band.</summary>
        public bool IsFar => _far;

        public void Reset()
        {
            _far = false;
            _accumulatedDeltaTime = 0f;
        }

        /// <summary>
        ///     Advances the governor for one Cognition tick.
        /// </summary>
        /// <param name="profile">Active gaze profile (LOD knobs).</param>
        /// <param name="distance">Distance (meters) from the character head pivot to the player anchor.</param>
        /// <param name="anyRendererVisible">Whether any of the character's renderers is visible to a camera.</param>
        /// <param name="deltaTime">Time since the last Cognition tick.</param>
        /// <param name="cognitionDeltaTime">
        ///     The delta time the executed tick should use — the sum of this and every skipped
        ///     tick since the last execution, so springs and ramps stay variable-dt correct.
        /// </param>
        /// <param name="skipExpression">Whether the LateUpdate solver stage should be skipped this frame.</param>
        /// <returns><c>true</c> when Cognition should run this tick; <c>false</c> to skip it.</returns>
        public bool TickCognition(
            ConvaiGazeProfile profile,
            float distance,
            bool anyRendererVisible,
            float deltaTime,
            out float cognitionDeltaTime,
            out bool skipExpression)
        {
            if (profile == null || !profile.EnableGazeLod)
            {
                _far = false;
                _accumulatedDeltaTime = 0f;
                cognitionDeltaTime = deltaTime;
                skipExpression = false;
                return true;
            }

            skipExpression = profile.SkipWhenInvisible && !anyRendererVisible;

            _accumulatedDeltaTime += Mathf.Max(0f, deltaTime);
            float interval = ResolveCognitionInterval(profile, distance);
            if (_accumulatedDeltaTime + IntervalToleranceSeconds < interval)
            {
                cognitionDeltaTime = 0f;
                return false;
            }

            cognitionDeltaTime = _accumulatedDeltaTime;
            _accumulatedDeltaTime = 0f;
            return true;
        }

        private float ResolveCognitionInterval(ConvaiGazeProfile profile, float distance)
        {
            if (_far)
            {
                if (distance < profile.LodFarDistance - DistanceHysteresis) _far = false;
            }
            else
            {
                if (distance > profile.LodFarDistance + DistanceHysteresis) _far = true;
            }

            return _far ? 1f / Mathf.Max(0.01f, profile.LodFarCognitionHz) : 0f;
        }
    }
}
