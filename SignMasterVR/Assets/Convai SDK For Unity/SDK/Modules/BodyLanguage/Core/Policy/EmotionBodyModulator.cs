using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Modules.BodyLanguage.Data;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Policy
{
    /// <summary>
    ///     Blended emotion modulation of the body language posture/breath/gesture targets from
    ///     the full <see cref="EmotionReading" /> score table: every scored label
    ///     contributes its posture bias and gesture/breath scales scaled by its own
    ///     <c>[0, 1]</c> confidence, so a mixed emotion (e.g. half joy, half fear) reads as a
    ///     mixed posture rather than snapping to whichever label happens to be dominant, and the
    ///     overall magnitude tracks how strongly the emotion is felt — a lone weak emotion moves
    ///     the body proportionally little, not fully.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Labels authored in the profile's emotion table (<see cref="ConvaiBodyLanguageProfile.TryGetEmotionModifier" />)
    ///         use their hand-tuned modifier directly. Labels absent from the table fall back to
    ///         a static valence/arousal derivation (opt-in via
    ///         <see cref="ConvaiBodyLanguageProfile.ValenceArousalFallback" />) covering the
    ///         taxonomy the SDK's emotion module ships (Plutchik's eight plus neutral — see
    ///         <c>EmotionTaxonomyAsset.BuildDefault</c>): joy, trust, fear, surprise, sadness,
    ///         disgust, anger, anticipation. When modulation is disabled, or an unauthored label
    ///         has the fallback disabled too, that label contributes the identity modifier
    ///         (no bias, ×1 scales) — the "Emotion module" degradation row.
    ///     </para>
    ///     <para>
    ///         Zero-allocation in steady state: the everyday tick (no emotion active,
    ///         <see cref="EmotionReading.IsNeutral" />) short-circuits before touching the score
    ///         table at all, and an ACTIVE emotion re-blends only when the published reading
    ///         actually changes (score-table reference, dominant label, or dominant score) —
    ///         <see cref="EmotionReading" /> construction copies its table, so upstream can only
    ///         publish new score content by publishing a new reading, which makes reading
    ///         identity a sound change key. Holding an emotion therefore costs nothing per tick.
    ///         Only on a genuine reading change is the table copied once into a preallocated
    ///         scratch dictionary via <see cref="EmotionReading.CopyScoresTo" /> (whose
    ///         interface-typed <c>foreach</c> boxes one enumerator — an event-cadence cost, not
    ///         a per-frame one), then iterated with a plain <c>foreach</c> against the concrete
    ///         <see cref="Dictionary{TKey,TValue}" /> scratch field so this modulator's own loop
    ///         never boxes — no LINQ, no closures, no per-label allocation.
    ///     </para>
    /// </remarks>
    internal sealed class EmotionBodyModulator
    {
        private readonly Dictionary<string, float> _scoreScratch = new(16);

        // Identity of the last reading blended: EmotionReading freezes its score table on
        // construction, so a same-reference table with the same dominant label/score is the
        // same emotional content — skipping the re-blend keeps an actively held emotion
        // allocation-free per tick (the CopyScoresTo enumerator box becomes event-cadence).
        private IReadOnlyDictionary<string, float> _lastScores;
        private string _lastDominantLabel;
        private float _lastDominantScore;
        private ConvaiBodyLanguageProfile _lastProfile;

        /// <summary>Blended openness bias, -1..1.</summary>
        public float OpennessBias { get; private set; }

        /// <summary>Blended sagittal lean bias, -1..1.</summary>
        public float LeanBias { get; private set; }

        /// <summary>Blended shoulder tension bias, -1..1.</summary>
        public float ShoulderTensionBias { get; private set; }

        /// <summary>Blended gesticulation intensity multiplier (1 = unmodified).</summary>
        public float GestureIntensityScale { get; private set; } = 1f;

        /// <summary>Blended gesticulation rate multiplier (1 = unmodified).</summary>
        public float GestureRateScale { get; private set; } = 1f;

        /// <summary>Blended breathing rate multiplier (1 = unmodified).</summary>
        public float BreathRateScale { get; private set; } = 1f;

        /// <summary>Blended breathing depth multiplier (1 = unmodified).</summary>
        public float BreathDepthScale { get; private set; } = 1f;

        /// <summary>
        ///     Signed blended arousal, -1 (calm) .. 1 (activated) — see <see cref="Arousal01" />.
        /// </summary>
        private float _arousalSigned;

        /// <summary>
        ///     Blended physiological arousal, 0..1 (physiological
        ///     coherence). <see cref="EmotionValenceArousalTable" /> uses a SIGNED -1 (calm) .. 1
        ///     (activated) convention with 0 = neutral; this rescales that to the SDK's usual
        ///     0..1 convention with <b>0.5 = neutral</b> so a caller that composes it as
        ///     <c>0.85 + 0.3 * Arousal01</c> (sway) or <c>1.15 - 0.3 * Arousal01</c> (fidget gap)
        ///     gets EXACTLY 1 at neutral, leaving an unemotional character unscaled. Read directly
        ///     from <see cref="EmotionValenceArousalTable" /> per scored label (score-weighted,
        ///     mirroring the bias-blending loop in <see cref="Tick" />), independent of
        ///     <see cref="ConvaiBodyLanguageProfile.ValenceArousalFallback" /> — that toggle only
        ///     gates whether an UNAUTHORED label's POSTURE bias falls back to the derived
        ///     modifier; arousal is a separate, always-available physiological read whenever the
        ///     label matches a table entry (an authored posture row and a coherent arousal signal
        ///     are not mutually exclusive). A label absent from the table contributes 0 (neutral)
        ///     arousal, the same graceful degradation as every other unauthored/unknown label path
        ///     in this modulator.
        /// </summary>
        internal float Arousal01 => (Mathf.Clamp(_arousalSigned, -1f, 1f) + 1f) * 0.5f;

        public void Reset()
        {
            OpennessBias = 0f;
            LeanBias = 0f;
            ShoulderTensionBias = 0f;
            GestureIntensityScale = 1f;
            GestureRateScale = 1f;
            BreathRateScale = 1f;
            BreathDepthScale = 1f;
            _arousalSigned = 0f;
            _lastScores = null;
            _lastDominantLabel = null;
            _lastDominantScore = -1f;
            _lastProfile = null;
            _lastFrameVersion = -1;
        }

        public void Tick(ConvaiBodyLanguageProfile profile, in EmotionReading emotion)
        {
            if (profile == null || !profile.EnableEmotionModulation || emotion.IsNeutral)
            {
                // Neutral is the common steady state (no emotion active) — short-circuit before
                // touching the score table so the everyday tick never pays for the table copy.
                Reset();
                return;
            }

            if (ReferenceEquals(_lastProfile, profile) &&
                ReferenceEquals(_lastScores, emotion.AllScores) &&
                _lastDominantScore == emotion.DominantScore &&
                string.Equals(_lastDominantLabel, emotion.DominantLabel, System.StringComparison.Ordinal))
            {
                // Same published reading as the last blend — outputs are already correct and a
                // held emotion stays allocation-free per tick. (In-place edits to the profile's
                // emotion table re-apply on the next reading change or profile swap.)
                return;
            }

            _lastProfile = profile;
            _lastScores = emotion.AllScores;
            _lastDominantLabel = emotion.DominantLabel;
            _lastDominantScore = emotion.DominantScore;
            _lastFrameVersion = -1;

            emotion.CopyScoresTo(_scoreScratch);
            BlendCurrentScores(profile, emotion.DominantLabel, emotion.DominantScore);
        }

        /// <summary>Allocation-free borrowed-frame overload used by the runtime controller.</summary>
        public void Tick(ConvaiBodyLanguageProfile profile, in EmotionStateFrame emotion)
        {
            if (profile == null || !profile.EnableEmotionModulation || emotion.IsNeutral)
            {
                Reset();
                return;
            }

            if (ReferenceEquals(_lastProfile, profile) &&
                _lastDominantScore == emotion.DominantScore &&
                string.Equals(_lastDominantLabel, emotion.DominantLabel, System.StringComparison.Ordinal) &&
                _lastFrameVersion == emotion.Version)
                return;

            _lastProfile = profile;
            _lastScores = null;
            _lastDominantLabel = emotion.DominantLabel;
            _lastDominantScore = emotion.DominantScore;
            _lastFrameVersion = emotion.Version;

            _scoreScratch.Clear();
            int count = System.Math.Min(emotion.Labels?.Count ?? 0, emotion.Scores?.Count ?? 0);
            for (int i = 0; i < count; i++)
                _scoreScratch[emotion.Labels[i]] = emotion.GetScore(i);

            BlendCurrentScores(profile, emotion.DominantLabel, emotion.DominantScore);
        }

        private int _lastFrameVersion = -1;

        private void BlendCurrentScores(ConvaiBodyLanguageProfile profile, string dominantLabel, float dominantScore)
        {

            if (_scoreScratch.Count == 0)
            {
                // No score table published (e.g. Emotion module absent) — fall back to the
                // single dominant label at its reported score so modulation still tracks a
                // basic ConvaiEmotionController without the full table.
                BlendLabel(profile, dominantLabel, dominantScore,
                    out float dominantOpenness, out float dominantLean, out float dominantTension,
                    out float dominantGestureIntensity, out float dominantGestureRate,
                    out float dominantBreathRate, out float dominantBreathDepth);
                _arousalSigned = ArousalFor(dominantLabel) * Mathf.Clamp01(dominantScore);
                Publish(dominantOpenness, dominantLean, dominantTension, dominantGestureIntensity,
                    dominantGestureRate, dominantBreathRate, dominantBreathDepth);
                return;
            }

            // Accumulate every scored label's contribution in DEVIATION-FROM-IDENTITY space
            // (bias identity 0, scale identity 1), each scaled by that label's own [0, 1]
            // confidence. This is the exact multi-label generalization of the single-label path
            // in BlendLabel (which lerps one modifier toward identity by its score): a lone
            // emotion at score s reproduces `bias × s` / `1 + (scale - 1) × s` exactly, aligned
            // emotions reinforce, opposing emotions cancel, and the overall magnitude tracks
            // total confidence instead of being normalized away, so a weak lone emotion moves the
            // body proportionally little rather than at full strength. Publish clamps
            // biases to [-1, 1] and scales to [0, 2], so a strong multi-emotion pile-up saturates
            // rather than overshooting.
            float openness = 0f;
            float lean = 0f;
            float tension = 0f;
            float gestureIntensityDeviation = 0f;
            float gestureRateDeviation = 0f;
            float breathRateDeviation = 0f;
            float breathDepthDeviation = 0f;
            float arousal = 0f;
            bool anyContribution = false;

            foreach (KeyValuePair<string, float> entry in _scoreScratch)
            {
                float score = entry.Value;
                if (score <= 0f) continue;
                anyContribution = true;

                BlendLabel(profile, entry.Key, 1f,
                    out float labelOpenness, out float labelLean, out float labelTension,
                    out float labelGestureIntensity, out float labelGestureRate,
                    out float labelBreathRate, out float labelBreathDepth);

                openness += labelOpenness * score;
                lean += labelLean * score;
                tension += labelTension * score;
                gestureIntensityDeviation += (labelGestureIntensity - 1f) * score;
                gestureRateDeviation += (labelGestureRate - 1f) * score;
                breathRateDeviation += (labelBreathRate - 1f) * score;
                breathDepthDeviation += (labelBreathDepth - 1f) * score;
                // Score-weighted arousal, read straight from the VA
                // table (see Arousal01's remarks) regardless of whether this label also has an
                // authored posture-bias row.
                arousal += ArousalFor(entry.Key) * score;
            }

            if (!anyContribution)
            {
                Reset();
                return;
            }

            _arousalSigned = arousal;

            Publish(
                openness,
                lean,
                tension,
                1f + gestureIntensityDeviation,
                1f + gestureRateDeviation,
                1f + breathRateDeviation,
                1f + breathDepthDeviation);
        }

        private void Publish(
            float openness, float lean, float tension,
            float gestureIntensity, float gestureRate, float breathRate, float breathDepth)
        {
            OpennessBias = Mathf.Clamp(openness, -1f, 1f);
            LeanBias = Mathf.Clamp(lean, -1f, 1f);
            ShoulderTensionBias = Mathf.Clamp(tension, -1f, 1f);
            GestureIntensityScale = Mathf.Clamp(gestureIntensity, 0f, 2f);
            GestureRateScale = Mathf.Clamp(gestureRate, 0f, 2f);
            BreathRateScale = Mathf.Clamp(breathRate, 0f, 2f);
            BreathDepthScale = Mathf.Clamp(breathDepth, 0f, 2f);
        }

        /// <summary>
        ///     Resolves one label's modifier — authored table row, else valence/arousal
        ///     fallback, else identity — and blends it toward identity by
        ///     <paramref name="intensity" /> exactly as <c>EmotionGazeModulator</c> blends its
        ///     single-label modifier, so onsets/decays stay smooth for each contributing label.
        /// </summary>
        private static void BlendLabel(
            ConvaiBodyLanguageProfile profile, string label, float intensity,
            out float openness, out float lean, out float tension,
            out float gestureIntensity, out float gestureRate, out float breathRate, out float breathDepth)
        {
            float t = Mathf.Clamp01(intensity);

            if (profile.TryGetEmotionModifier(label, out BodyLanguageEmotionModifier modifier))
            {
                openness = Mathf.Lerp(0f, modifier.OpennessBias, t);
                lean = Mathf.Lerp(0f, modifier.LeanBias, t);
                tension = Mathf.Lerp(0f, modifier.ShoulderTensionBias, t);
                gestureIntensity = Mathf.Lerp(1f, modifier.GestureIntensityScale, t);
                gestureRate = Mathf.Lerp(1f, modifier.GestureRateScale, t);
                breathRate = Mathf.Lerp(1f, modifier.BreathRateScale, t);
                breathDepth = Mathf.Lerp(1f, modifier.BreathDepthScale, t);
                return;
            }

            if (profile.ValenceArousalFallback &&
                EmotionValenceArousalTable.TryGetValenceArousal(label, out float valence, out float arousal))
            {
                EmotionValenceArousalTable.DeriveModifier(valence, arousal,
                    out float derivedOpenness, out float derivedLean, out float derivedTension,
                    out float derivedGestureIntensity, out float derivedGestureRate,
                    out float derivedBreathRate, out float derivedBreathDepth);

                openness = Mathf.Lerp(0f, derivedOpenness, t);
                lean = Mathf.Lerp(0f, derivedLean, t);
                tension = Mathf.Lerp(0f, derivedTension, t);
                gestureIntensity = Mathf.Lerp(1f, derivedGestureIntensity, t);
                gestureRate = Mathf.Lerp(1f, derivedGestureRate, t);
                breathRate = Mathf.Lerp(1f, derivedBreathRate, t);
                breathDepth = Mathf.Lerp(1f, derivedBreathDepth, t);
                return;
            }

            openness = 0f;
            lean = 0f;
            tension = 0f;
            gestureIntensity = 1f;
            gestureRate = 1f;
            breathRate = 1f;
            breathDepth = 1f;
        }

        /// <summary>
        ///     Raw signed arousal (-1..1) for a label from <see cref="EmotionValenceArousalTable" />,
        ///     or 0 (neutral) when the label is not in the table.
        /// </summary>
        private static float ArousalFor(string label) =>
            EmotionValenceArousalTable.TryGetValenceArousal(label, out _, out float arousal) ? arousal : 0f;
    }
}
