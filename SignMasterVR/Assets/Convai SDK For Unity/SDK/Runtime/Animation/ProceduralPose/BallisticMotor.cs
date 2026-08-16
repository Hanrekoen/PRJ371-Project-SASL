using UnityEngine;

namespace Convai.Runtime.Animation.ProceduralPose
{
    /// <summary>
    ///     A single planned movement from wherever the channel currently is to a goal, over a
    ///     duration decided when the movement starts: minimum-jerk, re-planned every frame so the
    ///     goal can move while it is in flight.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The other half of <see cref="MotorFilter" />.</b> Procedural motion in this SDK
    ///         has two verbs and they need different machinery:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <i>hold and track</i> — following something that is already moving (a target
    ///                 drifting, an animated pose being cancelled). The goal is continuous, the
    ///                 requirement is not to lag, and <see cref="MotorFilter" /> is right: it is
    ///                 transparent to in-budget motion and only shapes rates a body could not
    ///                 produce.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <i>shift</i> — a decision to move somewhere else. The goal arrives as a step,
    ///                 and the movement it should produce has a shape: a bell-shaped velocity
    ///                 profile whose duration is chosen from how far there is to go. That is this
    ///                 type.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Running a step through a rate limiter does not produce a movement — it produces the
    ///         time-optimal trajectory, which accelerates at the cap, cruises, and brakes. That is
    ///         the harshest motion the limits permit, with discontinuous acceleration at launch, at
    ///         the accel-to-brake switch and at arrival. It is also the wrong duration law: bang-bang
    ///         time scales with the square root of the distance, so small movements come out
    ///         proportionally the snappiest, which is exactly backwards from how bodies move.
    ///     </para>
    ///     <para>
    ///         <b>Why re-planned rather than baked.</b> A movement's goal is rarely still: the
    ///         thing being looked at drifts, and the allocation a movement was planned against keeps
    ///         evolving underneath it. Baking a polynomial at launch and playing it back would need
    ///         a cancel-and-restart every time the goal moved, and every restart is a visible break.
    ///         Instead the whole polynomial is re-derived every frame from the CURRENT
    ///         position/velocity/acceleration to the CURRENT goal over the time that is left, and
    ///         only its first <c>deltaTime</c> is consumed. A still goal reproduces the textbook
    ///         profile exactly; a moving one is absorbed without a seam, because continuity of the
    ///         state is what is carried forward, not a plan.
    ///     </para>
    ///     <para>
    ///         Struct, zero-alloc, no UnityEngine.Object references — safe for per-frame use and
    ///         plain-C# tests, the same contract <see cref="MotorFilter" /> keeps.
    ///     </para>
    /// </remarks>
    internal struct BallisticMotor
    {
        private float _current;
        private float _velocity;
        private float _acceleration;
        private float _remaining;
        private float _duration;
        private bool _active;
        private bool _envelopeEngaged;

        /// <summary>The last value produced by <see cref="Step" />.</summary>
        public float Current => _current;

        /// <summary>
        ///     Channel velocity (units/second) as of the last <see cref="Step" />. Read by the
        ///     caller when handing this channel back to a tracking filter, so the hand-off carries
        ///     momentum instead of restarting from rest.
        /// </summary>
        public float Velocity => _velocity;

        /// <summary>Channel acceleration (units/second²) as of the last <see cref="Step" />.</summary>
        public float Acceleration => _acceleration;

        /// <summary>True while a movement is in flight.</summary>
        public bool IsActive => _active;

        /// <summary>Seconds left in the current movement; 0 when none is running.</summary>
        public float Remaining => _remaining;

        /// <summary>How far through the current movement, 0–1; 1 when none is running.</summary>
        public float Progress => _duration > 0f ? Mathf.Clamp01(1f - _remaining / _duration) : 1f;

        /// <summary>
        ///     True when the last <see cref="Step" /> had to fall back on the speed/acceleration
        ///     envelope. A movement planned at a sane duration for its distance never trips it;
        ///     when it does fire, the cause is a duration far too short for the distance, and the
        ///     movement degrades toward the time-optimal shape the envelope exists to bound.
        ///     Exposed so that can be asserted in tests rather than inferred from the output.
        /// </summary>
        public bool EnvelopeEngaged => _envelopeEngaged;

