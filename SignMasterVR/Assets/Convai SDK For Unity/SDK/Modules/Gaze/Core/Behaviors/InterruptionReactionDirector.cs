using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     One-shot startle micro-reaction fired when the character is interrupted mid-sentence
    ///     (<see cref="DialogueState.Speaking" /> → <see cref="DialogueState.Interrupted" />): a
    ///     re-acquisition saccade toward the current target, a triggered blink, and a brief head
    ///     tilt over a ~1 s envelope. Non-repeating — refractory until the character enters
    ///     <see cref="DialogueState.Speaking" /> again, so bouncing in and out of
    ///     <see cref="DialogueState.Interrupted" /> (or lingering there) never replays the beat.
    /// </summary>
    /// <remarks>
    ///     Pure POCO, ticked once per frame from the gaze controller's expression stage
    ///     (<c>LateUpdate</c>) alongside the other one-shot expression directors (blink,
    ///     aversion, backchannel). <see cref="WantsReacquisition" /> and <see cref="WantsBlink" />
    ///     are single-tick pulses — true only on the tick the reaction fires — meant to be
    ///     consumed the same tick, exactly like <c>EyeSolver.SaccadeStartedAmplitude</c>.
    ///     <see cref="TiltDegrees" /> is a continuous envelope over the reaction's lifetime,
    ///     additive to the head-gesture roll channel (<c>HeadTorsoSolveInput.GestureRollDegrees</c>).
    /// </remarks>
    internal sealed class InterruptionReactionDirector
    {
        /// <summary>Total lifetime (seconds) of the reaction envelope.</summary>
        private const float ReactionDurationSeconds = 1f;

        /// <summary>Fraction of the duration spent easing into the tilt's peak.</summary>
        private const float AttackFraction = 0.15f;

        /// <summary>Decay sharpness applied to the tilt after its peak.</summary>
        private const float DecayRate = 2.4f;

        /// <summary>Peak tilt magnitude (degrees) at zero intensity — still readable at the floor.</summary>
        private const float MinTiltDegrees = 2f;

        /// <summary>Peak tilt magnitude (degrees) at full intensity (1.0).</summary>
        private const float MaxTiltDegrees = 4f;

        private DialogueState _lastState = DialogueState.Idle;
        private bool _hasLastState;
        private bool _armed = true;
        private bool _active;
        private float _elapsed;
        private float _tiltSign = 1f;

        /// <summary>True while the reaction envelope is playing.</summary>
        public bool ReactionActive => _active;

        /// <summary>Additive head-roll degrees this tick; exactly zero outside an active reaction.</summary>
        public float TiltDegrees { get; private set; }

        /// <summary>
        ///     True only on the tick the reaction fires — a one-shot pulse requesting the eye
        ///     stage treat the current target as freshly acquired (a fresh re-acquisition
        ///     saccade instead of holding the in-flight fixation).
        /// </summary>
        public bool WantsReacquisition { get; private set; }

        /// <summary>
        ///     True only on the tick the reaction fires — a one-shot pulse requesting a forced
        ///     blink via <see cref="BlinkDirector.TryTriggerForcedBlink" />.
        /// </summary>
        public bool WantsBlink { get; private set; }

        /// <summary>Clears all internal state (disable/rebind).</summary>
        public void Reset()
        {
            _lastState = DialogueState.Idle;
            _hasLastState = false;
            _armed = true;
            _active = false;
            _elapsed = 0f;
            _tiltSign = 1f;
            TiltDegrees = 0f;
            WantsReacquisition = false;
            WantsBlink = false;
        }

        /// <param name="state">Current dialogue state; read fresh every tick by the caller.</param>
        /// <param name="profile">Tuning source; a null or disabled profile suppresses the reaction.</param>
        /// <param name="deltaTime">Frame delta seconds.</param>
        /// <param name="random">Deterministic RNG for the tilt direction.</param>
        public void Tick(
            DialogueState state,
            ConvaiGazeProfile profile,
            float deltaTime,
            ref DeterministicEmbodimentRandom random)
        {
            WantsReacquisition = false;
            WantsBlink = false;

            bool enabled = profile != null && profile.EnableInterruptionReaction;

            DialogueState previous = _hasLastState ? _lastState : state;
            _lastState = state;
            _hasLastState = true;

            // Re-entering Speaking re-arms the beat for the next interruption; this is the
            // entire refractory mechanism — it needs no separate timer.
            if (state == DialogueState.Speaking)
                _armed = true;

            if (enabled && _armed && previous == DialogueState.Speaking && state == DialogueState.Interrupted)
            {
                _armed = false;
                _active = true;
                _elapsed = 0f;
                _tiltSign = random.Value < 0.5f ? -1f : 1f;
                WantsReacquisition = true;
                WantsBlink = true;
            }

            if (!_active)
            {
                TiltDegrees = 0f;
                return;
            }

            _elapsed += deltaTime;
            float p = _elapsed / ReactionDurationSeconds;
            if (p >= 1f)
            {
                _active = false;
                TiltDegrees = 0f;
                return;
            }

            float intensity = profile != null ? Mathf.Clamp01(profile.InterruptionReactionIntensity) : 0f;
            float peakDegrees = Mathf.Lerp(MinTiltDegrees, MaxTiltDegrees, intensity);
            TiltDegrees = _tiltSign * peakDegrees * Envelope(p);
        }

        /// <summary>
        ///     Single-lobe startle shape over normalized phase <paramref name="p" /> ∈ [0,1]: a
        ///     fast eased attack to the peak within <see cref="AttackFraction" /> of the
        ///     duration, then a decay back to exactly zero at <c>p = 1</c>. Internal so the
        ///     endpoints can be asserted directly.
        /// </summary>
        internal static float Envelope(float p)
        {
            p = Mathf.Clamp01(p);
            if (p <= AttackFraction)
                return Mathf.SmoothStep(0f, 1f, p / AttackFraction);

            float tail = (p - AttackFraction) / (1f - AttackFraction);
            return (1f - tail) * Mathf.Exp(-DecayRate * tail);
        }
    }
}
