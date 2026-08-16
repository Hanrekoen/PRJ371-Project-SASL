using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.Emotion.Profiles
{
    /// <summary>
    ///     The area of the face a single expression channel belongs to.
    /// </summary>
    /// <remarks>
    ///     Regions are how one expression can move different parts of the face on different
    ///     timings — brows lift faster than a smile spreads — and how the runtime keeps mouth
    ///     channels separable from the rest, so LipSync retains ownership of the mouth while an
    ///     emotion is playing.
    /// </remarks>
    public enum EmotionFacialRegion
    {
        /// <summary>Brow raise, knit and furrow.</summary>
        Brow,

        /// <summary>Lids, squint and widening.</summary>
        Eyes,

        /// <summary>Cheek raise and nose wrinkle.</summary>
        CheekNose,

        /// <summary>Jaw open and slide.</summary>
        Jaw,

        /// <summary>Lip shapes. Kept separable so speech can keep ownership of the mouth.</summary>
        Mouth
    }

    /// <summary>One semantic facial channel in an authored emotion performance recipe.</summary>
    [Serializable]
    public sealed class EmotionExpressionChannel
    {
        [SerializeField] private StandardBlendshape semantic;
        [SerializeField] private EmotionFacialRegion region;
        [SerializeField, Range(0f, 100f)] private float fullWeight = 50f;
        [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float prosodySensitivity;

        public StandardBlendshape Semantic => semantic;
        public EmotionFacialRegion Region => region;
        public float FullWeight => fullWeight;
        public AnimationCurve IntensityCurve => intensityCurve;
        public float ProsodySensitivity => prosodySensitivity;

        public EmotionExpressionChannel() { }

        public EmotionExpressionChannel(StandardBlendshape semantic, EmotionFacialRegion region,
            float fullWeight, float prosodySensitivity = 0f)
        {
            this.semantic = semantic;
            this.region = region;
            this.fullWeight = Mathf.Clamp(fullWeight, 0f, 100f);
            this.prosodySensitivity = Mathf.Clamp01(prosodySensitivity);
            intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    /// <summary>
    /// Semantic, rig-independent facial performance recipe with explicit episode timing.
    /// Mesh names never belong here; <see cref="StandardBlendshape"/> is resolved by the rig.
    /// </summary>
    [Serializable]
    public sealed class EmotionExpressionRecipe
    {
        [SerializeField, ConvaiEmotionLabel] private string emotionLabel;
        [SerializeField, Range(0.03f, 2f)] private float onsetSeconds = 0.2f;
        [SerializeField, Range(0f, 3f)] private float apexHoldSeconds = 0.15f;
        [SerializeField, Range(0.05f, 5f)] private float releaseSeconds = 0.65f;
        [SerializeField, Range(0f, 1f)] private float maximumIntensity = 0.85f;
        [SerializeField] private List<EmotionExpressionChannel> channels = new();

        public string EmotionLabel => emotionLabel;
        public float OnsetSeconds => onsetSeconds;
        public float ApexHoldSeconds => apexHoldSeconds;
        public float ReleaseSeconds => releaseSeconds;
        public float MaximumIntensity => maximumIntensity;
        public IReadOnlyList<EmotionExpressionChannel> Channels => channels;

        public EmotionExpressionRecipe() { }

        public EmotionExpressionRecipe(string label, float onset, float hold, float release,
            float maximumIntensity, params EmotionExpressionChannel[] channels)
        {
            emotionLabel = label ?? string.Empty;
            onsetSeconds = Mathf.Clamp(onset, 0.03f, 2f);
            apexHoldSeconds = Mathf.Clamp(hold, 0f, 3f);
            releaseSeconds = Mathf.Clamp(release, 0.05f, 5f);
            this.maximumIntensity = Mathf.Clamp01(maximumIntensity);
            this.channels = channels != null
                ? new List<EmotionExpressionChannel>(channels)
                : new List<EmotionExpressionChannel>();
        }

        /// <summary>AAA-safe semantic defaults; deliberately below anatomical extremes.</summary>
        public static List<EmotionExpressionRecipe> CreateDefaultSet()
        {
            EmotionExpressionChannel C(StandardBlendshape s, EmotionFacialRegion r, float w, float p = 0f) =>
                new(s, r, w, p);

            return new List<EmotionExpressionRecipe>
            {
                new("joy", 0.16f, 0.18f, 0.75f, 0.9f,
                    C(StandardBlendshape.MouthSmileLeft, EmotionFacialRegion.Mouth, 68f, 0.10f),
                    C(StandardBlendshape.MouthSmileRight, EmotionFacialRegion.Mouth, 71f, 0.10f),
                    C(StandardBlendshape.CheekSquintLeft, EmotionFacialRegion.CheekNose, 42f, 0.22f),
                    C(StandardBlendshape.CheekSquintRight, EmotionFacialRegion.CheekNose, 44f, 0.22f),
                    C(StandardBlendshape.EyeSquintLeft, EmotionFacialRegion.Eyes, 20f),
                    C(StandardBlendshape.EyeSquintRight, EmotionFacialRegion.Eyes, 22f)),
                new("trust", 0.35f, 0.4f, 1.1f, 0.65f,
                    C(StandardBlendshape.MouthSmileLeft, EmotionFacialRegion.Mouth, 31f),
                    C(StandardBlendshape.MouthSmileRight, EmotionFacialRegion.Mouth, 33f),
                    C(StandardBlendshape.CheekSquintLeft, EmotionFacialRegion.CheekNose, 18f),
                    C(StandardBlendshape.CheekSquintRight, EmotionFacialRegion.CheekNose, 19f)),
                new("sadness", 0.48f, 0.45f, 1.45f, 0.82f,
                    C(StandardBlendshape.BrowInnerUp, EmotionFacialRegion.Brow, 58f),
                    C(StandardBlendshape.MouthFrownLeft, EmotionFacialRegion.Mouth, 47f),
                    C(StandardBlendshape.MouthFrownRight, EmotionFacialRegion.Mouth, 50f),
                    C(StandardBlendshape.EyeBlinkLeft, EmotionFacialRegion.Eyes, 12f),
                    C(StandardBlendshape.EyeBlinkRight, EmotionFacialRegion.Eyes, 13f)),
                new("anger", 0.11f, 0.5f, 0.9f, 0.88f,
                    C(StandardBlendshape.BrowDownLeft, EmotionFacialRegion.Brow, 62f, 0.08f),
                    C(StandardBlendshape.BrowDownRight, EmotionFacialRegion.Brow, 66f, 0.08f),
                    C(StandardBlendshape.EyeSquintLeft, EmotionFacialRegion.Eyes, 31f),
                    C(StandardBlendshape.EyeSquintRight, EmotionFacialRegion.Eyes, 34f),
                    C(StandardBlendshape.MouthPressLeft, EmotionFacialRegion.Mouth, 42f),
                    C(StandardBlendshape.MouthPressRight, EmotionFacialRegion.Mouth, 45f)),
                new("fear", 0.09f, 0.12f, 0.75f, 0.86f,
                    C(StandardBlendshape.EyeWideLeft, EmotionFacialRegion.Eyes, 58f),
                    C(StandardBlendshape.EyeWideRight, EmotionFacialRegion.Eyes, 61f),
                    C(StandardBlendshape.BrowInnerUp, EmotionFacialRegion.Brow, 52f),
                    C(StandardBlendshape.BrowOuterUpLeft, EmotionFacialRegion.Brow, 44f),
                    C(StandardBlendshape.BrowOuterUpRight, EmotionFacialRegion.Brow, 47f),
                    C(StandardBlendshape.MouthStretchLeft, EmotionFacialRegion.Mouth, 37f),
                    C(StandardBlendshape.MouthStretchRight, EmotionFacialRegion.Mouth, 40f)),
                new("surprise", 0.065f, 0.08f, 0.42f, 0.92f,
                    C(StandardBlendshape.EyeWideLeft, EmotionFacialRegion.Eyes, 68f, 0.12f),
                    C(StandardBlendshape.EyeWideRight, EmotionFacialRegion.Eyes, 71f, 0.12f),
                    C(StandardBlendshape.BrowOuterUpLeft, EmotionFacialRegion.Brow, 62f, 0.18f),
                    C(StandardBlendshape.BrowOuterUpRight, EmotionFacialRegion.Brow, 65f, 0.18f),
                    C(StandardBlendshape.JawOpen, EmotionFacialRegion.Jaw, 38f, 0.05f)),
                new("disgust", 0.18f, 0.25f, 0.7f, 0.82f,
                    C(StandardBlendshape.NoseSneerLeft, EmotionFacialRegion.CheekNose, 60f),
                    C(StandardBlendshape.NoseSneerRight, EmotionFacialRegion.CheekNose, 66f),
                    C(StandardBlendshape.MouthUpperUpLeft, EmotionFacialRegion.Mouth, 36f),
                    C(StandardBlendshape.MouthUpperUpRight, EmotionFacialRegion.Mouth, 42f),
                    C(StandardBlendshape.BrowDownLeft, EmotionFacialRegion.Brow, 24f)),
                new("anticipation", 0.3f, 0.22f, 0.8f, 0.68f,
                    C(StandardBlendshape.BrowOuterUpLeft, EmotionFacialRegion.Brow, 29f, 0.2f),
                    C(StandardBlendshape.BrowOuterUpRight, EmotionFacialRegion.Brow, 32f, 0.2f),
                    C(StandardBlendshape.MouthSmileLeft, EmotionFacialRegion.Mouth, 22f),
                    C(StandardBlendshape.MouthSmileRight, EmotionFacialRegion.Mouth, 24f))
            };
        }
    }
}
