using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.Inspectors;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Editor;
using UnityEditor;
using UnityEngine;
using Convai.Editor.UI;

namespace Convai.Editor.Embodiment.Inspectors
{
    /// <summary>
    ///     The Gaze Profile asset inspector: three plain-language dials and a personality row over
    ///     six intention-named sections, replacing eleven engineering-named ones.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two things changed here and both were defects rather than polish. The sections used
    ///         to mirror the solver's internal decomposition — TARGETING, STATE POLICIES, HEAD &amp;
    ///         TORSO, EYES, BLINK &amp; LIDS, BODY TURN, IDLE LIFE, … — so answering "how do I make
    ///         him look at me more?" required knowing that the answer is spread across three of
    ///         them. And every field rendered with Unity's derived label, which put
    ///         <c>Saccade Duration Per Degree</c> and <c>Synthetic Interpupillary Distance</c> on a
    ///         customer-facing surface, against the project's own naming rule. Both are fixed by
    ///         <see cref="GazeProfileLabels" /> and the grouping below.
    ///     </para>
    ///     <para>
    ///         Every serialized field still appears exactly once — verified by
    ///         <c>GazeProfileInspectorCompletenessTests</c>, so a field added to the profile and
    ///         forgotten here fails the build rather than becoming invisible.
    ///     </para>
    /// </remarks>
    [CustomEditor(typeof(ConvaiGazeProfile))]
    internal sealed class ConvaiGazeProfileInspector : ConvaiEmbodimentProfileEditorBase<ConvaiGazeProfile>
    {
        internal const string SectionPersonality = "Personality";
        internal const string SectionWhoItLooksAt = "WhoItLooksAt";
        internal const string SectionHeadAndBody = "HeadAndBody";
        internal const string SectionEyesAndBlinking = "EyesAndBlinking";
        internal const string SectionReactions = "Reactions";
        internal const string SectionWhileWalking = "WhileWalking";
        internal const string SectionAdvanced = "Advanced";

        protected override string HeaderTitle => "Gaze Profile";
        protected override string HeaderSubtitle => "How this character looks at things";

        protected override void DrawProfileInspector()
        {
            DrawPersonalitySection();
            DrawWhoItLooksAtSection();
            DrawHeadAndBodySection();
            DrawEyesAndBlinkingSection();
            DrawReactionsSection();
            DrawWhileWalkingSection();
            DrawAdvancedSection();
        }

        // ------------------------------------------------------------------ personality

        private void DrawPersonalitySection()
        {
            if (!DrawSection(SectionPersonality, "Personality", ConvaiEditorGlyphs.Profile)) return;

            DrawSectionBody(() =>
            {
                DrawPurpose("Pick a personality, then nudge it. Everything below is the detail behind these three controls.");

                GazePersonality.DrawArchetypeRow(Profile);
                EditorGUILayout.Space(4f);
                GazePersonality.DrawDials(Profile, serializedObject);

                ConvaiEditorControls.GroupCaption("Behaviour per conversation state");
                DrawPurpose("Every state the character can be in, and how it looks at things there. " +
                            "Rows that no longer match the personality above are marked.");

                // Drawn as states rather than as the underlying array: see GazeStatePolicyTable for
                // why the raw list was the last place a user had to understand the data model.
                GazeStatePolicyTable.Draw(Profile, serializedObject);

                EditorGUILayout.Space(4f);
                DrawLabelled("policyBlendSpeed");

                if (!HasStatePolicy(DialogueState.Idle))
                    WarningBox("Idle Behaviour Missing",
                        "Idle is the fallback for every state the table does not list. Without it, " +
                        "unlisted states have nothing to fall back to.");
            });
        }

        // ------------------------------------------------------------------ who it looks at

