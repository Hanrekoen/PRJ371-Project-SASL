using System.Collections.Generic;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     One world-object candidate offered to <see cref="JointAttentionDirector" /> for the
    ///     current evaluation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Id" /> is the wiring layer's object identity — the value
    ///         <c>ConvaiObjectId.Of</c> returns for the candidate's transform — and it is passed
    ///         back out through <see cref="JointAttentionDirector.GlanceTargetId" /> so the wiring
    ///         layer can resolve the transform to glance at. It is therefore a real id, not merely
    ///         an equality token.
    ///     </para>
    ///     <para>
    ///         It used to be documented as an opaque token that "the director never dereferences
    ///         beyond equality comparisons", and the wiring layer narrowed the 64-bit object id to
    ///         its 32-bit hash to fit it. The wiring layer then dereferenced that hash anyway, so
    ///         two candidates whose ids collided under hashing could make the character glance at
    ///         the wrong object. The id is carried at full width now, and this contract says what
    ///         the code actually does.
    ///     </para>
    /// </remarks>
    internal readonly struct JointAttentionCandidate
    {
        public readonly long Id;
        public readonly Vector3 WorldPoint;
        public readonly string Name;

        public JointAttentionCandidate(long id, Vector3 worldPoint, string name)
        {
            Id = id;
            WorldPoint = worldPoint;
            Name = name;
        }
    }

    /// <summary>
    ///     Decides whether the character should notice and glance at whatever world object the
    ///     PLAYER is currently looking at ("joint attention"). Scene-free and deterministic so
    ///     the whole rule set — cone test, dwell hysteresis, reaction delay, per-object and
    ///     global cooldowns, and the edge-triggered "attended object" signal used for dynamic
    ///     context — is unit-testable without a scene.
    /// </summary>
    /// <remarks>
    ///     Runs on evaluation ticks only (the wiring layer accumulates real frame time and calls
    ///     <see cref="Tick" /> at ~5 Hz), not every frame — <paramref name="deltaTime" />
    ///     (see <see cref="Tick" />) is the elapsed time since the previous evaluation, not the
    ///     frame delta.
    /// </remarks>
    internal sealed class JointAttentionDirector
    {
        /// <summary>
        ///     "No candidate." Zero rather than a magic sentinel: <c>ConvaiObjectId.Of</c> returns
        ///     <c>0</c> for a null or destroyed object, so zero is already the one value that can
        ///     never name a live object, and the wiring layer drops any candidate that produces it.
        /// </summary>
        private const long NoTarget = 0L;

        private float _clock;

        // Current dwell session (the candidate the player's ray has been landing on).
        private long _dwellId = NoTarget;
        private float _dwellStartClock;
        private Vector3 _dwellWorldPoint;
        private string _dwellName;
        private bool _dwellGraceAvailable;
        private bool _dwellPendingScheduled;

        // A scheduled-but-not-yet-fired glance (the reaction delay window).
        private bool _hasPending;
        private long _pendingId;
        private Vector3 _pendingWorldPoint;
        private float _pendingFireClock;

        private readonly Dictionary<long, float> _cooldownUntilClock = new(8);
        private float _lastGlanceClock = float.NegativeInfinity;

        private long _attendedId = NoTarget;

        /// <summary>Set for exactly the tick a glance decision fires. Consume immediately.</summary>
        public bool HasGlanceToFire { get; private set; }

        /// <summary>
        ///     Id of the candidate to glance at when <see cref="HasGlanceToFire" /> is set — the
        ///     same value the wiring layer supplied on <see cref="JointAttentionCandidate.Id" />,
        ///     so it can be resolved straight back to the object.
        /// </summary>
        public long GlanceTargetId { get; private set; }

        /// <summary>World point of the candidate to glance at when <see cref="HasGlanceToFire" /> is set.</summary>
        public Vector3 GlanceWorldPoint { get; private set; }

        /// <summary>
        ///     Name of the object the player is currently confirmed to be attending to
        ///     (dwell threshold satisfied), or <c>null</c> when nothing is currently attended.
        /// </summary>
        public string AttendedObjectName { get; private set; }

        /// <summary>Set for exactly the tick <see cref="AttendedObjectName" /> changed (set or cleared).</summary>
        public bool AttendedChangedThisTick { get; private set; }

        /// <summary>Resets all state — call on disable so re-enable starts clean.</summary>
        public void Reset()
        {
            _clock = 0f;
            ResetDwellSession();
            _hasPending = false;
            _pendingId = NoTarget;
            _pendingWorldPoint = default;
            _cooldownUntilClock.Clear();
            _lastGlanceClock = float.NegativeInfinity;
            HasGlanceToFire = false;
            GlanceTargetId = NoTarget;
            GlanceWorldPoint = default;
            AttendedChangedThisTick = false;
            // AttendedObjectName is cleared by ResetDwellSession() via SetAttended.
        }

        /// <summary>
        ///     Advances one evaluation. <paramref name="deltaTime" /> is the elapsed time since
        ///     the previous evaluation (not the frame delta — the wiring layer calls this at the
        ///     ~5 Hz evaluation cadence). <paramref name="candidates" /> is this evaluation's
        ///     full candidate set (already filtered for self and out-of-range/relevance by the
        ///     wiring layer); an empty or null list is a valid "no ray this evaluation" input.
        /// </summary>
        public void Tick(
            in Ray playerRay,
            IReadOnlyList<JointAttentionCandidate> candidates,
            float deltaTime,
            bool active,
            float coneAngleDegrees,
            float maxDistanceMeters,
            float dwellSeconds,
            float reactionDelayMinSeconds,
            float reactionDelayMaxSeconds,
            float cooldownSeconds,
            float globalMinIntervalSeconds,
            ref DeterministicEmbodimentRandom random)
        {
            HasGlanceToFire = false;
            AttendedChangedThisTick = false;
            _clock += Mathf.Max(0f, deltaTime);

            if (!active)
            {
                ResetDwellSession();
                _hasPending = false;
                return;
            }

            FireDuePending(cooldownSeconds);

            FindNearestHit(
                in playerRay, candidates, coneAngleDegrees, maxDistanceMeters,
                out long hitId, out Vector3 hitPoint, out string hitName);

            UpdateDwell(hitId, hitPoint, hitName);

            TryScheduleGlance(
                dwellSeconds, reactionDelayMinSeconds, reactionDelayMaxSeconds, globalMinIntervalSeconds, ref random);
        }

        private void UpdateDwell(long hitId, Vector3 hitPoint, string hitName)
        {
            if (hitId != NoTarget)
            {
                if (_dwellId != hitId) StartDwellSession(hitId, hitPoint, hitName);
                else
                {
                    _dwellWorldPoint = hitPoint;
                    _dwellName = hitName;
                    _dwellGraceAvailable = true; // a fresh hit always refreshes the one-miss grace
                }

                return;
            }

            if (_dwellId == NoTarget) return;

            if (_dwellGraceAvailable)
                _dwellGraceAvailable = false; // consume the grace; dwell survives this one miss
            else
                ResetDwellSession();
        }

        private void StartDwellSession(long id, Vector3 worldPoint, string name)
        {
            ResetDwellSession();
            _dwellId = id;
            _dwellStartClock = _clock;
            _dwellWorldPoint = worldPoint;
            _dwellName = name;
            _dwellGraceAvailable = true;
            _dwellPendingScheduled = false;
        }

        private void ResetDwellSession()
        {
            _dwellId = NoTarget;
            _dwellStartClock = 0f;
            _dwellWorldPoint = default;
            _dwellName = null;
            _dwellGraceAvailable = false;
            _dwellPendingScheduled = false;
            SetAttended(NoTarget, null);
        }

        private void TryScheduleGlance(
            float dwellSeconds, float reactionDelayMinSeconds, float reactionDelayMaxSeconds,
            float globalMinIntervalSeconds, ref DeterministicEmbodimentRandom random)
        {
            if (_dwellId == NoTarget || _dwellPendingScheduled) return;
            if (_clock - _dwellStartClock < dwellSeconds) return;

            _dwellPendingScheduled = true;
            SetAttended(_dwellId, _dwellName);

            if (_hasPending) return; // a glance is already queued (defensive — should not happen)

            bool onCooldown = _cooldownUntilClock.TryGetValue(_dwellId, out float until) && _clock < until;
            bool tooSoon = _clock - _lastGlanceClock < globalMinIntervalSeconds;
            if (onCooldown || tooSoon) return;

            _hasPending = true;
            _pendingId = _dwellId;
            _pendingWorldPoint = _dwellWorldPoint;

            float min = Mathf.Min(reactionDelayMinSeconds, reactionDelayMaxSeconds);
            float max = Mathf.Max(reactionDelayMinSeconds, reactionDelayMaxSeconds);
            _pendingFireClock = _clock + random.Range(min, max);
        }

        private void FireDuePending(float cooldownSeconds)
        {
            if (!_hasPending || _clock < _pendingFireClock) return;

            HasGlanceToFire = true;
            GlanceTargetId = _pendingId;
            GlanceWorldPoint = _pendingWorldPoint;

            _cooldownUntilClock[_pendingId] = _clock + Mathf.Max(0f, cooldownSeconds);
            _lastGlanceClock = _clock;
            _hasPending = false;
        }

        private void SetAttended(long id, string name)
        {
            string resolvedName = id == NoTarget ? null : name;
            if (id == _attendedId && string.Equals(resolvedName, AttendedObjectName, System.StringComparison.Ordinal))
                return;

            _attendedId = id;
            AttendedObjectName = resolvedName;
            AttendedChangedThisTick = true;
        }

        /// <summary>
        ///     Nearest ANGULAR hit inside <paramref name="coneAngleDegrees" /> and within
        ///     <paramref name="maxDistanceMeters" />. No allocations — plain indexed loop over
        ///     the caller-owned list.
        /// </summary>
        private static void FindNearestHit(
            in Ray ray, IReadOnlyList<JointAttentionCandidate> candidates,
            float coneAngleDegrees, float maxDistanceMeters,
            out long hitId, out Vector3 hitPoint, out string hitName)
        {
            hitId = NoTarget;
            hitPoint = default;
            hitName = null;

            if (candidates == null || candidates.Count == 0) return;
            if (ray.direction.sqrMagnitude < 1e-8f) return;

            float bestAngle = float.MaxValue;
            Vector3 origin = ray.origin;
            Vector3 direction = ray.direction;

            for (int i = 0; i < candidates.Count; i++)
            {
                JointAttentionCandidate candidate = candidates[i];
                Vector3 toCandidate = candidate.WorldPoint - origin;
                float distance = toCandidate.magnitude;
                if (distance <= 1e-4f || distance > maxDistanceMeters) continue;

                float angle = Vector3.Angle(direction, toCandidate);
                if (angle > coneAngleDegrees || angle >= bestAngle) continue;

                bestAngle = angle;
                hitId = candidate.Id;
                hitPoint = candidate.WorldPoint;
                hitName = candidate.Name;
            }
        }
    }
}
