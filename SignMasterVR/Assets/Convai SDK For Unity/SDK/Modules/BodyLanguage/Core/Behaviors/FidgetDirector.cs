using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO that produces a slow, idle weight-shift "program": the body
    ///     picks a side, eases its weight/side-bend toward it, holds for a
    ///     randomized dwell, eases back to neutral, then waits a randomized gap before the next
    ///     shift. This is the substrate that makes Idle/Thinking/Settling read as a body that
    ///     is alive rather than frozen, without ever reading as a nervous tic.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Program shape.</b> Four phases per cycle: Ease-in → Hold → Ease-out → Gap. The
    ///         side (left/right), the ease durations, the hold duration, and the gap duration are
    ///         all seeded draws so consecutive cycles never read as a metronome (same idiom as
    ///         <see cref="GesticulationDirector" />'s beat interval jitter).
    ///     </para>
    ///     <para>
    ///         <b>Suppression.</b> Hard-suppressed (output decays smoothly to zero, no new cycle
    ///         starts) while <c>state</c> is <see cref="DialogueState.Reacting" /> or
    ///         <see cref="DialogueState.Interrupted" />; while <paramref name="suppression" /> (as
    ///         passed to <see cref="Tick" />) is not <see cref="GestureSuppression.None" />; or
    ///         while the policy's <c>FidgetsEnabled</c> is false. The decay uses the same
    ///         exponential-slew idiom as <see cref="PostureDirector" /> so a state change never
    ///         pops the weight-shift back to zero.
    ///     </para>
    ///     <para>
    ///         <b>Speaking sway.</b> Unlike Reacting/Interrupted,
    ///         <see cref="DialogueState.Speaking" /> does NOT hard-suppress: it runs the same
    ///         engage/hold/return program at a fixed, deliberately-subtle internal rate
    ///         (<see cref="SpeakingLateralSwayRate" />) instead — a "subtle speaking sway", never
    ///         a hip/root weight-shift (the solver only ever applies this as spine side-bend). The
    ///         profile's own Speaking-state <c>FidgetsEnabled</c>/<c>FidgetRate</c> are simply not
    ///         consulted for this state, so this is a pure code behavior with no serialized-profile
    ///         back-compat impact. The sway also survives <see cref="GestureSuppression.UpperBody" />
    ///         while Speaking — the LipSync talk clip reports UpperBody suppression for the whole
    ///         duration of speech, so treating it as a hard-stop here would kill the sway exactly
    ///         when it should show; it is instead reduced downstream at the solver
    ///         ("posture at reduced weight under UpperBody, breath stays"). Only
    ///         <see cref="GestureSuppression.FullBody" /> hard-stops the sway while Speaking; every
    ///         other state keeps the original "any non-None suppression hard-stops" behavior.
    ///     </para>
    ///     <para>
    ///         <b>Clip-fidget cue.</b> The SDK's <see cref="GestureCueKind" /> enum has no kind
    ///         reserved for an idle fidget clip (Affirmative/Negative/Greeting/Uncertain are all
    ///         semantically specific; Emphatic/Beat are reserved for co-speech), and adding a
    ///         public enum value purely for a fidget clip would widen customer API for a behavior
    ///         that reads perfectly well procedurally. This director therefore never requests a
    ///         semantic cue and <see cref="WantsClipFidget" /> always reads <c>false</c>; the
    ///         field and property exist so a real cue kind can be wired in later without changing
    ///         this director's shape.
    ///     </para>
    ///     <para>
    ///         Deterministic and allocation-free: <see cref="Seed" /> takes a
    ///         <see cref="DeterministicEmbodimentRandom" /> seed; identical (seed, tick input
    ///         sequence) pairs always produce an identical weight-shift schedule.
    ///     </para>
    /// </remarks>
    internal sealed class FidgetDirector
    {
        private enum Phase
        {
            Gap,
            EaseIn,
            Hold,
            EaseOut
        }

        /// <summary>± fraction applied to ease/hold/gap durations for anti-metronome jitter.</summary>
        private const float DurationVarianceFraction = 0.35f;

        /// <summary>Time constant (seconds) for the hard-suppression decay-to-zero slew.</summary>
        private const float SuppressionDecaySeconds = 0.5f;

        /// <summary>
        ///     Internal "effective FidgetRate" the Speaking state runs its lateral-sway program
        ///     at, regardless of the profile's own Speaking policy (which keeps
        ///     <c>FidgetsEnabled = false</c> / <c>FidgetRate = 0</c> unchanged — this is a
        ///     code-only behavior, not a profile knob). Doubles as both the cadence divisor
        ///     (<c>effectiveGapSeconds = gapSeconds / rate</c> — a smaller rate means a longer,
        ///     slower gap between shifts) and the final amplitude multiplier (the program's own
        ///     <c>amplitudeScale = rate * (1 - stillness)</c>), so one constant in the 0.3–0.4
        ///     range reads as BOTH "slow rate" and "low amplitude" without a
        ///     second knob. Named <c>speakingLateralSway</c> in remarks/docs — this is a subtle
        ///     speaking sway, never a hip/root weight-shift (see <see cref="Core.Pose.PostureSolver" />'s
        ///     swing-only spine side-bend), and deliberately far subtler than the idle fidget
        ///     program so it never doubles-up on motion over an already-expressive talk clip.
        /// </summary>
        private const float SpeakingLateralSwayRate = 0.35f;

        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        private Phase _phase = Phase.Gap;
        private float _phaseElapsed;
        private float _phaseDurationSeconds;
        private float _targetSign = 1f;

        private float _weightShiftValue;
        private bool _hasStarted;

        /// <summary>
        ///     Exponentially-slewed effective rate actually driving cadence/amplitude this tick —
        ///     NOT snapped straight to <see cref="SpeakingLateralSwayRate" />/the policy's
        ///     FidgetRate on a state flip. Entering/leaving Speaking is the one transition where
        ///     the rate itself changes WITHOUT the hard-suppression decay engaging (Speaking no
        ///     longer suppresses), so without this slew a state flip mid-hold would pop the
        ///     output amplitude in a single frame (e.g. Idle's 0.25 → Speaking's 0.35 rate jumping
        ///     the visible amplitude ~40% instantly) — exactly the kind of snap this module's
        ///     directors otherwise universally avoid.
        /// </summary>
        private float _slewedRate;

        private bool _rateInitialized;

        /// <summary>
        ///     Current weight-shift program value, -1..1. Sign convention matches
        ///     <c>PostureSolveInput.LateralShiftTarget</c>: positive shifts weight/side-bends
        ///     toward the character's own right.
        /// </summary>
        public float WeightShiftValue => _weightShiftValue;

        /// <summary>Whether this tick wants a clip-fidget cue emitted (always <c>false</c> — see class remarks).</summary>
        public bool WantsClipFidget => false;

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        /// <summary>Restores the director to its initial, inactive state. Does not reset the seed.</summary>
        public void Reset()
        {
            _phase = Phase.Gap;
            _phaseElapsed = 0f;
            _phaseDurationSeconds = 0f;
            _targetSign = 1f;
            _weightShiftValue = 0f;
            _hasStarted = false;
            _slewedRate = 0f;
            _rateInitialized = false;
        }

        /// <summary>
        ///     Advances the weight-shift program. Producer-only: never touches bones — the
        ///     controller folds <see cref="WeightShiftValue" /> into
        ///     <c>PostureSolveInput.LateralShiftTarget</c>.
        /// </summary>
        /// <param name="state">
        ///     Current dialogue state — Reacting/Interrupted still hard-suppress (frozen/still).
        ///     Speaking no longer hard-suppresses: it runs the same program at a
        ///     fixed, reduced internal rate (<see cref="SpeakingLateralSwayRate" />) — a subtle
        ///     "speaking sway" — regardless of the profile's own <paramref name="fidgetsEnabled" />/
        ///     <paramref name="fidgetRate" /> for that state (which stay <c>false</c>/<c>0</c> and
        ///     are simply not consulted while Speaking).
        /// </param>
        /// <param name="fidgetsEnabled">The smoothed policy's <c>FidgetsEnabled</c> gate (ignored while Speaking — see <paramref name="state" />).</param>
        /// <param name="fidgetRate">The smoothed policy's <c>FidgetRate</c> (0..1) — scales cycle cadence and amplitude (ignored while Speaking — see <paramref name="state" />).</param>
        /// <param name="suppression">
        ///     Current suppression reported by the conversational gesture performer. While
        ///     Speaking, only <see cref="GestureSuppression.FullBody" /> hard-stops the sway —
        ///     <see cref="GestureSuppression.UpperBody" /> (as reported by the talk clip for the
        ///     whole duration of speech) lets it through, since it is reduced downstream at the
        ///     solver instead. Every other state keeps the original "any non-None
        ///     suppression hard-stops" behavior.
        /// </param>
        /// <param name="stillnessScale">
        ///     0..1 controller-supplied damping (e.g. <see cref="ListeningPostureDirector.StillnessFactor" />) —
        ///     multiplies the output amplitude on top of everything else, without affecting scheduling.
        /// </param>
        /// <param name="deltaTime">Seconds since the previous tick.</param>
        /// <param name="gapSeconds">Profile's mean seconds between weight-shift cycles at FidgetRate == 1 (scales inversely with rate).</param>
        /// <param name="easeSeconds">Profile's ease-in/ease-out duration (seconds) of a single weight-shift.</param>
        /// <param name="holdSeconds">Profile's hold duration (seconds) at the peak of a weight-shift.</param>
        public void Tick(
            DialogueState state,
            bool fidgetsEnabled,
            float fidgetRate,
            GestureSuppression suppression,
            float stillnessScale,
            float deltaTime,
            float gapSeconds,
            float easeSeconds,
            float holdSeconds)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0xF1D6E7A5u);
                _randomSeeded = true;
            }

            float dt = deltaTime > 0f ? deltaTime : 0f;

            // Speaking sway: drive the SAME engage/hold/return program the idle
            // fidget uses, but at a fixed, deliberately-subtle internal rate — never the
            // profile's own Speaking FidgetsEnabled/FidgetRate (which stay false/0 unchanged, so
            // an existing serialized profile's Idle/Listening/Thinking/Settling fidget tuning is
            // completely unaffected). Reacting/Interrupted are untouched: both must stay
            // frozen/still, so they are NOT given this treatment.
            bool isSpeaking = state == DialogueState.Speaking;
            bool effectiveFidgetsEnabled = isSpeaking || fidgetsEnabled;
            float effectiveFidgetRate = isSpeaking ? SpeakingLateralSwayRate : fidgetRate;

            // While Speaking, only FullBody suppression hard-stops the sway: the LipSync talk
            // clip reports UpperBody suppression for the ENTIRE duration of speech, so treating
            // UpperBody as a hard-stop here would kill the speaking sway exactly when it should
            // show. UpperBody is instead reduced downstream at the solver via
            // _postureSuppressionWeight ("posture at reduced weight under UpperBody,
            // breath stays") — not this class's concern. Every other state keeps the original
            // "any non-None suppression hard-stops" behavior unchanged.
            bool suppressionStops = isSpeaking
                ? suppression == GestureSuppression.FullBody
                : suppression != GestureSuppression.None;

            bool hardSuppressed =
                state == DialogueState.Reacting ||
                state == DialogueState.Interrupted ||
                suppressionStops ||
                !effectiveFidgetsEnabled;

            if (hardSuppressed)
            {
                // Never starts a new cycle while suppressed; smoothly decay whatever amplitude
                // was live back to zero and freeze the phase machine (resumes from Gap once
                // suppression lifts, so a released fidget always eases back in cleanly).
                float alpha = 1f - Mathf.Exp(-dt / SuppressionDecaySeconds);
                _weightShiftValue -= _weightShiftValue * alpha;
                if (Mathf.Abs(_weightShiftValue) < 0.001f) _weightShiftValue = 0f;
                _phase = Phase.Gap;
                _phaseElapsed = 0f;
                _phaseDurationSeconds = 0f;
                // Force a fresh, freshly-sampled gap the next time suppression lifts, instead of
                // resuming with a zero-length gap (which would fire a shift the instant it lifts).
                _hasStarted = false;
                return;
            }

            // A suppression shorter than the decay hands back a partial weight-shift that is
            // still live. Ease that residual out with the same slew the suppression path uses
            // BEFORE the scheduler resumes — otherwise the first unsuppressed tick re-enters
            // Phase.Gap and hard-zeros the residual, discarding the smooth decay this director
            // promises (the phase machine only restarts, below, once the residual is gone, since
            // _hasStarted is still false here).
            if (!_hasStarted && Mathf.Abs(_weightShiftValue) > 0.001f)
            {
                float releaseAlpha = 1f - Mathf.Exp(-dt / SuppressionDecaySeconds);
                _weightShiftValue -= _weightShiftValue * releaseAlpha;
                if (Mathf.Abs(_weightShiftValue) < 0.001f) _weightShiftValue = 0f;
                return;
            }

            float rateTarget = Mathf.Clamp01(effectiveFidgetRate);

            if (!_rateInitialized)
            {
                // First-ever active tick (or the first since Reset): snap rather than ease in
                // from zero, matching every other director's "first tick never ramps from a
                // zeroed state" convention.
                _slewedRate = rateTarget;
                _rateInitialized = true;
            }
            else
            {
                float rateAlpha = 1f - Mathf.Exp(-dt / SuppressionDecaySeconds);
                _slewedRate += (rateTarget - _slewedRate) * rateAlpha;
            }

            float rate = _slewedRate;
            if (rate <= 0.001f)
            {
                _weightShiftValue = 0f;
                return;
            }

            // Higher FidgetRate ⇒ shorter gap, more frequent cycles. Ease/hold durations are
            // left rate-independent (a shift itself always reads at a natural human pace); only
            // the CADENCE between shifts scales with the policy's rate.
            float effectiveGapSeconds = Mathf.Max(0.1f, gapSeconds) / rate;
            float effectiveEaseSeconds = Mathf.Max(0.05f, easeSeconds);
            float effectiveHoldSeconds = Mathf.Max(0.05f, holdSeconds);

            if (!_hasStarted)
            {
                // First-ever active tick: start the phase machine on a gap so a fresh director
                // never opens with an immediate shift (mirrors "never snaps on enable" elsewhere).
                _hasStarted = true;
                _phase = Phase.Gap;
                _phaseElapsed = 0f;
                _phaseDurationSeconds = SampleVaried(effectiveGapSeconds);
            }

            _phaseElapsed += dt;

            switch (_phase)
            {
                case Phase.Gap:
                    _weightShiftValue = 0f;
                    if (_phaseElapsed >= _phaseDurationSeconds)
                    {
                        _phase = Phase.EaseIn;
                        _phaseElapsed = 0f;
                        _phaseDurationSeconds = SampleVaried(effectiveEaseSeconds);
                        _targetSign = _random.Value < 0.5f ? -1f : 1f;
                    }
                    break;

                case Phase.EaseIn:
                {
                    float t = _phaseDurationSeconds > 0f ? Mathf.Clamp01(_phaseElapsed / _phaseDurationSeconds) : 1f;
                    _weightShiftValue = _targetSign * EaseInOutQuad(t);
                    if (t >= 1f)
                    {
                        _phase = Phase.Hold;
                        _phaseElapsed = 0f;
                        _phaseDurationSeconds = SampleVaried(effectiveHoldSeconds);
                    }
                    break;
                }

                case Phase.Hold:
                    _weightShiftValue = _targetSign;
                    if (_phaseElapsed >= _phaseDurationSeconds)
                    {
                        _phase = Phase.EaseOut;
                        _phaseElapsed = 0f;
                        _phaseDurationSeconds = SampleVaried(effectiveEaseSeconds);
                    }
                    break;

                case Phase.EaseOut:
                {
                    float t = _phaseDurationSeconds > 0f ? Mathf.Clamp01(_phaseElapsed / _phaseDurationSeconds) : 1f;
                    _weightShiftValue = _targetSign * (1f - EaseInOutQuad(t));
                    if (t >= 1f)
                    {
                        _phase = Phase.Gap;
                        _phaseElapsed = 0f;
                        _phaseDurationSeconds = SampleVaried(effectiveGapSeconds);
                        _weightShiftValue = 0f;
                    }
                    break;
                }
            }

            // Amplitude scales with FidgetRate (a subtler policy still shows small shifts, not
            // just slower ones) and the controller-supplied stillness damping (e.g. attentive
            // listening quiets fidgets without turning the scheduler off outright).
            float amplitudeScale = Mathf.Clamp01(rate) * Mathf.Clamp01(1f - Mathf.Clamp01(stillnessScale));
            _weightShiftValue *= amplitudeScale;
        }

        private static float EaseInOutQuad(float t) =>
            t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;

        private float SampleVaried(float baseSeconds)
        {
            float variance = baseSeconds * DurationVarianceFraction;
            return Mathf.Max(0.05f, baseSeconds + _random.Range(-variance, variance));
        }
    }
}
