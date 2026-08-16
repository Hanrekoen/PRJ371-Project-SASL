using System.Collections.Generic;
using UnityEditor;

namespace Convai.Modules.BodyLanguage.Editor
{
    /// <summary>
    ///     The one place a Body Language setting is named. Every surface that shows a setting to a
    ///     user — the profile inspector, the two authored tables, the setup report, and the assistant
    ///     tools that project it — reads its label from here.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unity renders a serialized field's own name when no label is passed, which put anatomy
    ///         and internal machinery in front of customers: "Max Pelvis Obliquity Degrees",
    ///         "Release Hysteresis Fraction". These names say what each control does instead.
    ///     </para>
    ///     <para>
    ///         They live apart from the inspector that draws them because a second surface names the
    ///         same settings: the diagnosis that answers "why isn't this character moving?" tells a
    ///         user which switch is off. If the two kept their own lists, the diagnosis would name a
    ///         control the Inspector does not show — which is not a cosmetic mismatch but a dead end,
    ///         because the user cannot find what they were told to look for.
    ///     </para>
    ///     <para>
    ///         Labels only. No serialized field, public accessor or struct member is renamed, so no
    ///         asset re-serializes and no customer's code breaks. A field whose own name is already
    ///         clear is deliberately absent and keeps Unity's nicified version.
    ///     </para>
    /// </remarks>
    internal static class BodyLanguageLabels
    {
        /// <summary>Profile field name to the label the Inspector draws for it.</summary>
        internal static readonly Dictionary<string, string> Fields = new()
        {
            // Posture
            { "maxOpennessDegrees", "Openness Range" },
            { "maxLeanDegrees", "Lean Range" },
            { "maxTensionDegrees", "Shoulder Tension Range" },
            { "maxLateralShiftDegrees", "Weight-Shift Range" },
            { "postureSpringSharpness", "Posture Responsiveness" },
            { "postureMaxAngularSpeed", "Posture Speed Limit" },
            { "postureTargetSlewSeconds", "Posture Settle Time" },
            { "postureFadeSeconds", "Posture Fade Time" },

            // Stance & Sway
            { "enableWeightShifts", "Shift Weight While Standing" },
            { "weightShiftIntervalSeconds", "Time Between Weight Shifts" },
            { "weightShiftIntervalVarianceSeconds", "Weight-Shift Timing Variance" },
            { "weightShiftTransferSeconds", "Weight-Shift Duration" },
            { "maxPelvisOffsetCentimeters", "Weight-Shift Travel" },
            { "maxPelvisObliquityDegrees", "Hip Hike" },
            { "maxPelvisYawDegrees", "Hip Turn" },
            { "enableLegCompensation", "Keep Feet Planted" },
            { "enableAmbientSway", "Sway On The Spot" },
            { "maxSwayDegrees", "Sway Range" },

            // Breathing
            { "maxBreathChestExpansionDegrees", "Chest Expansion" },
            { "maxBreathShoulderLiftDegrees", "Shoulder Lift" },
            { "enableBreathAdaptiveLayering", "Blend With Animated Breathing" },
            { "breathHeadStabilization", "Keep Head Level" },
            { "enableCatchBreath", "Catch Breath When Interrupted" },
            { "enableSigh", "Sigh When Settling" },
            { "enableInhaleBeforeSpeaking", "Inhale Before Speaking" },
            { "exertionRateBoost", "Breathe Faster When Exerted" },
            { "exertionDepthBoost", "Breathe Deeper When Exerted" },

            // Gesticulation
            { "beatMinIntervalSeconds", "Minimum Time Between Accents" },
            { "beatIntervalVarianceSeconds", "Accent Timing Variance" },
            { "beatHeadIntensity", "Head Accent Strength" },
            { "posturePulseAmplitude", "Posture Accent Strength" },
            { "posturePulseAttackSeconds", "Accent Rise Time" },
            { "posturePulseDecaySeconds", "Accent Fall Time" },
            { "energyToIntensityGain", "Speech Energy Sensitivity" },
            { "statisticalCadenceIntervalSeconds", "Fallback Accent Interval" },
            { "statisticalCadenceVarianceSeconds", "Fallback Accent Variance" },
            { "upperBodySuppressionPostureWeight", "Posture Kept While Sharing" },
            { "semanticCueRefractorySeconds", "Minimum Time Between Gestures" },
            { "maxShrugDegrees", "Shrug Size" },
            { "enableHandMicro", "Move Hands While Idle" },
            { "maxFingerCurlDegrees", "Finger Motion" },
            { "maxWristMicroDegrees", "Wrist Motion" },
            { "enableProceduralGestureFallback", "Gesture Without Animation Clips" },
            { "proceduralGestureAmplitude", "Procedural Gesture Size" },

            // Listening & Fidgets
            { "fidgetGapSeconds", "Time Between Fidgets" },
            { "fidgetEaseSeconds", "Fidget Ease Time" },
            { "fidgetHoldSeconds", "Fidget Hold Time" },
            { "listeningTiltCadenceSeconds", "Time Between Listening Tilts" },
            { "listeningTiltIntensity", "Listening Tilt Strength" },

            // Reactions, idle presence, camera
            { "enableReactions", "React To Sudden Emotion" },
            { "maxFlinchDegrees", "Flinch Size" },
            { "maxAmusementBounceDegrees", "Amused Bounce Size" },
            { "enableIdleMacroCycles", "Vary Over Long Idles" },
            { "enableCameraDistanceLod", "Scale With Camera Distance" },

            // Emotion
            { "enableEmotionModulation", "Let Emotion Shape The Body" },
            { "emotionModifiers", "Per-Emotion Adjustments" },
            { "valenceArousalFallback", "Estimate Unlisted Emotions" },

            // Head gestures
            { "headGestureNodMaxPitchDegrees", "Nod Size" },
            { "headGestureShakeMaxYawDegrees", "Shake Size" },
            { "headGestureTiltMaxRollDegrees", "Tilt Size" },
            { "headGestureRefractorySeconds", "Time Between Head Gestures" },
            { "headGestureRefractoryVarianceSeconds", "Head Gesture Timing Variance" },

            // States
            { "statePolicies", "Per-State Behavior" },
            { "policyTransitionSeconds", "State Blend Time" },

            // Signals
            { "attackSeconds", "Energy Rise Smoothing" },
            { "releaseSeconds", "Energy Fall Smoothing" },
            { "baselineWindowSeconds", "Background Level Window" },
            { "onsetThresholdAboveBaseline", "Speech Start Threshold" },
            { "releaseHysteresisFraction", "Speech End Threshold" },
            { "emphasisDerivativeThreshold", "Emphasis Sensitivity" },
            { "refractorySeconds", "Minimum Time Between Pulses" },
            { "sustainIntervalSeconds", "Continuous Speech Heartbeat" }
        };

