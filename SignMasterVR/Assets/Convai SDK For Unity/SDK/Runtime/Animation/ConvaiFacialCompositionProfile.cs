using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Runtime.Animation
{
    /// <summary>
    ///     Configures per-region facial blendshape composition for the <see cref="FacialBlendshapeCompositorHost" />.
    ///     Each blendshape is classified into a <see cref="FacialBlendshapeRegion" /> via semicolon-separated
    ///     name patterns, and each region defines how Emotion, LipSync, and Custom
    ///     layers blend together during idle and speech states.
    /// </summary>
    /// <remarks>
    ///     Patterns match ignoring case <em>and</em> word separators, so one pattern covers every
    ///     naming style a face arrives in: <c>Jaw_Open</c> matches Character Creator's
    ///     <c>Jaw_Open</c>, ARKit's <c>jawOpen</c> and <c>Jaw Open</c> alike. This lets the built-in
    ///     classification recognize ARKit and Ready Player Me faces — the common case — without any
    ///     configuration, instead of classifying them almost entirely as
    ///     <see cref="FacialBlendshapeRegion.Other" /> and composing with the wrong per-region
    ///     weights.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ConvaiFacialCompositionProfile",
        menuName = "Convai/Embodiment/Facial Composition Profile",
        order = 160)]
    public sealed class ConvaiFacialCompositionProfile : ScriptableObject
    {
        private const char PatternSeparator = ';';

        [Tooltip("Blendshapes whose name contains any of these patterns are classified as Mouth. " +
                 "Case and separators are ignored, so Jaw_Open also matches jawOpen.")]
        [SerializeField] [ConvaiInspectorSection("Region Name Patterns")] private string _mouthPatterns = "Mouth;Lip;Tongue;Jaw_Open";

        [Tooltip("Blendshapes classified as Brow/Forehead. Case and separators are ignored.")]
        [SerializeField] [ConvaiInspectorSection("Region Name Patterns")] private string _browPatterns = "Brow;Forehead";

        // Eye_L_Look / Eye_R_Look are listed beside Eye_Look because separators are ignored but
        // ORDER is not: Character Creator writes the side into the middle of the name
        // (Eye_L_Look_Up), which "Eye_Look" cannot match. Every eye-look target on a CC rig was
        // landing in Other. Eyelash follows the lids, so it belongs to this region rather than to
        // the leftovers.
        [Tooltip("Blendshapes classified as Eye. Case and separators are ignored, so Eye_Blink also " +
                 "matches eyeBlinkLeft.")]
        [SerializeField] [ConvaiInspectorSection("Region Name Patterns")] private string _eyePatterns = "Eye_Blink;Eye_Squint;Eye_Wide;Eye_Look;Eye_L_Look;Eye_R_Look;Eyelash";

        [Tooltip("Blendshapes classified as Cheek/Nose. Case and separators are ignored.")]
        [SerializeField] [ConvaiInspectorSection("Region Name Patterns")] private string _cheekPatterns = "Cheek;Nose;Sneer";

        // Jaw_L / Jaw_R cover both spellings — "jawl" is a prefix of "jawleft", so the abbreviated
        // form matches Character Creator's Jaw_L and ARKit-style jawLeft alike. Backward/Up/Down
        // were simply missing: five directional jaw targets on a CC rig fell to Other and drove at
        // the leftover region's lip-sync weight instead of the jaw's.
        [Tooltip("Blendshapes classified as Jaw (separate from Mouth for directional jaw movement). " +
                 "Case and separators are ignored, so Jaw_Forward also matches jawForward.")]
        [SerializeField] [ConvaiInspectorSection("Region Name Patterns")] private string _jawPatterns = "Jaw_Forward;Jaw_Backward;Jaw_L;Jaw_R;Jaw_Up;Jaw_Down";

        [Tooltip("Mesh names matching these patterns are prioritized first for facial blendshape " +
                 "discovery. Case and separators are ignored.")]
        [SerializeField] [ConvaiInspectorSection("Mesh Discovery Priority")] private string _headMeshPatterns = "head;face";

        [Tooltip("Secondary priority mesh name patterns (teeth, jaw meshes).")]
        [SerializeField] [ConvaiInspectorSection("Mesh Discovery Priority")] private string _secondaryMeshPatterns = "teeth;tooth";

        [Tooltip("Tertiary priority mesh name patterns (tongue meshes).")]
        [SerializeField] [ConvaiInspectorSection("Mesh Discovery Priority")] private string _tertiaryMeshPatterns = "tongue";

        [Tooltip("Seconds to ramp speech blend factor from 0 to 1 when speech starts.")]
        [SerializeField] [Min(0.01f)] [ConvaiInspectorSection("Speech Blend Timing")] private float _speechRampUpDuration = 0.15f;

        [Tooltip("Seconds to ramp speech blend factor from 1 to 0 when speech ends.")]
        [SerializeField] [Min(0.01f)] [ConvaiInspectorSection("Speech Blend Timing")] private float _speechRampDownDuration = 0.4f;

        [Tooltip("When enabled, composed values exceeding 100 are clamped after all layers contribute.")]
        [SerializeField] [ConvaiInspectorSection("Global Normalization")] private bool _enableGlobalNormalization;

        [Tooltip("How Mouth blendshapes blend Emotion, LipSync, and Custom layers while idle and while speaking.")]
        [SerializeField] [ConvaiInspectorSection("Per-Region Composition")] private RegionBlendConfig _mouthConfig = RegionBlendConfig.Create(
            idleEmotion: 1f, idleLipSync: 0f,
            speakingEmotion: 0.2f, speakingLipSync: 1f);

        [Tooltip("How Brow/Forehead blendshapes blend Emotion, LipSync, and Custom layers while idle and while speaking.")]
        [SerializeField] [ConvaiInspectorSection("Per-Region Composition")] private RegionBlendConfig _browConfig = RegionBlendConfig.Create(
            idleEmotion: 1f, idleLipSync: 0f,
            speakingEmotion: 0.85f, speakingLipSync: 0.15f);

        [Tooltip("How Eye blendshapes blend Emotion, LipSync, and Custom layers while idle and while speaking.")]
        [SerializeField] [ConvaiInspectorSection("Per-Region Composition")] private RegionBlendConfig _eyeConfig = RegionBlendConfig.Create(
            idleEmotion: 0.8f, idleLipSync: 0f,
            speakingEmotion: 0.7f, speakingLipSync: 0.1f);

        [Tooltip("How Cheek/Nose blendshapes blend Emotion, LipSync, and Custom layers while idle and while speaking.")]
        [SerializeField] [ConvaiInspectorSection("Per-Region Composition")] private RegionBlendConfig _cheekConfig = RegionBlendConfig.Create(
            idleEmotion: 1f, idleLipSync: 0f,
            speakingEmotion: 0.7f, speakingLipSync: 0.25f);

        [Tooltip("How Jaw blendshapes blend Emotion, LipSync, and Custom layers while idle and while speaking.")]
        [SerializeField] [ConvaiInspectorSection("Per-Region Composition")] private RegionBlendConfig _jawConfig = RegionBlendConfig.Create(
            idleEmotion: 0.5f, idleLipSync: 0f,
            speakingEmotion: 0.1f, speakingLipSync: 1f);

        [Tooltip("How all other blendshapes blend Emotion, LipSync, and Custom layers while idle and while speaking.")]
        [SerializeField] [ConvaiInspectorSection("Per-Region Composition")] private RegionBlendConfig _otherConfig = RegionBlendConfig.Create(
            idleEmotion: 1f, idleLipSync: 0f,
            speakingEmotion: 0.8f, speakingLipSync: 0.3f);

        private List<string> _parsedMouth;
        private List<string> _parsedBrow;
        private List<string> _parsedEye;
        private List<string> _parsedCheek;
        private List<string> _parsedJaw;
        private List<string> _parsedHeadMesh;
        private List<string> _parsedSecondaryMesh;
        private List<string> _parsedTertiaryMesh;
        private int _cachedHash;

        /// <summary>
        ///     Creates the runtime-default profile used when no asset is assigned, matching the
        ///     <c>CreateDefault</c> every other Convai profile provides.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Every value this returns is a field initializer a few lines above, so a bare
        ///         <see cref="ScriptableObject.CreateInstance{T}" /> is already the shipped default —
        ///         this exists to say so at the call site, and to flag the instance
        ///         <see cref="HideFlags.HideAndDontSave" /> so a runtime-owned profile can never be
        ///         saved into a scene.
        ///     </para>
        ///     <para>
        ///         This replaced a <c>Resources.Load</c> of a <c>.asset</c> whose serialized contents
        ///         were byte-for-byte these same initializers. That asset bought nothing and could
        ///         fail: a missing, renamed or shadowed load dropped the character onto the
        ///         compositor's degraded max-blend path — with a warning — while the correct default
        ///         sat in this file the whole time. The caller owns what this returns and must
        ///         destroy it.
        ///     </para>
        /// </remarks>
        public static ConvaiFacialCompositionProfile CreateDefault()
        {
            ConvaiFacialCompositionProfile instance = CreateInstance<ConvaiFacialCompositionProfile>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        public float SpeechRampUpDuration => _speechRampUpDuration;
        public float SpeechRampDownDuration => _speechRampDownDuration;
        public bool EnableGlobalNormalization => _enableGlobalNormalization;

        public ref readonly RegionBlendConfig MouthConfig => ref _mouthConfig;
        public ref readonly RegionBlendConfig BrowConfig => ref _browConfig;
        public ref readonly RegionBlendConfig EyeConfig => ref _eyeConfig;
        public ref readonly RegionBlendConfig CheekConfig => ref _cheekConfig;
        public ref readonly RegionBlendConfig JawConfig => ref _jawConfig;
        public ref readonly RegionBlendConfig OtherConfig => ref _otherConfig;

        public RegionBlendConfig GetRegionConfig(FacialBlendshapeRegion region)
        {
            return region switch
            {
                FacialBlendshapeRegion.Mouth => _mouthConfig,
                FacialBlendshapeRegion.Brow => _browConfig,
                FacialBlendshapeRegion.Eye => _eyeConfig,
                FacialBlendshapeRegion.Cheek => _cheekConfig,
                FacialBlendshapeRegion.Jaw => _jawConfig,
                FacialBlendshapeRegion.Other => _otherConfig,
                _ => _otherConfig
            };
        }

        public FacialBlendshapeRegion ClassifyBlendshape(string blendshapeName)
        {
            EnsureParsedPatterns();

            if (MatchesAny(blendshapeName, _parsedMouth)) return FacialBlendshapeRegion.Mouth;
            if (MatchesAny(blendshapeName, _parsedJaw)) return FacialBlendshapeRegion.Jaw;
            if (MatchesAny(blendshapeName, _parsedBrow)) return FacialBlendshapeRegion.Brow;
            if (MatchesAny(blendshapeName, _parsedEye)) return FacialBlendshapeRegion.Eye;
            if (MatchesAny(blendshapeName, _parsedCheek)) return FacialBlendshapeRegion.Cheek;

            return FacialBlendshapeRegion.Other;
        }

        /// <summary>
        ///     Returns the discovery priority for a mesh based on configured name patterns.
        ///     Lower values = higher priority. Returns <c>int.MaxValue</c> for unmatched meshes.
        /// </summary>
        public int GetMeshDiscoveryPriority(string meshName)
        {
            EnsureParsedPatterns();

            if (MatchesAny(meshName, _parsedHeadMesh)) return 0;
            if (MatchesAny(meshName, _parsedSecondaryMesh)) return 1;
            if (MatchesAny(meshName, _parsedTertiaryMesh)) return 2;
            return 3;
        }

        public int ComputeConfigurationHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (_mouthPatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_browPatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_eyePatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_cheekPatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_jawPatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_headMeshPatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_secondaryMeshPatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_tertiaryMeshPatterns ?? string.Empty).GetHashCode();
                hash = hash * 31 + _speechRampUpDuration.GetHashCode();
                hash = hash * 31 + _speechRampDownDuration.GetHashCode();
                hash = hash * 31 + _enableGlobalNormalization.GetHashCode();
                hash = hash * 31 + HashRegionConfig(_mouthConfig);
                hash = hash * 31 + HashRegionConfig(_browConfig);
                hash = hash * 31 + HashRegionConfig(_eyeConfig);
                hash = hash * 31 + HashRegionConfig(_cheekConfig);
                hash = hash * 31 + HashRegionConfig(_jawConfig);
                hash = hash * 31 + HashRegionConfig(_otherConfig);
                return hash;
            }
        }

        private void OnValidate()
        {
            InvalidatePatternCache();
        }

        private void OnEnable()
        {
            InvalidatePatternCache();
        }

        private void InvalidatePatternCache()
        {
            _parsedMouth = null;
            _parsedBrow = null;
            _parsedEye = null;
            _parsedCheek = null;
            _parsedJaw = null;
            _parsedHeadMesh = null;
            _parsedSecondaryMesh = null;
            _parsedTertiaryMesh = null;
            _cachedHash = 0;
        }

        private void EnsureParsedPatterns()
        {
            int currentHash = ComputeConfigurationHash();
            if (_parsedMouth != null && currentHash == _cachedHash)
                return;

            _parsedMouth = ParsePatterns(_mouthPatterns);
            _parsedBrow = ParsePatterns(_browPatterns);
            _parsedEye = ParsePatterns(_eyePatterns);
            _parsedCheek = ParsePatterns(_cheekPatterns);
            _parsedJaw = ParsePatterns(_jawPatterns);
            _parsedHeadMesh = ParsePatterns(_headMeshPatterns);
            _parsedSecondaryMesh = ParsePatterns(_secondaryMeshPatterns);
            _parsedTertiaryMesh = ParsePatterns(_tertiaryMeshPatterns);
            _cachedHash = currentHash;
        }

        /// <summary>
        ///     Splits the authored pattern list and reduces each entry to its letters and digits,
        ///     lower-cased, so a pattern is stored in the one form <see cref="MatchesAny" /> compares
        ///     against. Authoring stays in whatever style reads best — <c>Jaw_Open</c>, <c>jawOpen</c>
        ///     and <c>Jaw Open</c> are the same pattern once here.
        /// </summary>
        private static List<string> ParsePatterns(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            string[] parts = raw.Split(PatternSeparator, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string pattern = Normalize(parts[i]);
                if (pattern.Length > 0)
                    result.Add(pattern);
            }

            return result;
        }

        /// <summary>Letters and digits only, lower-cased. Empty when nothing survives.</summary>
        private static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var builder = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        private static bool MatchesAny(string name, List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0 || string.IsNullOrEmpty(name))
                return false;

            for (int i = 0; i < patterns.Count; i++)
            {
                if (ContainsNormalized(name, patterns[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether <paramref name="name" /> contains <paramref name="normalizedPattern" /> once
        ///     both are read as letters and digits only, ignoring case.
        /// </summary>
        /// <remarks>
        ///     Walks <paramref name="name" /> in place rather than normalizing it into a new string:
        ///     classification runs over every blendshape of every face mesh at discovery, and this
        ///     keeps that a zero-allocation pass.
        /// </remarks>
        private static bool ContainsNormalized(string name, string normalizedPattern)
        {
            if (string.IsNullOrEmpty(normalizedPattern))
                return false;

            for (int start = 0; start < name.Length; start++)
            {
                // Only start a comparison on a character the pattern could match, so a run of
                // separators is never mistaken for a match position.
                if (!char.IsLetterOrDigit(name[start]))
                    continue;

                int patternIndex = 0;
                for (int i = start; i < name.Length && patternIndex < normalizedPattern.Length; i++)
                {
                    char c = name[i];
                    if (!char.IsLetterOrDigit(c))
                        continue;

                    if (char.ToLowerInvariant(c) != normalizedPattern[patternIndex])
                        break;

                    patternIndex++;
                }

                if (patternIndex == normalizedPattern.Length)
                    return true;
            }

            return false;
        }

        private static int HashRegionConfig(in RegionBlendConfig config)
        {
            unchecked
            {
                int h = config.IdleEmotionWeight.GetHashCode();
                h = h * 31 + config.IdleLipSyncWeight.GetHashCode();
                h = h * 31 + config.IdleCustomWeight.GetHashCode();
                h = h * 31 + config.SpeakingEmotionWeight.GetHashCode();
                h = h * 31 + config.SpeakingLipSyncWeight.GetHashCode();
                h = h * 31 + config.SpeakingCustomWeight.GetHashCode();
                h = h * 31 + ((int)config.Mode).GetHashCode();
                h = h * 31 + config.EnableNormalization.GetHashCode();
                return h;
            }
        }
    }
}
