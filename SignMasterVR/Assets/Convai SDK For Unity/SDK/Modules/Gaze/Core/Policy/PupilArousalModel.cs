using UnityEngine;

namespace Convai.Modules.Gaze.Core.Policy
{
    /// <summary>
    ///     Computes a smoothed, normalized <c>[0, 1]</c> pupil-dilation signal from emotion
    ///     intensity and gaze engagement ("pupil response via material seam"). Pure math —
    ///     no Unity object dependencies beyond <see cref="Mathf" /> — so it is fully
    ///     unit-testable without a scene, and safe to tick every frame at zero steady-state cost.
    /// </summary>
    /// <remarks>
    ///     The instantaneous target is <c>saturate(0.6 * emotionIntensity + 0.4 * engagement)</c>;
    ///     the model then exponentially smooths toward that target with a ~1 second time
    ///     constant so onsets/decays read as organic dilation rather than a snap. This class
    ///     publishes the normalized 0-1 signal only — scaling it into a physical dilation range
    ///     (e.g. +10-15%) is the consumer's job (<see cref="Convai.Domain.Embodiment.Interfaces.IEyeAppearanceDriver" />
    ///     implementation).
    /// </remarks>
    internal sealed class PupilArousalModel
    {
        // Exponential smoothing reaches ~63% of a step input after this many seconds.
        private const float TimeConstantSeconds = 1f;

        private float _dilation;

        /// <summary>Current smoothed dilation, normalized <c>[0, 1]</c>.</summary>
        public float Dilation => _dilation;

        /// <summary>
        ///     Advances the smoothed dilation toward the instantaneous target derived from
        ///     <paramref name="emotionIntensity" /> and <paramref name="engagement" />.
        /// </summary>
        /// <param name="emotionIntensity">Dominant emotion intensity; clamped to <c>[0, 1]</c>.</param>
        /// <param name="engagement">Gaze engagement; clamped to <c>[0, 1]</c>.</param>
        /// <param name="deltaTime">Elapsed seconds since the last tick; clamped to non-negative.</param>
        public void Tick(float emotionIntensity, float engagement, float deltaTime)
        {
            float target = Mathf.Clamp01(0.6f * Mathf.Clamp01(emotionIntensity) + 0.4f * Mathf.Clamp01(engagement));
            float dt = Mathf.Max(0f, deltaTime);

            // Frame-rate independent exponential smoothing toward target with a ~1s time constant.
            float alpha = 1f - Mathf.Exp(-dt / TimeConstantSeconds);
            _dilation = Mathf.Clamp01(Mathf.Lerp(_dilation, target, alpha));
        }

        /// <summary>Resets the smoothed dilation to zero (e.g. when the controller disables).</summary>
        public void Reset() => _dilation = 0f;
    }
}
