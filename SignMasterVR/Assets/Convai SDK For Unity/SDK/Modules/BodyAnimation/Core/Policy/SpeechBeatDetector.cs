using UnityEngine;

namespace Convai.Modules.BodyAnimation.Core.Policy
{
    /// <summary>
    ///     Onset detector over a live 0..1 speech-energy signal: fires a rising-edge
    ///     event when energy climbs a fixed margin above a slow adaptive baseline, refuses
    ///     another onset until a caller-supplied refractory window elapses, and reports an
    ///     onset strength that scales monotonically with how far the signal jumped above the
    ///     threshold. Pure, zero-alloc, deterministic given identical input streams — a plain
    ///     POCO so it is unit-testable without a graph or animator.
    /// </summary>
    /// <remarks>
    ///     The adaptive baseline is a single-pole low-pass filter over the raw energy signal.
    ///     Loud, steady speech (energy holding near a constant value) lets the baseline catch
    ///     up to it, so the signal stops sitting above (baseline + margin) and onsets stop
    ///     firing — the detector self-quiets on sustained level instead of machine-gunning
    ///     beats. A fresh rise (a stressed syllable) still reads as a clear excursion above the
    ///     caught-up baseline and fires again once the refractory window has elapsed.
    /// </remarks>
    internal sealed class SpeechBeatDetector
    {
        /// <summary>Default margin above the adaptive baseline an energy sample must clear to count as an onset.</summary>
        internal const float DefaultMargin = 0.12f;

        /// <summary>Default time constant (seconds) of the adaptive-baseline low-pass filter.</summary>
        internal const float DefaultBaselineTauSeconds = 0.5f;

        private readonly float _margin;
        private readonly float _baselineTauSeconds;

        private float _baseline;
        private bool _wasAboveThreshold;
        private float _refractoryRemaining;

        public SpeechBeatDetector(
            float margin = DefaultMargin,
            float baselineTauSeconds = DefaultBaselineTauSeconds)
        {
            _margin = Mathf.Max(0.001f, margin);
            _baselineTauSeconds = Mathf.Max(0.01f, baselineTauSeconds);
        }

        /// <summary>Current adaptive baseline, exposed for diagnostics/tests.</summary>
        internal float BaselineForTests => _baseline;

        /// <summary>
        ///     Clears all adaptive state. Call whenever the detector's input stream stops being
        ///     meaningful (e.g. the character is no longer in the Talk pool) so a resumed
        ///     speech turn starts from a clean baseline instead of one caught up to a previous,
        ///     unrelated stretch of energy.
        /// </summary>
        public void Reset()
        {
            _baseline = 0f;
            _wasAboveThreshold = false;
            _refractoryRemaining = 0f;
        }

        /// <summary>
        ///     Advances the detector by one tick. Returns <c>true</c> exactly on a rising-edge
        ///     onset (energy crossing from at-or-below to above the adaptive threshold) that is
        ///     not currently inside the refractory window, and writes the onset's 0..1 strength
        ///     to <paramref name="strength" /> (0 when no onset fired this tick).
        /// </summary>
        public bool Tick(float energy01, float deltaTime, float refractorySeconds, out float strength)
        {
            strength = 0f;
            energy01 = Mathf.Clamp01(energy01);
            deltaTime = Mathf.Max(0f, deltaTime);

            float alpha = deltaTime > 0f ? 1f - Mathf.Exp(-deltaTime / _baselineTauSeconds) : 0f;
            _baseline = Mathf.Lerp(_baseline, energy01, alpha);

            if (_refractoryRemaining > 0f)
                _refractoryRemaining = Mathf.Max(0f, _refractoryRemaining - deltaTime);

            float threshold = Mathf.Clamp01(_baseline + _margin);
            bool above = energy01 > threshold;
            bool fire = above && !_wasAboveThreshold && _refractoryRemaining <= 0f;
            _wasAboveThreshold = above;

            if (!fire) return false;

            strength = Mathf.Clamp01((energy01 - threshold) / _margin);
            _refractoryRemaining = Mathf.Max(0.01f, refractorySeconds);
            return true;
        }
    }
}
