using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Editor
{
    /// <summary>
    ///     Plain-English display labels for the config fields whose serialized names are engine
    ///     jargon. Unity derives a field's inspector label from its identifier, which is fine for
    ///     <c>_walkSpeed</c> and useless for <c>_motionHandoffNormalizedTime</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Display only. Serialized names, public API and asset data are untouched, so this
    ///         changes nothing but what the user reads — which is the point: the terms below are
    ///         internal vocabulary that leaked onto a customer-facing surface.
    ///     </para>
    ///     <para>
    ///         Fields not listed here keep Unity's derived name, because it is already plain: a
    ///         field is renamed only where its identifier fails to describe what the setting does to
    ///         someone who has never read the module's source.
    ///     </para>
    /// </remarks>
    internal static class BodyAnimationConfigLabels
    {
        /// <summary>Serialized field name → the label a user should actually see.</summary>
        private static readonly Dictionary<string, string> Labels = new()
        {
            // Locomotion synchronisation — the densest cluster of internal vocabulary.
            { "_rateWarpMin", "Slowest Playback Adjustment" },
            { "_rateWarpMax", "Fastest Playback Adjustment" },
            { "_motionHandoffNormalizedTime", "Transition Point" },
            { "_lowSpeedStopFraction", "Slow-Arrival Threshold" },
            { "_plantedStopMinTravel", "Minimum Distance For A Full Stop" },
            { "_speedDampingSeconds", "Speed Smoothing" },
            { "_turnInPlaceMinAngle", "Turn In Place Above" },
            { "_turn180MinAngle", "Use A 180° Turn Above" },
            { "_enableFootIK", "Keep Feet On The Ground" },
            { "_enableSpeedWarping", "Match Playback To Real Speed" },
            { "_enableSpeedChangeClips", "Use Walk↔Jog Transition Clips" },

            // Talk overlay weights read as engine plumbing rather than as expressiveness.
            { "_talkOverlayWeight", "Maximum Gesture Strength" },
            { "_talkWeightAtLowEnergy", "Gesture Strength When Speaking Softly" },
            { "_useSpeechEnergy", "Scale Gestures With Speech Volume" },
            { "_talkReleasePlaybackSpeed", "Slow-Down While Settling" },
            { "_beatLayerWeight", "Speech Accent Strength" },
            { "_actionLayerWeight", "Action Strength" },
            { "_pointingLayerWeight", "Pointing Strength" },
            { "_blendCurve", "Blend Easing" },

            // "Moving talk" is the module's own term for talking while walking.
            { "_movingTalkMode", "While Walking" },
            { "_movingTalkWeight", "Gesture Strength While Walking" },
            { "_movingTalkOverrideWeight", "Fallback Strength While Walking" },
            { "_movingTalkBlendSeconds", "Standing ↔ Walking Blend" },

            // Persona scalars: the two highest-value controls in the module.
            { "_gestureLiveliness", "How Expressive" },
            { "_calmness", "How Calm" },

            // "Beat" and "referential" are gesture-research terms; name the behaviour instead.
            { "_enableBeatGestures", "Accent Speech Rhythm" },
            { "_beatRefractorySeconds", "Minimum Gap Between Accents" },
            { "_beatWeightScale", "Accent Strength" },
            { "_enableReferentialGestures", "Gesture At What It Says" },
            { "_referentialGestureRefractorySeconds", "Minimum Gap Between These Gestures" },
            { "_referentialGestureClassCooldownSeconds", "Minimum Gap Before The Same One Repeats" },
            { "_referentialGestureWeight", "Strength Of These Gestures" },
            { "_interruptedFreezeSeconds", "Pause When Interrupted" },
            { "_interruptedReleaseScale", "Settle Speed After An Interruption" },

            // Ambient life and proximity.
            { "_enableAmbientActivities", "Keeps Busy When Alone" },
            { "_ambientStartDelaySeconds", "Quiet Time Before Starting" },
            { "_ambientIntervalSeconds", "Average Gap Between Activities" },
            { "_ambientSuppressDistance", "Stop When The Player Is Closer Than" },
            { "_proximityExpressiveness", "Scale Gestures With Distance" },

            // Cross-module signal — "exertion" means nothing outside this codebase.
            { "_publishExertion", "Share Physical Effort With Other Systems" },
            { "_exertionRiseSeconds", "Effort Build-Up Time" },
            { "_exertionRecoverySeconds", "Effort Recovery Time" },

            // Co-speech internals.
            { "_enableAdvancedCoSpeech", "Enable Procedural Speech Accents" },
            { "_coSpeechMinimumAccentEnergy", "Minimum Volume For An Accent" },
            { "_coSpeechEmphasisDerivative", "Emphasis Sensitivity" },
            { "_coSpeechAccentProbability", "Accent Frequency" },
            { "_coSpeechAccentRefractorySeconds", "Minimum Gap Between Accents" },
            { "_coSpeechPhraseEnergyMargin", "Phrase Detection Margin" },
            { "_coSpeechPreparationSeconds", "Wind-Up Time" },
            { "_coSpeechStrokeSeconds", "Stroke Time" },
            { "_coSpeechReferentialHoldSeconds", "Hold Time" },
            { "_coSpeechRetractionSeconds", "Settle Time" },

            // Diagnostics: "Trace Verbosity" and "Firehose" are developer words.
            { "_traceVerbosity", "Diagnostic Detail" },
            { "_firehoseIntervalSeconds", "Per-Frame Report Interval" }
        };

        /// <summary>
        ///     Cached <see cref="GUIContent" /> per field. Inspectors redraw constantly, and building
        ///     a GUIContent per field per repaint is the same class of waste the editor style guide
        ///     forbids for GUIStyle.
        /// </summary>
        private static readonly Dictionary<string, GUIContent> Cache = new();

        /// <summary>
        ///     The label to draw <paramref name="property" /> with: the plain-English override when
        ///     one exists, otherwise Unity's own derived name. The property's authored tooltip is
        ///     preserved either way — it carries the explanation the label cannot.
        /// </summary>
        internal static GUIContent For(SerializedProperty property)
        {
            if (property == null) return GUIContent.none;

            string fieldName = property.name;
            if (Cache.TryGetValue(fieldName, out GUIContent cached)) return cached;

            string label = Labels.TryGetValue(fieldName, out string plain) ? plain : property.displayName;
            var content = new GUIContent(label, property.tooltip);
            Cache[fieldName] = content;
            return content;
        }

        /// <summary>Whether this field carries a plain-English override (used by the naming guard test).</summary>
        internal static bool HasOverride(string fieldName) => Labels.ContainsKey(fieldName);

        /// <summary>Every field name this table renames, for the naming guard test.</summary>
        internal static IEnumerable<string> OverriddenFields => Labels.Keys;
    }
}
