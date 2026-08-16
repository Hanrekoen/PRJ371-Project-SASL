using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO producing a very slow, multi-minute seeded drift (idle presence):
    ///     a single smoothed random walk, redrawn every few minutes, that nudges
    ///     breath depth, sway amplitude, and fidget cadence up or down together so a long idle
    ///     never settles into a perceptibly looping baseline. This is deliberately the slowest
    ///     timescale in the body language system — imperceptible frame to frame, only noticeable
    ///     across minutes of real-time observation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>One stream.</b> Mirrors <see cref="PosturalSwayDirector" />'s per-octave recipe
    ///         (smoothed random walk: draw a new target, slew toward it with a time constant tied
    ///         to the redraw period) but with a single very long period instead of two short ones.
    ///         The period itself is drawn ONCE, from <c>random(120, 300)</c> seconds, the first
    ///         time <see cref="Tick" /> runs after construction or <see cref="Reset" /> — every
    ///         subsequent target redraw within that director's lifetime reuses the SAME period
    ///         (scaled ±20% per redraw), so a director's overall drift cadence stays consistent for
    ///         as long as it lives.
    ///     </para>
    ///     <para>
    ///         <b>Enable envelope.</b> The underlying random-walk value keeps advancing regardless
    ///         of <c>Tick</c>'s <c>enabled</c> argument (mirrors <see cref="PosturalSwayDirector" />'s own
    ///         "phases keep advancing" remark) — only the published <see cref="Energy01" /> is
    ///         gated by a small internal envelope that slews to zero over ~2 seconds when disabled,
    ///         so a toggle never pops the consumers it feeds (breath depth, sway amplitude, fidget
    ///         cadence) and re-enabling never snaps back in from a stale phase.
    ///     </para>
    ///     <para>
    ///         Deterministic and allocation-free: <see cref="Seed" /> takes a
    ///         <see cref="DeterministicEmbodimentRandom" /> seed; identical (seed, tick input
    ///         sequence) pairs always produce an identical drift trace.
    ///     </para>
    /// </remarks>
    internal sealed class MacroCycleDirector
    {
        private const float MinPeriodSeconds = 120f;
        private const float MaxPeriodSeconds = 300f;

        /// <summary>Seconds (time constant) over which the enable envelope reaches ~95% of its target (~2s total, mirrors <see cref="PosturalSwayDirector" />'s enable-envelope pattern at a slower cadence).</summary>
        private const float EnvelopeTimeConstantSeconds = 2f / 3f;

        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        /// <summary>This director's own redraw period (seconds), drawn once from <c>random(120, 300)</c> on the first <see cref="Tick" /> after construction/<see cref="Reset" />.</summary>
        private float _periodSeconds;
        private bool _hasPeriod;

        private float _current;
        private float _target;
        private float _countdown;

        private float _enableEnvelope;

        /// <summary>Combined slow drift, -1..1, gated by the enable envelope. Feeds breath depth/sway amplitude/fidget cadence multipliers in the controller — producers never see this director directly.</summary>
        public float Energy01 => _current * _enableEnvelope;

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        /// <summary>Restores the director to its initial, silent state (including a fresh period draw on the next tick). Does not reset the seed.</summary>
        public void Reset()
        {
            _periodSeconds = 0f;
            _hasPeriod = false;
            _current = 0f;
            _target = 0f;
            _countdown = 0f;
            _enableEnvelope = 0f;
        }

        /// <summary>Advances the drift and the enable envelope. Producer-only: never touches bones.</summary>
        public void Tick(bool enabled, float deltaTime)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0xC1C1E5EEu);
                _randomSeeded = true;
            }

            float dt = deltaTime > 0f ? deltaTime : 0f;

            if (!_hasPeriod)
            {
                _periodSeconds = _random.Range(MinPeriodSeconds, MaxPeriodSeconds);
                _hasPeriod = true;
                // Force an immediate first target draw below rather than waiting a full period
                // for the very first value — a fresh director should start drifting right away.
                _countdown = 0f;
            }

            _countdown -= dt;
            if (_countdown <= 0f)
            {
                _target = _random.Range(-1f, 1f);
                _countdown = _periodSeconds * _random.Range(0.8f, 1.2f);
            }

            float tau = _periodSeconds / 3f;
            float alpha = tau > 0f ? 1f - Mathf.Exp(-dt / tau) : 1f;
            _current += (_target - _current) * alpha;

            float envelopeAlpha = 1f - Mathf.Exp(-dt / EnvelopeTimeConstantSeconds);
            float envelopeTarget = enabled ? 1f : 0f;
            _enableEnvelope += (envelopeTarget - _enableEnvelope) * envelopeAlpha;
        }
    }
}
