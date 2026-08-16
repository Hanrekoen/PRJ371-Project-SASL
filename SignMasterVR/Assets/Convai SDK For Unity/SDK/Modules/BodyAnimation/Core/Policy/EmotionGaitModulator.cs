using Convai.Domain.Embodiment.Readings;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Emotion → locomotion speed multiplier: excited characters walk faster, sad
    ///     ones trudge. Maps the current <see cref="EmotionReading" /> to a multiplier in
    ///     <c>[1-range, 1+range]</c> via <see cref="GaitEmotionArousalTable" /> (high arousal,
    ///     scaled by the emotion's own intensity, speeds the commanded walk/jog speed up; low
    ///     negative arousal slows it down), then smooths changes over ~1s so an emotion flip
    ///     never jerks the gait. Applied at the NavMesh-commanded-speed site
    ///     (<see cref="Components.ConvaiNavMeshLocomotion.SetGaitSpeedMultiplier" />) so the
    ///     existing measured-speed rate-warp machinery preserves foot sync by construction.
    /// </summary>
    internal sealed class EmotionGaitModulator
    {
        private const float SmoothingSeconds = 1f;

        private float _current = 1f;
        private float _velocity;

        /// <summary>Last smoothed multiplier produced by <see cref="Tick" />.</summary>
        public float Current => _current;

        /// <summary>
        ///     Advances the smoothing toward the target multiplier for <paramref name="emotion" />
        ///     and returns the new smoothed value.
        /// </summary>
        public float Tick(in EmotionReading emotion, float range, float deltaTime)
        {
            float target = ResolveTargetMultiplier(in emotion, range);
            float dt = deltaTime > 0f ? deltaTime : 0f;
            _current = Mathf.SmoothDamp(_current, target, ref _velocity, SmoothingSeconds, float.MaxValue, dt);
            return _current;
        }

        /// <summary>
        ///     Pure target-multiplier mapping (no smoothing state) — exactly 1 for a neutral or
        ///     unrecognized emotion, otherwise <c>1 + arousal * clampedRange * intensity</c>
        ///     clamped to <c>[1-clampedRange, 1+clampedRange]</c>.
        /// </summary>
        public static float ResolveTargetMultiplier(in EmotionReading emotion, float range)
        {
            float clampedRange = Mathf.Clamp(range, 0f, 0.3f);
            if (clampedRange <= 0f) return 1f;
            if (emotion.IsNeutral) return 1f;
            if (!GaitEmotionArousalTable.TryGetArousal(emotion.DominantLabel, out float arousal) || arousal == 0f)
                return 1f;

            float multiplier = 1f + arousal * clampedRange * Mathf.Clamp01(emotion.DominantScore);
            return Mathf.Clamp(multiplier, 1f - clampedRange, 1f + clampedRange);
        }

        /// <summary>Resets the smoothed multiplier back to neutral (1) with zero velocity.</summary>
        public void Reset()
        {
            _current = 1f;
            _velocity = 0f;
        }
    }
}
