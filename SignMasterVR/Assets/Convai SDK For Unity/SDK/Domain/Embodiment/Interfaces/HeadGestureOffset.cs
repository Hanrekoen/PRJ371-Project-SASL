namespace Convai.Domain.Embodiment.Interfaces
{
    /// <summary>
    ///     Scripted one-shot head-gesture kinds Body Language can request. Lives
    ///     in Domain so <see cref="Readings.BodyLanguageReading" /> can
    ///     reference it without the Domain assembly depending on the Body Language module.
    /// </summary>
    public enum HeadGestureKind
    {
        /// <summary>Pitch double-bob (down-up-down-settle) — an affirmative acknowledgment.</summary>
        Nod = 0,

        /// <summary>Yaw double alternation — a negative/refusal head shake.</summary>
        Shake = 1,

        /// <summary>Roll ease-in-hold-ease-out — a curious/considering head tilt.</summary>
        Tilt = 2
    }

    /// <summary>
    ///     An additive head-gesture offset in degrees: pitch (nod axis), yaw (shake axis), and
    ///     roll (tilt axis), plus a 0..1 weight. Published by <see cref="IHeadGestureChannel" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The offset is meant to be composed <em>additively</em> on top of whatever the
    ///         consumer's own head solve already produced this frame — it is never an absolute
    ///         pose. A registered consumer (the gaze module's head/torso solver) folds it into
    ///         its own post-spring gesture input, alongside its own internal gestures (e.g.
    ///         listening backchannel nods), arbitrating between the two. When no consumer is
    ///         registered, the producer (Body Language) applies the same offset itself as a
    ///         conservative fallback (see <see cref="IHeadGestureChannel" /> remarks).
    ///     </para>
    ///     <para>
    ///         <see cref="Weight" /> lets a consumer fade the offset in/out (e.g. across a
    ///         disable or a program's envelope ramp) without the producer needing to know
    ///         anything about the consumer's own blending; a weight of 0 means "no gesture is
    ///         active" and every angle should be treated as at-rest.
    ///     </para>
    /// </remarks>
    public readonly struct HeadGestureOffset
    {
        /// <summary>Additive pitch (nod axis) in degrees.</summary>
        public float PitchDegrees { get; }

        /// <summary>Additive yaw (shake axis) in degrees.</summary>
        public float YawDegrees { get; }

        /// <summary>Additive roll (tilt axis) in degrees.</summary>
        public float RollDegrees { get; }

        /// <summary>Blend weight, 0..1. 0 means no gesture is active (offset should read as rest).</summary>
        public float Weight { get; }

        public HeadGestureOffset(float pitchDegrees, float yawDegrees, float rollDegrees, float weight)
        {
            PitchDegrees = pitchDegrees;
            YawDegrees = yawDegrees;
            RollDegrees = rollDegrees;
            Weight = weight;
        }

        /// <summary>The zero offset at zero weight — no gesture in effect.</summary>
        public static HeadGestureOffset None => default;
    }
}
