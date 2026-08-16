using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO that produces the slow weight-shift/stance program:
    ///     the body periodically re-distributes its weight to the opposite side, with a small
    ///     pelvis yaw and a spine counter-curve that keeps the head over the base of support —
    ///     the substrate a standing, conversing body reads as alive rather than planted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Program shape.</b> Unlike <see cref="FidgetDirector" />'s ease-in/hold/ease-out/
    ///         gap cycle back to a neutral pose, this director holds a persistent asymmetric
    ///         stance and periodically re-targets it to the opposite side — a standing body does
    ///         not return to a perfectly symmetric stance between weight shifts. Every output is
    ///         exponentially slewed toward its current target with time constant
    ///         <c>transferSeconds / 3</c> (~95% settled), so a re-target never pops.
    ///     </para>
    ///     <para>
    ///         <b>Scheduling.</b> The next shift fires after <c>interval ± variance</c>, scaled by
    ///         the current dialogue state (Idle ×1, Listening ×1.8, Speaking ×1.6, Thinking ×1.4).
    ///         Reacting/Interrupted/Settling never schedule a new shift (the body holds still
    ///         through those beats). Entering Thinking freezes the schedule and holds a fixed
    ///         asymmetric stance (±0.7 lateral) for the whole state — a thinking body plants
    ///         itself rather than continuing to shift.
    ///     </para>
    ///     <para>
    ///         <b>Suppression.</b> Any non-<see cref="GestureSuppression.None" /> suppression
    ///         freezes scheduling and slews every output back toward neutral over
    ///         <c>transferSeconds</c> — a suppressed body hands the pelvis back to whatever else
    ///         owns it (locomotion, a full-body clip). When suppression lifts after having held
    ///         for at least two seconds, the next shift is scheduled soon (0.5–1.5s) at a reduced
    ///         (0.6×) magnitude — a "settle step" that reads as the body finding its feet again
    ///         after locomotion or an action, rather than snapping straight into a full shift.
    ///     </para>
    ///     <para>
    ///         Deterministic and allocation-free: <see cref="Seed" /> takes a
    ///         <see cref="DeterministicEmbodimentRandom" /> seed; identical (seed, tick input
    ///         sequence) pairs always produce an identical stance schedule.
    ///     </para>
    /// </remarks>
    internal sealed class StanceDirector
    {
        /// <summary>Fraction of the pelvis lateral offset the spine counter-curves against, keeping the head over the base of support.</summary>
        private const float CounterShare = 0.65f;

        /// <summary>Below this |current-target| difference, <see cref="IsShifting" /> reports settled.</summary>
        private const float ShiftEpsilon = 0.02f;

        /// <summary>Seconds a suppression must hold before its release arms a "settle step".</summary>
        private const float SettleSuppressedThresholdSeconds = 2f;

        /// <summary>Fixed lateral target magnitude while holding the Thinking asymmetric stance.</summary>
        private const float ThinkingHoldLateralMagnitude = 0.7f;

        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        private float _currentLateral;
        private float _currentYaw;
        private float _targetLateral;
        private float _targetYaw;

        // Stance pre-load / anticipation: a weight shift loads the destination
        // hip (obliquity) before the lateral/yaw travel itself starts — the body anticipates a
        // step before it takes it. FireShift sets _targetObliquity immediately but stashes the
        // lateral/yaw retarget as PENDING for a short pre-load window (_lateralRetargetCountdown,
        // seconds remaining; 0 = none pending), only applying it to _targetLateral/_targetYaw once
        // the countdown elapses.
        private float _currentObliquity;
        private float _targetObliquity;
        private float _pendingLateralTarget;
        private float _pendingYawTarget;
        private float _lateralRetargetCountdown;

        private float _timeToNextShift;
        private bool _hasScheduledFirstShift;
        private bool _nextShiftIsSettleStep;

        private DialogueState _lastState = DialogueState.Idle;
        private bool _hasLastState;
        private bool _thinkingHoldActive;

        private GestureSuppression _lastSuppression = GestureSuppression.None;
        private float _suppressedElapsedSeconds;
        private bool _pendingSettleStep;

        /// <summary>
        ///     This tick's richness gain, clamped to 0..1 and applied ONLY to the
        ///     settle-step magnitude scale in <see cref="FireShift" /> — richness gates how much
        ///     of the optional "settling in" repertoire beat shows, not the regular weight-shift
        ///     magnitude. Defaults to 1 (Natural, no-op) so a caller that never passes it composes
        ///     bit-identically to the pre-dial behavior.
        /// </summary>
        private float _richnessGain = 1f;

        /// <summary>Pelvis lateral weight-shift target, -1..1, +right.</summary>
        public float PelvisLateral01 => _currentLateral;

        /// <summary>Pelvis yaw target, -1..1.</summary>
        public float PelvisYaw01 => _currentYaw;

        /// <summary>
        ///     Pelvis obliquity (hip-hike) target, -1..1 (anticipation). Leads
        ///     <see cref="PelvisLateral01" />/<see cref="PelvisYaw01" /> by the pre-load window on
        ///     every scheduled weight shift (obliquity retargets immediately in
        ///     <see cref="FireShift" />; the lateral/yaw travel itself waits out a short
        ///     <c>0.25 × transferSeconds</c> countdown, clamped 0.15–0.6s, before retargeting) — the
        ///     body loads the destination hip before it actually shifts weight onto it. At steady
        ///     state (no shift in flight) this equals <see cref="PelvisLateral01" /> exactly, since
        ///     both then share the same settled target and slew.
        /// </summary>
        public float PelvisObliquity01 => _currentObliquity;

        /// <summary>Spine counter-lateral curve keeping the head over the base of support, -1..1.</summary>
        public float SpineCounterLateral01 => -_currentLateral * CounterShare;

        /// <summary>Whether the pelvis is actively transferring toward a new target this tick.</summary>
        public bool IsShifting =>
            Mathf.Abs(_currentLateral - _targetLateral) > ShiftEpsilon ||
            Mathf.Abs(_currentYaw - _targetYaw) > ShiftEpsilon;

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        /// <summary>Restores the director to its initial, inactive state. Does not reset the seed.</summary>
        public void Reset()
        {
            _currentLateral = 0f;
            _currentYaw = 0f;
            _targetLateral = 0f;
            _targetYaw = 0f;
            _currentObliquity = 0f;
            _targetObliquity = 0f;
            _pendingLateralTarget = 0f;
            _pendingYawTarget = 0f;
            _lateralRetargetCountdown = 0f;
            _timeToNextShift = 0f;
            _hasScheduledFirstShift = false;
            _nextShiftIsSettleStep = false;
            _lastState = DialogueState.Idle;
            _hasLastState = false;
            _thinkingHoldActive = false;
            _lastSuppression = GestureSuppression.None;
            _suppressedElapsedSeconds = 0f;
            _pendingSettleStep = false;
            _richnessGain = 1f;
        }

        /// <summary>Advances the stance schedule and slewed outputs. Producer-only: never touches bones.</summary>
        /// <param name="richnessGain">
        ///     Expressiveness richness gain, defaults to 1 (Natural) — see
        ///     <see cref="_richnessGain" />.
        /// </param>
        public void Tick(
            DialogueState state,
            bool enabled,
            GestureSuppression suppression,
            float intervalSeconds,
            float intervalVariance,
            float transferSeconds,
            float deltaTime,
            float richnessGain = 1f)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0x57A2CEDAu);
                _randomSeeded = true;
            }

            _richnessGain = Mathf.Clamp01(richnessGain);

            float dt = deltaTime > 0f ? deltaTime : 0f;
            float safeTransferSeconds = Mathf.Max(0.05f, transferSeconds);
            float tau = safeTransferSeconds / 3f;
            float alpha = tau > 0f ? 1f - Mathf.Exp(-dt / tau) : 1f;

            TrackSettleWindow(suppression, dt);

            bool suppressed = suppression != GestureSuppression.None;
            bool frozen = suppressed || !enabled;

            if (frozen)
            {
                _targetLateral = 0f;
                _targetYaw = 0f;
                _targetObliquity = 0f;
                _currentLateral += (_targetLateral - _currentLateral) * alpha;
                _currentYaw += (_targetYaw - _currentYaw) * alpha;
                _currentObliquity += (_targetObliquity - _currentObliquity) * alpha;

                // A frozen body hands the pelvis back — any in-flight pre-load retarget
                // is stale and must not later apply once unfrozen.
                _pendingLateralTarget = 0f;
                _pendingYawTarget = 0f;
                _lateralRetargetCountdown = 0f;

                // Force a fresh schedule once unfrozen instead of resuming a stale countdown.
                _hasScheduledFirstShift = false;
                _thinkingHoldActive = false;
                _lastState = state;
                _hasLastState = true;
                return;
            }

            bool enteringThinking = state == DialogueState.Thinking &&
                (!_hasLastState || _lastState != DialogueState.Thinking);
            bool leavingThinking = state != DialogueState.Thinking &&
                _hasLastState && _lastState == DialogueState.Thinking;

            if (enteringThinking)
            {
                _thinkingHoldActive = true;
                float sign = _random.Value < 0.5f ? -1f : 1f;
                _targetLateral = sign * ThinkingHoldLateralMagnitude;
                _targetYaw = _random.Range(-1f, 1f) * 0.6f;
                // Thinking-hold entry: no pre-load anticipation needed — this
                // is a single deliberate plant, not a scheduled shift — so obliquity retargets in
                // lockstep with lateral. Any pre-load retarget still in flight from a shift that
                // was interrupted by entering Thinking is now stale and must not later override
                // this plant.
                _targetObliquity = _targetLateral;
                _pendingLateralTarget = 0f;
                _pendingYawTarget = 0f;
                _lateralRetargetCountdown = 0f;
            }
            else if (leavingThinking)
            {
                _thinkingHoldActive = false;
            }

            bool schedulesShifts = !_thinkingHoldActive &&
                state != DialogueState.Reacting &&
                state != DialogueState.Interrupted &&
                state != DialogueState.Settling;

            if (schedulesShifts)
            {
                if (!_hasScheduledFirstShift)
                {
                    ScheduleNextShift(intervalSeconds, intervalVariance, state);
                    _hasScheduledFirstShift = true;
                }

                _timeToNextShift -= dt;
                if (_timeToNextShift <= 0f)
                {
                    FireShift(transferSeconds);
                    ScheduleNextShift(intervalSeconds, intervalVariance, state);
                }
            }

            // Stance pre-load countdown: once the short pre-load window FireShift
            // armed elapses, the stashed lateral/yaw targets finally take effect — obliquity (set
            // immediately in FireShift, above) has already been leading the slew below for however
            // many ticks this countdown has been running.
            if (_lateralRetargetCountdown > 0f)
            {
                _lateralRetargetCountdown -= dt;
                if (_lateralRetargetCountdown <= 0f)
                {
                    _lateralRetargetCountdown = 0f;
                    _targetLateral = _pendingLateralTarget;
                    _targetYaw = _pendingYawTarget;
                }
            }

            _currentLateral += (_targetLateral - _currentLateral) * alpha;
            _currentYaw += (_targetYaw - _currentYaw) * alpha;
            _currentObliquity += (_targetObliquity - _currentObliquity) * alpha;

            _lastState = state;
            _hasLastState = true;
        }

        private void TrackSettleWindow(GestureSuppression suppression, float dt)
        {
            bool suppressedNow = suppression != GestureSuppression.None;
            if (suppressedNow)
            {
                _suppressedElapsedSeconds += dt;
            }
            else if (_lastSuppression != GestureSuppression.None)
            {
                if (_suppressedElapsedSeconds >= SettleSuppressedThresholdSeconds)
                    _pendingSettleStep = true;
                _suppressedElapsedSeconds = 0f;
            }

            _lastSuppression = suppression;
        }

        private void ScheduleNextShift(float intervalSeconds, float intervalVariance, DialogueState state)
        {
            if (_pendingSettleStep)
            {
                _nextShiftIsSettleStep = true;
                _pendingSettleStep = false;
                _timeToNextShift = _random.Range(0.5f, 1.5f);
                return;
            }

            _nextShiftIsSettleStep = false;

            float stateScale = state switch
            {
                DialogueState.Idle => 1f,
                DialogueState.Listening => 1.8f,
                DialogueState.Speaking => 1.6f,
                DialogueState.Thinking => 1.4f,
                _ => 1f
            };

            float baseInterval = Mathf.Max(0.5f, intervalSeconds) * stateScale;
            float variance = Mathf.Max(0f, intervalVariance);
            _timeToNextShift = Mathf.Max(0.25f, baseInterval + _random.Range(-variance, variance));
        }

        /// <param name="transferSeconds">
        ///     This tick's weight-shift transfer duration — sizes the pre-load window:
        ///     <c>Mathf.Clamp(0.25f * transferSeconds, 0.15f, 0.6f)</c> seconds elapse
        ///     between the obliquity retarget below and the lateral/yaw retarget actually taking
        ///     effect (see <see cref="Tick" />'s countdown handling).
        /// </param>
        private void FireShift(float transferSeconds)
        {
            // Settle-step magnitude is richness-gated: the 0.6× "settling back in"
            // beat is part of the optional repertoire richness scales, not the regular shift
            // magnitude. At Natural (_richnessGain == 1) this is exactly the pre-dial 0.6×.
            float magnitudeScale = _nextShiftIsSettleStep ? 0.6f * _richnessGain : 1f;

            float oppositeSign = Mathf.Abs(_targetLateral) < 1e-4f
                ? (_random.Value < 0.5f ? -1f : 1f)
                : (_targetLateral > 0f ? -1f : 1f);

            float newLateral = oppositeSign * _random.Range(0.55f, 1.0f) * magnitudeScale;
            float newYaw = _random.Range(-1f, 1f) * 0.6f * magnitudeScale;

            // Stance pre-load / anticipation: obliquity retargets to the NEW
            // side immediately — the body loads the destination hip before it actually shifts
            // weight onto it — while the lateral/yaw travel itself is stashed as PENDING and only
            // takes effect once the pre-load countdown (Tick) elapses.
            _targetObliquity = newLateral;
            _pendingLateralTarget = newLateral;
            _pendingYawTarget = newYaw;
            _lateralRetargetCountdown = Mathf.Clamp(0.25f * transferSeconds, 0.15f, 0.6f);
        }
    }
}
