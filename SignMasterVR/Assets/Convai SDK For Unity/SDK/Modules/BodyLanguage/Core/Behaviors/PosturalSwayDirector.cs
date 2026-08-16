using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO producing a two-octave, band-limited postural sway:
    ///     continuous sub-degree drift on the sagittal and lateral spine axes that reads as a
    ///     standing body's constant micro-balancing rather than a perfectly locked pose. Amplitude
    ///     comes from the per-state
    ///     <see cref="Convai.Modules.BodyLanguage.Data.BodyLanguageStatePolicy.AmbientDrift" />
    ///     policy knob, of which this director is the sole consumer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Band-limited, never white noise.</b> Each axis sums two independent octaves — a
    ///         slow 8-second-period drift and a faster 2.5-second-period ripple weighted at
    ///         0.35× — each octave itself a smoothed random walk: a new target is drawn every
    ///         <c>period × random(0.7, 1.3)</c> seconds and the octave's current value slews
    ///         toward it with time constant <c>period / 2.5</c>. The result is a continuous,
    ///         self-similar drift with no discontinuities — structurally distinct from frame-to-
    ///         frame white noise, which this director never produces.
    ///     </para>
    ///     <para>
    ///         Four independent streams (2 axes × 2 octaves) all draw from ONE seeded RNG, so a
    ///         given seed's sway is fully deterministic and reproducible.
    ///     </para>
    ///     <para>
    ///         <b>Amplitude.</b> The combined per-axis signal (clamped -1..1) is scaled by the
    ///         caller-supplied smoothed per-state <c>AmbientDrift</c> value (0 silences the sway
    ///         entirely) and by an internal enable envelope: disabling slews the output to zero
    ///         over about a second while the underlying octave phases keep advancing — re-enabling
    ///         never pops back in from a stale phase.
    ///     </para>
    ///     <para>
    ///         Deterministic and allocation-free: <see cref="Seed" /> takes a
    ///         <see cref="DeterministicEmbodimentRandom" /> seed; identical (seed, tick input
    ///         sequence) pairs always produce an identical sway trace.
    ///     </para>
    /// </remarks>
    internal sealed class PosturalSwayDirector
    {
        private const float SlowOctavePeriodSeconds = 8f;
        private const float FastOctavePeriodSeconds = 2.5f;
        private const float FastOctaveWeight = 0.35f;

        /// <summary>Seconds (time constant) over which the enable envelope reaches ~95% of its target.</summary>
        private const float EnvelopeTimeConstantSeconds = 1f / 3f;

        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        private float _sagittalSlowCurrent, _sagittalSlowTarget, _sagittalSlowCountdown;
        private float _sagittalFastCurrent, _sagittalFastTarget, _sagittalFastCountdown;
        private float _lateralSlowCurrent, _lateralSlowTarget, _lateralSlowCountdown;
        private float _lateralFastCurrent, _lateralFastTarget, _lateralFastCountdown;

        private float _enableEnvelope;

        private float _swaySagittal01;
        private float _swayLateral01;

        /// <summary>Combined band-limited sagittal sway, -1..1, pre-scale by <c>MaxSwayDegrees</c>.</summary>
        public float SwaySagittal01 => _swaySagittal01;

        /// <summary>Combined band-limited lateral sway, -1..1, pre-scale by <c>MaxSwayDegrees</c>.</summary>
        public float SwayLateral01 => _swayLateral01;

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        /// <summary>Restores the director to its initial, silent state. Does not reset the seed.</summary>
        public void Reset()
        {
            _sagittalSlowCurrent = 0f;
            _sagittalSlowTarget = 0f;
            _sagittalSlowCountdown = 0f;
            _sagittalFastCurrent = 0f;
            _sagittalFastTarget = 0f;
            _sagittalFastCountdown = 0f;
            _lateralSlowCurrent = 0f;
            _lateralSlowTarget = 0f;
            _lateralSlowCountdown = 0f;
            _lateralFastCurrent = 0f;
            _lateralFastTarget = 0f;
            _lateralFastCountdown = 0f;
            _enableEnvelope = 0f;
            _swaySagittal01 = 0f;
            _swayLateral01 = 0f;
        }

        /// <summary>Advances the sway octaves and the enable envelope. Producer-only: never touches bones.</summary>
        public void Tick(bool enabled, float stateAmbientDrift01, float deltaTime)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0x5A755AEDu);
                _randomSeeded = true;
            }

            float dt = deltaTime > 0f ? deltaTime : 0f;

            TickOctave(ref _sagittalSlowCurrent, ref _sagittalSlowTarget, ref _sagittalSlowCountdown, SlowOctavePeriodSeconds, dt);
            TickOctave(ref _sagittalFastCurrent, ref _sagittalFastTarget, ref _sagittalFastCountdown, FastOctavePeriodSeconds, dt);
            TickOctave(ref _lateralSlowCurrent, ref _lateralSlowTarget, ref _lateralSlowCountdown, SlowOctavePeriodSeconds, dt);
            TickOctave(ref _lateralFastCurrent, ref _lateralFastTarget, ref _lateralFastCountdown, FastOctavePeriodSeconds, dt);

            float envelopeAlpha = 1f - Mathf.Exp(-dt / EnvelopeTimeConstantSeconds);
            float envelopeTarget = enabled ? 1f : 0f;
            _enableEnvelope += (envelopeTarget - _enableEnvelope) * envelopeAlpha;

            float amplitude = _enableEnvelope * Mathf.Clamp01(stateAmbientDrift01);

            float rawSagittal = Mathf.Clamp(_sagittalSlowCurrent + FastOctaveWeight * _sagittalFastCurrent, -1f, 1f);
            float rawLateral = Mathf.Clamp(_lateralSlowCurrent + FastOctaveWeight * _lateralFastCurrent, -1f, 1f);

            _swaySagittal01 = rawSagittal * amplitude;
            _swayLateral01 = rawLateral * amplitude;
        }

        private void TickOctave(ref float current, ref float target, ref float countdown, float periodSeconds, float dt)
        {
            countdown -= dt;
            if (countdown <= 0f)
            {
                target = _random.Range(-1f, 1f);
                countdown = periodSeconds * _random.Range(0.7f, 1.3f);
            }

            float tau = periodSeconds / 2.5f;
            float alpha = tau > 0f ? 1f - Mathf.Exp(-dt / tau) : 1f;
            current += (target - current) * alpha;
        }
    }
}