        /// <summary>
        ///     Starts a movement of <paramref name="durationSeconds" />, carrying the channel's
        ///     current value and velocity in so the launch is continuous.
        /// </summary>
        /// <remarks>
        ///     Velocity is an input rather than assumed zero because a shift can start while the
        ///     channel is already moving — a new decision taken mid-movement, or a hand-off from
        ///     the tracking filter while it was following something. Starting such a movement from
        ///     rest would put a velocity discontinuity at exactly the moment the eye is drawn to it.
        /// </remarks>
        public void Begin(float from, float velocity, float durationSeconds)
        {
            _current = from;
            _velocity = velocity;
            _acceleration = 0f;
            _duration = Mathf.Max(0f, durationSeconds);
            _remaining = _duration;
            _active = _duration > 0f;
            _envelopeEngaged = false;
        }

        /// <summary>
        ///     Advances the movement by <paramref name="deltaTime" /> toward
        ///     <paramref name="goal" />, which may have moved since the movement began.
        /// </summary>
        /// <param name="goal">Where the movement should end. Re-read every frame.</param>
        /// <param name="maxSpeed">Safety envelope, units/second. Should not normally engage.</param>
        /// <param name="maxAccel">Safety envelope, units/second². Should not normally engage.</param>
        /// <param name="skew">
        ///     0–1 asymmetry. At 0 the velocity profile is symmetric and peaks at the midpoint. Above
        ///     0 the movement spends its early time faster, moving the velocity peak earlier, which
        ///     is what real limb and head movements do. Implemented by shortening the horizon the
        ///     re-plan aims at while leaving the movement's actual clock alone, so the movement still
        ///     takes exactly the duration it was given — see the remarks on
        ///     <see cref="EffectiveHorizon" />.
        /// </param>
        /// <param name="deltaTime">Tick delta.</param>
        public float Step(float goal, float maxSpeed, float maxAccel, float skew, float deltaTime)
        {
            _envelopeEngaged = false;

            if (!_active || deltaTime <= 0f) return _current;

            // Last frame of the movement: land exactly, at rest. Re-planning into a horizon
            // shorter than the step being taken is where the 1/T³ terms blow up, so the movement
            // ends here rather than being asked to fit into the remainder.
            if (_remaining <= deltaTime)
            {
                _current = goal;
                _velocity = 0f;
                _acceleration = 0f;
                _remaining = 0f;
                _active = false;
                return _current;
            }

            _remaining -= deltaTime;

            // Horizon floor. The quintic's terms carry 1/t² and 1/t, and the distance it is
            // solving for shrinks toward zero at the same time, so planning into a horizon of a
            // frame or two is a small difference divided by a small number — the classic
            // ill-conditioned endgame. Two frames of floor costs nothing (the movement ends on
            // its own clock either way) and keeps every division bounded.
            float t = Mathf.Max(EffectiveHorizon(skew), 2f * deltaTime);
            float t2 = t * t;

            // Minimum-jerk from (current, velocity, acceleration) to (goal, 0, 0) over `t`. The
            // quintic's coefficients follow from those six boundary conditions; the familiar
            // 10/-15/6 profile is the special case of starting at rest with no acceleration.
            // Derived rather than quoted, and checked against the closed form in
            // BallisticMotorTests — a transcription slip here is invisible until it is a feel bug.
            //
            // Held in the SCALED form (c3·t³, c4·t⁴, c5·t⁵) rather than as the raw coefficients.
            // The two are algebraically identical and numerically are not: the raw c5 is a
            // distance divided by t⁵, which at 240 Hz late in a movement overflows float32 into
            // nonsense, while these three stay on the order of the distance itself and the powers
            // that consume them are all ≤ 1.
            float p = goal - _current - _velocity * t - 0.5f * _acceleration * t2;
            float q = -_velocity - _acceleration * t;
            float r = -_acceleration;

            float scaled3 = 10f * p - 4f * q * t + 0.5f * r * t2;
            float scaled4 = -15f * p + 7f * q * t - r * t2;
            float scaled5 = 6f * p - 3f * q * t + 0.5f * r * t2;

            // Consume the first deltaTime of that polynomial, in normalized time. Evaluating it
            // analytically rather than integrating the jerk keeps the step accurate at any frame
            // rate: a still goal traces the same path at 30 Hz and at 144 Hz, which is what makes
            // the shape testable and the feel frame-rate independent.
            float h = deltaTime;
            float s = h / t;
            float s2 = s * s;
            float s3 = s2 * s;
            float s4 = s3 * s;
            float s5 = s4 * s;

            float nextValue = _current + _velocity * h + 0.5f * _acceleration * h * h
                              + scaled3 * s3 + scaled4 * s4 + scaled5 * s5;
            float nextVelocity = _velocity + _acceleration * h
                                 + (3f * scaled3 * s2 + 4f * scaled4 * s3 + 5f * scaled5 * s4) / t;
            float nextAcceleration = _acceleration
                                     + (6f * scaled3 * s + 12f * scaled4 * s2 + 20f * scaled5 * s3) / t2;

            // Safety envelope. A planned movement should never reach it; when it does (a goal that
            // jumped a long way with little time left, or a profile asking for a superhuman
            // duration) the polynomial is abandoned for this frame in favour of a clamped step,
            // which is slower than planned but never produces a rate a body could not make.
            float speedLimit = Mathf.Max(0f, maxSpeed);
            float accelLimit = Mathf.Max(0f, maxAccel);
            if (Mathf.Abs(nextVelocity) > speedLimit || Mathf.Abs(nextAcceleration) > accelLimit)
            {
                _envelopeEngaged = true;
                nextAcceleration = Mathf.Clamp(nextAcceleration, -accelLimit, accelLimit);
                nextVelocity = Mathf.Clamp(_velocity + nextAcceleration * h, -speedLimit, speedLimit);
                nextValue = _current + nextVelocity * h;
            }

            _current = nextValue;
            _velocity = nextVelocity;
            _acceleration = nextAcceleration;
            return _current;
        }

