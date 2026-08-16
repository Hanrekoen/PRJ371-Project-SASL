using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    internal enum TurnTakingBreakKind
    {
        None = 0,
        Opening = 1,
        MidTurn = 2
    }

    /// <summary>
    ///     Turn-taking gaze choreography over the arc of one spoken utterance: on
    ///     <see cref="DialogueState.Speaking" /> entry the character may carry through a brief
    ///     Thinking-authorized opening break, holds contact through short answers, uses sparse
    ///     bounded breaks only in extended answers, then produces a
    ///     one-shot "floor-yield" cue near the end — a deliberate blink and, when enabled, a tiny head dip
    ///     under a pinned engagement — handing the floor back to the player.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Planning breaks.</b> With probability <see cref="ConvaiGazeProfile.PlanningBreakProbability" />,
    ///         a Thinking-authorized opening break or up to two well-spaced extended-answer
    ///         breaks may play. Short/reactive replies never deliberately break contact. A
    ///         break never fires while an eye-contact lock is in force. Rather than owning its
    ///         own offset channel, this director drives the existing <see cref="AversionDirector" />
    ///         directly: on the tick a break starts, <see cref="PlanningBreakStarted" /> pulses
    ///         with a descriptor. Opening breaks use a cognitive up/side shape with moderate head
    ///         participation; mid-turn breaks use a smaller natural eye-led shape. The caller feeds
    ///         that descriptor into <see cref="AversionDirector.Tick" /> instead of the state's
    ///         authored aversion for the bounded <see cref="PlanningBreakActive" /> interval.
    ///     </para>
    ///     <para>
    ///         <b>Single owner.</b> While enabled, <see cref="AversionSuppressionFactor" /> is 0
    ///         throughout Speaking so ordinary state-policy aversion cannot create a second,
    ///         overlapping cadence.
    ///     </para>
    ///     <para>
    ///         <b>Floor-yield.</b> Once per utterance, triggered by whichever comes first: a
    ///         character speech stopping, sustained post-final energy decay, or Speaking exit.
    ///         A final transcript only arms/classifies because it may precede audio completion.
    ///         The yield cancels any break and pins engagement at 1 for ~0.8 s
    ///         (<see cref="YieldEngagementPinActive" />), requests one forced blink
    ///         (<see cref="WantsYieldBlink" />, a one-shot pulse), and — when
    ///         <see cref="ConvaiGazeProfile.EnableYieldHeadDip" /> is enabled — plays a small downward
    ///         head-pitch envelope (<see cref="YieldHeadDipOffset" />). With no speech-energy
    ///         provider (energy always 0) the decay trigger never fires and the yield falls back
    ///         to the Speaking-exit fallback, which is the intended degradation.
    ///     </para>
    ///     <para>
    ///         Pure POCO, ticked once per frame from the gaze controller's expression stage
    ///         (<c>LateUpdate</c>), before <see cref="AversionDirector.Tick" /> so this tick's
    ///         break decision can drive it.
    ///     </para>
    /// </remarks>
    internal sealed class TurnTakingDirector
    {
        /// <summary>Earliest start delay (seconds) after the Speaking edge for the planning break.</summary>
        private const float PlanningBreakStartMin = 0.05f;

        /// <summary>Latest start delay (seconds) after the Speaking edge for the planning break.</summary>
        private const float PlanningBreakStartMax = 0.15f;

        /// <summary>Shortest planning-break duration (seconds).</summary>
        private const float PlanningBreakDurationMin = 0.35f;

        /// <summary>Longest planning-break duration (seconds).</summary>
        private const float PlanningBreakDurationMax = 0.7f;

        private const float ThinkingDwellForOpeningBreakSeconds = 0.35f;
        private const int ExtendedUtteranceWordCount = 16;
        private const float ExtendedUtteranceElapsedSeconds = 4.5f;
        private const float FirstMidBreakSeconds = 5f;
        private const float MidBreakContactGapSeconds = 4.5f;
        private const float MidBreakDurationMin = 0.25f;
        private const float MidBreakDurationMax = 0.55f;
        private const int MaxMidBreaks = 2;
        private const float YieldEnergyMinElapsedSeconds = 1.5f;
        private const float YieldEnergySustainSeconds = 0.2f;
        private const float SpeechStopDebounceSeconds = 0.12f;

        /// <summary>Length (seconds) of the normal-aversion suppression window at the start of Speaking.</summary>
        /// <summary>Fraction of the utterance's peak smoothed energy below which a decay is detected.</summary>
        private const float YieldEnergyPeakFraction = 0.3f;

        /// <summary>Time constant (seconds) of the speech-energy smoothing used for decay detection.</summary>
        private const float YieldEnergySmoothingSeconds = 0.15f;

        /// <summary>Minimum observed peak energy before decay detection is trusted (guards a silent/no-provider utterance).</summary>
        private const float MinEnergyForPeakTracking = 0.05f;

        /// <summary>Duration (seconds) the floor-yield pins engagement at 1.</summary>
        private const float YieldEngagementPinSeconds = 0.8f;

        /// <summary>Peak downward pitch (degrees) of the floor-yield head dip.</summary>
        private const float YieldHeadDipPeakDegrees = 2f;

        /// <summary>Envelope duration (seconds) of the floor-yield head dip.</summary>
        private const float YieldHeadDipEnvelopeSeconds = 0.4f;

        private DialogueState _lastState = DialogueState.Idle;
        private bool _hasLastState;
        private float _speakingElapsed;
        private float _stateElapsed;

        private bool _breakScheduled;
        private float _breakStartDelayRemaining;
        private float _breakDuration;
        private bool _breakActive;
        private float _breakRemaining;
        private int _midBreakCount;
        private float _nextMidBreakEvaluation;
        private bool _finalTranscriptSeen;
        private int _finalTranscriptWordCount;
        private bool _speechObserved;
        private float _lowEnergyElapsed;
        private float _speechStopElapsed;
        private bool _openingEvidence;
        private bool _openingEvaluated;
        private TurnTakingBreakKind _scheduledBreakKind;

        private bool _yieldFiredThisUtterance;
        private float _energyPeak;
        private float _smoothedEnergy;
        private float _pinRemaining;
        private bool _headDipActive;
        private float _headDipElapsed;

        /// <summary>True while the planning-break beat is being driven through <see cref="AversionDirector" />.</summary>
        public bool PlanningBreakActive => _breakActive;

        /// <summary>
        ///     True only on the tick the planning break starts — a one-shot pulse requesting the
        ///     caller invoke <see cref="AversionDirector.ForceBeat" /> with this tick's
        ///     mode, strength, duration, and head-participation descriptor.
        /// </summary>
        public bool PlanningBreakStarted { get; private set; }

        /// <summary>The sampled duration (seconds) of this utterance's planning break, valid whenever it was scheduled.</summary>
        public float PlanningBreakDurationSeconds => _breakDuration;

        public TurnTakingBreakKind StartedBreakKind { get; private set; }

        public GazeAversionMode StartedAversionMode =>
            StartedBreakKind == TurnTakingBreakKind.MidTurn
                ? GazeAversionMode.Natural
                : GazeAversionMode.Cognitive;

        public float StartedAversionStrength =>
            StartedBreakKind == TurnTakingBreakKind.MidTurn ? 0.65f : 1f;

        /// <summary>
        ///     How much of the active break's beat the head carries (0–1); 1 whenever no break is
        ///     running, so the caller can apply it unconditionally.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Eased, and that is not cosmetic. This is a GAIN on the aversion offset, and the
        ///         aversion offset is layered onto the head pose <i>after</i> the two-lane
        ///         actuator — downstream of everything that shapes motion — so a step in the gain
        ///         is indistinguishable from a step in the pose: the head jumps by
        ///         <c>offset × Δscale</c> in one frame, with nothing in the way.
        ///     </para>
        ///     <para>
        ///         It used to step twice per utterance. The floor-yield cancels any running break,
        ///         and the cancel drove this to 0 while the beat it belonged to was still easing
        ///         out; then the caller, which switched the whole term off outside Speaking, put
        ///         it back to 1 on the Speaking exit edge with that same residue still on the
        ///         offset. Both are now ramps, and a cancelled break returns the head to full
        ///         participation rather than to none — there is no break left for it to
        ///         under-participate in, and the AversionDirector is already fading the offset.
        ///     </para>
        /// </remarks>
        public float HeadParticipationScale { get; private set; } = 1f;

        /// <summary>Response rate (per second) of <see cref="HeadParticipationScale" />'s ease.</summary>
        /// <remarks>
        ///     Slower than <c>AversionDirector.BeatEaseSharpness</c> (9), so the gain can never be
        ///     the fastest thing on a channel whose signal is itself already eased.
        /// </remarks>
        private const float HeadParticipationSharpness = 6f;

        private float _headParticipationTarget = 1f;

        /// <summary>
        ///     Multiplier the caller applies to the state policy's authored aversion strength
        ///     while <see cref="PlanningBreakActive" /> is false: 0 throughout Speaking while
        ///     turn-taking gaze is enabled, 1 outside Speaking or when the feature is disabled.
        /// </summary>
        public float AversionSuppressionFactor { get; private set; } = 1f;

        /// <summary>
        ///     True only on the tick the floor-yield fires — a one-shot pulse requesting a
        ///     forced blink via <see cref="BlinkDirector.TryTriggerForcedBlink" />.
        /// </summary>
        public bool WantsYieldBlink { get; private set; }

        /// <summary>True while the floor-yield's engagement pin is holding engagement at 1.</summary>
        public bool YieldEngagementPinActive => _pinRemaining > 0f;

        /// <summary>
        ///     Additive head-gesture offset (yaw/pitch degrees) for the floor-yield head dip;
        ///     exactly <see cref="Vector2.zero" /> outside its short envelope.
        /// </summary>
        public Vector2 YieldHeadDipOffset { get; private set; }

        /// <summary>Cancels a scheduled or active planning break without disturbing floor-yield state.</summary>
        public void CancelPlanningBreak()
        {
            _breakScheduled = false;
            _breakStartDelayRemaining = 0f;
            _breakActive = false;
            _breakRemaining = 0f;
            PlanningBreakStarted = false;
            _scheduledBreakKind = TurnTakingBreakKind.None;
            StartedBreakKind = TurnTakingBreakKind.None;
            _headParticipationTarget = 1f;
        }

        /// <summary>Clears all internal state (disable/rebind).</summary>
        public void Reset()
        {
            _lastState = DialogueState.Idle;
            _hasLastState = false;
            _speakingElapsed = 0f;
            _stateElapsed = 0f;

            _breakScheduled = false;
            _breakStartDelayRemaining = 0f;
            _breakDuration = 0f;
            _breakActive = false;
            _breakRemaining = 0f;
            _midBreakCount = 0;
            _nextMidBreakEvaluation = 0f;
            _finalTranscriptSeen = false;
            _finalTranscriptWordCount = 0;
            _speechObserved = false;
            _lowEnergyElapsed = 0f;
            _speechStopElapsed = 0f;
            _openingEvidence = false;
            _openingEvaluated = false;
            _scheduledBreakKind = TurnTakingBreakKind.None;
            StartedBreakKind = TurnTakingBreakKind.None;
            HeadParticipationScale = 1f;
            _headParticipationTarget = 1f;
            PlanningBreakStarted = false;
            AversionSuppressionFactor = 1f;

            _yieldFiredThisUtterance = false;
            _energyPeak = 0f;
            _smoothedEnergy = 0f;
            _pinRemaining = 0f;
            _headDipActive = false;
            _headDipElapsed = 0f;
            WantsYieldBlink = false;
            YieldHeadDipOffset = Vector2.zero;
        }

        /// <param name="state">Current dialogue state; read fresh every tick by the caller.</param>
        /// <param name="profile">Tuning source; a null or disabled profile suppresses the whole arc.</param>
        /// <param name="eyeContactLocked">Whether an eye-contact lock is in force this tick — blocks the planning break only.</param>
        /// <param name="finalTranscriptReceived">One-shot pulse: a final transcript arrived since the last tick.</param>
        /// <param name="speechEnergy">This tick's normalized speech energy (0 when no provider is bound).</param>
        /// <param name="deltaTime">Frame delta seconds.</param>
        /// <param name="random">Deterministic RNG for the break's timing/schedule roll.</param>
        public void Tick(
            DialogueState state,
            ConvaiGazeProfile profile,
            bool eyeContactLocked,
            bool finalTranscriptReceived,
            float speechEnergy,
            float deltaTime,
            ref DeterministicEmbodimentRandom random)
        {
            Tick(state, profile, eyeContactLocked, finalTranscriptReceived,
                finalTranscriptReceived ? ExtendedUtteranceWordCount : 0,
                hasSpeechActivitySignal: false, speechActive: false,
                speechEnergy, deltaTime, ref random);
        }

        public void Tick(
            DialogueState state,
            ConvaiGazeProfile profile,
            bool eyeContactLocked,
            bool finalTranscriptReceived,
            int finalTranscriptWordCount,
            bool hasSpeechActivitySignal,
            bool speechActive,
            float speechEnergy,
            float deltaTime,
            ref DeterministicEmbodimentRandom random)
        {
            PlanningBreakStarted = false;
            WantsYieldBlink = false;

            bool enabled = profile != null && profile.EnableTurnTakingGaze;

            bool hadLastState = _hasLastState;
            DialogueState previous = hadLastState ? _lastState : state;
            float previousStateDwell = _stateElapsed;
            if (!_hasLastState || state != _lastState)
                _stateElapsed = 0f;
            else
                _stateElapsed += deltaTime;
            _lastState = state;
            _hasLastState = true;

            bool enteredSpeaking = state == DialogueState.Speaking &&
                                   (!hadLastState || previous != DialogueState.Speaking);
            bool exitedSpeaking = state != DialogueState.Speaking && previous == DialogueState.Speaking;

            if (enteredSpeaking)
                BeginUtterance(
                    enabled, eyeContactLocked, previous, previousStateDwell,
                    hasSpeechActivitySignal, speechActive, profile, ref random);

            if (!enabled)
            {
                _breakScheduled = false;
                _breakActive = false;
                _scheduledBreakKind = TurnTakingBreakKind.None;
                StartedBreakKind = TurnTakingBreakKind.None;
                _headParticipationTarget = 1f;
                EaseHeadParticipation(deltaTime);
                AversionSuppressionFactor = 1f;
                DecayPin(deltaTime);
                YieldHeadDipOffset = Vector2.zero;
                _headDipActive = false;
                return;
            }

            if (state == DialogueState.Speaking)
            {
                _speakingElapsed += deltaTime;
                if (finalTranscriptReceived)
                {
                    _finalTranscriptSeen = true;
                    _finalTranscriptWordCount = Mathf.Max(_finalTranscriptWordCount, finalTranscriptWordCount);
                    TryScheduleOpeningBreak(profile, eyeContactLocked, ref random);
                }
                if (_speakingElapsed > 1f) _openingEvaluated = true;

                TickPlanningBreak(profile, eyeContactLocked, deltaTime, ref random);
                // TurnTakingDirector is the sole intentional Speaking-break owner while enabled.
                AversionSuppressionFactor = 0f;
                TickYieldTrigger(
                    profile, hasSpeechActivitySignal, speechActive, speechEnergy, deltaTime);
            }
            else
            {
                AversionSuppressionFactor = 1f;
                _breakActive = false;
                _breakScheduled = false;
                // Outside Speaking there is no break, so the head carries the whole beat. The
                // caller no longer switches this term on and off at the Speaking edge — it reads
                // the scale unconditionally, and the ramp here is what makes that safe.
                _headParticipationTarget = 1f;

                if (exitedSpeaking && !_yieldFiredThisUtterance)
                    FireYield(profile);
            }

            EaseHeadParticipation(deltaTime);
            DecayPin(deltaTime);
            TickHeadDip(deltaTime);
        }

        /// <summary>
        ///     Advances <see cref="HeadParticipationScale" /> toward its target: instantly when it
        ///     falls, over a ramp when it rises.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The asymmetry is the whole trick, and it is not a compromise between crispness
        ///         and safety — the two directions genuinely differ. A gain step is a pose step
        ///         only in proportion to the signal it multiplies, and the signal here is the
        ///         aversion offset. The target FALLS when a break starts, and a break starts from
        ///         the contact phase with the offset at rest, so there is nothing to step; letting
        ///         it fall instantly keeps the beat as crisp as it was authored.
        ///     </para>
        ///     <para>
        ///         The target RISES when a break ends or is cancelled — and the floor-yield cancels
        ///         mid-beat, with the offset still most of the way out. That is the direction that
        ///         put <c>offset × Δscale</c> on the head in one frame, so that is the direction
        ///         that ramps.
        ///     </para>
        /// </remarks>
        private void EaseHeadParticipation(float deltaTime)
        {
            if (_headParticipationTarget <= HeadParticipationScale)
            {
                HeadParticipationScale = _headParticipationTarget;
                return;
            }

            float alpha = 1f - Mathf.Exp(-HeadParticipationSharpness * Mathf.Max(0f, deltaTime));
            HeadParticipationScale += (_headParticipationTarget - HeadParticipationScale) * alpha;
        }

        private void BeginUtterance(
            bool enabled,
            bool eyeContactLocked,
            DialogueState previousState,
            float previousStateDwell,
            bool hasSpeechActivitySignal,
            bool speechActive,
            ConvaiGazeProfile profile,
            ref DeterministicEmbodimentRandom random)
        {
            _speakingElapsed = 0f;
            _energyPeak = 0f;
            _smoothedEnergy = 0f;
            _yieldFiredThisUtterance = false;
            _breakActive = false;
            _breakScheduled = false;
            _midBreakCount = 0;
            _nextMidBreakEvaluation = FirstMidBreakSeconds;
            _finalTranscriptSeen = false;
            _finalTranscriptWordCount = 0;
            _speechObserved = hasSpeechActivitySignal && speechActive;
            _lowEnergyElapsed = 0f;
            _speechStopElapsed = 0f;
            _openingEvidence = previousState == DialogueState.Thinking &&
                               previousStateDwell >= ThinkingDwellForOpeningBreakSeconds;
            _openingEvaluated = false;
            _scheduledBreakKind = TurnTakingBreakKind.None;
            StartedBreakKind = TurnTakingBreakKind.None;
            // The target only — the scale itself keeps whatever it was carrying and ramps to it.
            // A new utterance starting is not a reason for the head to jump: the previous one's
            // beat may still be easing out on the same channel.
            _headParticipationTarget = 1f;

            if (!enabled || eyeContactLocked || profile == null) return;
        }

        private void TryScheduleOpeningBreak(
            ConvaiGazeProfile profile,
            bool eyeContactLocked,
            ref DeterministicEmbodimentRandom random)
        {
            if (_openingEvaluated || !_openingEvidence || eyeContactLocked || profile == null) return;
            _openingEvaluated = true;
            if (_finalTranscriptWordCount < ExtendedUtteranceWordCount ||
                random.Value >= Mathf.Clamp01(profile.PlanningBreakProbability)) return;

            _breakScheduled = true;
            _scheduledBreakKind = TurnTakingBreakKind.Opening;
            _breakStartDelayRemaining = random.Range(PlanningBreakStartMin, PlanningBreakStartMax);
            _breakDuration = random.Range(PlanningBreakDurationMin, PlanningBreakDurationMax);
            _midBreakCount++;
        }

        private void TickPlanningBreak(
            ConvaiGazeProfile profile,
            bool eyeContactLocked,
            float deltaTime,
            ref DeterministicEmbodimentRandom random)
        {
            if (eyeContactLocked || _yieldFiredThisUtterance)
            {
                CancelPlanningBreak();
                return;
            }

            if (_breakScheduled)
            {
                _breakStartDelayRemaining -= deltaTime;
                if (_breakStartDelayRemaining <= 0f)
                {
                    _breakScheduled = false;
                    _breakActive = true;
                    _breakRemaining = _breakDuration;
                    StartedBreakKind = _scheduledBreakKind;
                    _headParticipationTarget =
                        StartedBreakKind == TurnTakingBreakKind.MidTurn ? 0.25f : 0.65f;
                    _scheduledBreakKind = TurnTakingBreakKind.None;
                    PlanningBreakStarted = true;
                }

                return;
            }

            if (_breakActive)
            {
                _breakRemaining -= deltaTime;
                if (_breakRemaining <= 0f)
                {
                    _breakActive = false;
                    // The break is over, so the head resumes full participation in whatever beat
                    // follows. Leaving the scale parked on the finished break's value kept the
                    // head under-participating for the rest of the utterance, and then handed the
                    // difference to the next state as a step.
                    _headParticipationTarget = 1f;

                    // Opening breaks are scheduled off the transcript edge, outside the nominal
                    // mid-break timeline, so preserve the contact gap after them here. Mid-turn
                    // breaks already advanced the nominal timeline when they started (below);
                    // re-anchoring them on the frame-quantized end time would accumulate one
                    // frame of drift per break and break frame-rate equivalence of the schedule.
                    if (StartedBreakKind == TurnTakingBreakKind.Opening)
                        _nextMidBreakEvaluation = Mathf.Max(
                            _nextMidBreakEvaluation, _speakingElapsed + MidBreakContactGapSeconds);
                }
            }

            if (_breakActive) return;

            bool extended = _finalTranscriptWordCount >= ExtendedUtteranceWordCount ||
                            _speakingElapsed >= ExtendedUtteranceElapsedSeconds;
            if (!extended || _midBreakCount >= MaxMidBreaks ||
                _speakingElapsed < _nextMidBreakEvaluation) return;

            // _speakingElapsed overshoots the crossed threshold by up to one frame; the
            // threshold itself is the nominal (frame-rate independent) evaluation time, so the
            // rest of the cadence is anchored on it to keep same-seed schedules equivalent
            // across frame rates.
            float nominalEvaluation = _nextMidBreakEvaluation;
            if (profile == null || random.Value >= Mathf.Clamp01(profile.PlanningBreakProbability))
            {
                _nextMidBreakEvaluation = nominalEvaluation + MidBreakContactGapSeconds;
                return;
            }

            _breakDuration = random.Range(MidBreakDurationMin, MidBreakDurationMax);
            _midBreakCount++;
            _nextMidBreakEvaluation = nominalEvaluation + _breakDuration + MidBreakContactGapSeconds;

            // A mid-turn break has no start delay: start it on the evaluation tick itself.
            // Deferring it through the scheduled state would add one frame-rate dependent
            // tick of latency to every mid-turn start.
            _breakActive = true;
            _breakRemaining = _breakDuration;
            _scheduledBreakKind = TurnTakingBreakKind.None;
            StartedBreakKind = TurnTakingBreakKind.MidTurn;
            _headParticipationTarget = 0.25f;
            PlanningBreakStarted = true;
        }

        private void TickYieldTrigger(
            ConvaiGazeProfile profile,
            bool hasSpeechActivitySignal,
            bool speechActive,
            float speechEnergy,
            float deltaTime)
        {
            if (_yieldFiredThisUtterance) return;

            bool speechStopped = false;
            if (hasSpeechActivitySignal)
            {
                if (speechActive)
                {
                    _speechObserved = true;
                    _speechStopElapsed = 0f;
                }
                else if (_speechObserved)
                {
                    _speechStopElapsed += deltaTime;
                    speechStopped = _speechStopElapsed >= SpeechStopDebounceSeconds;
                }
            }

            _energyPeak = Mathf.Max(_energyPeak, speechEnergy);
            float smoothT = YieldEnergySmoothingSeconds > 0f ? Mathf.Clamp01(deltaTime / YieldEnergySmoothingSeconds) : 1f;
            _smoothedEnergy = Mathf.Lerp(_smoothedEnergy, speechEnergy, smoothT);

            bool belowEnergyFloor = _energyPeak > MinEnergyForPeakTracking &&
                                    _smoothedEnergy < _energyPeak * YieldEnergyPeakFraction;
            _lowEnergyElapsed = belowEnergyFloor ? _lowEnergyElapsed + deltaTime : 0f;
            bool energyDecayed = !hasSpeechActivitySignal && _finalTranscriptSeen &&
                                 _speakingElapsed >= YieldEnergyMinElapsedSeconds &&
                                 _lowEnergyElapsed >= YieldEnergySustainSeconds;

            if (speechStopped || energyDecayed)
                FireYield(profile);
        }

        private void FireYield(ConvaiGazeProfile profile)
        {
            _yieldFiredThisUtterance = true;
            CancelPlanningBreak();
            _pinRemaining = YieldEngagementPinSeconds;

            bool blinkEnabled = profile != null && profile.EnableYieldBlink;
            if (blinkEnabled)
                WantsYieldBlink = true;

            bool headDipEnabled = profile != null && profile.EnableYieldHeadDip;
            if (headDipEnabled)
            {
                _headDipActive = true;
                _headDipElapsed = 0f;
            }
        }

        private void DecayPin(float deltaTime)
        {
            if (_pinRemaining <= 0f) return;
            _pinRemaining = Mathf.Max(0f, _pinRemaining - deltaTime);
        }

        private void TickHeadDip(float deltaTime)
        {
            if (!_headDipActive)
            {
                YieldHeadDipOffset = Vector2.zero;
                return;
            }

            _headDipElapsed += deltaTime;
            float p = YieldHeadDipEnvelopeSeconds > 0f ? _headDipElapsed / YieldHeadDipEnvelopeSeconds : 1f;
            if (p >= 1f)
            {
                _headDipActive = false;
                YieldHeadDipOffset = Vector2.zero;
                return;
            }

            // A single smooth up-down bump (0 -> 1 -> 0) over the envelope, nodding downward
            // (negative pitch, matching the codebase's up-positive convention).
            float envelope = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
            YieldHeadDipOffset = new Vector2(0f, -YieldHeadDipPeakDegrees * envelope);
        }
    }
}
