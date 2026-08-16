using Convai.Domain.Embodiment.Semantics;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Policy
{
    /// <summary>
    ///     Pure static mapping from the resolved expressiveness dial (0..1, see
    ///     <see cref="ExpressivenessPreset" /> / <c>ConvaiBodyLanguageProfile.ResolveExpressiveness</c>)
    ///     to the three multiplicative gains <c>ConvaiBodyLanguageController</c> applies at every
    ///     amplitude/interval/optional-behavior feed point: how BIG the motion is
    ///     (<see cref="AmplitudeGain" />), how OFTEN it happens (<see cref="FrequencyGain" /> —
    ///     schedulers divide their interval by this gain, since a higher gain means a shorter,
    ///     more frequent cadence), and how much of the optional repertoire (shrugs, hand
    ///     micro-life, settle steps) is expressed at all (<see cref="RichnessGain" />).
    /// </summary>
    /// <remarks>
    ///     Each curve is piecewise-linear across five anchors at <c>e = 0, .25, .5, .75, 1</c>.
    ///     The anchors are chosen so <c>e = 0.5</c> (the shipped <see cref="ExpressivenessPreset.Natural" />
    ///     default) evaluates to EXACTLY <c>1.0</c> on all three curves — callers use this for a
    ///     zero-cost fast path (no gain multiplication needed at the default). Anchor evaluation
    ///     is exact in floating point (each segment's endpoint <c>t</c> is exactly 0 or 1), so
    ///     <c>AmplitudeGain(0.5f) == 1f</c> bit-exactly.
    /// </remarks>
    internal static class ExpressivenessCurves
    {
        /// <summary>
        ///     Scales amplitude/degree/centimeter feeds (breath, posture, sway, stance, shrug,
        ///     reactions, hand-micro degrees, head-beat/tilt intensity): 0.35 at Subtle, 1.0 at
        ///     Natural, 1.75 at Theatrical.
        /// </summary>
        public static float AmplitudeGain(float expressiveness) =>
            Piecewise(expressiveness, 0.35f, 0.62f, 1.0f, 1.35f, 1.75f);

        /// <summary>
        ///     Scales how often scheduled behaviors happen. Callers DIVIDE an interval by this
        ///     gain (a higher gain shortens the interval): 0.55 at Subtle, 1.0 at Natural, 1.5 at
        ///     Theatrical.
        /// </summary>
        public static float FrequencyGain(float expressiveness) =>
            Piecewise(expressiveness, 0.55f, 0.75f, 1.0f, 1.25f, 1.5f);

        /// <summary>
        ///     Gates/scales optional-behavior repertoire (shrugs, hand micro-life, settle-step
        ///     magnitude): 0.0 at Subtle, where these behaviors are absent entirely, 1.0 at
        ///     Natural, 1.5 at Theatrical.
        /// </summary>
        public static float RichnessGain(float expressiveness) =>
            Piecewise(expressiveness, 0.0f, 0.45f, 1.0f, 1.25f, 1.5f);

        /// <summary>
        ///     Resolves a fixed preset to its anchor expressiveness value (0.25/0.5/0.75/1 for
        ///     Subtle/Natural/Expressive/Theatrical). <see cref="ExpressivenessPreset.Custom" />
        ///     never throws here — it returns the Natural anchor (0.5) as an inert fallback; a
        ///     caller resolving <see cref="ExpressivenessPreset.Custom" /> is expected to read the
        ///     profile's own custom scalar instead of calling this method.
        /// </summary>
        public static float For(ExpressivenessPreset preset) => preset switch
        {
            ExpressivenessPreset.Subtle => 0.25f,
            ExpressivenessPreset.Natural => 0.5f,
            ExpressivenessPreset.Expressive => 0.75f,
            ExpressivenessPreset.Theatrical => 1f,
            _ => 0.5f
        };

        private static float Piecewise(float e, float a0, float a1, float a2, float a3, float a4)
        {
            float clamped = Mathf.Clamp01(e);

            if (clamped <= 0.25f) return Mathf.Lerp(a0, a1, clamped / 0.25f);
            if (clamped <= 0.5f) return Mathf.Lerp(a1, a2, (clamped - 0.25f) / 0.25f);
            if (clamped <= 0.75f) return Mathf.Lerp(a2, a3, (clamped - 0.5f) / 0.25f);
            return Mathf.Lerp(a3, a4, (clamped - 0.75f) / 0.25f);
        }
    }
}
