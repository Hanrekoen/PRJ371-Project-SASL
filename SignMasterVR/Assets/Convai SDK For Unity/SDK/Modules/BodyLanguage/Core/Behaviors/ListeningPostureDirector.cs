using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO that produces the embodied-listening posture:
    ///     a lean-in bias that engages while <see cref="DialogueState.Listening" /> and the
    ///     state policy's <c>ListeningPostureEnabled</c> is set, a stillness factor that damps
    ///     the fidget director while attentive, and an occasional slow tilt-hold (never a nod —
    ///     a listening character tilts its head to show attention, it does not bob).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Lean-in.</b> <see cref="LeanInBias" /> slews toward the policy's
    ///         <c>ListeningLeanIn</c> (0..1) over roughly a second while Listening, and slews back
    ///         to zero as soon as the state leaves Listening or the policy disables listening
    ///         posture — the controller folds this additively into the continuous posture lean
    ///         target (clamped), so it never fights the state policy's own
    ///         <c>SagittalLeanBias</c>, only adds on top of it.
    ///     </para>
    ///     <para>
    ///         <b>Stillness.</b> <see cref="StillnessFactor" /> rises toward 1 on the same
    ///         time constant as the lean-in — the controller multiplies
    ///         <c>(1 - StillnessFactor)</c> into the fidget director's amplitude scale so an
    ///         attentively listening character settles rather than fidgeting, without a second,
    ///         independent damping knob.
    ///     </para>
    ///     <para>
    ///         <b>Tilt-hold.</b> On a long, seeded, jittered cadence (several seconds) while
    ///         Listening and listening posture is enabled, <see cref="WantsTiltHold" /> pulses
    ///         true for one tick with an <see cref="TiltHoldIntensity" /> — the controller issues
    ///         <c>HeadGestureDirector.TryRequest(HeadGestureKind.Tilt, intensity)</c> only when
    ///         this is true AND gaze is not averting (<paramref name="gazeIsAverting" /> gates
    ///         scheduling here too, so the cadence timer does not advance/fire while gaze has
    ///         deliberately broken contact — e.g. a Thinking look-away bleeding into Listening).
    ///         This director never requests Nod or Shake.
    ///     </para>
    ///     <para>
    ///         Deterministic and allocation-free: <see cref="Seed" /> takes a
    ///         <see cref="DeterministicEmbodimentRandom" /> seed.
    ///     </para>
    ///     <para>
    ///         <b>User-pause backchannel.</b> <see cref="NotifyUserPause" />
    ///         arms a short (<see cref="UserPauseWindowSeconds" />) eligibility window: if a
    ///         tilt-hold is already scheduled (an existing cadence target, not one freshly
    ///         sampled this same tick) and its cadence timer still has at least
    ///         <see cref="EarlyFireCadenceRemainingFraction" /> of its total remaining, the timer
    ///         is pulled forward so the tilt-hold fires within the window instead of on its
    ///         original schedule — a listening "mm-hm" nod that lands in the user's own pause.
    ///         The window closes (deactivates) the instant EITHER it pulls a cadence forward, OR
    ///         a tilt-hold actually fires (pulled-forward or on its own natural schedule), OR it
    ///         decays on its own after <see cref="UserPauseWindowSeconds" /> — whichever comes
    ///         first. Closing on ANY fire (not just a pulled one) is what keeps a window re-armed
    ///         by a second, closely-spaced <see cref="NotifyUserPause" /> call from surviving
    ///         long enough to also snipe the FRESH cadence sampled right after the first fire —
    ///         at most one early-fire happens per open window episode. All existing gates
    ///         (enabled, Listening, gaze-aversion) still apply — the window can only ever pull
    ///         forward a cadence that would have fired on its own eventually; it never invents one.
    ///     </para>
    /// </remarks>
    internal sealed class ListeningPostureDirector
    {
        /// <summary>Time constant (seconds) for the lean-in/stillness engage and decay slew.</summary>
        private const float EngageSlewSeconds = 1f;

        /// <summary>± fraction applied to the tilt cadence for anti-metronome jitter.</summary>
        private const float TiltCadenceVarianceFraction = 0.4f;

        /// <summary>
        ///     Eligibility window (seconds) a <see cref="NotifyUserPause" /> call arms for an
        ///     early tilt-hold fire.
        /// </summary>
        private const float UserPauseWindowSeconds = 0.9f;

        /// <summary>
        ///     Minimum fraction of a scheduled tilt-hold's total cadence that must still remain
        ///     for a user pause to pull it forward — a cadence
        ///     already almost due is left alone (it will fire on its own within the window
        ///     anyway); one further along than this is worth accelerating into the pause.
        /// </summary>
        private const float EarlyFireCadenceRemainingFraction = 0.3f;

        /// <summary>
        ///     Safety margin: a pulled-forward cadence is set to at
        ///     most half the window's remaining time, so it comfortably fires within
        ///     <see cref="UserPauseWindowSeconds" /> even accounting for per-tick discretization.
        /// </summary>
        private const float PullForwardWindowFraction = 0.5f;

        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        private float _leanInBias;
        private float _stillnessFactor;

        private float _tiltCadenceRemaining;
        private float _tiltCadenceTotalSeconds;
        private bool _hasTiltCadenceTarget;
        private bool _wantsTiltHold;
        private float _tiltHoldIntensity;

        private float _userPauseWindowRemaining;
        private bool _userPauseWindowActive;

        /// <summary>Slewed lean-in bias, 0..1 — the controller adds this (scaled) into the posture lean target.</summary>
        public float LeanInBias => _leanInBias;

        /// <summary>Slewed stillness factor, 0..1 — the controller uses <c>(1 - StillnessFactor)</c> to damp fidget amplitude.</summary>
        public float StillnessFactor => _stillnessFactor;

        /// <summary>Whether this tick requests a tilt-hold head gesture (one-tick pulse).</summary>
        public bool WantsTiltHold => _wantsTiltHold;

        /// <summary>Intensity (0..1) to request the tilt-hold at when <see cref="WantsTiltHold" /> is true.</summary>
        public float TiltHoldIntensity => _tiltHoldIntensity;

        /// <summary>
        ///     Seconds remaining on the currently scheduled tilt-hold cadence, or 0 when none is
        ///     scheduled. Diagnostics/test seam only.
        /// </summary>
        internal float TiltCadenceRemainingSeconds => _tiltCadenceRemaining;

        /// <summary>
        ///     Total seconds the currently scheduled tilt-hold cadence was sampled for, or 0 when
        ///     none is scheduled. Diagnostics/test seam only.
        /// </summary>
        internal float TiltCadenceTotalSeconds => _tiltCadenceTotalSeconds;

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        /// <summary>Restores the director to its initial, inactive state. Does not reset the seed.</summary>
        public void Reset()
        {
            _leanInBias = 0f;
            _stillnessFactor = 0f;
            _tiltCadenceRemaining = 0f;
            _tiltCadenceTotalSeconds = 0f;
            _hasTiltCadenceTarget = false;
            _wantsTiltHold = false;
            _tiltHoldIntensity = 0f;
            _userPauseWindowRemaining = 0f;
            _userPauseWindowActive = false;
        }

        /// <summary>
        ///     Notifies the director that the user just stopped speaking — a pause boundary.
        ///     Arms a <see cref="UserPauseWindowSeconds" /> eligibility
        ///     window during which an already-scheduled, sufficiently-along tilt-hold cadence may
        ///     fire early. Safe to call at any time (including outside Listening); the pull-forward
        ///     itself only ever applies while <see cref="Tick" /> is engaged (see the class remarks) —
        ///     the caller is expected to gate calls to while the dialogue state is Listening.
        /// </summary>
        public void NotifyUserPause()
        {
            _userPauseWindowRemaining = UserPauseWindowSeconds;
            _userPauseWindowActive = true;
        }

        /// <summary>
        ///     Advances the listening-posture program. Producer-only: never touches bones or the
        ///     head-gesture channel — the controller reads <see cref="LeanInBias" />/
        ///     <see cref="StillnessFactor" /> and issues the tilt-hold request itself.
        /// </summary>
        /// <param name="state">Current dialogue state.</param>
        /// <param name="listeningPostureEnabled">The smoothed policy's <c>ListeningPostureEnabled</c> gate.</param>
        /// <param name="listeningLeanIn">The smoothed policy's <c>ListeningLeanIn</c> (0..1) target amplitude.</param>
        /// <param name="gazeIsAverting">Whether gaze has deliberately broken contact this tick (suppresses tilt scheduling only).</param>
        /// <param name="deltaTime">Seconds since the previous tick.</param>
        /// <param name="tiltCadenceSeconds">Profile's mean seconds between listening tilt-hold head gestures.</param>
        /// <param name="tiltIntensity">Profile's tilt-hold intensity (0..1).</param>
        public void Tick(
            DialogueState state,
            bool listeningPostureEnabled,
            float listeningLeanIn,
            bool gazeIsAverting,
            float deltaTime,
            float tiltCadenceSeconds,
            float tiltIntensity)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0xB0DE1157u);
                _randomSeeded = true;
            }

            float dt = deltaTime > 0f ? deltaTime : 0f;
            _wantsTiltHold = false;

            bool engaged = state == DialogueState.Listening && listeningPostureEnabled;

            float leanGoal = engaged ? Mathf.Clamp01(listeningLeanIn) : 0f;
            float stillnessGoal = engaged ? 1f : 0f;

            float alpha = 1f - Mathf.Exp(-dt / EngageSlewSeconds);
            _leanInBias += (leanGoal - _leanInBias) * alpha;
            _stillnessFactor += (stillnessGoal - _stillnessFactor) * alpha;

            float effectiveTiltCadenceSeconds = Mathf.Max(0.5f, tiltCadenceSeconds);

            if (!engaged || gazeIsAverting)
            {
                // Not engaged (or gaze has deliberately broken contact): never advance or fire
                // the tilt cadence. Reset the cadence target so re-entering Listening always
                // starts a fresh, freshly-jittered wait rather than resuming a stale countdown.
                _hasTiltCadenceTarget = false;
                _tiltCadenceRemaining = 0f;
                _tiltCadenceTotalSeconds = 0f;
                // The pause window still decays while disengaged rather than lingering forever —
                // it is only ever meaningfully armed while Listening (see NotifyUserPause), so
                // this is a defensive drain, not a feature path.
                DecayUserPauseWindow(dt);
                return;
            }

            if (!_hasTiltCadenceTarget)
            {
                _tiltCadenceRemaining = SampleTiltCadence(effectiveTiltCadenceSeconds);
                _tiltCadenceTotalSeconds = _tiltCadenceRemaining;
                _hasTiltCadenceTarget = true;
                // A freshly-sampled target is not "already scheduled" in the sense the pull-forward
                // cares about (its fraction-remaining is trivially 1.0) — deliberately skip the
                // pull-forward check on the very tick a new cadence starts; the next tick considers it.
                return;
            }

            // User-pause pull-forward: an EXISTING scheduled
            // tilt-hold with enough cadence remaining gets accelerated into the eligibility
            // window instead of waiting out its full original schedule.
            if (_userPauseWindowActive)
            {
                _userPauseWindowRemaining -= dt;
                if (_userPauseWindowRemaining <= 0f)
                {
                    _userPauseWindowActive = false;
                }
                else if (_tiltCadenceTotalSeconds > 0f &&
                    _tiltCadenceRemaining / _tiltCadenceTotalSeconds >= EarlyFireCadenceRemainingFraction)
                {
                    _tiltCadenceRemaining = Mathf.Min(
                        _tiltCadenceRemaining, _userPauseWindowRemaining * PullForwardWindowFraction);
                    // Consumed: at most one early-fire per armed window. Also self-defending even
                    // without this flag — pulling forward drops the remaining/total fraction well
                    // under the threshold, so a second NotifyUserPause before this fires cannot
                    // pull the SAME target forward again.
                    _userPauseWindowActive = false;
                }
            }

            _tiltCadenceRemaining -= dt;
            if (_tiltCadenceRemaining > 0f) return;

            _wantsTiltHold = true;
            _tiltHoldIntensity = Mathf.Clamp01(tiltIntensity);
            _tiltCadenceRemaining = SampleTiltCadence(effectiveTiltCadenceSeconds);
            _tiltCadenceTotalSeconds = _tiltCadenceRemaining;
            // Any armed pause window's opportunity is spent the instant a tilt-hold actually
            // fires (pulled-forward OR on its own natural schedule) — closing it here stops a
            // window re-armed by a second, closely-spaced NotifyUserPause() call from surviving
            // long enough to also snipe the FRESH cadence just sampled above
            // ("a second pause inside one window does not double-fire").
            _userPauseWindowActive = false;
        }

        private void DecayUserPauseWindow(float dt)
        {
            if (!_userPauseWindowActive) return;

            _userPauseWindowRemaining -= dt;
            if (_userPauseWindowRemaining <= 0f) _userPauseWindowActive = false;
        }

        private float SampleTiltCadence(float baseSeconds)
        {
            float variance = baseSeconds * TiltCadenceVarianceFraction;
            return Mathf.Max(0.5f, baseSeconds + _random.Range(-variance, variance));
        }
    }
}
