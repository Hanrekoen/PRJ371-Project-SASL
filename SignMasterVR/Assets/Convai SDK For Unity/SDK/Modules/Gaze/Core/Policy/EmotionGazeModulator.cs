using Convai.Domain.Embodiment.Readings;
using Convai.Modules.Gaze.Core.Behaviors;
using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Policy
{
    /// <summary>
    ///     Opt-in emotional coloring of the gaze: while an authored emotion is dominant, its
    ///     modifier scales engagement (e.g. shame looks away more), aversion strength, and
    ///     blink rate, blended by the emotion's intensity so onsets and decays stay smooth.
    /// </summary>
    internal sealed class EmotionGazeModulator
    {
        /// <summary>Resulting engagement multiplier (1 = unmodified).</summary>
        public float EngagementScale { get; private set; } = 1f;

        /// <summary>Resulting aversion-strength multiplier (1 = unmodified).</summary>
        public float AversionScale { get; private set; } = 1f;

        /// <summary>Resulting blink-rate multiplier (1 = unmodified).</summary>
        public float BlinkRateScale { get; private set; } = 1f;

        /// <summary>Resulting eyelid-aperture multiplier (&lt;1 narrows, &gt;1 widens, 1 = unmodified).</summary>
        public float LidApertureScale { get; private set; } = 1f;

        /// <summary>
        ///     Resulting aversion-beat direction bias. <see cref="GazeAversionBias.CognitiveDefault" />
        ///     when unmodified — leaves <see cref="AversionDirector" />'s own mode-based direction pick alone.
        /// </summary>
        public GazeAversionBias AversionBias { get; private set; } = GazeAversionBias.CognitiveDefault;

        /// <summary>
        ///     Resulting saccade tempo multiplier: scales saccade reaction latency and
        ///     fixation dwell downstream, clamped to 0.7–1.3 (1 = unmodified).
        /// </summary>
        public float SaccadeTempoScale { get; private set; } = 1f;

        /// <summary>Resulting fixation-liveliness multiplier (1 = unmodified).</summary>
        public float FixationLivelinessScale { get; private set; } = 1f;

        public void Reset()
        {
            EngagementScale = 1f;
            AversionScale = 1f;
            BlinkRateScale = 1f;
            LidApertureScale = 1f;
            AversionBias = GazeAversionBias.CognitiveDefault;
            SaccadeTempoScale = 1f;
            FixationLivelinessScale = 1f;
        }

        public void Tick(ConvaiGazeProfile profile, in EmotionReading emotion)
        {
            if (profile == null || !profile.EnableEmotionModulation ||
                !profile.TryGetEmotionModifier(emotion.DominantLabel, out EmotionGazeModifier modifier))
            {
                Reset();
                return;
            }

            float intensity = Mathf.Clamp01(emotion.DominantScore);
            EngagementScale = Mathf.Lerp(1f, modifier.EngagementScale, intensity);
            AversionScale = Mathf.Lerp(1f, modifier.AversionScale, intensity);
            BlinkRateScale = Mathf.Lerp(1f, modifier.BlinkRateScale, intensity);

            // An unauthored aperture (0 on a freshly added entry) means "no aperture change",
            // not "clamp the lids shut" — treat it as neutral so only intentional values bite.
            float aperture = modifier.LidApertureScale <= 0f ? 1f : modifier.LidApertureScale;
            LidApertureScale = Mathf.Lerp(1f, aperture, intensity);

            // The beat direction is discrete (can't be blended toward "neutral" the way a
            // scale can), so it switches on once the emotion is at all dominant and reverts
            // the instant it isn't — matching the same near-zero-intensity threshold the
            // AversionDirector itself uses to disengage a beat.
            AversionBias = intensity > 0.001f ? modifier.AversionBias : GazeAversionBias.CognitiveDefault;

            // Same unauthored-reads-as-neutral fallback as LidApertureScale, then blended by
            // intensity like the other scales, then clamped to the authored range so a stray
            // authored value outside 0.7–1.3 can't push saccade timing outside sane bounds.
            float tempo = modifier.SaccadeTempoScale <= 0f ? 1f : modifier.SaccadeTempoScale;
            SaccadeTempoScale = Mathf.Clamp(Mathf.Lerp(1f, tempo, intensity), 0.7f, 1.3f);

            float liveliness = modifier.FixationLivelinessScale <= 0f ? 1f : modifier.FixationLivelinessScale;
            FixationLivelinessScale = Mathf.Lerp(1f, liveliness, intensity);
        }

        /// <summary>Allocation-free borrowed-frame overload used by SDK runtime consumers.</summary>
        public void Tick(ConvaiGazeProfile profile, in EmotionStateFrame emotion)
        {
            if (profile == null || !profile.EnableEmotionModulation ||
                !profile.TryGetEmotionModifier(emotion.DominantLabel, out EmotionGazeModifier modifier))
            {
                Reset();
                return;
            }

            ApplyModifier(modifier, emotion.DominantScore);
        }

        private void ApplyModifier(EmotionGazeModifier modifier, float score)
        {
            float intensity = Mathf.Clamp01(score);
            EngagementScale = Mathf.Lerp(1f, modifier.EngagementScale, intensity);
            AversionScale = Mathf.Lerp(1f, modifier.AversionScale, intensity);
            BlinkRateScale = Mathf.Lerp(1f, modifier.BlinkRateScale, intensity);

            float aperture = modifier.LidApertureScale <= 0f ? 1f : modifier.LidApertureScale;
            LidApertureScale = Mathf.Lerp(1f, aperture, intensity);
            AversionBias = intensity > 0.001f ? modifier.AversionBias : GazeAversionBias.CognitiveDefault;

            float tempo = modifier.SaccadeTempoScale <= 0f ? 1f : modifier.SaccadeTempoScale;
            SaccadeTempoScale = Mathf.Clamp(Mathf.Lerp(1f, tempo, intensity), 0.7f, 1.3f);

            float liveliness = modifier.FixationLivelinessScale <= 0f ? 1f : modifier.FixationLivelinessScale;
            FixationLivelinessScale = Mathf.Lerp(1f, liveliness, intensity);
        }
    }
}