        private void DrawWhoItLooksAtSection()
        {
            if (!DrawSection(SectionWhoItLooksAt, "Who It Looks At", ConvaiEditorGlyphs.Discovery,
                    defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                DrawPurpose("How the character decides who or what is worth looking at, and how long it stays interested.");

                DrawLabelled("playerMaxDistance");
                DrawLabelled("playerFullRelevanceDistance");
                DrawLabelled("playerLineOfSight");
                DrawLabelled("playerObstructionMask");
                DrawLabelled("commitmentAcquireSeconds");
                DrawLabelled("commitmentReleaseSeconds");
                DrawLabelled("targetLossHoldSeconds");
                DrawLabelled("enableTargetLossSearch");
                DrawLabelled("targetLossSearchMaxSeconds");
                DrawLabelled("enableLookAtActionTargets");
                DrawLabelled("interestDecayPerSecond");
                DrawLabelled("interestRecoveryPerSecond");
                DrawLabelled("maxContinuousHoldSeconds");
                DrawLabelled("interestBreakThreshold");
                DrawLabelled("targetTeleportThreshold");

                ConvaiEditorControls.GroupCaption("When nothing has its attention");
                DrawLabelled("enableAmbientExploration");
                DrawLabelled("ambientYawRangeDegrees");
                DrawLabelled("ambientPitchUpDegrees");
                DrawLabelled("ambientPitchDownDegrees");
                DrawLabelled("ambientIntervalMin");
                DrawLabelled("ambientIntervalMax");
                DrawLabelled("ambientHeadFollow");
                DrawLabelled("ambientRecenterBias");
                DrawLabelled("enableCuriosityGlances");
                DrawLabelled("curiosityGlanceIntervalMin");
                DrawLabelled("curiosityGlanceIntervalMax");
                DrawLabelled("curiosityGlanceDuration");
                DrawLabelled("curiosityRespondsToAttention");
            });
        }

        // ------------------------------------------------------------------ while walking

        private void DrawWhileWalkingSection()
        {
            if (!DrawSection(SectionWhileWalking, "While Walking", ConvaiEditorGlyphs.Motion,
                    defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                DrawPurpose(
                    "Where the character looks while it is going somewhere: down the path, with a glance " +
                    "at what it is walking toward every few seconds, settling onto it as it arrives.");

                DrawLabelled("enableTravelGaze");
                DrawLabelled("enableDestinationGlances");

                ConvaiEditorControls.GroupCaption("Watching the path");
                DrawLabelled("pathLookAheadMinMeters");
                DrawLabelled("pathLookAheadMaxMeters");
                DrawLabelled("travelHeadContributionScale");
                DrawLabelled("travelEngageSeconds");
                DrawLabelled("travelPathPriority");

                ConvaiEditorControls.GroupCaption("Checking on where it is going");
                DrawLabelled("travelGlanceIntervalMin");
                DrawLabelled("travelGlanceIntervalMax");
                DrawLabelled("companionGlanceIntervalMin");
                DrawLabelled("companionGlanceIntervalMax");
                DrawLabelled("travelGlanceHoldSeconds");
                DrawLabelled("travelGlanceConversationScale");

                ConvaiEditorControls.GroupCaption("Coming to rest");
                DrawLabelled("arrivalSettleEyeDropDegrees");
                DrawLabelled("arrivalSettleSeconds");

                ConvaiEditorControls.GroupCaption("Arriving");
                DrawLabelled("arrivalApproachMeters");
                DrawLabelled("arrivalReleaseMeters");
            });
        }

        // ------------------------------------------------------------------ head & body

