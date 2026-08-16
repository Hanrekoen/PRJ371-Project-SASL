using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Embodiment.Taxonomy;
using Convai.Domain.Emotion;
using Convai.Editor.AI;
using Convai.Modules.Emotion.Components;
using Convai.Modules.Emotion.Profiles;
using Convai.Runtime.Components;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Modules.Emotion.Editor.AI
{
    /// <summary>
    ///     Ownership of a personality, as this assembly is allowed to hold it.
    /// </summary>
    /// <remarks>
    ///     The Convai MCP assemblies reach only <c>Convai.Editor.AI</c> and their own module's editor
    ///     assembly, never the editor UI assembly the shared ownership vocabulary lives in. The
    ///     verdict still comes from that one shared scan — this only carries it, so a tool and the
    ///     inspector cannot describe the same character differently.
    /// </remarks>
    internal readonly struct EmotionOwnership
    {
        private EmotionOwnership(bool shipsWithSdk, int userCount, bool affectsOthers, string message)
        {
            RequiresProjectCopy = shipsWithSdk;
            UserCount = userCount;
            EditingAffectsOthers = affectsOthers;
            NoticeMessage = message;
        }

        internal bool RequiresProjectCopy { get; }
        internal int UserCount { get; }
        internal bool EditingAffectsOthers { get; }
        internal string NoticeMessage { get; }

        internal static EmotionOwnership Of(ConvaiEmotionProfile profile)
        {
            EmotionPersonality.DescribeOwnership(
                profile, out bool shipsWithSdk, out int users, out bool affectsOthers, out string message);
            return new EmotionOwnership(shipsWithSdk, users, affectsOthers, message);
        }
    }


    /// <summary>Input for <c>Convai.ConfigureEmotion</c>.</summary>
    public sealed class ConfigureEmotionRequest
    {
        public long CharacterInstanceId { get; set; }
        public string PersonalityAssetPath { get; set; } = string.Empty;
        public string EmotionDetection { get; set; } = string.Empty;
        public string RestingMood { get; set; } = string.Empty;
        public float? RestingMoodStrength { get; set; }
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.DiagnoseEmotion</c>.</summary>
    public sealed class DiagnoseEmotionRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeRuntimeState { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.InspectEmotionPersonalities</c>.</summary>
    public sealed class InspectEmotionPersonalitiesRequest
    {
        public string[] FolderPaths { get; set; } = Array.Empty<string>();
    }

    /// <summary>Input for <c>Convai.TuneEmotionPersonality</c>.</summary>
    public sealed class TuneEmotionPersonalityRequest
    {
        public long CharacterInstanceId { get; set; }
        public string CharacterType { get; set; } = string.Empty;
        public string RestingMood { get; set; } = string.Empty;
        public float? RestingMoodStrength { get; set; }
        public float? HowStronglyItShows { get; set; }
        public float? HowQuicklyItReacts { get; set; }
        public bool? NeverSitsPerfectlyStill { get; set; }
        public bool? MoodFollowsConversation { get; set; }
        public bool? ShowsMoreThanOneEmotion { get; set; }
        public bool? PicksUpOtherCharactersMoods { get; set; }
        public bool MakePersonalityUnique { get; set; }
        public bool DryRun { get; set; } = true;
    }

    /// <summary>
    ///     Convai Emotions exposed through Unity's official MCP server: give a character a face
    ///     that reacts to what is said, see what it will actually do, and tune its temperament
    ///     without quietly restyling every other character that shares its personality.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every verdict comes from <see cref="EmotionSetupService" /> and
    ///         <see cref="EmotionTroubleshooter" /> — the same code the component inspector's
    ///         checklist and the Emotion editor window draw. This class contains no check of its
    ///         own, so an assistant and the editor cannot describe the same character differently.
    ///     </para>
    ///     <para>
    ///         Settings live in two places with very different blast radii. Emotion detection and
    ///         this character's own resting mood are fields on the component, so
    ///         <c>Convai.ConfigureEmotion</c> writes them freely. Everything else — character type,
    ///         the personality's resting mood, the feel switches — lives on a
    ///         <see cref="ConvaiEmotionProfile" /> that may be shared by every character in the
    ///         scene, so it is reached only through <c>Convai.TuneEmotionPersonality</c>, which
    ///         copies before it writes.
    ///     </para>
    ///     <para>
    ///         Nothing here reads a stored field to answer "is this on?" — see
    ///         <see cref="ConvaiEmotionBehaviours" /> for why that would be wrong.
    ///     </para>
    /// </remarks>
    public static class ConvaiEmotionMcpTools
    {
        private const string ConfigureTool = "Convai.ConfigureEmotion";
        private const string DiagnoseTool = "Convai.DiagnoseEmotion";
        private const string PersonalitiesTool = "Convai.InspectEmotionPersonalities";
        private const string TuneTool = "Convai.TuneEmotionPersonality";

        private const string AddComponentMenuPath = "Add Component → Convai → Embodiment → Emotion";
        private const string SetUpRoute =
            "select the character, open the Emotion component and press Set Up Emotions";
        private const string EditorWindowPath = "Convai → Emotion Editor";

        private const string FaceFixAdvice =
            "Emotions move a face by driving its blendshapes, so the character needs a skinned mesh " +
            "that has some. Import the model with blendshapes enabled, or use a character whose face " +
            "can deform.";

        private const string RestingMoodNeutralKeyword = "Neutral";
        private const string RestingMoodUseProfileKeyword = "UseProfile";

        // ------------------------------------------------------------------ configure

        [McpTool(
            ConfigureTool,
            "Adds Convai Emotions to a character so its face reacts to what is said, gives it a personality the project already has, and sets how it detects feelings and what it rests at. Previews by default. Only ever writes fields on the character itself — it never creates or edits a personality asset, so it can never restyle other characters by accident.",
            "Configure Convai Emotions",
            Groups = new[] { "convai", "emotion" },
            EnabledByDefault = true)]
        public static object Configure(JObject input) =>
            Configure(input?.ToObject<ConfigureEmotionRequest>());

        public static object Configure(ConfigureEmotionRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "Emotions can only be configured in Edit Mode.",
                    new { code = "PLAY_MODE_ACTIVE" });

            request ??= new ConfigureEmotionRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            if (!TryResolveDetection(request.EmotionDetection, out EmotionDetectionMode? detection, out error))
            {
                return Response(false, error, new
                {
                    code = "INVALID_DETECTION",
                    requiredInputs = new[] { "emotionDetection" }
                });
            }

            if (!TryResolvePersonality(request.PersonalityAssetPath, out ConvaiEmotionProfile personality,
                    out error))
            {
                return Response(false, error, new
                {
                    code = "PERSONALITY_NOT_FOUND",
                    setUpRoute = SetUpRoute,
                    availablePersonalities = DescribeAvailablePersonalities()
                });
            }

            var controller = character.GetComponentInChildren<ConvaiEmotionController>(true);
            bool hadController = controller != null;

            // Adding a component that can never move a face is not a favour. A character that
            // already has one is a choice the user made, so tuning it stays allowed and the blocker
            // is reported as a finding instead.
            if (!hadController)
            {
                if (!HasFaceWithBlendshapes(character.gameObject))
                {
                    return Response(false,
                        $"'{character.gameObject.name}' cannot show emotions yet: no skinned mesh " +
                        "with blendshapes was found under it.",
                        new
                        {
                            code = "EMOTION_FACE",
                            blocker = "No skinned mesh with blendshapes found. Emotions need a face " +
                                      "that can deform.",
                            advice = FaceFixAdvice,
                            characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                            nextSteps = new[] { FaceFixAdvice, "Then call Convai.ConfigureEmotion again." }
                        });
                }
            }

            var changes = new List<string>(4);
            var notes = new List<string>(3);
            if (!hadController)
            {
                changes.Add("Add the Emotion component to this character");
                changes.Add(
                    $"Set Emotion detection to {EmotionDetectionModes.ShortNameFor(EmotionDetectionModes.Default)} " +
                    "(what a new character starts on)");
            }

            ConvaiEmotionProfile assigned = EmotionSetupService.ResolveAssignedProfile(controller);
            if (personality != null && assigned != personality)
                changes.Add($"Set Personality: '{personality.name}'");

            if (detection.HasValue && (!hadController || ReadDetection(controller) != detection.Value))
                changes.Add($"Set Emotion detection to {EmotionDetectionModes.ShortNameFor(detection.Value)}");

            bool wantsRestingMood = !string.IsNullOrWhiteSpace(request.RestingMood);
            string restingMoodLabel = string.Empty;
            if (wantsRestingMood)
            {
                if (!TryResolveRestingMood(request.RestingMood, personality ?? assigned,
                        out restingMoodLabel, out error))
                {
                    return Response(false, error, new
                    {
                        code = "UNKNOWN_EMOTION",
                        requiredInputs = new[] { "restingMood" },
                        knownEmotions = ListEmotionLabels(personality ?? assigned)
                    });
                }

                changes.Add(string.IsNullOrEmpty(restingMoodLabel)
                    ? "Set this character to rest at the personality's mood"
                    : string.Equals(restingMoodLabel, RestingMoodNeutralKeyword, StringComparison.Ordinal)
                        ? "Set this character to rest neutral, overriding the personality"
                        : $"Set this character to rest at {restingMoodLabel}");
            }

            if (request.RestingMoodStrength.HasValue)
                changes.Add($"Set this character's resting strength to {request.RestingMoodStrength.Value:0.00}");

            if (request.DryRun)
                return ConfigureResponse(true, character, controller, changes, notes);

            if (changes.Count == 0)
                return ConfigureResponse(false, character, controller, changes, notes);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                if (controller == null)
                    controller = Undo.AddComponent<ConvaiEmotionController>(character.gameObject);

                Undo.RecordObject(controller, "Configure Convai Emotions");
                var serialized = new SerializedObject(controller);

                if (personality != null)
                    serialized.FindProperty("profile").objectReferenceValue = personality;

                // Written as a VALUE. enumValueIndex indexes the enum's declaration order
                // (Off, Llm, Nrclex), which is not the order these are presented in — using it here
                // is what shipped the two detection providers swapped.
                if (detection.HasValue)
                    serialized.FindProperty("detectionMode").intValue = (int)detection.Value;

                if (wantsRestingMood)
                {
                    serialized.FindProperty("initialMoodLabel").stringValue =
                        string.Equals(restingMoodLabel, RestingMoodNeutralKeyword, StringComparison.Ordinal)
                            ? "neutral"
                            : restingMoodLabel;

                    // Picking a mood while the strength sits at 0 is the control that looks set and
                    // does nothing, so a mood without a strength gets a usable one.
                    if (!string.IsNullOrEmpty(restingMoodLabel) &&
                        !string.Equals(restingMoodLabel, RestingMoodNeutralKeyword, StringComparison.Ordinal) &&
                        !request.RestingMoodStrength.HasValue &&
                        serialized.FindProperty("initialMoodIntensity").floatValue <= 0f)
                    {
                        serialized.FindProperty("initialMoodIntensity").floatValue =
                            EmotionPersonalityTable.DefaultRestingMoodIntensity;
                        notes.Add(
                            "Gave this character's resting mood a usable strength of " +
                            $"{EmotionPersonalityTable.DefaultRestingMoodIntensity}, since a mood at 0 " +
                            "strength shows nothing.");
                    }
                }

                if (request.RestingMoodStrength.HasValue)
                {
                    serialized.FindProperty("initialMoodIntensity").floatValue =
                        Mathf.Clamp01(request.RestingMoodStrength.Value);
                }

                serialized.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                Undo.SetCurrentGroupName("Configure Convai Emotions");
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return Response(false, exception.Message, new { code = "AUTHORING_FAILED" });
            }

            return ConfigureResponse(false, character, controller, changes, notes);
        }

        [McpSchema(ConfigureTool)]
        public static object ConfigureSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character to configure. 0 uses the only one in the active scene."),
                ["personalityAssetPath"] = ConvaiMcpResponses.StringProperty(
                    "Project path of an existing personality to give this character. Never creates " +
                    "one — run Convai.InspectEmotionPersonalities to see what the project has. Omit " +
                    "to leave the personality unchanged."),
                ["emotionDetection"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "How this character works out what it feels. Responsive updates while the reply " +
                    "is spoken and is the default; Accurate reads the whole reply once and works in " +
                    "any language; Off means it never reacts. Omit to leave unchanged.",
                    new[] { "Responsive", "Accurate", "Off" }),
                ["restingMood"] = ConvaiMcpResponses.StringProperty(
                    "What THIS character rests at between emotions, overriding its personality. An " +
                    "emotion name such as \"joy\"; \"Neutral\" to force no resting mood at all; " +
                    "\"UseProfile\" to defer to the personality. Omit to leave unchanged."),
                ["restingMoodStrength"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "How strong this character's resting mood is, 0 to 1. A mood at 0 shows nothing."),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the changes without touching the scene.", true)
            },
            "characterInstanceId");

        [McpOutputSchema(ConfigureTool)]
        public static object ConfigureOutput() => StandardSchema();

        // ------------------------------------------------------------------ diagnose

        [McpTool(
            DiagnoseTool,
            "Explains what a Convai character's face will actually do and why: whether it can show emotions at all, how it detects them, which personality tunes it, what it rests at and which setting decided that, and which of its behaviours are switched off or quietly gated by another setting. Answers \"why isn't this character's face doing anything?\". Read-only.",
            "Diagnose Convai Emotions",
            Groups = new[] { "convai", "emotion", "validation" },
            EnabledByDefault = true)]
        public static object Diagnose(JObject input) =>
            Diagnose(input?.ToObject<DiagnoseEmotionRequest>());

        public static object Diagnose(DiagnoseEmotionRequest request)
        {
            request ??= new DiagnoseEmotionRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            long characterId = ConvaiMcpEntityRef.ToToolId(character.gameObject);
            ConvaiEmotionReport report = ConvaiEmotionReport.For(character.gameObject);

            if (!report.IsPresent)
            {
                bool canHost = HasFaceWithBlendshapes(character.gameObject);
                return Response(true, $"'{character.gameObject.name}' has no Emotion component yet.",
                    new
                    {
                        present = false,
                        readiness = Readiness(report),
                        isWorking = false,
                        characterInstanceId = characterId,
                        summary = report.Summary,
                        checks = Array.Empty<object>(),
                        issues = new[]
                        {
                            Issue("EMOTION_COMPONENT_MISSING", "Warning",
                                "This character has no Emotion component, so its face never reacts to " +
                                $"anything that is said. Add it with {AddComponentMenuPath} — then " +
                                $"{SetUpRoute} to give it a personality. Detection starts on " +
                                $"{EmotionDetectionModes.ShortNameFor(EmotionDetectionModes.Default)}, " +
                                "so it works as soon as it is added.",
                                character.gameObject.name, characterId, characterId, canHost)
                        },
                        nextSteps = canHost
                            ? new[]
                            {
                                $"Add Emotions to '{character.gameObject.name}' — {AddComponentMenuPath}, " +
                                "or call Convai.ConfigureEmotion.",
                                "Then run Convai.DiagnoseEmotion again to see what its face offers."
                            }
                            : new[]
                            {
                                "No skinned mesh with blendshapes was found under this character, so " +
                                "it has no face to move.",
                                FaceFixAdvice
                            }
                    });
            }

            long componentId = ConvaiMcpEntityRef.ToToolId(report.Controller);
            return Response(
                true,
                report.IsWorking
                    ? $"Convai Emotions are working on '{character.gameObject.name}'."
                    : $"Convai Emotions will not show on '{character.gameObject.name}' yet.",
                new
                {
                    present = true,
                    readiness = Readiness(report),
                    isWorking = report.IsWorking,
                    characterInstanceId = characterId,
                    componentInstanceId = componentId,
                    summary = report.Summary,
                    checks = DescribeChecks(report.Preflight),
                    issues = DescribeIssues(report, characterId, componentId),
                    detection = DescribeDetection(report),
                    restingMood = DescribeRestingMood(report),
                    personality = DescribePersonality(report),
                    behaviour = DescribeBehaviours(report),
                    whyTheFaceMightNotMove = BuildWhyTheFaceMightNotMove(report),
                    runtime = DescribeRuntime(report, request.IncludeRuntimeState),
                    nextSteps = BuildNextSteps(report)
                });
        }

        [McpSchema(DiagnoseTool)]
        public static object DiagnoseSchema() => ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character to diagnose. 0 uses the only one in the active scene.", 0),
                ["includeRuntimeState"] = ConvaiMcpResponses.BooleanProperty(
                    "Include what the character is feeling right now. Play Mode only.", true)
            });

        [McpOutputSchema(DiagnoseTool)]
        public static object DiagnoseOutput() => StandardSchema();

        // ------------------------------------------------------------------ personalities

        [McpTool(
            PersonalitiesTool,
            "Lists the Convai emotion personalities in this project — which character type each one is, what it rests at, which of its behaviours are on, whether it ships with the SDK, and which characters already use it. Read this before giving a character a personality, since these tools assign one but never author one from nothing. Read-only.",
            "Inspect Convai Emotion Personalities",
            Groups = new[] { "convai", "emotion" },
            EnabledByDefault = true)]
        public static object InspectPersonalities(JObject input) =>
            InspectPersonalities(input?.ToObject<InspectEmotionPersonalitiesRequest>());

        public static object InspectPersonalities(InspectEmotionPersonalitiesRequest request)
        {
            request ??= new InspectEmotionPersonalitiesRequest();

            string[] folders = request.FolderPaths is { Length: > 0 } ? request.FolderPaths : null;
            string[] guids = folders == null
                ? AssetDatabase.FindAssets("t:ConvaiEmotionProfile")
                : AssetDatabase.FindAssets("t:ConvaiEmotionProfile", folders);

            Dictionary<ConvaiEmotionProfile, List<string>> usage = GatherPersonalityUsage();
            var personalities = new List<object>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var profile = AssetDatabase.LoadAssetAtPath<ConvaiEmotionProfile>(path);
                if (profile == null) continue;

                usage.TryGetValue(profile, out List<string> users);
                EmotionOwnership ownership = EmotionOwnership.Of(profile);

                personalities.Add(new
                {
                    name = profile.name,
                    assetPath = path,
                    characterType = DescribeCharacterType(profile),
                    readsAs = DescribeReadsAs(profile),
                    restingMood = DescribeProfileRestingMood(profile),
                    shipsWithSdk = ownership.RequiresProjectCopy,
                    isEditableInPlace = !ownership.EditingAffectsOthers,
                    usedByCharacterCount = users?.Count ?? 0,
                    usedByCharacters = users ?? (IReadOnlyList<string>)Array.Empty<string>()
                });
            }

            return Response(
                true,
                personalities.Count == 0
                    ? "This project has no Convai emotion personalities."
                    : $"Found {personalities.Count} Convai emotion personality asset(s).",
                new
                {
                    count = personalities.Count,
                    personalities,
                    setUpRoute = SetUpRoute,
                    message = personalities.Count == 0
                        ? "A character with no personality is not broken — it expresses what it feels " +
                          $"on the Convai defaults. To give one a temperament, {SetUpRoute}, which " +
                          "creates a personality asset for it."
                        : "Assign one with Convai.ConfigureEmotion. A personality used by several " +
                          "characters tunes all of them, so Convai.TuneEmotionPersonality gives a " +
                          "character its own copy before changing anything.",
                    nextSteps = personalities.Count == 0
                        ? new[] { $"To create a personality, {SetUpRoute}." }
                        : new[]
                        {
                            "Call Convai.ConfigureEmotion with personalityAssetPath to give a " +
                            "character one of these.",
                            "Call Convai.TuneEmotionPersonality to change how a character feels — it " +
                            "copies a shared or SDK personality first, so no other character moves."
                        }
                });
        }

        [McpSchema(PersonalitiesTool)]
        public static object InspectPersonalitiesSchema() =>
            ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
                new Dictionary<string, object>
                {
                    ["folderPaths"] = ConvaiMcpResponses.ArrayProperty(
                        "Project folders to search, e.g. \"Assets/Characters\". Omit to search the " +
                        "whole project.",
                        ConvaiMcpResponses.StringSchema())
                });

        [McpOutputSchema(PersonalitiesTool)]
        public static object InspectPersonalitiesOutput() => StandardSchema();

        // ------------------------------------------------------------------ tune

        [McpTool(
            TuneTool,
            "Changes how a Convai character feels — its character type, what it rests at, how strongly and quickly it shows things, and its feel switches. These live on a personality asset that other characters may share, so this previews first and, on explicit consent, gives the character its own copy and writes only that. It never edits a shared or SDK-shipped personality in place.",
            "Tune Convai Emotion Personality",
            Groups = new[] { "convai", "emotion" },
            EnabledByDefault = true)]
        public static object Tune(JObject input) =>
            Tune(input?.ToObject<TuneEmotionPersonalityRequest>());

        public static object Tune(TuneEmotionPersonalityRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "A personality can only be tuned in Edit Mode.",
                    new { code = "PLAY_MODE_ACTIVE" });

            request ??= new TuneEmotionPersonalityRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            var controller = character.GetComponentInChildren<ConvaiEmotionController>(true);
            if (controller == null)
            {
                return Response(false,
                    $"'{character.gameObject.name}' has no Emotion component, so it has no personality " +
                    "to tune. Call Convai.ConfigureEmotion first.",
                    new
                    {
                        code = "EMOTION_COMPONENT_MISSING",
                        nextSteps = new[] { "Call Convai.ConfigureEmotion to add Emotions to this character." }
                    });
            }

            ConvaiEmotionProfile profile = EmotionSetupService.ResolveAssignedProfile(controller);
            if (profile == null)
            {
                return Response(false,
                    $"'{character.gameObject.name}' has no personality assigned, so there is nothing " +
                    "to tune. These tools never create one from nothing — assign an existing " +
                    $"personality with Convai.ConfigureEmotion, or {SetUpRoute}.",
                    new
                    {
                        code = "PERSONALITY_MISSING",
                        setUpRoute = SetUpRoute,
                        availablePersonalities = DescribeAvailablePersonalities(),
                        nextSteps = new[]
                        {
                            "Run Convai.InspectEmotionPersonalities to see what this project has.",
                            $"Or {SetUpRoute} to create one for this character."
                        }
                    });
            }

            if (!TryResolveCharacterType(request.CharacterType, out CharacterDemeanor? characterType, out error))
            {
                return Response(false, error, new
                {
                    code = "UNKNOWN_CHARACTER_TYPE",
                    requiredInputs = new[] { "characterType" }
                });
            }

            string restingMoodLabel = string.Empty;
            bool wantsRestingMood = !string.IsNullOrWhiteSpace(request.RestingMood);
            if (wantsRestingMood &&
                !TryResolveRestingMood(request.RestingMood, profile, out restingMoodLabel, out error))
            {
                return Response(false, error, new
                {
                    code = "UNKNOWN_EMOTION",
                    requiredInputs = new[] { "restingMood" },
                    knownEmotions = ListEmotionLabels(profile)
                });
            }

            EmotionOwnership ownership = EmotionOwnership.Of(profile);
            List<object> changed = DescribeRequestedChanges(
                profile, characterType, wantsRestingMood, restingMoodLabel, request);

            if (changed.Count == 0)
            {
                return TuneResponse(request.DryRun, false, character, controller, profile, ownership,
                    changed, string.Empty,
                    new List<string> { "Nothing to change — this personality already reads that way." },
                    Array.Empty<string>());
            }

            bool needsConsent = ownership.EditingAffectsOthers && !request.MakePersonalityUnique;
            if (needsConsent)
            {
                return Response(false,
                    $"'{profile.name}' {(ownership.RequiresProjectCopy ? "ships with the Convai SDK" : $"is used by {ownership.UserCount} characters")}, " +
                    "so it was not changed. Call again with makePersonalityUnique true to give " +
                    $"'{character.gameObject.name}' its own copy and tune that instead.",
                    new
                    {
                        code = "PERSONALITY_SHARED_CONSENT_REQUIRED",
                        requiredInputs = new[] { "makePersonalityUnique" },
                        ownership = DescribeOwnership(profile, ownership),
                        wouldCreateAssetNamed = EmotionPersonality.PredictedCopyPath(controller),
                        changedFields = changed,
                        nextSteps = new[]
                        {
                            "Call Convai.TuneEmotionPersonality again with makePersonalityUnique true " +
                            "to copy this personality for this character and apply the changes to the copy.",
                            $"Or tune '{profile.name}' by hand in the Inspector if every character " +
                            "using it should change."
                        }
                    });
            }

            if (request.DryRun)
            {
                return TuneResponse(true, false, character, controller, profile, ownership, changed,
                    ownership.EditingAffectsOthers
                        ? EmotionPersonality.PredictedCopyPath(controller)
                        : string.Empty,
                    new List<string>(), Array.Empty<string>());
            }

            var notes = new List<string>(3);
            var warnings = new List<string>(2);
            string createdAssetPath = string.Empty;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                if (ownership.EditingAffectsOthers)
                {
                    Undo.RecordObject(controller, "Tune Convai Emotion Personality");
                    if (!EmotionPersonality.TryMakeUnique(profile, controller,
                            out EmotionMakeUniqueResult copy))
                    {
                        Undo.RevertAllDownToGroup(group);
                        return Response(false, copy.FailureReason, new { code = "COPY_FAILED" });
                    }

                    profile = copy.Profile;
                    createdAssetPath = copy.AssetPath;
                    notes.Add(
                        $"Gave '{character.gameObject.name}' its own personality at '{createdAssetPath}' " +
                        "and changed only that. No other character was affected.");
                    warnings.Add(
                        "Creating an asset cannot be undone with Ctrl+Z. Delete " +
                        $"'{createdAssetPath}' to reverse this.");
                }

                ApplyTuning(profile, characterType, wantsRestingMood, restingMoodLabel, request);

                EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                Undo.SetCurrentGroupName("Tune Convai Emotion Personality");
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return Response(false, exception.Message, new { code = "AUTHORING_FAILED" });
            }

            return TuneResponse(false, true, character, controller, profile,
                EmotionOwnership.Of(profile), changed, createdAssetPath, notes, warnings);
        }

        [McpSchema(TuneTool)]
        public static object TuneSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character whose personality to tune. 0 uses the only one in the active scene."),
                ["characterType"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "Set the whole temperament at once. Composed reads calm and even; Warm is " +
                    "approachable and easy to read; Energetic gives big, fast reactions; Reserved " +
                    "barely shows anything. Omit to leave unchanged.",
                    new[] { "Composed", "Warm", "Energetic", "Reserved" }),
                ["restingMood"] = ConvaiMcpResponses.StringProperty(
                    "What every character on this personality rests at between emotions. An emotion " +
                    "name such as \"joy\", or \"Neutral\" for no resting mood. Omit to leave unchanged."),
                ["restingMoodStrength"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "How strong the resting mood is, 0 to 1. A mood at 0 shows nothing."),
                ["howStronglyItShows"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "Overall strength of expression, -1 to 1. Above 0 shows more, below 0 shows less."),
                ["howQuicklyItReacts"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "How fast expressions arrive, 0.1 to 20. Higher snaps on, lower eases in."),
                ["neverSitsPerfectlyStill"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Keep a trace of movement in the face instead of holding it perfectly still. " +
                    "Also what plays the listening, thinking, reacting and interrupted reactions — " +
                    "turning it off silently disables all four."),
                ["moodFollowsConversation"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Let a sustained conversation slowly tint what this character rests at."),
                ["showsMoreThanOneEmotion"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Let related emotions show together instead of one at a time."),
                ["picksUpOtherCharactersMoods"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Faintly echo a nearby Convai character's strong emotion. Does nothing in a " +
                    "scene with only one character."),
                ["makePersonalityUnique"] = ConvaiMcpResponses.BooleanProperty(
                    "Consent to giving this character its own copy of the personality before " +
                    "tuning. Required when the personality is shared or ships with the SDK; without " +
                    "it, nothing is written.", false),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the changes without writing anything or creating any asset.", true)
            },
            "characterInstanceId");

        [McpOutputSchema(TuneTool)]
        public static object TuneOutput() => StandardSchema();

        // ------------------------------------------------------------------ configure helpers

        private static object ConfigureResponse(
            bool dryRun,
            ConvaiCharacter character,
            ConvaiEmotionController controller,
            List<string> changes,
            List<string> notes)
        {
            ConvaiEmotionReport report = ConvaiEmotionReport.For(controller);

            if (report.IsPresent && report.Profile == null)
            {
                notes.Add(
                    "This character has no personality assigned, so it expresses what it feels on " +
                    $"the Convai defaults. To give it a temperament, {SetUpRoute}, or assign an " +
                    "existing one — run Convai.InspectEmotionPersonalities to see them.");
            }

            var nextSteps = new List<string>(3);
            if (report.State == EmotionReadiness.Blocked) nextSteps.Add(report.Blocker);
            if (report.State == EmotionReadiness.Inert)
            {
                nextSteps.Add(
                    "Emotion detection is Off, so this character will never receive anything to " +
                    "feel. Call Convai.ConfigureEmotion with emotionDetection \"Responsive\".");
            }

            if (dryRun && changes.Count > 0)
                nextSteps.Add("Call Convai.ConfigureEmotion again with dryRun false to apply these changes.");
            if (!dryRun) nextSteps.Add("Run Convai.DiagnoseEmotion to confirm what this character will do.");

            return Response(
                true,
                dryRun ? "Previewed the Convai Emotions setup." : "Configured Convai Emotions.",
                new
                {
                    dryRun,
                    complete = report.IsWorking,
                    changes,
                    notes,
                    blockers = report.State == EmotionReadiness.Blocked
                        ? new[] { new { code = "EMOTION_FACE", message = report.Blocker } }
                        : Array.Empty<object>(),
                    readiness = Readiness(report),
                    detection = report.IsPresent ? DescribeDetection(report) : null,
                    restingMood = report.IsPresent ? DescribeRestingMood(report) : null,
                    personality = report.IsPresent
                        ? DescribePersonality(report)
                        : (object)new { assigned = false, message = "No Emotion component on this character yet." },
                    availablePersonalities = DescribeAvailablePersonalities(),
                    characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    componentInstanceId = ConvaiMcpEntityRef.ToToolId(controller),
                    affectedInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps
                });
        }

        private static bool TryResolveDetection(
            string requested, out EmotionDetectionMode? mode, out string error)
        {
            mode = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(requested)) return true;

            for (int i = 0; i < EmotionDetectionModes.Order.Length; i++)
            {
                EmotionDetectionMode candidate = EmotionDetectionModes.Order[i];
                if (!string.Equals(EmotionDetectionModes.ShortNameFor(candidate), requested.Trim(),
                        StringComparison.OrdinalIgnoreCase)) continue;
                mode = candidate;
                return true;
            }

            error = $"'{requested}' is not an emotion detection setting. Use Responsive (updates " +
                    "while the reply is spoken), Accurate (one reading of the whole reply), or Off.";
            return false;
        }

        /// <summary>Reads the stored mode by VALUE — never <c>enumValueIndex</c>. See the class remarks.</summary>
        private static EmotionDetectionMode ReadDetection(ConvaiEmotionController controller) =>
            (EmotionDetectionMode)new SerializedObject(controller).FindProperty("detectionMode").intValue;

        private static bool TryResolvePersonality(
            string path, out ConvaiEmotionProfile personality, out string error)
        {
            personality = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return true;

            personality = AssetDatabase.LoadAssetAtPath<ConvaiEmotionProfile>(path);
            if (personality != null) return true;

            error = $"No Convai emotion personality exists at '{path}'. Run " +
                    "Convai.InspectEmotionPersonalities to see what this project has, or " +
                    $"{SetUpRoute} to create one. These tools never create a personality from nothing.";
            return false;
        }

        /// <summary>
        ///     Resolves a requested resting mood against the character's own vocabulary.
        /// </summary>
        /// <returns>
        ///     Empty for "defer to the personality", <see cref="RestingMoodNeutralKeyword" /> for
        ///     "force no resting mood", otherwise the canonical label.
        /// </returns>
        private static bool TryResolveRestingMood(
            string requested, ConvaiEmotionProfile profile, out string label, out string error)
        {
            label = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(requested)) return true;

            string trimmed = requested.Trim();
            if (string.Equals(trimmed, RestingMoodUseProfileKeyword, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(trimmed, RestingMoodNeutralKeyword, StringComparison.OrdinalIgnoreCase))
            {
                label = RestingMoodNeutralKeyword;
                return true;
            }

            if (profile == null)
            {
                // With no personality there is no vocabulary to check against, and inventing one
                // would let a typo through. The built-in set is what the runtime would use.
                label = trimmed;
                return true;
            }

            bool synthesized = false;
            Taxonomy.EmotionTaxonomyAsset taxonomy = null;
            try
            {
                taxonomy = profile.ResolveTaxonomyOrDefault(out synthesized);
                if (taxonomy.TryResolve(trimmed, out EmotionDescriptor descriptor))
                {
                    label = descriptor.IsNeutral ? RestingMoodNeutralKeyword : descriptor.Label;
                    return true;
                }

                error = $"This character's vocabulary has no emotion called '{trimmed}'. " +
                        $"It knows: {string.Join(", ", ListEmotionLabels(profile))}.";
                return false;
            }
            finally
            {
                if (synthesized && taxonomy != null) UnityEngine.Object.DestroyImmediate(taxonomy);
            }
        }

        private static string[] ListEmotionLabels(ConvaiEmotionProfile profile)
        {
            if (profile == null) return Array.Empty<string>();

            bool synthesized = false;
            Taxonomy.EmotionTaxonomyAsset taxonomy = null;
            try
            {
                taxonomy = profile.ResolveTaxonomyOrDefault(out synthesized);
                IReadOnlyList<EmotionDescriptor> emotions = taxonomy.Emotions;
                var labels = new List<string>(emotions.Count);
                for (int i = 0; i < emotions.Count; i++)
                    if (!emotions[i].IsNeutral)
                        labels.Add(emotions[i].Label);
                return labels.ToArray();
            }
            finally
            {
                if (synthesized && taxonomy != null) UnityEngine.Object.DestroyImmediate(taxonomy);
            }
        }

        private static bool HasFaceWithBlendshapes(GameObject characterRoot)
        {
            if (characterRoot == null) return false;

            foreach (SkinnedMeshRenderer renderer in
                     characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                if (renderer.sharedMesh.blendShapeCount > 0) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ diagnose helpers

        private static object Readiness(in ConvaiEmotionReport report) => new
        {
            state = report.State.ToString(),
            isWorking = report.IsWorking,
            blocker = report.Blocker,
            message = report.Summary
        };

        private static object[] DescribeChecks(EmotionPreflight preflight)
        {
            IReadOnlyList<EmotionCheck> checks = preflight.Checks;
            if (checks == null) return Array.Empty<object>();

            var described = new object[checks.Count];
            for (int i = 0; i < checks.Count; i++)
            {
                described[i] = new
                {
                    label = checks[i].Label,
                    detail = checks[i].Detail,
                    state = checks[i].State.ToString()
                };
            }

            return described;
        }

        /// <summary>
        ///     The setup service's blockers and the troubleshooter's findings, serialised with their
        ///     own severities and wording. Never a second opinion about the same character.
        /// </summary>
        private static object[] DescribeIssues(
            in ConvaiEmotionReport report, long characterId, long componentId)
        {
            var issues = new List<object>(6);

            IReadOnlyList<EmotionCheck> checks = report.Preflight.Checks;
            if (checks != null)
            {
                for (int i = 0; i < checks.Count; i++)
                {
                    if (checks[i].State != EmotionCheckState.Blocked) continue;
                    issues.Add(Issue(
                        ConvaiEmotionReport.IssueCode(checks[i].Id), "Error",
                        $"{checks[i].Detail} {FaceFixAdvice}", checks[i].Label,
                        componentId, characterId, false));
                }
            }

            IReadOnlyList<EmotionFinding> findings = report.Findings;
            for (int i = 0; i < findings.Count; i++)
            {
                EmotionFinding finding = findings[i];
                issues.Add(Issue(
                    $"EMOTION_{finding.Fix.ToString().ToUpperInvariant()}",
                    finding.Severity.ToString(),
                    finding.Fix == EmotionFixId.None
                        ? finding.Message
                        : $"{finding.Message} In the Inspector this is the " +
                          $"'{EmotionTroubleshooter.DescribeFix(finding.Fix)}' button.",
                    finding.Title, componentId, characterId,
                    finding.Fix == EmotionFixId.TurnOnDetection));
            }

            return issues.ToArray();
        }

        private static object DescribeDetection(in ConvaiEmotionReport report) => new
        {
            mode = report.ModeName,
            description = EmotionDetectionModes.DescriptionFor(report.DetectionMode),
            isOff = report.DetectionMode == EmotionDetectionMode.Off,
            message = report.DetectionMode == EmotionDetectionMode.Off
                ? "Off, so no feelings ever arrive and nothing below can show. This setting is the " +
                  "source of truth — it is not read from any dashboard or backend configuration."
                : "This setting is the source of truth — it is not read from any dashboard or " +
                  "backend configuration."
        };

        /// <summary>
        ///     What the character rests at, and which link of the chain decided it.
        /// </summary>
        /// <remarks>
        ///     The authored chain is deterministic and reported as such. A live <c>SetMood()</c>
        ///     call and mood drift are indistinguishable from outside the runtime, so in Play Mode
        ///     the live value is reported beside the authored one with both candidate causes named,
        ///     rather than guessing at one and stating it as fact.
        /// </remarks>
        private static object DescribeRestingMood(in ConvaiEmotionReport report)
        {
            EmotionRestingMood resting = report.RestingMood;
            ConvaiEmotionController controller = report.Controller;

            bool playing = EditorApplication.isPlaying && controller != null && controller.isActiveAndEnabled;
            string liveLabel = playing ? controller.CurrentMoodLabel : string.Empty;
            float liveScore = playing ? controller.CurrentMoodScore : 0f;
            bool differs = playing &&
                           (!string.Equals(liveLabel, resting.Label, StringComparison.OrdinalIgnoreCase) ||
                            Mathf.Abs(liveScore - resting.Intensity) > 0.02f);

            return new
            {
                effectiveLabel = resting.Label,
                effectiveStrength = resting.Intensity,
                decidedBy = resting.Source.ToString(),
                decidedByMeaning = DescribeRestingMoodSource(resting.Source),
                explanation = resting.Explanation,
                suppressed = resting.Suppressed,
                labelResolves = resting.LabelResolves,
                live = !playing
                    ? null
                    : new
                    {
                        label = liveLabel,
                        strength = liveScore,
                        differsFromAuthored = differs,
                        message = differs
                            ? "The live resting mood differs from what is authored. Either gameplay " +
                              "code called SetMood on this character, or its mood is following the " +
                              "conversation — from outside the runtime these look the same, so both " +
                              "are named rather than one being guessed at. ClearMood returns it to " +
                              "the authored mood."
                            : "The live resting mood matches what is authored."
                    }
            };
        }

        private static string DescribeRestingMoodSource(EmotionRestingMoodSource source) => source switch
        {
            EmotionRestingMoodSource.ProfileBaseline =>
                "The personality's own resting mood, shared by every character using it.",
            EmotionRestingMoodSource.InitialMoodOverride =>
                "This character's own resting mood, set on its Emotion component and overriding " +
                "the personality.",
            EmotionRestingMoodSource.ForcedNeutralOverride =>
                "This character is deliberately set to rest neutral, which suppresses the " +
                "personality's resting mood rather than falling through to it.",
            _ => "Nothing sets a resting mood, so the face relaxes to plain neutral."
        };

        private static object DescribePersonality(in ConvaiEmotionReport report)
        {
            ConvaiEmotionProfile profile = report.Profile;
            if (profile == null)
            {
                return new
                {
                    assigned = false,
                    assetPath = string.Empty,
                    characterType = string.Empty,
                    setUpRoute = SetUpRoute,
                    message = "No personality is assigned, so this character expresses what it feels " +
                              "on the Convai defaults — which drive a face. Give it one to shape its " +
                              "temperament."
                };
            }

            EmotionOwnership ownership = EmotionOwnership.Of(profile);
            return new
            {
                assigned = true,
                name = profile.name,
                assetPath = AssetDatabase.GetAssetPath(profile),
                characterType = DescribeCharacterType(profile),
                readsAs = DescribeReadsAs(profile),
                restingMood = DescribeProfileRestingMood(profile),
                howStronglyItShows = profile.IntensityOffset,
                howQuicklyItReacts = profile.LerpSpeed,
                ownership = DescribeOwnership(profile, ownership),
                message = ownership.EditingAffectsOthers
                    ? $"{ownership.NoticeMessage} Convai.TuneEmotionPersonality does that copy for you."
                    : $"This personality belongs to this character alone, so " +
                      "Convai.TuneEmotionPersonality can change it directly."
            };
        }

        private static object DescribeOwnership(
            ConvaiEmotionProfile profile, EmotionOwnership ownership) => new
        {
            assetPath = AssetDatabase.GetAssetPath(profile),
            shipsWithSdk = ownership.RequiresProjectCopy,
            usedByCharacterCount = ownership.UserCount,
            mustCopyBeforeTuning = ownership.EditingAffectsOthers,
            message = ownership.NoticeMessage
        };

        private static object[] DescribeBehaviours(in ConvaiEmotionReport report)
        {
            IReadOnlyList<ConvaiEmotionBehaviour> behaviours = ConvaiEmotionBehaviours.Describe(in report);
            var described = new object[behaviours.Count];
            for (int i = 0; i < behaviours.Count; i++)
            {
                described[i] = new
                {
                    label = behaviours[i].Label,
                    effective = behaviours[i].Effective,
                    why = behaviours[i].Why
                };
            }

            return described;
        }

        /// <summary>
        ///     The direct answer to "why isn't this character's face doing anything?", cheapest
        ///     cause first. A projection — every entry restates something else in the same response.
        /// </summary>
        private static string[] BuildWhyTheFaceMightNotMove(in ConvaiEmotionReport report)
        {
            var reasons = new List<string>(8);

            if (report.HasBlocker) reasons.Add($"{report.Blocker} {FaceFixAdvice}");

            if (report.DetectionMode == EmotionDetectionMode.Off)
            {
                reasons.Add(
                    "Emotion detection is Off, so this character never receives anything to feel. " +
                    "Nothing else can make the face move until that changes.");
            }

            IReadOnlyList<EmotionFinding> findings = report.Findings;
            for (int i = 0; i < findings.Count; i++)
                if (findings[i].Severity != EmotionSeverity.Info)
                    reasons.Add($"{findings[i].Title}: {findings[i].Message}");

            IReadOnlyList<ConvaiEmotionBehaviour> behaviours = ConvaiEmotionBehaviours.Describe(in report);
            for (int i = 0; i < behaviours.Count; i++)
                if (!behaviours[i].Effective && !string.IsNullOrEmpty(behaviours[i].Why))
                    reasons.Add($"{behaviours[i].Label}: {behaviours[i].Why}");

            if (!EditorApplication.isPlaying)
            {
                reasons.Add(
                    "Nothing moves a face in Edit Mode — emotions arrive from a live conversation. " +
                    "Press Play and talk to the character, or hold an expression from the Emotion " +
                    "component's Live section to see it without a conversation.");
            }

            if (reasons.Count == 0)
                reasons.Add("Nothing is stopping this character's face reacting to what is said.");

            return reasons.ToArray();
        }

        private static object DescribeRuntime(in ConvaiEmotionReport report, bool include)
        {
            ConvaiEmotionController controller = report.Controller;
            if (!include || !EditorApplication.isPlaying || controller == null ||
                !controller.isActiveAndEnabled) return null;

            EmotionReading reading = controller.Current;
            return new
            {
                currentEmotion = controller.CurrentResolvedEmotion,
                strength = controller.CurrentNormalizedIntensity,
                restingMood = controller.CurrentMoodLabel,
                restingMoodStrength = controller.CurrentMoodScore,
                mouthInfluence = reading.MouthInfluence,
                faceShapeCount = report.Preflight.ResolvedShapeCount,
                message = "The current emotion is what the character is expressing right now; the " +
                          "resting mood is its temperament underneath, and never appears as the " +
                          "current emotion."
            };
        }

        private static string[] BuildNextSteps(in ConvaiEmotionReport report)
        {
            var steps = new List<string>(4);

            if (report.HasBlocker)
            {
                steps.Add($"{report.Blocker} {FaceFixAdvice}");
                return steps.ToArray();
            }

            if (report.DetectionMode == EmotionDetectionMode.Off)
            {
                steps.Add(
                    "Turn emotions on: call Convai.ConfigureEmotion with emotionDetection " +
                    "\"Responsive\" (updates while the reply is spoken) or \"Accurate\" (one reading " +
                    "of the whole reply, better in any language other than English).");
            }

            if (report.Profile == null)
            {
                steps.Add(
                    "This character works as it is. To shape its temperament, run " +
                    "Convai.InspectEmotionPersonalities and assign one with Convai.ConfigureEmotion, " +
                    $"or {SetUpRoute}.");
            }
            else
            {
                steps.Add(
                    "Change how this character feels with Convai.TuneEmotionPersonality — it copies " +
                    "a shared or SDK personality first, so no other character moves. Every setting " +
                    $"is also editable by hand at {EditorWindowPath}.");
            }

            if (!EditorApplication.isPlaying)
                steps.Add("Press Play and run this again to see what the character is actually feeling.");

            if (steps.Count == 0)
                steps.Add("Nothing needs doing — this character's face reacts to what is said.");

            return steps.ToArray();
        }

        // ------------------------------------------------------------------ personality helpers

        private static string DescribeCharacterType(ConvaiEmotionProfile profile)
        {
            CharacterDemeanor? type = EmotionDemeanorTooling.Identify(profile);
            return type.HasValue ? CharacterDemeanors.DisplayName(type.Value) : "Custom";
        }

        private static string DescribeReadsAs(ConvaiEmotionProfile profile)
        {
            CharacterDemeanor? type = EmotionDemeanorTooling.Identify(profile);
            if (!type.HasValue)
                return "Tuned away from all four character types, so it reads as its own thing.";

            for (int i = 0; i < EmotionPersonality.Archetypes.Length; i++)
                if (EmotionPersonality.Archetypes[i].Type == type.Value)
                    return EmotionPersonality.Archetypes[i].Description;

            return string.Empty;
        }

        private static string DescribeProfileRestingMood(ConvaiEmotionProfile profile)
        {
            if (profile == null) return "neutral";
            return !string.IsNullOrWhiteSpace(profile.BaselineEmotionLabel) && profile.BaselineIntensity > 0f
                ? $"{profile.BaselineEmotionLabel} at {profile.BaselineIntensity:0.00}"
                : "neutral";
        }

        private static object[] DescribeAvailablePersonalities()
        {
            string[] guids = AssetDatabase.FindAssets("t:ConvaiEmotionProfile");
            var described = new List<object>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var profile = AssetDatabase.LoadAssetAtPath<ConvaiEmotionProfile>(path);
                if (profile == null) continue;

                described.Add(new
                {
                    name = profile.name,
                    assetPath = path,
                    characterType = DescribeCharacterType(profile),
                    readsAs = DescribeReadsAs(profile),
                    shipsWithSdk = EmotionPersonality.ShipsWithSdk(profile)
                });
            }

            return described.ToArray();
        }

        private static Dictionary<ConvaiEmotionProfile, List<string>> GatherPersonalityUsage()
        {
            var usage = new Dictionary<ConvaiEmotionProfile, List<string>>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    ConvaiEmotionController[] controllers =
                        roots[r].GetComponentsInChildren<ConvaiEmotionController>(true);
                    for (int c = 0; c < controllers.Length; c++)
                    {
                        ConvaiEmotionProfile profile =
                            EmotionSetupService.ResolveAssignedProfile(controllers[c]);
                        if (profile == null) continue;

                        if (!usage.TryGetValue(profile, out List<string> names))
                        {
                            names = new List<string>(2);
                            usage[profile] = names;
                        }

                        names.Add(controllers[c].gameObject.name);
                    }
                }
            }

            return usage;
        }

        // ------------------------------------------------------------------ tune helpers

        private static bool TryResolveCharacterType(
            string requested, out CharacterDemeanor? type, out string error)
        {
            type = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(requested)) return true;

            for (int i = 0; i < EmotionPersonality.Archetypes.Length; i++)
            {
                CharacterDemeanor candidate = EmotionPersonality.Archetypes[i].Type;
                if (!string.Equals(CharacterDemeanors.DisplayName(candidate), requested.Trim(),
                        StringComparison.OrdinalIgnoreCase)) continue;
                type = candidate;
                return true;
            }

            error = $"'{requested}' is not a Convai character type. Use Composed, Warm, Energetic or Reserved.";
            return false;
        }

        /// <summary>
        ///     What this request would change, computed identically for a preview and for a real
        ///     run so the two can never disagree.
        /// </summary>
        private static List<object> DescribeRequestedChanges(
            ConvaiEmotionProfile profile,
            CharacterDemeanor? characterType,
            bool wantsRestingMood,
            string restingMoodLabel,
            TuneEmotionPersonalityRequest request)
        {
            var changed = new List<object>(9);

            if (characterType.HasValue && EmotionDemeanorTooling.Identify(profile) != characterType.Value)
            {
                changed.Add(Change("Character type", DescribeCharacterType(profile),
                    CharacterDemeanors.DisplayName(characterType.Value)));
            }

            if (wantsRestingMood)
            {
                string next = string.Equals(restingMoodLabel, RestingMoodNeutralKeyword, StringComparison.Ordinal)
                    ? "neutral"
                    : restingMoodLabel;
                if (!string.Equals(profile.BaselineEmotionLabel ?? string.Empty, next,
                        StringComparison.OrdinalIgnoreCase))
                    changed.Add(Change("Resting mood", DescribeProfileRestingMood(profile), next));
            }

            AddFloatChange(changed, "Resting mood strength", profile.BaselineIntensity,
                request.RestingMoodStrength);
            AddFloatChange(changed, "How strongly it shows", profile.IntensityOffset,
                request.HowStronglyItShows);
            AddFloatChange(changed, "How quickly it reacts", profile.LerpSpeed,
                request.HowQuicklyItReacts);

            AddBoolChange(changed, "Never sits perfectly still", profile.MicroExpressionsEnabled,
                request.NeverSitsPerfectlyStill);
            AddBoolChange(changed, "Mood follows the conversation", profile.MoodDriftEnabled,
                request.MoodFollowsConversation);
            AddBoolChange(changed, "Shows more than one emotion at once", profile.EnableEmotionBlending,
                request.ShowsMoreThanOneEmotion);
            AddBoolChange(changed, "Picks up other characters' moods", profile.ContagionEnabled,
                request.PicksUpOtherCharactersMoods);

            return changed;
        }

        private static void ApplyTuning(
            ConvaiEmotionProfile profile,
            CharacterDemeanor? characterType,
            bool wantsRestingMood,
            string restingMoodLabel,
            TuneEmotionPersonalityRequest request)
        {
            // Character type first: it writes every field the individual knobs below also touch, so
            // an explicit knob in the same call must win over the preset it came with.
            if (characterType.HasValue) EmotionPersonality.Apply(profile, characterType.Value);

            var serialized = new SerializedObject(profile);
            serialized.Update();

            if (wantsRestingMood)
            {
                bool forcedNeutral = string.Equals(restingMoodLabel, RestingMoodNeutralKeyword,
                    StringComparison.Ordinal);
                serialized.FindProperty("baselineEmotionLabel").stringValue =
                    forcedNeutral ? string.Empty : restingMoodLabel;

                // A resting mood at 0 strength is a control that looks set and does nothing.
                if (!forcedNeutral && !request.RestingMoodStrength.HasValue &&
                    serialized.FindProperty("baselineIntensity").floatValue <= 0f)
                    serialized.FindProperty("baselineIntensity").floatValue =
                        EmotionPersonalityTable.DefaultRestingMoodIntensity;

                if (forcedNeutral) serialized.FindProperty("baselineIntensity").floatValue = 0f;
            }

            if (request.RestingMoodStrength.HasValue)
                serialized.FindProperty("baselineIntensity").floatValue =
                    Mathf.Clamp01(request.RestingMoodStrength.Value);
            if (request.HowStronglyItShows.HasValue)
                serialized.FindProperty("intensityOffset").floatValue =
                    Mathf.Clamp(request.HowStronglyItShows.Value, -1f, 1f);
            if (request.HowQuicklyItReacts.HasValue)
                serialized.FindProperty("lerpSpeed").floatValue =
                    Mathf.Clamp(request.HowQuicklyItReacts.Value, 0.1f, 20f);

            if (request.NeverSitsPerfectlyStill.HasValue)
                serialized.FindProperty("microExpressionsEnabled").boolValue =
                    request.NeverSitsPerfectlyStill.Value;
            if (request.MoodFollowsConversation.HasValue)
                serialized.FindProperty("moodDriftEnabled").boolValue =
                    request.MoodFollowsConversation.Value;
            if (request.ShowsMoreThanOneEmotion.HasValue)
                serialized.FindProperty("enableEmotionBlending").boolValue =
                    request.ShowsMoreThanOneEmotion.Value;
            if (request.PicksUpOtherCharactersMoods.HasValue)
                serialized.FindProperty("contagionEnabled").boolValue =
                    request.PicksUpOtherCharactersMoods.Value;

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static object TuneResponse(
            bool dryRun,
            bool applied,
            ConvaiCharacter character,
            ConvaiEmotionController controller,
            ConvaiEmotionProfile profile,
            EmotionOwnership ownership,
            List<object> changed,
            string createdAssetPath,
            List<string> notes,
            IReadOnlyList<string> warnings)
        {
            ConvaiEmotionReport report = ConvaiEmotionReport.For(controller);

            var nextSteps = new List<string>(3);
            if (dryRun && changed.Count > 0)
            {
                nextSteps.Add(ownership.EditingAffectsOthers
                    ? "Call Convai.TuneEmotionPersonality again with dryRun false and " +
                      "makePersonalityUnique true to apply these to a copy made for this character."
                    : "Call Convai.TuneEmotionPersonality again with dryRun false to apply these.");
            }

            if (applied)
                nextSteps.Add("Run Convai.DiagnoseEmotion to confirm what this character will now do.");

            return Response(
                true,
                dryRun
                    ? "Previewed the Convai emotion personality changes."
                    : applied
                        ? "Tuned the Convai emotion personality."
                        : "Nothing needed changing.",
                new
                {
                    dryRun,
                    applied,
                    ownership = DescribeOwnership(profile, ownership),
                    requiresConsent = ownership.EditingAffectsOthers,
                    createdAssetPath,
                    changedFields = changed,
                    behaviour = DescribeBehaviours(report),
                    notes,
                    warnings,
                    characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    affectedInstanceId = ConvaiMcpEntityRef.ToToolId(profile),
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps
                });
        }

        private static object Change(string label, object from, object to) => new { label, from, to };

        private static void AddFloatChange(
            List<object> changed, string label, float current, float? requested)
        {
            if (!requested.HasValue) return;
            if (Mathf.Abs(current - requested.Value) <= 1e-4f) return;
            changed.Add(Change(label, current, requested.Value));
        }

        private static void AddBoolChange(
            List<object> changed, string label, bool current, bool? requested)
        {
            if (!requested.HasValue || current == requested.Value) return;
            changed.Add(Change(label, current, requested.Value));
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Character";
            var builder = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
                builder.Append(char.IsLetterOrDigit(name[i]) ? name[i] : '_');
            return builder.ToString();
        }

        // ------------------------------------------------------------------ shared

        /// <summary>
        ///     One finding, addressed two ways: <paramref name="affectedId" /> points at the thing
        ///     that is wrong, while the suggested arguments carry the <em>character</em> id, because
        ///     that is what <c>Convai.ConfigureEmotion</c> takes. An assistant following a
        ///     suggestion built from the component id gets INVALID_CHARACTER.
        /// </summary>
        private static object Issue(
            string code, string severity, string message, string evidence,
            long affectedId, long characterId, bool fixable) =>
            ConvaiMcpResponses.Issue(code, severity, message, evidence, affectedId, fixable, ConfigureTool,
                new { characterInstanceId = characterId, dryRun = true });

        private static object Response(bool success, string message, object data) =>
            ConvaiMcpResponses.Envelope(success, message, data);

        private static object StandardSchema() => ConvaiMcpResponses.StandardResponseSchema(true);
    }

    /// <summary>The same four tools, for Unity's in-editor assistant.</summary>
    public static class ConvaiEmotionAssistantTools
    {
        [AgentTool(
            "Add Convai Emotions to a character, give it an existing personality, and set how it detects feelings and what it rests at. Never creates or edits a personality asset.",
            "Convai.ConfigureEmotion")]
        public static object ConfigureEmotion(
            long characterInstanceId,
            string personalityAssetPath = "",
            string emotionDetection = "",
            string restingMood = "",
            float restingMoodStrength = -1f,
            bool dryRun = true) =>
            ConvaiEmotionMcpTools.Configure(new ConfigureEmotionRequest
            {
                CharacterInstanceId = characterInstanceId,
                PersonalityAssetPath = personalityAssetPath,
                EmotionDetection = emotionDetection,
                RestingMood = restingMood,
                RestingMoodStrength = restingMoodStrength < 0f ? null : restingMoodStrength,
                DryRun = dryRun
            });

        [AgentTool(
            "Explain what a Convai character's face will actually do, and why it might not be moving.",
            "Convai.DiagnoseEmotion")]
        public static object DiagnoseEmotion(long characterInstanceId = 0, bool includeRuntimeState = true) =>
            ConvaiEmotionMcpTools.Diagnose(new DiagnoseEmotionRequest
            {
                CharacterInstanceId = characterInstanceId,
                IncludeRuntimeState = includeRuntimeState
            });

        [AgentTool(
            "List the Convai emotion personalities this project has, and which characters use them.",
            "Convai.InspectEmotionPersonalities")]
        public static object InspectEmotionPersonalities(string[] folderPaths = null) =>
            ConvaiEmotionMcpTools.InspectPersonalities(new InspectEmotionPersonalitiesRequest
            {
                FolderPaths = folderPaths ?? Array.Empty<string>()
            });

        [AgentTool(
            "Change how a Convai character feels. Copies a shared or SDK-shipped personality for that character first, on explicit consent.",
            "Convai.TuneEmotionPersonality")]
        public static object TuneEmotionPersonality(
            long characterInstanceId,
            string characterType = "",
            string restingMood = "",
            float restingMoodStrength = -1f,
            bool makePersonalityUnique = false,
            bool dryRun = true) =>
            ConvaiEmotionMcpTools.Tune(new TuneEmotionPersonalityRequest
            {
                CharacterInstanceId = characterInstanceId,
                CharacterType = characterType,
                RestingMood = restingMood,
                RestingMoodStrength = restingMoodStrength < 0f ? null : restingMoodStrength,
                MakePersonalityUnique = makePersonalityUnique,
                DryRun = dryRun
            });
    }
}
