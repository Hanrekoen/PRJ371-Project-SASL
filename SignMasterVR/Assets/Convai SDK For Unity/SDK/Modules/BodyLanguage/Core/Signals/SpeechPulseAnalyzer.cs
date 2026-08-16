using System;

namespace Convai.Modules.BodyLanguage.Core.Signals
{
    /// <summary>
    ///     Tuning knobs for <see cref="SpeechPulseAnalyzer" />. All time constants are in
    ///     seconds; thresholds are in the same 0..~1 units as the smoothed speech-energy
    ///     envelope. Defaults are tuned for conversational speech sampled at interactive
    ///     frame rates (30-90 Hz).
    /// </summary>
    internal struct SpeechPulseAnalyzerConfig
    {
        /// <summary>Envelope smoothing time constant (seconds) while energy is rising.</summary>
        public float AttackSeconds;

        /// <summary>Envelope smoothing time constant (seconds) while energy is falling.</summary>
        public float ReleaseSeconds;

        /// <summary>
        ///     Time constant (seconds) of the slow-tracking baseline used as an adaptive noise
        ///     floor, so quiet voices still cross threshold and loud rooms do not spam pulses.
        /// </summary>
        public float BaselineWindowSeconds;

        /// <summary>Envelope must exceed baseline by at least this much to trigger <see cref="SpeechPulseKind.Onset" />.</summary>
        public float OnsetThresholdAboveBaseline;

        /// <summary>
        ///     Fraction (0..1) of <see cref="OnsetThresholdAboveBaseline" /> the envelope must
        ///     fall back below (relative to baseline) before a <see cref="SpeechPulseKind.Release" />
        ///     fires. Keeping this below 1 gives the onset threshold hysteresis so a fluttering
        ///     envelope near the boundary does not chatter onset/release every step.
        /// </summary>
        public float ReleaseHysteresisFraction;

        /// <summary>Positive envelope derivative (units/second) that qualifies as an <see cref="SpeechPulseKind.Emphasis" /> spike.</summary>
        public float EmphasisDerivativeThreshold;

        /// <summary>Minimum time (seconds) between two fired pulses, regardless of kind.</summary>
        public float RefractorySeconds;

        /// <summary>While continuously active, emit a <see cref="SpeechPulseKind.Sustain" /> heartbeat at this cadence (seconds).</summary>
        public float SustainIntervalSeconds;

        /// <summary>Sensible defaults for conversational speech.</summary>
        public static SpeechPulseAnalyzerConfig Default => new()
        {
            AttackSeconds = 0.05f,
            ReleaseSeconds = 0.15f,
            BaselineWindowSeconds = 2.5f,
            OnsetThresholdAboveBaseline = 0.12f,
            ReleaseHysteresisFraction = 0.5f,
            EmphasisDerivativeThreshold = 1.6f,
            RefractorySeconds = 0.22f,
            SustainIntervalSeconds = 0.9f
        };
    }

    /// <summary>
    ///     Zero-allocation, deterministic analyzer that turns a continuous 0..~1 speech-energy
    ///     stream (see the SDK's <c>ISpeechEnergyProvider</c>) into discrete conversational
    ///     pulses: <see cref="SpeechPulseKind.Onset" />, <see cref="SpeechPulseKind.Emphasis" />,
    ///     <see cref="SpeechPulseKind.Sustain" />, and <see cref="SpeechPulseKind.Release" />.
    ///     Downstream directors use these pulses to drive head-beats and posture pulses without
    ///     re-deriving envelope logic per consumer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The analyzer maintains a fast attack/release envelope of the raw energy and a
    ///         much slower "baseline" tracking the envelope's resting level. The gap between the
    ///         two is the adaptive threshold: a quiet voice still produces a healthy gap above
    ///         its own quiet baseline, and a persistently loud room raises its own baseline so it
    ///         stops re-triggering onsets.
    ///     </para>
    ///     <para>
    ///         <see cref="Step" /> is intentionally allocation-free (no LINQ, closures, boxing,
    ///         or string operations) and fully deterministic: identical (energy, deltaTime)
    ///         sequences always produce identical pulse sequences. All internal time is
    ///         accumulated from the supplied <c>deltaTime</c> — the analyzer never reads the
    ///         wall clock and never uses randomness.
    ///     </para>
    /// </remarks>
    internal sealed class SpeechPulseAnalyzer
    {
        private readonly SpeechPulseAnalyzerConfig _config;

        private float _time;
        private float _envelope;
        private float _previousEnvelope;
        private float _baseline;
        private bool _active;
        private float _refractoryRemaining;
        private float _sinceLastSustain;

        /// <summary>Creates an analyzer with the given configuration (defaults if omitted).</summary>
        public SpeechPulseAnalyzer(SpeechPulseAnalyzerConfig config = default)
        {
            _config = config.AttackSeconds > 0f || config.ReleaseSeconds > 0f
                ? config
                : SpeechPulseAnalyzerConfig.Default;
            Reset();
        }

        /// <summary>The current smoothed speech-energy envelope.</summary>
        public float Envelope => _envelope;

        /// <summary>The current slow-tracking adaptive baseline.</summary>
        public float Baseline => _baseline;

        /// <summary>Whether the analyzer currently considers speech "active" (above the onset threshold, not yet released).</summary>
        public bool IsActive => _active;