        private void DrawHeadAndBodySection()
        {
            if (!DrawSection(SectionHeadAndBody, "Head & Body", ConvaiEditorGlyphs.Motion,
                    defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                DrawPurpose("How much of the character moves: eyes only, eyes and head, or a full turn of the body.");

                DrawLabelled("headEntryDegrees");
                DrawLabelled("torsoEntryDegrees");
                DrawLabelled("feetEntryDegrees");

                EditorGUILayout.Space(4f);
                DrawLabelled("headOnsetSeconds");
                DrawLabelled("torsoOnsetSeconds");
                DrawLabelled("feetOnsetSeconds");

                EditorGUILayout.Space(4f);
                DrawLabelled("headTurnBaseSeconds");
                DrawLabelled("headTurnSecondsPerDegree");
                DrawLabelled("torsoTurnBaseSeconds");
                DrawLabelled("torsoTurnSecondsPerDegree");
                DrawLabelled("movementSkew");
                DrawLabelled("shiftTriggerDegrees");
                DrawLabelled("idleDriftTempoScale");

                EditorGUILayout.Space(4f);
                DrawLabelled("eyeComfortDegrees");
                DrawLabelled("headComfortYawDegrees");

                EditorGUILayout.Space(4f);
                DrawLabelled("headStabilityDegrees");
                DrawLabelled("headStabilization");
                DrawLabelled("maxHeadAngularSpeed");
                DrawLabelled("maxTorsoAngularSpeed");
                DrawLabelled("maxHeadYawDegrees");
                DrawLabelled("maxHeadPitchDegrees");
                DrawLabelled("neckShare");
                DrawLabelled("chainFollowThrough");

                EditorGUILayout.Space(4f);
                DrawLabelled("enableTorsoRecruitment");
                DrawLabelled("maxTorsoYawDegrees");
                DrawLabelled("maxTorsoPitchDegrees");

                EditorGUILayout.Space(4f);
                DrawLabelled("enableBodyTurn");
                DrawLabelled("bodyTurnCompletionToleranceDegrees");
                DrawLabelled("bodyTurnHeadRelief");
                DrawLabelled("proceduralTurnSpeed");
            });
        }

        // ------------------------------------------------------------------ eyes & blinking

        private void DrawEyesAndBlinkingSection()
        {
            if (!DrawSection(SectionEyesAndBlinking, "Eyes & Blinking", ConvaiEditorGlyphs.Blink,
                    defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                DrawPurpose("The small movements that make eyes read as alive rather than as painted on.");

                DrawLabelled("eyeActuationMode");
                DrawLabelled("eyeMaxYawDegrees");
                DrawLabelled("eyeMaxPitchUpDegrees");
                DrawLabelled("eyeMaxPitchDownDegrees");
                DrawLabelled("eyeSoftLimitFraction");
                DrawLabelled("orbitRecenteringStrength");
                DrawLabelled("eyeTrackingSharpness");

                ConvaiEditorControls.GroupCaption("Movement between fixations");
                DrawLabelled("saccadeMinDurationSeconds");
                DrawLabelled("saccadeDurationPerDegree");
                DrawLabelled("saccadeDeadzoneDegrees");
                DrawLabelled("saccadeReactionSeconds");
                DrawLabelled("catchUpErrorDegrees");
                DrawLabelled("pursuitLeadSeconds");
                DrawLabelled("fixationDriftDegrees");
                DrawLabelled("fixationDriftFrequency");
                DrawLabelled("microSaccadeIntervalMean");
                DrawLabelled("microSaccadeIntervalJitter");
                DrawLabelled("microSaccadeAmplitudeDegrees");

                ConvaiEditorControls.GroupCaption("Looking at a face");
                DrawLabelled("enableFaceScan");
                DrawLabelled("faceScanIntervalMean");
                DrawLabelled("faceScanIntervalJitter");
                DrawLabelled("faceScanRadiusDegrees");
                DrawLabelled("enableListenerMouthBias");
                DrawLabelled("listenerMouthBiasStrength");

                ConvaiEditorControls.GroupCaption("Close-up and VR");
                DrawLabelled("enableVergence");
                DrawLabelled("vergenceMinDistance");
                DrawLabelled("maxConvergenceDegrees");
                DrawLabelled("syntheticInterpupillaryDistance");

                ConvaiEditorControls.GroupCaption("Blinking");
                DrawLabelled("enableBlink");
                DrawLabelled("blinkIntervalMean");
                DrawLabelled("blinkIntervalJitter");
                DrawLabelled("blinkCloseSeconds");
                DrawLabelled("blinkOpenSeconds");
                DrawLabelled("blinkRefractorySeconds");
                DrawLabelled("gazeShiftBlinkThresholdDegrees");
                DrawLabelled("gazeShiftBlinkProbability");
                DrawLabelled("enableEyelidFollow");
                DrawLabelled("eyelidFollowStrength");
                DrawLabelled("enableBlinkClustering");
                DrawLabelled("blinkClusterRateMultiplier");
            });
        }