        /// <summary>
        ///     Row field name to its label, for the per-state policy and per-emotion adjustment tables.
        /// </summary>
        internal static readonly Dictionary<string, string> Rows = new()
        {
            // Per-state policy
            { "State", "Dialogue State" },
            { "GesticulationEnabled", "Gesture While Speaking" },
            { "GesticulationIntensity", "Gesture Strength" },
            { "ListeningPostureEnabled", "Lean In While Listening" },
            { "ListeningLeanIn", "Lean-In Amount" },
            { "PostureOpennessBias", "Openness" },
            { "SagittalLeanBias", "Lean" },
            { "AmbientDrift", "Sway Amount" },
            { "BreathRateCpm", "Breaths Per Minute" },
            { "BreathDepth", "Breath Depth" },
            { "BreathIrregularity", "Breath Unevenness" },
            { "FidgetsEnabled", "Fidget When Idle" },
            { "FidgetRate", "Fidget Rate" },

            // Per-emotion adjustment
            { "EmotionLabel", "Emotion" },
            { "OpennessBias", "Openness" },
            { "LeanBias", "Lean" },
            { "ShoulderTensionBias", "Shoulder Tension" },
            { "GestureIntensityScale", "Gesture Strength" },
            { "GestureRateScale", "Gesture Rate" },
            { "BreathRateScale", "Breathing Rate" },
            { "BreathDepthScale", "Breathing Depth" }
        };

        /// <summary>The label shown for a profile field, falling back to Unity's nicified name.</summary>
        internal static string ForField(string fieldName) =>
            Fields.TryGetValue(fieldName, out string plain)
                ? plain
                : ObjectNames.NicifyVariableName(fieldName);

        /// <summary>The label shown for a field inside one of the authored tables.</summary>
        internal static string ForRowField(string fieldName) =>
            Rows.TryGetValue(fieldName, out string plain)
                ? plain
                : ObjectNames.NicifyVariableName(fieldName);
    }
}
