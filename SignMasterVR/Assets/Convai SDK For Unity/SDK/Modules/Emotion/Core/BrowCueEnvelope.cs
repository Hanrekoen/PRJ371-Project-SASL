using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.Emotion.Core
{
    /// <summary>
    ///     Zero-allocation, deterministic one-shot envelope for a brow cue raised through
    ///     <see cref="IBrowCueSink" /> — how the eyebrows answer where the character just
    ///     looked. Owned by
    ///     <see cref="MicroExpressionDirector" />, which composes <see cref="OuterWeight" />/
    ///     <see cref="InnerWeight" /> into its own <see cref="MicroExpressionChannel.BrowOuterUp" />/
    ///     <see cref="MicroExpressionChannel.BrowInnerUp" /> channel weights alongside idle drift,
    ///     speech accent, listening, thinking, and beat-accent contributions — this type owns only
    ///     the cue's own attack/decay shape, never the compositing.
    /// </summary>
    /// <remarks>
    ///     A new <see cref="Trigger" /> call always replaces whatever is currently in flight
    ///     (re-attacking from the envelope's current value toward the new peak), mirroring
    ///     <see cref="MicroExpressionDirector.TriggerBeatAccent" />'s shared-envelope
    ///     simplification — brow cues are edge events raised by a single upstream coordinator
    ///     (<c>BrowCueCoordinator</c> in the Gaze module) that already rate-limits itself, so two
    ///     cues never need to layer here.
    /// </remarks>
    internal sealed class BrowCueEnvelope
    {
        // SubtleRaise: a small, sustained brow-up bias that eases in fast and out slowly — reads
        // as "the brows lifted a touch while the eyes went up", never a flash.
        private const float SubtleRaisePeak = 0.15f;
        private const float SubtleRaiseAttackSeconds = 0.15f;
        private const float SubtleRaiseDecaySeconds = 0.6f;

        // Flash: a quick up-down pop paired with an acknowledgment/backchannel nod.
        private const float FlashPeak = 0.3f;
        private const float FlashAttackSeconds = 0.08f;
        private const float FlashDecaySeconds = 0.27f;

        // SurpriseFlash: stronger and a touch snappier — paired with an interruption startle.
        private const float SurpriseFlashPeak = 0.5f;
        private const float SurpriseFlashAttackSeconds = 0.06f;
        private const float SurpriseFlashDecaySeconds = 0.34f;

        private const float EnvelopeFloor = 0.0005f;
        private const float AttackSettleFloor = 0.02f;

        // Mirrors MicroExpressionDirector's BeatInnerScale house convention: a raised brow reads
        // as both inner+outer physiologically, with the inner contribution fainter.
        private const float InnerBrowScale = 0.6f;

        private BrowCueKind _kind = BrowCueKind.SubtleRaise;
        private float _peak;
        private float _envelope;
        private float _normalizedEnvelope;
        private bool _attacking;

        /// <summary>Current envelope value (0..peak) to compose into <see cref="MicroExpressionChannel.BrowOuterUp" />.</summary>
        public float OuterWeight => _envelope;

        /// <summary>Fainter co-activation to compose into <see cref="MicroExpressionChannel.BrowInnerUp" />.</summary>
        public float InnerWeight => _envelope * InnerBrowScale;

        /// <summary>
        ///     Fires (or re-attacks) a one-shot cue. <paramref name="intensity01" /> &lt;= 0 is a
        ///     no-op (no state change); values are clamped to <c>[0, 1]</c>.
        /// </summary>
        public void Trigger(BrowCueKind kind, float intensity01)
        {
            float intensity = Mathf.Clamp01(intensity01);
            if (intensity <= 0f) return;

            // Keep the temporal envelope normalized so every trigger intensity follows the
            // exact same curve. This prevents the absolute epsilon from making half-strength
            // cues decay/settle at a different sampled amplitude than full-strength cues.
            float normalizedCurrent = _peak > EnvelopeFloor ? Mathf.Clamp01(_envelope / _peak) : 0f;
            _kind = kind;
            _peak = PeakFor(kind) * intensity;
            _normalizedEnvelope = normalizedCurrent;
            _envelope = _normalizedEnvelope * _peak;
            _attacking = true;
        }

        /// <summary>Advances the envelope by <paramref name="deltaTime" /> seconds.</summary>
        public void Tick(float deltaTime)
        {
            if (!(deltaTime > 0f) || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;

            if (_attacking)
            {
                float attackCoefficient = ExponentialCoefficient(AttackFor(_kind), deltaTime);
                _normalizedEnvelope += (1f - _normalizedEnvelope) * attackCoefficient;

                if (1f - _normalizedEnvelope < AttackSettleFloor)
                {
                    _normalizedEnvelope = 1f;
                    _attacking = false;
                }
                _envelope = _normalizedEnvelope * _peak;
            }
            else if (_normalizedEnvelope > 0f)
            {
                float decayCoefficient = ExponentialCoefficient(DecayFor(_kind), deltaTime);
                _normalizedEnvelope -= _normalizedEnvelope * decayCoefficient;

                if (_normalizedEnvelope < EnvelopeFloor) _normalizedEnvelope = 0f;
                _envelope = _normalizedEnvelope * _peak;
            }
        }

        /// <summary>Resets all runtime state (envelope back to exactly 0).</summary>
        public void Reset()
        {
            _envelope = 0f;
            _normalizedEnvelope = 0f;
            _peak = 0f;
            _attacking = false;
            _kind = BrowCueKind.SubtleRaise;
        }

        private static float PeakFor(BrowCueKind kind) => kind switch
        {
            BrowCueKind.SubtleRaise => SubtleRaisePeak,
            BrowCueKind.Flash => FlashPeak,
            BrowCueKind.SurpriseFlash => SurpriseFlashPeak,
            _ => 0f
        };

        private static float AttackFor(BrowCueKind kind) => kind switch
        {
            BrowCueKind.SubtleRaise => SubtleRaiseAttackSeconds,
            BrowCueKind.Flash => FlashAttackSeconds,
            BrowCueKind.SurpriseFlash => SurpriseFlashAttackSeconds,
            _ => 0.1f
        };

        private static float DecayFor(BrowCueKind kind) => kind switch
        {
            BrowCueKind.SubtleRaise => SubtleRaiseDecaySeconds,
            BrowCueKind.Flash => FlashDecaySeconds,
            BrowCueKind.SurpriseFlash => SurpriseFlashDecaySeconds,
            _ => 0.3f
        };

        private static float ExponentialCoefficient(float timeConstantSeconds, float deltaTime)
        {
            if (timeConstantSeconds <= 0f) return 1f;
            float exponent = -deltaTime / timeConstantSeconds;
            float value = 1f - Mathf.Exp(exponent);
            return Mathf.Clamp01(value);
        }
    }
}