        /// <summary>The analyzer's accumulated internal time, in seconds, driven entirely by <c>deltaTime</c> inputs.</summary>
        public float Time => _time;

        /// <summary>Restores the analyzer to its initial, inactive state.</summary>
        public void Reset()
        {
            _time = 0f;
            _envelope = 0f;
            _previousEnvelope = 0f;
            _baseline = 0f;
            _active = false;
            _refractoryRemaining = 0f;
            _sinceLastSustain = 0f;
        }

        /// <summary>
        ///     Advances the analyzer by <paramref name="deltaTime" /> seconds given the raw
        ///     <paramref name="energy" /> sample, and reports at most one pulse for this step.
        /// </summary>
        /// <param name="energy">Raw (unsmoothed) 0..~1 speech-energy sample for this step.</param>
        /// <param name="deltaTime">Elapsed seconds since the previous step. Non-positive values are ignored (no state advance, no pulse).</param>
        /// <param name="pulse">The pulse fired this step, or a default (<see cref="SpeechPulseKind.None" />) pulse if none fired.</param>
        /// <returns><see langword="true" /> if a pulse fired this step.</returns>
        public bool Step(float energy, float deltaTime, out SpeechPulse pulse)
        {
            pulse = default;

            if (!(deltaTime > 0f) || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return false;

            if (float.IsNaN(energy) || float.IsInfinity(energy))
                energy = 0f;
            else if (energy < 0f)
                energy = 0f;

            _time += deltaTime;
            if (_refractoryRemaining > 0f)
                _refractoryRemaining = Math.Max(0f, _refractoryRemaining - deltaTime);

            // Attack/release envelope: rises fast, falls slower, so short dropouts between
            // syllables don't collapse the envelope back to the noise floor.
            float envelopeTimeConstant = energy > _envelope ? _config.AttackSeconds : _config.ReleaseSeconds;
            float envelopeCoefficient = ExponentialCoefficient(envelopeTimeConstant, deltaTime);
            _previousEnvelope = _envelope;
            _envelope += (energy - _envelope) * envelopeCoefficient;

            // Slow-tracking baseline: always chases the envelope, just far more slowly, so it
            // approximates the resting noise/room level rather than the speech itself.
            float baselineCoefficient = ExponentialCoefficient(_config.BaselineWindowSeconds, deltaTime);
            _baseline += (_envelope - _baseline) * baselineCoefficient;

            float onsetLevel = _baseline + _config.OnsetThresholdAboveBaseline;
            float releaseLevel = _baseline + _config.OnsetThresholdAboveBaseline * _config.ReleaseHysteresisFraction;

            float derivative = (_envelope - _previousEnvelope) / deltaTime;

            bool canFire = _refractoryRemaining <= 0f;

            if (!_active)
            {
                if (canFire && _envelope >= onsetLevel)
                {
                    _active = true;
                    _sinceLastSustain = 0f;
                    float strength = NormalizeStrength(_envelope - _baseline, _config.OnsetThresholdAboveBaseline);
                    pulse = new SpeechPulse(SpeechPulseKind.Onset, strength, _time);
                    _refractoryRemaining = _config.RefractorySeconds;
                    return true;
                }
                return false;
            }

            // Active: check release first (it takes priority once the envelope has genuinely
            // collapsed), then emphasis spikes, then the sustain heartbeat.
            if (_envelope < releaseLevel)
            {
                _active = false;
                float strength = NormalizeStrength(releaseLevel - _envelope, _config.OnsetThresholdAboveBaseline);
                pulse = new SpeechPulse(SpeechPulseKind.Release, strength, _time);
                _refractoryRemaining = _config.RefractorySeconds;
                return true;
            }

            _sinceLastSustain += deltaTime;

            if (canFire && derivative >= _config.EmphasisDerivativeThreshold)
            {
                float strength = NormalizeStrength(derivative - _config.EmphasisDerivativeThreshold,
                    _config.EmphasisDerivativeThreshold);
                pulse = new SpeechPulse(SpeechPulseKind.Emphasis, strength, _time);
                _refractoryRemaining = _config.RefractorySeconds;
                _sinceLastSustain = 0f;
                return true;
            }

            if (_sinceLastSustain >= _config.SustainIntervalSeconds)
            {
                _sinceLastSustain = 0f;
                float strength = NormalizeStrength(_envelope - _baseline, _config.OnsetThresholdAboveBaseline);
                pulse = new SpeechPulse(SpeechPulseKind.Sustain, strength, _time);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Converts a fixed per-frame smoothing time constant into the exponential
        ///     blend coefficient for this step's <paramref name="deltaTime" />, so the
        ///     envelope's response speed is frame-rate independent.
        /// </summary>
        private static float ExponentialCoefficient(float timeConstantSeconds, float deltaTime)
        {
            if (timeConstantSeconds <= 0f) return 1f;
            float exponent = -deltaTime / timeConstantSeconds;
            // 1 - e^x via Math.Exp keeps this on plain float math (no UnityEngine.Mathf needed).
            float value = 1f - (float)Math.Exp(exponent);
            return Clamp01(value);
        }

        private static float NormalizeStrength(float amount, float scale)
        {
            if (scale <= 0f) return 0f;
            return Clamp01(amount / scale);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