        /// <summary>
        ///     The horizon the re-plan aims at, which is the time actually left only when
        ///     <paramref name="skew" /> is 0.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Skew works by lying to the planner about how much time is left, early in the
        ///         movement, and telling the truth by the end: aiming at a horizon shorter than the
        ///         real one makes the plan more urgent, so the channel leaves faster; as the
        ///         movement progresses the lie shrinks to nothing, so the landing is planned
        ///         against the real remaining time and still arrives on the movement's own clock.
        ///     </para>
        ///     <para>
        ///         Doing it this way — rather than warping the time the polynomial is evaluated at —
        ///         is what keeps skew compatible with re-planning. A time warp has to be composed
        ///         with a trajectory that is being re-derived every frame, and its derivative at the
        ///         movement's start is where it wants to be infinite. Scaling the horizon touches
        ///         one number and cannot break the boundary conditions, because the plan it feeds
        ///         is a valid minimum-jerk movement either way.
        ///     </para>
        /// </remarks>
        private readonly float EffectiveHorizon(float skew)
        {
            float clampedSkew = Mathf.Clamp01(skew);
            if (clampedSkew <= 0f) return _remaining;

            float toGo = _duration > 0f ? Mathf.Clamp01(_remaining / _duration) : 0f;
            return _remaining * (1f - clampedSkew * toGo * toGo);
        }

        /// <summary>
        ///     Re-states where the channel actually is, without disturbing the movement's
        ///     momentum or its clock.
        /// </summary>
        /// <remarks>
        ///     For a channel whose value is derived from live geometry that something else can
        ///     move underneath it — a body turn closing the angle between a character's facing
        ///     and a target is the case this exists for. The angle still to cover changes when
        ///     the target moves, but the rate the character is turning at is still true, and the
        ///     movement is still the same movement. Re-planning from the corrected position is
        ///     exactly what the per-frame re-derivation is for; what must NOT be disturbed is the
        ///     velocity, which is why this is not <see cref="Begin" />.
        /// </remarks>
        public void SeedPreservingVelocity(float value)
        {
            _current = value;
        }

        /// <summary>Abandons any movement in flight, leaving the channel where it is, at rest.</summary>
        public void Reset()
        {
            _velocity = 0f;
            _acceleration = 0f;
            _remaining = 0f;
            _duration = 0f;
            _active = false;
            _envelopeEngaged = false;
        }
    }
}