        // ------------------------------------------------------------------ reactions

        private void DrawReactionsSection()
        {
            if (!DrawSection(SectionReactions, "Reactions", ConvaiEditorGlyphs.Reaction, defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                DrawPurpose("What the character does in response to the conversation: nodding, being interrupted, taking and giving up the floor, and how emotion and closeness colour all of it.");

                DrawLabelled("enableListeningNods");
                DrawLabelled("nodPitchDegrees");
                DrawLabelled("nodDurationSeconds");
                DrawLabelled("listeningNodIntervalMin");
                DrawLabelled("listeningNodIntervalMax");
                DrawLabelled("acknowledgeNodProbability");

                EditorGUILayout.Space(4f);
                DrawLabelled("enableInterruptionReaction");
                DrawLabelled("interruptionReactionIntensity");

                EditorGUILayout.Space(4f);
                DrawLabelled("enableTurnTakingGaze");
                DrawLabelled("planningBreakProbability");
                DrawLabelled("enableYieldBlink");
                DrawLabelled("enableYieldHeadDip");

                EditorGUILayout.Space(4f);
                DrawLabelled("enableEmotionModulation");
                DrawLabelled("emotionModifiers");

                EditorGUILayout.Space(4f);
                DrawLabelled("enableProxemicRegulation");
                DrawLabelled("proxemicCloseDistanceMeters");
                DrawLabelled("proxemicIntensity");
            });
        }

        // ------------------------------------------------------------------ advanced

        private void DrawAdvancedSection()
        {
            if (!DrawSection(SectionAdvanced, "Advanced", ConvaiEditorGlyphs.Validation,
                    defaultExpanded: false, accent: ConvaiEditorTheme.StatusWarn))
                return;

            DrawSectionBody(() =>
            {
                DrawPurpose("Performance for crowded scenes, and the diagnostic detail written to the console.");

                DrawLabelled("enableGazeLod");
                DrawLabelled("lodFarDistance");
                DrawLabelled("lodFarCognitionHz");
                DrawLabelled("skipWhenInvisible");

                EditorGUILayout.Space(4f);
                DrawLabelled("traceVerbosity");
                DrawLabelled("firehoseHz");
            });
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        ///     Draws a field with its plain-English label, keeping the authored tooltip. Missing
        ///     fields report loudly rather than silently vanishing — the completeness test relies on
        ///     every name here being real.
        /// </summary>
        private void DrawLabelled(string fieldName)
        {
            // By plain name, not by path: the settings live in nested blocks, and which block owns
            // which setting is the profile's business, not this file's.
            SerializedProperty property = GazeProfileSerializedPaths.Find(serializedObject, fieldName);
            if (property == null)
            {
                WarningBox("Missing Setting", $"ConvaiGazeProfile.{fieldName} was not found.");
                return;
            }

            EditorGUILayout.PropertyField(property, GazeProfileLabels.For(property), true);
        }

        /// <summary>
        ///     The one sentence that lets someone find a setting without knowing the module's
        ///     internal decomposition — the thing eleven engineering-named sections could not do.
        /// </summary>
        private static void DrawPurpose(string text) =>
            EditorGUILayout.LabelField(text, ConvaiEditorStyles.CaptionWrapped);

        private bool HasStatePolicy(DialogueState state)
        {
            SerializedProperty list = GazeProfileSerializedPaths.Find(serializedObject, "statePolicies");
            if (list == null || !list.isArray) return false;

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                SerializedProperty stateProperty = element.FindPropertyRelative("State");
                if (stateProperty != null && stateProperty.enumValueIndex == (int)state)
                    return true;
            }

            return false;
        }
    }
}
