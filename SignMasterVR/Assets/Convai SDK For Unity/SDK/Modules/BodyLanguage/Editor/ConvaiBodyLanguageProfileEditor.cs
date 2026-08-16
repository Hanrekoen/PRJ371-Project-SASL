using System.Collections.Generic;
using System.Reflection;
using Convai.Editor.Inspectors.Framework;
using Convai.Modules.BodyLanguage.Data;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Modules.BodyLanguage.Editor
{
    /// <summary>
    ///     Sectioned inspector for <see cref="ConvaiBodyLanguageProfile" />: one collapsible section per
    ///     authoring group (Expressiveness, Posture, Breathing, Stance &amp; Sway, Gesticulation,
    ///     Listening &amp; Fidgets, Reactions, Idle Presence, Camera LOD, Emotion, Head Gestures,
    ///     States, Signals, Diagnostics) with default property drawing inside each.
    /// </summary>
    /// <remarks>
    ///     The profile's fields carry no section attributes, so this editor takes the body over rather
    ///     than using the attribute-driven renderer, and declares the grouping in one table. Section
    ///     expansion persists under the original host id so a user's open/closed sections survive this
    ///     editor's move onto the shared base.
    /// </remarks>
    [CustomEditor(typeof(ConvaiBodyLanguageProfile))]
    internal sealed class ConvaiBodyLanguageProfileEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Body Language Profile";
        private const string SubtitleText = "Convai Body Language";

        private const string PurposeText =
            "Body Animation moves the body; Body Language makes it speak — gesticulation, posture, " +
            "breathing, and embodied listening, per dialogue state.";

        private const string DemeanorHelp =
            "Demeanor is a one-shot tuning starting point, not a live link — it matches the Emotion " +
            "module's demeanor vocabulary. Applying it multiplies a few fields once; keep tuning by hand " +
            "afterward.";

        private static readonly GUIContent DemeanorSection = new("Demeanor");
        private static readonly GUIContent DemeanorLabel = new("Demeanor");
        private static readonly GUIContent ApplyButton = new("Apply");

        internal static readonly (GUIContent Title, string Glyph, string[] Properties)[] Sections =
        {
            (new GUIContent("Expressiveness"), Glyphs.Profile, new[]
            {
                "expressivenessPreset", "customExpressiveness"
            }),
            (new GUIContent("Posture"), Glyphs.Motion, new[]
            {
                "maxOpennessDegrees", "maxLeanDegrees", "maxTensionDegrees", "maxLateralShiftDegrees",
                "postureSpringSharpness", "postureMaxAngularSpeed", "postureTargetSlewSeconds",
                "postureFadeSeconds"
            }),
            (new GUIContent("Breathing"), Glyphs.Motion, new[]
            {
                "maxBreathChestExpansionDegrees", "maxBreathShoulderLiftDegrees",
                "enableBreathAdaptiveLayering", "breathHeadStabilization",
                "enableCatchBreath", "enableSigh", "enableInhaleBeforeSpeaking",
                "exertionRateBoost", "exertionDepthBoost"
            }),
            (new GUIContent("Stance & Sway"), Glyphs.Motion, new[]
            {
                "enableWeightShifts", "weightShiftIntervalSeconds", "weightShiftIntervalVarianceSeconds",
                "weightShiftTransferSeconds", "maxPelvisOffsetCentimeters", "maxPelvisObliquityDegrees",
                "maxPelvisYawDegrees", "enableLegCompensation",
                "enableAmbientSway", "maxSwayDegrees"
            }),
            (new GUIContent("Gesticulation"), Glyphs.Motion, new[]
            {
                "beatMinIntervalSeconds", "beatIntervalVarianceSeconds", "beatHeadIntensity",
                "posturePulseAmplitude", "posturePulseAttackSeconds", "posturePulseDecaySeconds",
                "energyToIntensityGain", "statisticalCadenceIntervalSeconds",
                "statisticalCadenceVarianceSeconds", "upperBodySuppressionPostureWeight",
                "semanticCueRefractorySeconds", "maxShrugDegrees",
                "enableHandMicro", "maxFingerCurlDegrees", "maxWristMicroDegrees",
                "enableProceduralGestureFallback", "proceduralGestureAmplitude"
            }),
            (new GUIContent("Listening & Fidgets"), Glyphs.Motion, new[]
            {
                "fidgetGapSeconds", "fidgetEaseSeconds", "fidgetHoldSeconds",
                "listeningTiltCadenceSeconds", "listeningTiltIntensity"
            }),
            (new GUIContent("Reactions"), Glyphs.Run, new[]
            {
                "enableReactions", "maxFlinchDegrees", "maxAmusementBounceDegrees"
            }),
            (new GUIContent("Idle Presence"), Glyphs.Motion, new[]
            {
                "enableIdleMacroCycles"
            }),
            (new GUIContent("Camera LOD"), Glyphs.Range, new[]
            {
                "enableCameraDistanceLod"
            }),
            (new GUIContent("Emotion"), Glyphs.Profile, new[]
            {
                "enableEmotionModulation", "emotionModifiers", "valenceArousalFallback"
            }),
            (new GUIContent("Head Gestures"), Glyphs.Motion, new[]
            {
                "headGestureNodMaxPitchDegrees", "headGestureShakeMaxYawDegrees",
                "headGestureTiltMaxRollDegrees", "headGestureRefractorySeconds",
                "headGestureRefractoryVarianceSeconds"
            }),
            (new GUIContent("States"), Glyphs.Contract, new[]
            {
                "statePolicies", "policyTransitionSeconds"
            }),
            (new GUIContent("Signals"), Glyphs.Contract, new[]
            {
                "attackSeconds", "releaseSeconds", "baselineWindowSeconds",
                "onsetThresholdAboveBaseline", "releaseHysteresisFraction",
                "emphasisDerivativeThreshold", "refractorySeconds", "sustainIntervalSeconds"
            }),
            (new GUIContent("Diagnostics"), Glyphs.Validation, new[]
            {
                "traceVerbosity"
            })
        };

        /// <summary>Label content per property, built once — the tooltip still comes from the field.</summary>
        private readonly Dictionary<string, GUIContent> _labels = new();

        private string[] _demeanorNames;
        private int _demeanorPresetIndex;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        /// <summary>Kept at the pre-migration id so persisted section expansion is not orphaned.</summary>
        protected override string EditorStateHostId => "BodyLanguageProfileEditor";

        protected override void OnEnable()
        {
            base.OnEnable();

            BodyLanguageDemeanorPresets.Multipliers[] presets = BodyLanguageDemeanorPresets.Presets;
            _demeanorNames = new string[presets.Length];
            for (int i = 0; i < presets.Length; i++)
                _demeanorNames[i] = presets[i].Name;
        }

        protected override void DrawBody()
        {
            using var readOnly = Convai.Editor.Ownership.ConvaiOwnershipNotice.BeginAssetEdit(target);

            DrawDemeanorCard();

            for (int s = 0; s < Sections.Length; s++)
            {
                (GUIContent title, string glyph, string[] properties) = Sections[s];

                if (DrawSection(title.text, title, glyph))
                    DrawSectionBody(() => DrawSectionProperties(properties));
            }
        }

        private void DrawSectionProperties(string[] properties)
        {
            for (int p = 0; p < properties.Length; p++)
            {
                SerializedProperty property = serializedObject.FindProperty(properties[p]);
                if (property == null)
                {
                    WarningBox("Field missing", $"Missing serialized field '{properties[p]}'.");
                    continue;
                }

                EditorGUILayout.PropertyField(property, LabelFor(property), true);
            }
        }

        /// <summary>
        ///     The label to draw this property with: a plain-English override where one exists,
        ///     otherwise Unity's own nicified field name. Either way it carries the field's tooltip,
        ///     which stays authored on the field itself so there is one place to write it.
        /// </summary>
        private GUIContent LabelFor(SerializedProperty property)
        {
            if (_labels.TryGetValue(property.propertyPath, out GUIContent cached))
                return cached;

            string text = BodyLanguageLabels.ForField(property.propertyPath);

            // property.tooltip is empty until the property has been drawn once, so read the
            // attribute directly — this runs once per field for the lifetime of the inspector.
            var content = new GUIContent(text, TooltipFor(property.propertyPath));
            _labels[property.propertyPath] = content;
            return content;
        }

        private static string TooltipFor(string fieldName)
        {
            FieldInfo field = typeof(ConvaiBodyLanguageProfile).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field?.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? string.Empty;
        }

        /// <summary>
        ///     Demeanor row: a dropdown of <see cref="BodyLanguageDemeanorPresets.Presets" /> plus an
        ///     Apply button — a one-shot tuning starting point, not a live link.
        /// </summary>
        private void DrawDemeanorCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Profile, DemeanorSection);

            EditorGUILayout.BeginHorizontal();
            _demeanorPresetIndex = EditorGUILayout.Popup(DemeanorLabel, _demeanorPresetIndex, _demeanorNames);
            if (GUILayout.Button(ApplyButton, GUILayout.Width(60f)))
                BodyLanguageDemeanorPresets.Apply(
                    serializedObject, BodyLanguageDemeanorPresets.Presets[_demeanorPresetIndex]);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label(DemeanorHelp, Theme.MutedWrapped);

            Theme.EndCard();
        }
    }
}
