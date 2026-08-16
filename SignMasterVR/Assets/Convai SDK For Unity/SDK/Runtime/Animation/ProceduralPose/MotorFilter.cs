using UnityEngine;

namespace Convai.Runtime.Animation.ProceduralPose
{
    /// <summary>
    ///     Per-channel velocity + acceleration limiter: a rate limiter with
    ///     trapezoidal braking — NOT a spring. A spring always lags its target; this filter is
    ///     numerically transparent to healthy motion: when the target's implied velocity and
    ///     acceleration are inside the caps, <see cref="Step" /> lands exactly on the target
    ///     every frame (up to float rounding) and only engages on super-human rates, where it
    ///     accelerates at the cap, cruises at the max speed, and brakes on a trapezoidal
    ///     profile so the clamp itself never induces overshoot or ringing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Braking is applied to the velocity RELATIVE to the target's own (finite-difference)
    ///         velocity, not to the absolute velocity: braking toward a stop at the current
    ///         target position would permanently starve a moving in-budget target (the classic
    ///         trapezoidal-tracker lag), breaking the identity property above. For a static
    ///         target the feedforward term is zero and this reduces exactly to plain
    ///         trapezoidal braking (<c>±sqrt(2·maxAccel·|distance|)</c>).
    ///     </para>
    ///     <para>
    ///         The first <see cref="Step" /> after construction or <see cref="Reset" /> snaps to
    ///         the target at rest (no transient from an arbitrary zero start), and a
    ///         zero/negative <c>deltaTime</c> holds the current state. Struct, zero-alloc, no
    ///         UnityEngine.Object references — safe for per-frame use and plain-C# tests.
    ///     </para>
    /// </remarks>
    internal struct MotorFilter
    {
        private float _current;
        private float _velocity;
        private float _previousTarget;
        private bool _initialized;

        /// <summary>The last value returned by <see cref="Step" /> (0 before the first Step).</summary>
        public float Current => _current;

        /// <summary>
        ///     The filter's internal velocity (units/second) as of the last <see cref="Step" />.
        ///     Exposed for tests/diagnostics: acceleration compliance is asserted on this state,
        ///     because differentiating float32 position samples twice amplifies quantization
        ///     noise by 1/dt² — far above any meaningful tolerance at 120 Hz.
        /// </summary>
        public float Velocity => _velocity;

        /// <summary>
        ///     Advances the filter toward <paramref name="target" /> under the given velocity and
        ///     acceleration caps, returning the limited value. Identity for in-budget signals
        ///     (see type remarks); caps are passed per call so one channel's tonic and ballistic
        ///     lanes can share the struct without per-instance configuration.
        /// </summary>
        public float Step(float target, float maxSpeed, float maxAccel, float deltaTime)
        {
            if (deltaTime <= 0f) return _current;

            if (!_initialized)
            {
                _current = target;
                _velocity = 0f;
                _previousTarget = target;
                _initialized = true;
                return _current;
            }

            // Feedforward: the target's own velocity this frame (finite difference).
            float targetVelocity = (target - _previousTarget) / deltaTime;
            _previousTarget = target;

            float distance = target - _current;

            // Velocity that would land exactly on the target this frame.
            float velocityToLandThisFrame = distance / deltaTime;

            // Max approach speed RELATIVE to the target that can still brake to relative rest
            // at the target (trapezoidal profile — prevents clamp-induced overshoot/ringing).
            float brakeSpeed = Mathf.Sqrt(2f * maxAccel * Mathf.Abs(distance));

            float goalVelocity = targetVelocity +
                Mathf.Clamp(velocityToLandThisFrame - targetVelocity, -brakeSpeed, brakeSpeed);
            goalVelocity = Mathf.Clamp(goalVelocity, -maxSpeed, maxSpeed);

            float acceleration = Mathf.Clamp((goalVelocity - _velocity) / deltaTime, -maxAccel, maxAccel);
            _velocity += acceleration * deltaTime;
            _current += _velocity * deltaTime;
            return _current;
        }

        /// <summary>
        ///     Re-states where the filtered value actually is, without disturbing how fast it is
        ///     moving. For callers whose quantity is derived from live geometry that something
        ///     else can move underneath them.
        /// </summary>
        /// <remarks>
        ///     A body turn closing the angle between a character's facing and a target is the
        ///     case this exists for: the character is mid-turn at some rate, and then the target
        ///     moves, so the angle still to cover jumps. The rate is still true — the character
        ///     is genuinely turning that fast — but the distance is not. Re-seeding the value
        ///     and keeping the velocity says exactly that; <see cref="Seed" /> would throw away
        ///     the momentum and restart the turn from a standstill.
        /// </remarks>
        public void SeedPreservingVelocity(float value)
        {
            _current = value;
            _initialized = true;

            // _previousTarget is deliberately NOT touched: it tracks where the TARGET was last
            // frame and exists only to feed the target's own velocity forward. Overwriting it
            // with a current-value sample makes the filter believe the target teleported, and
            // the resulting feedforward kick is large enough to shove the value straight past
            // where it was going.
        }

        /// <summary>
        ///     Starts the filter at <paramref name="value" />, at rest, so the first
        ///     <see cref="Step" /> moves away from it under the caps instead of snapping to the
        ///     target.
        /// </summary>
        /// <remarks>
        ///     The snap-to-target behaviour of an un-seeded first Step is right for a channel
        ///     whose target IS where it already is (posture, breath). It is wrong for a channel
        ///     driven toward a fixed goal from a known starting offset — a body turn closing a
        ///     remaining angle down to zero, say, where snapping would complete the whole turn
        ///     in one frame. Those callers seed the starting value instead.
        /// </remarks>
        public void Seed(float value)
        {
            _current = value;
            _velocity = 0f;
            _previousTarget = value;
            _initialized = true;
        }

        /// <summary>Zeroes all state; the next <see cref="Step" /> snaps to its target at rest.</summary>
        public void Reset()
        {
            _current = 0f;
            _velocity = 0f;
            _previousTarget = 0f;
            _initialized = false;
        }
    }
}
