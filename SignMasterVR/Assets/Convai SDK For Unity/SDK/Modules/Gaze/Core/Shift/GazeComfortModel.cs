using UnityEngine;

namespace Convai.Modules.Gaze.Core.Shift
{
    /// <summary>
    ///     What a held pose costs, and what the body does about it: eyes that have been sitting
    ///     off-centre recruit more head, and a head that has been held turned gives way to the
    ///     feet.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The ladder alone produces a pose that is correct the instant it is struck and
    ///         wrong a few seconds later. Nobody holds a 50° neck turn to keep talking to
    ///         someone, and nobody holds their eyes at the corner of the socket — both are
    ///         work, and the body spends a moment and then stops paying. This is the part of
    ///         the model that makes a settled pose keep evolving instead of freezing.
    ///     </para>
    ///     <para>
    ///         Two pressures, both slow, both bounded:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <b>Orbit return.</b> Sustained eye eccentricity raises how much of the
    ///                 shift the head is asked for, up to its full range. This is what turns
    ///                 "the eyes got there first" into "and then the head arrived and the eyes
    ///                 came back to centre", which is the single most recognisable beat in a
    ///                 real gaze shift and the one the previous model could not produce at all:
    ///                 the eyes simply stayed at their clamp.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <b>Comfort return.</b> Sustained head deviation past a comfortable angle
    ///                 makes the ladder ask for the feet even when the residual alone would not,
    ///                 and then relaxes the neck as the body arrives. A body turn becomes the
    ///                 consequence of a neck that has been working too long, which is why people
    ///                 actually turn — rather than a fixed angle tripwire.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Both pressures are asymmetric on purpose: they build slowly and release quickly.
    ///         Discomfort accumulates over seconds; the relief of having turned is immediate,
    ///         and a pressure that decayed as slowly as it grew would keep asking for the feet
    ///         after the turn had already fixed the problem.
    ///     </para>
    /// </remarks>
    internal sealed class GazeComfortModel
    {
        /// <summary>Seconds of sustained discomfort to reach full pressure.</summary>
        private const float BuildSeconds = 1.6f;

        /// <summary>Seconds to shed pressure once the pose is comfortable again.</summary>
        private const float ReleaseSeconds = 0.35f;

        /// <summary>
        ///     Fraction of the comfort angle the pose must come back inside before the strain is
        ///     considered relieved.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Without it the strain test is a bare threshold driving an integrator whose
        ///         output moves the very pose the test measures — a loop with gain and no
        ///         deadband, which does not settle, it oscillates. Orbit return is the case that
        ///         bites: pressure recruits the head, the head takes more of the shift, the eyes
        ///         come back inside the comfort angle, the pressure sheds — nearly five times
        ///         faster than it built, because release is deliberately quick — the head gives
        ///         the share back, and the eyes go straight back out past the threshold. The
        ///         character's head then hunts back and forth for as long as it is talking to
        ///         someone at an angle, at whatever amplitude the willingness gap is worth.
        ///     </para>
        ///     <para>
        ///         A relief band wide enough to cover a full pressure swing's worth of pose change
        ///         turns that limit cycle into a single settle. 25% of the comfort angle is about
        ///         3.5° at the shipped 14° eye comfort — comfortably wider than the chatter and
        ///         far narrower than the behaviour the pressures exist to produce.
        ///     </para>
        /// </remarks>
        private const float ReliefFraction = 0.75f;

        private float _orbitPressure;
        private float _comfortPressure;
        private bool _eyesStrained;
        private bool _neckStrained;

        /// <summary>
        ///     0–1: how strongly the eyes are asking the head to take over. Raises the head's
        ///     share of the shift toward its full range.
        /// </summary>
        /// <remarks>
        ///     Smoothstepped, not the raw integrator. The value scales a movement's GOAL, and the
        ///     lane that consumes it is transparent to in-budget motion, so the goal's shape is
        ///     the shape that reaches the neck: a linear ramp starts and stops the head with a
        ///     velocity step at each end. The ease costs nothing and removes both.
        /// </remarks>
        public float OrbitPressure => Smooth(_orbitPressure);

        /// <summary>
        ///     0–1: how strongly a held head turn is asking for the feet. At 1 the ladder
        ///     requests a body turn regardless of how small the leftover angle is.
        /// </summary>
        /// <remarks>
        ///     Raw, unlike <see cref="OrbitPressure" />: this one is read as a threshold
        ///     (<c>&gt;= 1</c>) rather than scaled onto an angle, so easing it would only move
        ///     when the turn fires, and the hysteresis above already owns that.
        /// </remarks>
        public float ComfortPressure => _comfortPressure;

        public void Reset()
        {
            _orbitPressure = 0f;
            _comfortPressure = 0f;
            _eyesStrained = false;
            _neckStrained = false;
        }

        /// <summary>
        ///     Advances both pressures from the pose actually achieved last frame.
        /// </summary>
        /// <param name="eyeEccentricityDegrees">How far the eyes currently sit from centre.</param>
        /// <param name="headYawDegrees">Signed head yaw currently held.</param>
        /// <param name="eyeComfortDegrees">Eye eccentricity beyond which the eyes want relief.</param>
        /// <param name="headComfortYawDegrees">Head yaw beyond which the neck wants relief.</param>
        /// <param name="engaged">
        ///     False when there is no target. Pressure decays rather than building: a character
        ///     looking at nothing is not straining to hold anything.
        /// </param>
        /// <param name="deltaTime">Tick delta.</param>
        public void Tick(
            float eyeEccentricityDegrees,
            float headYawDegrees,
            float eyeComfortDegrees,
            float headComfortYawDegrees,
            bool engaged,
            float deltaTime)
        {
            float dt = Mathf.Max(0f, deltaTime);

            _eyesStrained = Strained(
                _eyesStrained, engaged, eyeEccentricityDegrees, eyeComfortDegrees);
            _neckStrained = Strained(
                _neckStrained, engaged, Mathf.Abs(headYawDegrees), headComfortYawDegrees);

            _orbitPressure = Step(_orbitPressure, _eyesStrained, dt);
            _comfortPressure = Step(_comfortPressure, _neckStrained, dt);
        }

        /// <summary>
        ///     Schmitt trigger on one strain measure: engages past the comfort angle, releases
        ///     only once the pose is back inside <see cref="ReliefFraction" /> of it.
        /// </summary>
        private static bool Strained(bool wasStrained, bool engaged, float magnitude, float comfort)
        {
            if (!engaged || comfort <= 0f) return false;
            return wasStrained ? magnitude > comfort * ReliefFraction : magnitude > comfort;
        }

        private static float Step(float pressure, bool straining, float deltaTime)
        {
            float rate = straining ? 1f / BuildSeconds : -1f / ReleaseSeconds;
            return Mathf.Clamp01(pressure + rate * deltaTime);
        }

        /// <summary>Smoothstep: zero slope at both ends, so a pressure ramp has no velocity step.</summary>
        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
