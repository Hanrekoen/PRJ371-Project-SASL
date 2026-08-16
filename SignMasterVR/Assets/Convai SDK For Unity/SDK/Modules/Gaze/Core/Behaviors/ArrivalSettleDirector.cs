using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     The small downward drop of the eyes as a character comes to rest at the end of a
    ///     walk, and their lift back up a moment later.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         People do not arrive somewhere and immediately hold your gaze. The eyes come down
    ///         for a beat as the body settles, then come back up — it is the difference between
    ///         a person stopping and a camera being repositioned.
    ///     </para>
    ///     <para>
    ///         <b>Eyes only, and small.</b> The offset goes through the micro-motion channel,
    ///         which the head never reads: a settle that moved the neck would be the head-bow
    ///         this module works hard to prevent, not a settle. A few degrees for under a second
    ///         is the whole effect; anything larger stops reading as settling and starts reading
    ///         as looking at the floor.
    ///     </para>
    ///     <para>
    ///         Fires on the travelling → not-travelling edge, so it is one beat per arrival
    ///         rather than something that can retrigger while the character stands there.
    ///     </para>
    /// </remarks>
    internal sealed class ArrivalSettleDirector
    {
        /// <summary>Fraction of the beat spent dropping; the rest is the lift back.</summary>
        private const float DropFraction = 0.35f;

        private bool _wasTraveling;
        private float _elapsed;
        private float _duration;
        private float _depthDegrees;

        /// <summary>Eye-only pitch offset (degrees, negative is down) for this frame; zero when idle.</summary>
        public float PitchOffsetDegrees { get; private set; }

        /// <summary>Whether a settle beat is playing.</summary>
        public bool IsSettling => _duration > 0f;

        public void Reset()
        {
            _wasTraveling = false;
            _elapsed = 0f;
            _duration = 0f;
            _depthDegrees = 0f;
            PitchOffsetDegrees = 0f;
        }

        /// <summary>
        ///     Advances the beat and reports this frame's offset.
        /// </summary>
        /// <param name="isTraveling">Whether the character is still going somewhere.</param>
        /// <param name="depthDegrees">How far the eyes drop at the bottom of the beat. Zero disables.</param>
        /// <param name="durationSeconds">Length of the whole drop-and-lift.</param>
        /// <param name="deltaTime">Tick delta.</param>
        public void Tick(bool isTraveling, float depthDegrees, float durationSeconds, float deltaTime)
        {
            bool arrived = _wasTraveling && !isTraveling;
            _wasTraveling = isTraveling;

            if (arrived && depthDegrees > 0f && durationSeconds > 0f)
            {
                _elapsed = 0f;
                _duration = durationSeconds;
                _depthDegrees = depthDegrees;
            }

            if (_duration <= 0f)
            {
                PitchOffsetDegrees = 0f;
                return;
            }

            _elapsed += Mathf.Max(0f, deltaTime);
            if (_elapsed >= _duration)
            {
                _duration = 0f;
                PitchOffsetDegrees = 0f;
                return;
            }

            PitchOffsetDegrees = -_depthDegrees * Envelope(_elapsed / _duration);
        }

        /// <summary>
        ///     0 → 1 → 0 across the beat, smoothstepped at both ends and asymmetric: the drop is
        ///     quicker than the lift, the way a settling movement actually decays.
        /// </summary>
        internal static float Envelope(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            float phase = t < DropFraction
                ? t / DropFraction
                : 1f - (t - DropFraction) / (1f - DropFraction);

            return phase * phase * (3f - 2f * phase);
        }
    }
}
