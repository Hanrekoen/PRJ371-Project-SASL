using Convai.Domain.Embodiment.Interfaces;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Gestures
{
    /// <summary>
    ///     Schedules scripted one-shot head gestures (Nod/Shake/Tilt) and advances the active
    ///     program's envelope into a <see cref="HeadGestureOffset" /> every tick. This is the
    ///     producer half of the <c>IHeadGestureChannel</c> contract: the offset this
    ///     director computes is what the channel publishes (composed by a registered consumer)
    ///     or self-actuates (when no consumer is registered).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Queueing:</b> a single-slot queue — one active program plus at most one
    ///         pending request. A request while a program is active and no slot is pending
    ///         becomes the pending request (it starts the instant the active program completes
    ///         or is refused if the active program never completes on its own, which cannot
    ///         happen — programs always run to completion or are explicitly reset). A request
    ///         while a program is already pending is refused outright: <see cref="TryRequest" />
    ///         returns <c>false</c>. This keeps the system from queuing an unbounded backlog of
    ///         gestures — at most two gestures are ever "in flight" (one playing, one waiting).
    ///     </para>
    ///     <para>
    ///         <b>Refractory:</b> after a program completes, a refractory window (profile-tunable
    ///         seconds ± a seeded variance draw) must elapse before the NEXT program may START —
    ///         a pending request queued during the refractory simply waits it out rather than
    ///         being refused, since it was already accepted into the single pending slot before
    ///         the window began. The variance draw comes from
    ///         <see cref="DeterministicEmbodimentRandom" /> so repeated gestures never read as a
    ///         metronome.
    ///     </para>
    ///     <para>
    ///         Every program envelope (<see cref="HeadGestureProgram" />) starts and ends at
    ///         exactly zero magnitude with zero derivative, so starting a new program the instant
    ///         a previous one completes (or after any refractory wait) never pops the offset.
    ///     </para>
    /// </remarks>
    internal sealed class HeadGestureDirector
    {
        /// <summary>
        ///     Minimum duration (seconds) of a co-speech head-beat request
        ///     (<see cref="TryRequestBeat" />) — the low end of the per-beat random draw.
        ///     Beats now run at normal human-nod length (0.45–0.65s), NOT a compressed
        ///     twitch: the fast channel's latency budget is met by <see cref="HeadGestureProgram.BeatNod" />
        ///     peaking early (30% of its own duration, so 135–195ms time-to-peak across this
        ///     range), never by shrinking the whole gesture the way the old fixed 0.35s
        ///     (peaking at ~23.5% of duration under the old <c>Nod</c> envelope, ~82ms) did.
        /// </summary>
        internal const float BeatDurationMinSeconds = 0.45f;

        /// <summary>Maximum duration (seconds) of a co-speech head-beat request. See <see cref="BeatDurationMinSeconds" />.</summary>
        internal const float BeatDurationMaxSeconds = 0.65f;

        /// <summary>Minimum per-beat amplitude-scale draw — see <see cref="_activeAmplitudeScale" />.</summary>
        private const float BeatAmplitudeScaleMin = 0.75f;

        /// <summary>Maximum per-beat amplitude-scale draw. See <see cref="BeatAmplitudeScaleMin" />.</summary>
        private const float BeatAmplitudeScaleMax = 1.25f;

        /// <summary>
        ///     Proximal-to-distal lead time (seconds) the neck initiates a gesture ahead of the
        ///     head: real necks/spines lead a head motion by a short beat before
        ///     the head itself follows. See <see cref="CurrentNeckLead" />.
        /// </summary>
        internal const float NeckLeadSeconds = 0.055f;

        private bool _hasActive;
        private HeadGestureKind _activeKind;
        private float _activeElapsed;
        private float _activeDurationSeconds;
        private float _activeIntensity;
        private bool _activeIsBeat;
        private int _activeRequestId;

        /// <summary>
        ///     Per-beat amplitude scale, drawn once per beat from
        ///     <c>_random.Range(0.75, 1.25)</c> in <see cref="StartProgram" /> — 1 for non-beat
        ///     (scripted/autonomous) programs. Multiplies the beat-nod pitch only (see
        ///     <see cref="Evaluate" />), so consecutive beats never read as identically sized.
        /// </summary>
        private float _activeAmplitudeScale = 1f;

        private bool _hasPending;
        private HeadGestureKind _pendingKind;
        private float _pendingIntensity;
        private bool _pendingIsBeat;
        private int _pendingRequestId;

        private float _refractoryRemaining;

        /// <summary>
        ///     Monotonic counter used to correlate an accepted request (see
        ///     <see cref="TryRequest(HeadGestureKind, float, out int)" />) with its eventual
        ///     completion — mirrors Gaze's <c>ScriptedGazeStack</c> entry-id correlation.
        ///     0 is reserved as "no request"; requests are numbered from 1.
        /// </summary>
        private int _nextRequestId;

        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        private HeadGestureOffset _current;
        private HeadGestureOffset _currentNeckLead;

        /// <summary>The current additive offset; zero weight when no program is active.</summary>
        public HeadGestureOffset Current => _current;

        /// <summary>
        ///     Proximal-to-distal sequencing: the active program evaluated
        ///     <see cref="NeckLeadSeconds" /> ahead of <see cref="Current" /> — the neck
        ///     initiates a gesture and the head follows ~55ms behind, the same lead/lag real
        ///     necks show. Same kind/intensity/amplitude-scale as <see cref="Current" />, just a
        ///     different phase sample of the identical envelope. <see cref="HeadGestureOffset.None" />
        ///     when idle. Consumers that only read <see cref="Current" /> (e.g. Gaze) are
        ///     unaffected — this is an additional, optional signal for a consumer/fallback that
        ///     wants to drive the neck bone slightly ahead of the head bone.
        /// </summary>
        public HeadGestureOffset CurrentNeckLead => _currentNeckLead;

        /// <summary>Whether a program is currently playing (diagnostics/tests).</summary>
        public bool IsPlaying => _hasActive;

        /// <summary>The kind of the currently playing program (only meaningful when <see cref="IsPlaying" />).</summary>
        public HeadGestureKind ActiveKind => _activeKind;

        /// <summary>
        ///     Whether the currently playing program is a short co-speech beat
        ///     (<see cref="TryRequestBeat" />) rather than a full scripted gesture — kept
        ///     observable so diagnostics/HUDs can tell the two apart (they share the same
        ///     envelope and slot, differing only in duration).
        /// </summary>
        public bool ActiveIsBeat => _hasActive && _activeIsBeat;

        /// <summary>Normalized progress 0..1 of the currently playing program (0 when idle).</summary>
        public float ActiveProgress =>
            _hasActive && _activeDurationSeconds > 0f
                ? Mathf.Clamp01(_activeElapsed / _activeDurationSeconds)
                : 0f;

        /// <summary>Whether a request is queued behind the active program.</summary>
        public bool HasPending => _hasPending;

        /// <summary>
        ///     Correlation id of the currently ACTIVE request (0 when nothing is playing). A
        ///     caller that recorded the id returned by
        ///     <see cref="TryRequest(HeadGestureKind, float, out int)" /> can tell its own
        ///     request apart from a later, unrelated one that happens to share the same
        ///     <see cref="ActiveKind" />.
        /// </summary>
        public int ActiveRequestId => _hasActive ? _activeRequestId : 0;

        /// <summary>Correlation id of the currently PENDING (queued) request (0 when none is queued).</summary>
        public int PendingRequestId => _hasPending ? _pendingRequestId : 0;

        /// <summary>
        ///     Whether <paramref name="requestId" /> (as returned by
        ///     <see cref="TryRequest(HeadGestureKind, float, out int)" />) has finished playing —
        ///     it is ended iff it is neither the active nor the pending request. Valid ids are
        ///     &gt;= 1 and monotonic; 0 is the "no request" sentinel (both slots read 0 when
        ///     idle), so a never-issued/cancelled id, or one from before the last
        ///     <see cref="Reset" />, reports <c>true</c> and a caller never waits forever on a
        ///     stale id. A pending id carries forward to the active slot unchanged, so it reads
        ///     "not ended" continuously across the pending→active transition.
        /// </summary>
        public bool HasRequestEnded(int requestId) =>
            requestId == 0 || (requestId != ActiveRequestId && requestId != PendingRequestId);

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        /// <summary>Stops any playing/pending program and clears the offset. Does not reset the refractory timer.</summary>
        public void Reset()
        {
            _hasActive = false;
            _activeElapsed = 0f;
            _activeDurationSeconds = 0f;
            _activeIntensity = 0f;
            _activeIsBeat = false;
            _activeRequestId = 0;
            _hasPending = false;
            _pendingIntensity = 0f;
            _pendingIsBeat = false;
            _pendingRequestId = 0;
            _refractoryRemaining = 0f;
            _current = HeadGestureOffset.None;
            _currentNeckLead = HeadGestureOffset.None;
            _activeAmplitudeScale = 1f;
        }

        /// <summary>
        ///     Cancels a single in-flight request by id — the surgical alternative to
        ///     <see cref="Reset" /> that <c>ClearScriptedOverrides</c> uses so autonomous
        ///     programs (co-speech beats, listening tilt-holds) are never collateral damage.
        ///     Clears the ACTIVE program iff its id matches <paramref name="requestId" />, or the
        ///     PENDING program iff its id matches; a non-matching id (an autonomous program, a
        ///     stale/already-ended id, or the 0 sentinel) is a no-op and returns <c>false</c>.
        /// </summary>
        /// <remarks>
        ///     Cancelling the active program does NOT arm the post-completion refractory — a
        ///     cancel is an early-out at the caller's request, not a natural completion, so a
        ///     queued autonomous program (or the next request) may start on the very next
        ///     <see cref="Tick" /> rather than waiting out a refractory the user never triggered.
        ///     Every envelope is C¹ with zero-magnitude endpoints, so clearing the offset
        ///     mid-program cannot pop the head. If a scripted active program is cancelled while
        ///     an (autonomous) request sits pending, the pending program simply promotes on the
        ///     next tick — the autonomous behavior resumes.
        /// </remarks>
        internal bool CancelRequest(int requestId)
        {
            if (requestId == 0) return false;

            if (_hasActive && _activeRequestId == requestId)
            {
                _hasActive = false;
                _activeElapsed = 0f;
                _activeDurationSeconds = 0f;
                _activeIntensity = 0f;
                _activeIsBeat = false;
                _activeRequestId = 0;
                _activeAmplitudeScale = 1f;
                _current = HeadGestureOffset.None;
                _currentNeckLead = HeadGestureOffset.None;
                return true;
            }

            if (_hasPending && _pendingRequestId == requestId)
            {
                _hasPending = false;
                _pendingIntensity = 0f;
                _pendingIsBeat = false;
                _pendingRequestId = 0;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Requests <paramref name="kind" /> play at <paramref name="intensity" /> (0..1,
        ///     scales amplitude). Returns <c>false</c> when the single pending slot is already
        ///     occupied (a program is active AND another request is already queued behind it) —
        ///     the caller should treat that as "busy, try again later", never throw.
        /// </summary>
        public bool TryRequest(HeadGestureKind kind, float intensity) => TryRequestInternal(kind, intensity, isBeat: false, out _);

        /// <summary>
        ///     Same as <see cref="TryRequest(HeadGestureKind, float)" />, additionally returning a
        ///     correlation id (see <see cref="HasRequestEnded" />) a scripted caller can poll to
        ///     learn when THIS specific request's program has ended. <paramref name="requestId" />
        ///     is 0 when the request was refused.
        /// </summary>
        public bool TryRequest(HeadGestureKind kind, float intensity, out int requestId) =>
            TryRequestInternal(kind, intensity, isBeat: false, out requestId);

        /// <summary>
        ///     Requests a short co-speech head-beat (GesticulationDirector fast
        ///     channel): a <see cref="HeadGestureKind.Nod" /> program using the signed single-dip
        ///     <see cref="HeadGestureProgram.BeatNod" /> envelope instead of
        ///     the full acknowledgment double-bob, run over a duration drawn from
        ///     [<see cref="BeatDurationMinSeconds" />, <see cref="BeatDurationMaxSeconds" />] —
        ///     normal human-nod length. BeatNod's own 30%-of-duration peak keeps the fast
        ///     channel's latency budget (135–195ms time-to-peak across that range) without
        ///     compressing the whole gesture. Shares the same single active/pending slot and
        ///     refractory window as scripted requests — a beat can be refused exactly like any
        ///     other request when the pending slot is already occupied.
        /// </summary>
        public bool TryRequestBeat(HeadGestureKind kind, float intensity) =>
            TryRequestInternal(kind, intensity, isBeat: true, explicitDurationSeconds: null, out _);

        /// <summary>
        ///     Same as <see cref="TryRequestBeat(HeadGestureKind, float)" /> but with an explicit
        ///     duration that wins over the per-beat random draw — used by the phrase-end slow nod
        ///     (GesticulationDirector) so a strong <c>Release</c> pulse can request
        ///     a deliberately longer, calmer beat than the normal 0.45–0.65s draw. Still fire-now-
        ///     or-drop and still shares the beat refractory/interval with ordinary beats.
        /// </summary>
        internal bool TryRequestBeat(HeadGestureKind kind, float intensity, float durationSeconds) =>
            TryRequestInternal(kind, intensity, isBeat: true, explicitDurationSeconds: durationSeconds, out _);

        private bool TryRequestInternal(HeadGestureKind kind, float intensity, bool isBeat, out int requestId) =>
            TryRequestInternal(kind, intensity, isBeat, explicitDurationSeconds: null, out requestId);

        private bool TryRequestInternal(
            HeadGestureKind kind, float intensity, bool isBeat, float? explicitDurationSeconds, out int requestId)
        {
            float clampedIntensity = Mathf.Clamp01(intensity);

            if (!_hasActive && _refractoryRemaining <= 0f)
            {
                requestId = NextRequestId();
                StartProgram(kind, clampedIntensity, isBeat, requestId, explicitDurationSeconds);
                return true;
            }

            // Beats are rhythm-bound: a queued beat would fire hundreds of ms after its speech
            // pulse — past the fast channel's ≤150ms budget and off-rhythm by definition — and
            // would occupy the pending slot against scripted requests, which must win.
            // Fire-now-or-drop; the caller's posture pulse still accents the moment.
            if (isBeat)
            {
                requestId = 0;
                return false;
            }

            // Either a program is active or the post-completion refractory is still draining —
            // both go through the single pending slot so back-to-back requests can never
            // machine-gun past the refractory window (Tick starts the pending program only once
            // the refractory has fully elapsed).
            if (_hasPending)
            {
                requestId = 0;
                return false;
            }

            _hasPending = true;
            _pendingKind = kind;
            _pendingIntensity = clampedIntensity;
            _pendingIsBeat = isBeat;
            requestId = NextRequestId();
            _pendingRequestId = requestId;
            return true;
        }

        /// <summary>
        ///     Allocates the next monotonic request id. Valid ids are always &gt;= 1; if the
        ///     counter would ever wrap past <see cref="int.MaxValue" /> into a negative or zero
        ///     value (astronomically unlikely — billions of gestures on one character — but
        ///     guarded so a wrapped id can never be mistaken for the 0 sentinel or collide with
        ///     the "ended" test), it restarts from 1.
        /// </summary>
        private int NextRequestId()
        {
            if (_nextRequestId >= int.MaxValue) _nextRequestId = 0;
            return ++_nextRequestId;
        }

        /// <summary>
        ///     Advances the active program (if any) and starts the pending request once the
        ///     active program completes and the refractory window has elapsed.
        /// </summary>
        public void Tick(
            float deltaTime,
            float nodMaxPitchDegrees,
            float shakeMaxYawDegrees,
            float tiltMaxRollDegrees,
            float refractorySeconds,
            float refractoryVarianceSeconds)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0x4EADB3A7u);
                _randomSeeded = true;
            }

            float dt = deltaTime > 0f ? deltaTime : 0f;

            if (_refractoryRemaining > 0f)
                _refractoryRemaining = Mathf.Max(0f, _refractoryRemaining - dt);

            if (_hasActive)
            {
                _activeElapsed += dt;
                float p = _activeDurationSeconds > 0f ? _activeElapsed / _activeDurationSeconds : 1f;

                if (p >= 1f)
                {
                    _hasActive = false;
                    _current = HeadGestureOffset.None;
                    _currentNeckLead = HeadGestureOffset.None;
                    _refractoryRemaining = SampleRefractory(refractorySeconds, refractoryVarianceSeconds);
                }
                else
                {
                    _current = Evaluate(_activeKind, p, _activeIntensity, _activeIsBeat, _activeAmplitudeScale,
                        nodMaxPitchDegrees, shakeMaxYawDegrees, tiltMaxRollDegrees);

                    // Neck-lead sequencing: the same envelope, sampled
                    // NeckLeadSeconds further along — one extra Evaluate call (zero alloc, it
                    // returns a struct), no extra state machine.
                    float pNeck = _activeDurationSeconds > 0f
                        ? Mathf.Min(1f, p + NeckLeadSeconds / _activeDurationSeconds)
                        : 1f;
                    _currentNeckLead = Evaluate(_activeKind, pNeck, _activeIntensity, _activeIsBeat, _activeAmplitudeScale,
                        nodMaxPitchDegrees, shakeMaxYawDegrees, tiltMaxRollDegrees);
                }
            }

            if (!_hasActive && _hasPending && _refractoryRemaining <= 0f)
            {
                HeadGestureKind kind = _pendingKind;
                float intensity = _pendingIntensity;
                bool isBeat = _pendingIsBeat;
                int requestId = _pendingRequestId;
                _hasPending = false;
                _pendingRequestId = 0;
                // Beats never queue (fire-now-or-drop, see TryRequestInternal), so a promoted
                // pending request is always a scripted (non-beat) kind — no explicit duration to
                // carry through the pending slot.
                StartProgram(kind, intensity, isBeat, requestId, explicitDurationSeconds: null);
            }
        }

        private void StartProgram(
            HeadGestureKind kind, float intensity, bool isBeat, int requestId, float? explicitDurationSeconds)
        {
            // Lazily seeded here too (not just in Tick): TryRequestBeat can call this directly,
            // before Tick has ever run, and a beat's duration/amplitude draw below needs a real
            // stream, not the struct's zero default.
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0x4EADB3A7u);
                _randomSeeded = true;
            }

            _hasActive = true;
            _activeKind = kind;
            _activeElapsed = 0f;
            _activeIntensity = intensity;
            _activeIsBeat = isBeat;
            _activeRequestId = requestId;
            _activeAmplitudeScale = isBeat ? _random.Range(BeatAmplitudeScaleMin, BeatAmplitudeScaleMax) : 1f;
            _activeDurationSeconds = isBeat
                ? explicitDurationSeconds ?? _random.Range(BeatDurationMinSeconds, BeatDurationMaxSeconds)
                : DurationFor(kind);
            _current = HeadGestureOffset.None;
            _currentNeckLead = HeadGestureOffset.None;
        }

        private static float DurationFor(HeadGestureKind kind) => kind switch
        {
            HeadGestureKind.Nod => 1.15f,
            HeadGestureKind.Shake => 1.2f,
            HeadGestureKind.Tilt => 1.4f,
            _ => 1f
        };

        private static HeadGestureOffset Evaluate(
            HeadGestureKind kind,
            float p,
            float intensity,
            bool isBeat,
            float amplitudeScale,
            float nodMaxPitchDegrees,
            float shakeMaxYawDegrees,
            float tiltMaxRollDegrees)
        {
            switch (kind)
            {
                case HeadGestureKind.Nod:
                {
                    // A co-speech beat uses the signed single-dip BeatNod envelope
                    // with its own per-beat amplitude scale; the full scripted/
                    // autonomous Nod keeps the damped double-bob acknowledgment shape.
                    float pitch = isBeat
                        ? -HeadGestureProgram.BeatNod(p) * nodMaxPitchDegrees * intensity * amplitudeScale
                        : -HeadGestureProgram.Nod(p) * nodMaxPitchDegrees * intensity;
                    return new HeadGestureOffset(pitch, 0f, 0f, intensity);
                }
                case HeadGestureKind.Shake:
                {
                    float yaw = HeadGestureProgram.Shake(p) * shakeMaxYawDegrees * intensity;
                    return new HeadGestureOffset(0f, yaw, 0f, intensity);
                }
                case HeadGestureKind.Tilt:
                {
                    float roll = HeadGestureProgram.Tilt(p) * tiltMaxRollDegrees * intensity;
                    return new HeadGestureOffset(0f, 0f, roll, intensity);
                }
                default:
                    return HeadGestureOffset.None;
            }
        }

        private float SampleRefractory(float baseSeconds, float varianceSeconds)
        {
            float baseline = Mathf.Max(0f, baseSeconds);
            if (varianceSeconds <= 0f) return baseline;
            return Mathf.Max(0f, baseline + _random.Range(-varianceSeconds, varianceSeconds));
        }
    }
}
