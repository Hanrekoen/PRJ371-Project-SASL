using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Gaze.Editor
{
    /// <summary>
    ///     Plain-English display labels for the Gaze Profile fields whose serialized names are
    ///     oculomotor research vocabulary. Unity derives a field's inspector label from its
    ///     identifier, which is fine for <c>playerMaxDistance</c> and useless for
    ///     <c>saccadeDurationPerDegree</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This closes a standing violation, not a nicety.</b> The project's naming rule
    ///         permits clinical jargon — Saccade, VOR, OMR, Oculomotor — <em>in internal and
    ///         tooling types only</em>. Every term renamed below was appearing on a customer-facing
    ///         ScriptableObject inspector, where a non-technical user was asked to reason about
    ///         "Saccade Duration Per Degree" and "Synthetic Interpupillary Distance".
    ///     </para>
    ///     <para>
    ///         Display only. Serialized names, public API and asset data are untouched, so this
    ///         changes nothing but what the user reads — which is the point. Modelled directly on
    ///         <c>BodyAnimationConfigLabels</c>, including the per-field <see cref="GUIContent" />
    ///         cache: inspectors redraw constantly and building a GUIContent per field per repaint
    ///         is the waste the editor style guide forbids.
    ///     </para>
    ///     <para>
    ///         Fields not listed here keep Unity's derived name, because it is already plain. A
    ///         field is renamed only where its identifier fails to describe what the setting does
    ///         to someone who has never read the module's source.
    ///     </para>
    /// </remarks>
    internal static class GazeProfileLabels
    {
        /// <summary>Serialized field name → the label a user should actually see.</summary>
        private static readonly Dictionary<string, string> Labels = new()
        {
            // ── Eyes: the densest cluster of research vocabulary in the whole SDK ──────────
            { "saccadeMinDurationSeconds", "Fastest Eye Movement" },
            { "saccadeDurationPerDegree", "Eye Movement Time Per Degree" },
            { "saccadeDeadzoneDegrees", "Ignore Movements Smaller Than" },
            { "saccadeReactionSeconds", "Reaction Delay" },
            { "microSaccadeIntervalMean", "Average Gap Between Eye Flicks" },
            { "microSaccadeIntervalJitter", "Eye Flick Timing Variation" },
            { "microSaccadeAmplitudeDegrees", "Eye Flick Size" },
            { "fixationDriftDegrees", "Eye Wander Amount" },
            { "fixationDriftFrequency", "Eye Wander Speed" },
            { "orbitRecenteringStrength", "Eyes Re-Centre As The Head Turns" },
            { "eyeSoftLimitFraction", "Where The Eyes Start To Strain" },
            { "eyeTrackingSharpness", "Tracking Tightness" },
            { "catchUpErrorDegrees", "Catch Up When Behind By" },
            { "pursuitLeadSeconds", "Anticipate Moving Targets" },
            { "eyeActuationMode", "How The Eyes Are Driven" },
            { "eyeMaxYawDegrees", "Furthest The Eyes Look Sideways" },
            { "eyeMaxPitchUpDegrees", "Furthest The Eyes Look Up" },
            { "eyeMaxPitchDownDegrees", "Furthest The Eyes Look Down" },

            // "Vergence" and "interpupillary" are optometry terms on a game-engine inspector.
            { "enableVergence", "Cross The Eyes On Close Objects" },
            { "vergenceMinDistance", "Closest Focus Distance" },
            { "maxConvergenceDegrees", "Maximum Eye Crossing" },
            { "syntheticInterpupillaryDistance", "Eye Spacing (Rigs Without Eye Bones)" },

            // ── Face scan ──────────────────────────────────────────────────────────────────
            { "enableFaceScan", "Looks Around Your Face" },
            { "faceScanIntervalMean", "Average Gap Between Face Glances" },
            { "faceScanIntervalJitter", "Face Glance Timing Variation" },
            { "faceScanRadiusDegrees", "Face Scan Spread" },
            { "enableListenerMouthBias", "Watches Your Mouth While You Speak" },
            { "listenerMouthBiasStrength", "How Strongly It Watches The Mouth" },

            // ── Head & torso: "recruitment", "share" and "sharpness" are solver words ──────
            { "headStabilityDegrees", "Ignore Small Target Movement" },
            { "headStabilization", "Keep Head Level While Looking" },
            { "maxHeadAngularSpeed", "Head Speed Limit" },
            { "maxTorsoAngularSpeed", "Chest Speed Limit" },
            { "maxHeadYawDegrees", "Furthest The Head Turns Sideways" },
            { "maxHeadPitchDegrees", "Furthest The Head Tilts Up Or Down" },
            { "neckShare", "How Much The Neck Bends" },
            { "chainFollowThrough", "Follow Through" },
            { "enableTorsoRecruitment", "Chest Joins In On Big Looks" },
            { "maxTorsoYawDegrees", "Furthest The Chest Turns" },
            { "maxTorsoPitchDegrees", "Furthest The Chest Leans" },

            // ── Gaze shift ladder: "entry", "onset" and "residual" are solver words ───────
            { "headEntryDegrees", "Head Joins In Above" },
            { "torsoEntryDegrees", "Chest Joins In Above" },
            { "feetEntryDegrees", "Turns The Feet When This Much Is Left Over" },
            { "headOnsetSeconds", "Head Starts After The Eyes By" },
            { "torsoOnsetSeconds", "Chest Starts After The Eyes By" },
            { "feetOnsetSeconds", "Feet Start After The Eyes By" },
            { "headTurnBaseSeconds", "Head Turn Time" },
            { "headTurnSecondsPerDegree", "Added Time Per Degree" },
            { "torsoTurnBaseSeconds", "Chest Turn Time" },
            { "torsoTurnSecondsPerDegree", "Chest Added Time Per Degree" },
            { "movementSkew", "Movement Front-Loading" },
            { "shiftTriggerDegrees", "New Look Starts Above" },
            { "idleDriftTempoScale", "Idle Drift Slowdown" },
            { "eyeComfortDegrees", "Eyes Get Tired Of Looking Sideways Past" },
            { "headComfortYawDegrees", "Neck Gets Tired Of Staying Turned Past" },

            // ── Targeting: "commitment", "interest", "relevance" are arbiter internals ─────
            { "playerMaxDistance", "Stops Noticing You Beyond" },
            { "playerFullRelevanceDistance", "Fully Interested Within" },
            { "playerLineOfSight", "Won't Look Through Walls" },
            { "playerObstructionMask", "What Counts As A Wall" },
            { "targetTeleportThreshold", "Treat A Jump Bigger Than This As A Cut" },
            { "commitmentAcquireSeconds", "Time To Lock On" },
            { "commitmentReleaseSeconds", "Time To Let Go" },
            { "targetLossHoldSeconds", "Keeps Looking After Losing Sight" },
            { "interestDecayPerSecond", "How Fast It Gets Bored" },
            { "interestRecoveryPerSecond", "How Fast Interest Returns" },
            { "maxContinuousHoldSeconds", "Never Stares At One Thing Longer Than" },
            { "interestBreakThreshold", "Looks Away When Bored Below" },
            { "enableTargetLossSearch", "Searches When It Loses You" },
            { "targetLossSearchMaxSeconds", "Gives Up Searching After" },
            { "enableLookAtActionTargets", "Looks At Action Targets" },

            // ── State policies ─────────────────────────────────────────────────────────────
            { "statePolicies", "Behaviour Per Conversation State" },
            { "policyBlendSpeed", "Mood Change Speed" },

            // ── Idle life ──────────────────────────────────────────────────────────────────
            { "enableAmbientExploration", "Looks Around When Idle" },
            { "ambientYawRangeDegrees", "How Far It Looks Sideways When Idle" },
            { "ambientPitchUpDegrees", "How Far It Looks Up When Idle" },
            { "ambientPitchDownDegrees", "How Far It Looks Down When Idle" },
            { "ambientIntervalMin", "Shortest Gap Between Idle Looks" },
            { "ambientIntervalMax", "Longest Gap Between Idle Looks" },
            { "ambientHeadFollow", "How Much The Head Follows Idle Looks" },
            { "ambientRecenterBias", "Prefers Looking Straight Ahead" },
            { "enableCuriosityGlances", "Glances At You While Idle" },
            { "curiosityGlanceIntervalMin", "Shortest Gap Between Glances" },
            { "curiosityGlanceIntervalMax", "Longest Gap Between Glances" },
            { "curiosityGlanceDuration", "How Long A Glance Lasts" },
            { "curiosityRespondsToAttention", "Glances Back Sooner When You Watch It" },

            // ── While walking ──────────────────────────────────────────────────────────────
            { "enableTravelGaze", "Watches Where It Is Going" },
            { "enableDestinationGlances", "Glances At Its Destination" },
            { "travelPathPriority", "How Much The Path Outranks Everything Else" },
            { "pathLookAheadMinMeters", "How Far Ahead It Looks When Walking" },
            { "pathLookAheadMaxMeters", "How Far Ahead It Looks At Full Pace" },
            { "travelEngageSeconds", "How Long Looking Ahead Takes To Settle" },
            { "travelGlanceIntervalMin", "Shortest Gap Between Looks At The Destination" },
            { "travelGlanceIntervalMax", "Longest Gap Between Looks At The Destination" },
            { "companionGlanceIntervalMin", "Shortest Gap Between Looks At Whoever It Follows" },
            { "companionGlanceIntervalMax", "Longest Gap Between Looks At Whoever It Follows" },
            { "travelGlanceHoldSeconds", "How Long Each Look Lasts" },
            { "arrivalSettleEyeDropDegrees", "Eyes Drop As It Comes To Rest" },
            { "arrivalSettleSeconds", "How Long The Settle Takes" },
            { "travelGlanceConversationScale", "Looks More Often While Talking" },
            { "arrivalApproachMeters", "Starts Settling This Far Out" },
            { "arrivalReleaseMeters", "Stops Watching The Path This Close" },
            { "travelHeadContributionScale", "How Much The Head Follows The Path" },

            // ── Blink & lids ───────────────────────────────────────────────────────────────
            { "enableBlink", "Blinks" },
            { "blinkIntervalMean", "Average Gap Between Blinks" },
            { "blinkIntervalJitter", "Blink Timing Variation" },
            { "blinkCloseSeconds", "Lids Close In" },
            { "blinkOpenSeconds", "Lids Open In" },
            { "blinkRefractorySeconds", "Minimum Gap Between Blinks" },
            { "gazeShiftBlinkThresholdDegrees", "Blink On Big Look-Aways Above" },
            { "gazeShiftBlinkProbability", "Chance Of Blinking On A Big Look-Away" },
            { "enableEyelidFollow", "Eyelids Follow The Eyes" },
            { "eyelidFollowStrength", "How Much The Eyelids Follow" },
            { "enableBlinkClustering", "Blinks More At Sentence Breaks" },
            { "blinkClusterRateMultiplier", "How Much More It Blinks There" },

            // ── Body turn ──────────────────────────────────────────────────────────────────
            { "enableBodyTurn", "Turns Its Whole Body For Far Targets" },
            { "bodyTurnCompletionToleranceDegrees", "Counts The Turn Done Within" },
            { "bodyTurnHeadRelief", "Neck Relaxes During A Turn" },
            { "proceduralTurnSpeed", "Turn Speed (Rigs Without Turn Animation)" },

            // ── Conversational gestures & rhythm ───────────────────────────────────────────
            { "enableListeningNods", "Nods While Listening" },
            { "nodPitchDegrees", "How Deep A Nod Is" },
            { "nodDurationSeconds", "How Long A Nod Lasts" },
            { "listeningNodIntervalMin", "Shortest Gap Between Nods" },
            { "listeningNodIntervalMax", "Longest Gap Between Nods" },
            { "acknowledgeNodProbability", "Chance Of Nodding When You Start Talking" },
            { "enableInterruptionReaction", "Reacts When Interrupted" },
            { "interruptionReactionIntensity", "How Strong The Reaction Is" },
            { "enableTurnTakingGaze", "Uses Conversational Eye Rhythm" },
            { "planningBreakProbability", "How Often It Looks Away To Think" },
            { "enableYieldBlink", "Blinks When Handing Over The Turn" },
            { "enableYieldHeadDip", "Dips Its Head When Handing Over The Turn" },

            // ── Emotion & proxemics ────────────────────────────────────────────────────────
            { "enableEmotionModulation", "Gaze Changes With Emotion" },
            { "emotionModifiers", "Per-Emotion Adjustments" },
            { "enableProxemicRegulation", "Softens When You Get Close" },
            { "proxemicCloseDistanceMeters", "\"Close\" Starts At" },
            { "proxemicIntensity", "How Much It Softens" },

            // ── Performance: "LOD" and "cognition Hz" are engine words ─────────────────────
            { "enableGazeLod", "Save Performance In Crowds" },
            { "lodFarDistance", "Crowd Distance" },
            { "lodFarCognitionHz", "Crowd Update Rate" },
            { "skipWhenInvisible", "Pause When Off-Screen" },

            // ── Diagnostics: "verbosity" and "firehose" are developer words ────────────────
            { "traceVerbosity", "Diagnostic Detail" },
            { "firehoseHz", "Per-Frame Report Rate" }
        };

        /// <summary>Cached <see cref="GUIContent" /> per field — never build one per repaint.</summary>
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
