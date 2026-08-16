using Convai.Domain.Embodiment.Interfaces;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Gestures
{
    /// <summary>
    ///     Pure math for the three scripted head-gesture envelopes: given a normalized phase
    ///     <c>p ∈ [0,1]</c> and an amplitude in degrees, returns the pitch/yaw/roll magnitude at
    ///     that phase. Every envelope starts and ends at exactly zero value AND zero derivative
    ///     (a C¹-continuous one-shot, the same discipline <c>BreathSolver</c>'s waveform and
    ///     Gaze's <c>BackchannelDirector.Shape</c> use), so a program can begin or end on any
    ///     frame without popping the head.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Nod" /> reuses the exact damped double-bob recipe as Gaze's shipped
    ///         backchannel nod (down-up-down-settle, second lobe ~45% of the first, was ~55%) —
    ///         the two should read as the same "kind of nod" whether
    ///         it comes from Gaze's own listening acknowledgment or a Body Language scripted
    ///         request. <see cref="Shake" /> is a continuous damped oscillation (Hann-windowed
    ///         sinusoid) on the yaw axis — one pendulum motion that never parks at center
    ///         mid-gesture. <see cref="Tilt" /> is a single ease-in/hold/ease-out envelope (not
    ///         lobed) — a considering head tilt is one motion, not a double-bob.
    ///         <see cref="BeatNod" /> is a SIGNED single-dip envelope for
    ///         co-speech beats — a soft one-shot dip with a small settle overshoot, deliberately
    ///         not the lobed acknowledgment shape <see cref="Nod" /> uses, since a beat is a
    ///         quick accent riding a syllable, not a full nod.
    ///     </para>
    /// </remarks>
    internal static class HeadGestureProgram
    {
        // Matches BackchannelDirector's normalization exactly: (1-cos(4πp))/2 peaks at 1 per
        // lobe; the exp(-1.6p) decay (retune, was 1.2) pulls the first lobe's
        // crest down to ≈0.68 (numerically: peak sits near p≈0.23, crest value ≈0.6812), so
        // dividing by ≈1.468 restores the configured peak amplitude at the first lobe and leaves
        // the second lobe at ≈0.45× (was ≈0.55×) — a damped double-bob whose second bob reads
        // clearly as an echo, not a near-equal repeat.
        private const float LobeNormalization = 1.468f;
        private const float LobeDecay = 1.6f;

        /// <summary>Fraction of the tilt envelope spent easing in before the hold begins.</summary>
        private const float TiltEaseInFraction = 0.35f;

        /// <summary>Fraction of the tilt envelope spent easing out after the hold ends.</summary>
        private const float TiltEaseOutFraction = 0.4f;

        /// <summary>
        ///     Nod envelope: a downward-biased damped double-bob on the pitch axis. Returns a
        ///     non-negative 0..~1 magnitude; the caller applies sign and amplitude.
        /// </summary>
        public static float Nod(float p)
        {
            p = Mathf.Clamp01(p);
            float lobes = (1f - Mathf.Cos(p * 4f * Mathf.PI)) * 0.5f;
            float decay = Mathf.Exp(-LobeDecay * p);
            return lobes * decay * LobeNormalization;
        }

        /// <summary>Full left-right oscillations a shake completes over its duration.</summary>
        private const float ShakeCycles = 2f;

        /// <summary>
        ///     Shake envelope: a CONTINUOUS damped oscillation on the yaw axis — a sinusoid under
        ///     a Hann window (zero value AND zero velocity at both endpoints, smooth crest
        ///     mid-program). Returns a signed -1..~1 value; the caller applies amplitude only.
        /// </summary>
        /// <remarks>
        ///     Deliberately NOT the nod's lobed-decay magnitude with a sign flip: that magnitude
        ///     returns to zero at every lobe boundary, so a sign-alternated version swings
        ///     right, STOPS, swings left, STOPS — which reads as a stuttering, robotic shake
        ///     (in-editor finding). A real head shake is one continuous pendulum motion that
        ///     never parks at center mid-gesture; the Hann-windowed sinusoid gives exactly that
        ///     while keeping the C¹ endpoints every program in this class guarantees.
        /// </remarks>
        public static float Shake(float p)
        {
            p = Mathf.Clamp01(p);
            float window = (1f - Mathf.Cos(p * 2f * Mathf.PI)) * 0.5f; // Hann: 0→1→0, C¹ at both ends
            float oscillation = Mathf.Sin(p * ShakeCycles * 2f * Mathf.PI);
            return oscillation * window;
        }

        /// <summary>Fraction of the beat-nod envelope spent rising to its peak.</summary>
        private const float BeatNodPeakFraction = 0.30f;

        /// <summary>Fraction of the beat-nod envelope at which the settle-overshoot ease begins.</summary>
        private const float BeatNodSettleStartFraction = 0.88f;

        /// <summary>Magnitude of the beat-nod's settle overshoot below zero (fraction of peak amplitude).</summary>
        private const float BeatNodOvershoot = 0.08f;

        /// <summary>
        ///     Beat-nod envelope: a SIGNED single soft dip with a settle
        ///     overshoot — a co-speech beat, not a lobed acknowledgment nod. Three
        ///     <see cref="EaseInOutQuad" /> pieces, each with zero endpoint derivative (C¹
        ///     everywhere, including at the two internal breakpoints, where both adjoining
        ///     pieces independently reach zero slope): rise 0→1 over <c>[0, 0.30]</c>, fall
        ///     1→−<see cref="BeatNodOvershoot" /> over <c>[0.30, 0.88]</c>, ease back
        ///     −<see cref="BeatNodOvershoot" />→0 over <c>[0.88, 1]</c>. Peak sits at 30% of the
        ///     program's own duration so a 0.45–0.65 s beat program reaches visible peak in
        ///     135–195 ms — the fast channel's latency budget is met by this EARLY peak, never
        ///     by compressing the whole gesture into a shorter program. The
        ///     caller applies sign and amplitude, exactly like <see cref="Shake" />.
        /// </summary>
        public static float BeatNod(float p)
        {
            p = Mathf.Clamp01(p);

            if (p <= BeatNodPeakFraction)
            {
                float t = BeatNodPeakFraction > 0f ? p / BeatNodPeakFraction : 1f;
                return EaseInOutQuad(t);
            }

            if (p <= BeatNodSettleStartFraction)
            {
                float span = BeatNodSettleStartFraction - BeatNodPeakFraction;
                float t = span > 0f ? (p - BeatNodPeakFraction) / span : 1f;
                return Mathf.Lerp(1f, -BeatNodOvershoot, EaseInOutQuad(t));
            }

            float tailSpan = 1f - BeatNodSettleStartFraction;
            float u = tailSpan > 0f ? (p - BeatNodSettleStartFraction) / tailSpan : 1f;
            return Mathf.Lerp(-BeatNodOvershoot, 0f, EaseInOutQuad(u));
        }

        /// <summary>
        ///     Tilt envelope: ease-in to full amplitude, hold, ease-out back to zero — a single
        ///     considering hold, not a lobed bob. Returns a non-negative 0..1 magnitude; the
        ///     caller applies sign and amplitude.
        /// </summary>
        public static float Tilt(float p)
        {
            p = Mathf.Clamp01(p);
            float holdStart = TiltEaseInFraction;
            float holdEnd = 1f - TiltEaseOutFraction;

            if (p < holdStart)
            {
                float t = holdStart > 0f ? p / holdStart : 1f;
                return EaseInOutQuad(t);
            }

            if (p <= holdEnd) return 1f;

            float easeOutSpan = 1f - holdEnd;
            float u = easeOutSpan > 0f ? (p - holdEnd) / easeOutSpan : 1f;
            return 1f - EaseInOutQuad(u);
        }

        private static float EaseInOutQuad(float t) =>
            t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
    }
}
