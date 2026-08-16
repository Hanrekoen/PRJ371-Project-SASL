using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.AI;
using Convai.Modules.BodyLanguage.Components;
using Convai.Modules.BodyLanguage.Core.Diagnostics;
using Convai.Modules.BodyLanguage.Data;
using Convai.Runtime.Components;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Modules.BodyLanguage.Editor.AI
{
    /// <summary>Input for <c>Convai.ConfigureBodyLanguage</c>.</summary>
    public sealed class ConfigureBodyLanguageRequest
    {
        public long CharacterInstanceId { get; set; }
        public string PersonalityAssetPath { get; set; }
        public bool AssignDefaultPersonality { get; set; }
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.DiagnoseBodyLanguage</c>.</summary>
    public sealed class DiagnoseBodyLanguageRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeRuntimeState { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.InspectBodyLanguagePersonalities</c>.</summary>
    public sealed class InspectBodyLanguagePersonalitiesRequest
    {
        public string[] FolderPaths { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    ///     Convai Body Language exposed through Unity's official MCP server: give a character
    ///     conversational body movement, see which personality it runs on, and answer "why isn't this
    ///     character moving?" — including when the real cause is another Convai module holding the
    ///     pose.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every verdict in every response comes from <see cref="BodyLanguageSetupService" /> —
    ///         the same code the component inspector's <b>This Character</b> card draws. This class
    ///         contains no check of its own, so an assistant and the editor cannot describe the same
    ///         character differently.
    ///     </para>
    ///     <para>
    ///         Adding the component is the one step performed here rather than in the service. The
    ///         service deliberately starts from a controller that already exists, because Add
    ///         Component is the gesture every Unity user knows — but an assistant has no Add
    ///         Component button, so this layer performs that single step and then hands off.
    ///     </para>
    ///     <para>
    ///         Nothing here creates or edits an asset. Every amplitude, cadence and toggle lives on
    ///         the Body Language Profile, and these tools only ever read one or assign one that
    ///         already exists. When a project has none, the response names the menu path that
    ///         creates one.
    ///     </para>
    /// </remarks>
    public static class ConvaiBodyLanguageMcpTools
    {
        private const string ConfigureTool = "Convai.ConfigureBodyLanguage";
        private const string DiagnoseTool = "Convai.DiagnoseBodyLanguage";
        private const string PersonalitiesTool = "Convai.InspectBodyLanguagePersonalities";

        private const string AddComponentMenuPath = "Add Component → Convai → Embodiment → Body Language";
        private const string CreateProfileMenuPath =
            "Assets → Create → Convai → Embodiment → Body Language Profile";

        private const string RigFixAdvice =
            "Body Language layers small rotations onto an animated skeleton, so it needs a Humanoid " +
            "Avatar with the spine chain mapped. Set the model's Rig → Animation Type to Humanoid, " +
            "or add a Character Rig and map its Spine to the character's spine bone.";

        // ------------------------------------------------------------------ configure

        [McpTool(
            ConfigureTool,
            "Adds Convai Body Language to a character so it breathes, shifts its weight, sways and gestures as it talks, and gives it a personality by assigning a Body Language Profile the project already has. Previews by default. Never creates or edits an asset, and refuses a character whose rig cannot drive the module rather than adding something inert.",
            "Configure Convai Body Language",
            Groups = new[] { "convai", "body-language" },
            EnabledByDefault = true)]
        public static object Configure(JObject input) =>
            Configure(input?.ToObject<ConfigureBodyLanguageRequest>());

        public static object Configure(ConfigureBodyLanguageRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "Body Language can only be configured in Edit Mode.",
                    new { code = "PLAY_MODE_ACTIVE" });

            request ??= new ConfigureBodyLanguageRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            bool wantsNamedPersonality = !string.IsNullOrWhiteSpace(request.PersonalityAssetPath);
            if (wantsNamedPersonality && request.AssignDefaultPersonality)
            {
                return Response(false,
                    "Name a personality with personalityAssetPath, or ask for the default one with " +
                    "assignDefaultPersonality — not both.",
                    new { code = "PERSONALITY_AMBIGUOUS", requiredInputs = new[] { "personalityAssetPath" } });
            }

            if (!TryResolvePersonality(request.PersonalityAssetPath, out ConvaiBodyLanguageProfile personality,
                    out error))
            {
                return Response(false, error, new
                {
                    code = "INVALID_PERSONALITY",
                    createProfileMenuPath = CreateProfileMenuPath,
                    availablePersonalities = DescribeAvailablePersonalities()
                });
            }

            var controller = character.GetComponentInChildren<ConvaiBodyLanguageController>(true);
            bool hadController = controller != null;

            // Adding a component that can never move the character is not a favour. On a rig the
            // module cannot drive, this reports the blocker and changes nothing — the contract every
            // Convai configure tool holds. A character that already has the component is a choice the
            // user has already made, so assigning a personality to it stays allowed and the blocker
            // is carried as a warning instead.
            if (!hadController)
            {
                BodyLanguagePreflight candidate =
                    BodyLanguageSetupService.InspectCandidate(character.gameObject);
                if (candidate.TryGetBlocker(out BodyLanguageCheck rigBlocker))
                {
                    return Response(false,
                        $"'{character.gameObject.name}' cannot run Body Language yet: {rigBlocker.Detail}.",
                        new
                        {
                            code = ConvaiBodyLanguageReport.IssueCode(rigBlocker.Id),
                            blocker = rigBlocker.Detail,
                            advice = RigFixAdvice,
                            characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                            checks = DescribeChecks(candidate),
                            nextSteps = new[]
                            {
                                RigFixAdvice,
                                "Then call Convai.ConfigureBodyLanguage again."
                            }
                        });
                }
            }

            var changes = new List<string>(3);
            var notes = new List<string>(3);
            if (!hadController) changes.Add("Add the Body Language component to this character");

            ConvaiBodyLanguageProfile assigned = BodyLanguageSetupService.ResolveAssignedProfile(controller);
            bool wantsDefaultPersonality = request.AssignDefaultPersonality && assigned == null;

            if (personality != null && assigned != personality)
                changes.Add($"Set Personality: '{personality.name}'");
            else if (wantsDefaultPersonality)
                changes.Add($"{BodyLanguageSetupService.DescribeFix(BodyLanguageFixId.AssignDefaultProfile)} (personality)");
            else if (request.AssignDefaultPersonality)
                notes.Add($"This character already has the '{assigned.name}' personality, so it was left alone.");

            if (request.DryRun)
                return ConfigureResponse(true, character, controller, changes, notes);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                if (controller == null)
                    controller = Undo.AddComponent<ConvaiBodyLanguageController>(character.gameObject);

                if (personality != null)
                {
                    Undo.RecordObject(controller, "Configure Convai Body Language");
                    var serialized = new SerializedObject(controller);
                    SerializedProperty property = serialized.FindProperty("profile");
                    if (property == null)
                        notes.Add("This character's Body Language component has no Personality field to write.");
                    else
                    {
                        property.objectReferenceValue = personality;
                        serialized.ApplyModifiedProperties();
                    }
                }
                else if (request.AssignDefaultPersonality &&
                         !BodyLanguageSetupService.ApplyFix(controller, BodyLanguageFixId.AssignDefaultProfile) &&
                         BodyLanguageSetupService.ResolveAssignedProfile(controller) == null)
                {
                    notes.Add(
                        "No Body Language Profile exists in this project yet, so the character keeps " +
                        $"the SDK defaults — which work. Create one with {CreateProfileMenuPath}.");
                }

                EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                Undo.SetCurrentGroupName("Configure Convai Body Language");
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
                    "Project path of an existing Body Language Profile to give this character a " +
                    "personality. Never creates one — if the project has none, the response lists " +
                    "what it does have and names the menu path that makes a new one. Omit to leave " +
                    "the personality unchanged."),
                ["assignDefaultPersonality"] = ConvaiMcpResponses.BooleanProperty(
                    "Give a character with no personality the shipped one, or the first the project " +
                    "has. Leaves an existing personality alone.", false),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the changes without touching the scene.", true)
            },
            "characterInstanceId");

        [McpOutputSchema(ConfigureTool)]
        public static object ConfigureOutput() => StandardSchema();

        // ------------------------------------------------------------------ diagnose

        [McpTool(
            DiagnoseTool,
            "Explains what a character's body is doing and why: what its rig offers, which personality tunes it and which behaviours that personality switches off, which other Convai modules share its body and what each one changes, and — in Play Mode — its live posture, breathing, weight shifts and gesture suppression. Answers \"why isn't this character moving?\". Read-only.",
            "Diagnose Convai Body Language",
            Groups = new[] { "convai", "body-language", "validation" },
            EnabledByDefault = true)]
        public static object Diagnose(JObject input) =>
            Diagnose(input?.ToObject<DiagnoseBodyLanguageRequest>());

        public static object Diagnose(DiagnoseBodyLanguageRequest request)
        {
            request ??= new DiagnoseBodyLanguageRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            long characterId = ConvaiMcpEntityRef.ToToolId(character.gameObject);
            ConvaiBodyLanguageReport report = ConvaiBodyLanguageReport.For(character.gameObject);

            if (!report.IsPresent)
            {
                BodyLanguagePreflight candidate =
                    BodyLanguageSetupService.InspectCandidate(character.gameObject);
                bool canHost = !candidate.HasBlocker;

                return Response(true, $"'{character.gameObject.name}' has no Body Language component yet.",
                    new
                    {
                        present = false,
                        readiness = BodyLanguageReadiness.NotInstalled.ToString(),
                        isWorking = false,
                        characterInstanceId = characterId,
                        summary = report.Summary,
                        checks = DescribeChecks(candidate),
                        issues = new[]
                        {
                            Issue("BODY_LANGUAGE_COMPONENT_MISSING", "Warning",
                                "This character has no Body Language component, so its body holds a " +
                                "still pose between animations — it will not breathe, shift its " +
                                $"weight or gesture as it talks. Add it with {AddComponentMenuPath}. " +
                                "That is the only required step: it needs no profile and no clips.",
                                character.gameObject.name, characterId, characterId, canHost)
                        },
                        coordination = DescribeCoordination(BodyLanguageSetupService.InspectCoordination(
                            character.transform, BodyLanguageSetupService.ResolveEffectiveProfile(null))),
                        nextSteps = canHost
                            ? new[]
                            {
                                $"Add Body Language to '{character.gameObject.name}' — " +
                                $"{AddComponentMenuPath}, or call Convai.ConfigureBodyLanguage.",
                                "Then run Convai.DiagnoseBodyLanguage again to see what its rig offers."
                            }
                            : new[]
                            {
                                candidate.TryGetBlocker(out BodyLanguageCheck blocker)
                                    ? blocker.Detail
                                    : "This character's rig cannot drive Body Language yet.",
                                RigFixAdvice
                            }
                    });
            }

            long componentId = ConvaiMcpEntityRef.ToToolId(report.Controller);
            var issues = new List<object>(4);
            IReadOnlyList<BodyLanguageCheck> checks = report.Preflight.Checks;
            for (int i = 0; i < checks.Count; i++)
            {
                BodyLanguageCheck check = checks[i];
                if (check.State is BodyLanguageCheckState.Ok or BodyLanguageCheckState.Optional) continue;

                bool blocked = check.State == BodyLanguageCheckState.Blocked;
                issues.Add(Issue(
                    ConvaiBodyLanguageReport.IssueCode(check.Id),
                    blocked ? "Error" : "Info",
                    blocked ? $"{check.Detail}. {RigFixAdvice}" : check.Detail,
                    check.Label,
                    componentId,
                    characterId,
                    !blocked));
            }

            return Response(
                true,
                report.IsWorking
                    ? $"Convai Body Language is working on '{character.gameObject.name}'."
                    : $"Convai Body Language cannot run on '{character.gameObject.name}' yet.",
                new
                {
                    present = true,
                    readiness = report.State.ToString(),
                    isWorking = report.IsWorking,
                    characterInstanceId = characterId,
                    componentInstanceId = componentId,
                    summary = report.Summary,
                    checks = DescribeChecks(report.Preflight),
                    issues,
                    rig = DescribeRig(report.Preflight),
                    personality = DescribePersonality(report),
                    coordination = DescribeCoordination(report.Coordination),
                    whyItMightNotMove = BuildWhyItMightNotMove(report),
                    runtime = DescribeRuntime(report.Controller, request.IncludeRuntimeState),
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
                    "Include what the character's body is doing right now. Play Mode only.", true)
            });

        [McpOutputSchema(DiagnoseTool)]
        public static object DiagnoseOutput() => StandardSchema();

        // ------------------------------------------------------------------ personalities

        [McpTool(
            PersonalitiesTool,
            "Lists the Body Language Profiles in this project — what each one's expressiveness is, which behaviours it switches off, and which characters in the open scenes already use it. Read this before giving a character a personality, since these tools assign a personality but never author one. Read-only.",
            "Inspect Convai Body Language Personalities",
            Groups = new[] { "convai", "body-language" },
            EnabledByDefault = true)]
        public static object InspectPersonalities(JObject input) =>
            InspectPersonalities(input?.ToObject<InspectBodyLanguagePersonalitiesRequest>());

        public static object InspectPersonalities(InspectBodyLanguagePersonalitiesRequest request)
        {
            request ??= new InspectBodyLanguagePersonalitiesRequest();

            string[] folders = request.FolderPaths is { Length: > 0 } ? request.FolderPaths : null;
            string[] guids = folders == null
                ? AssetDatabase.FindAssets("t:ConvaiBodyLanguageProfile")
                : AssetDatabase.FindAssets("t:ConvaiBodyLanguageProfile", folders);

            Dictionary<ConvaiBodyLanguageProfile, List<string>> usage = GatherPersonalityUsage();
            var personalities = new List<object>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var profile = AssetDatabase.LoadAssetAtPath<ConvaiBodyLanguageProfile>(path);
                if (profile == null) continue;

                usage.TryGetValue(profile, out List<string> users);
                personalities.Add(new
                {
                    name = profile.name,
                    assetPath = path,
                    shipsWithSdk = BodyLanguageSetupService.ProfileShipsWithSdk(profile),
                    expressivenessPreset = profile.ExpressivenessPreset.ToString(),
                    resolvedExpressiveness = profile.ResolveExpressiveness(),
                    headline = BodyLanguageSetupService.HeadlineOf(profile),
                    settings = DescribeSwitches(profile),
                    usedByCharacterCount = users?.Count ?? 0,
                    usedByCharacters = users ?? (IReadOnlyList<string>)Array.Empty<string>()
                });
            }

            return Response(
                true,
                personalities.Count == 0
                    ? "This project has no Body Language Profiles."
                    : $"Found {personalities.Count} Body Language personality asset(s).",
                new
                {
                    count = personalities.Count,
                    personalities,
                    createProfileMenuPath = CreateProfileMenuPath,
                    message = personalities.Count == 0
                        ? "A character with no personality is not broken — it runs on the SDK " +
                          $"defaults, which work. Create one with {CreateProfileMenuPath} to shape a " +
                          "character."
                        : "Assign one to a character with Convai.ConfigureBodyLanguage. A personality " +
                          "shared by several characters tunes all of them, so give a character its " +
                          "own asset before shaping it.",
                    nextSteps = personalities.Count == 0
                        ? new[] { $"Create a personality with {CreateProfileMenuPath}." }
                        : new[]
                        {
                            "Call Convai.ConfigureBodyLanguage with personalityAssetPath to give a " +
                            "character one of these.",
                            "Every amplitude and cadence is edited on the asset itself in the " +
                            "Inspector — these tools deliberately never write to one."
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

        // ------------------------------------------------------------------ configure helpers

        private static object ConfigureResponse(
            bool dryRun,
            ConvaiCharacter character,
            ConvaiBodyLanguageController controller,
            List<string> changes,
            List<string> notes)
        {
            ConvaiBodyLanguageReport report = ConvaiBodyLanguageReport.For(controller);

            if (!dryRun && report.UsingSdkDefaults && report.IsPresent)
            {
                notes.Add(
                    "This character has no Body Language Profile, so it runs on the SDK defaults — " +
                    $"which work. Create one with {CreateProfileMenuPath} to shape its personality.");
            }

            var nextSteps = new List<string>(3);
            if (report.State == BodyLanguageReadiness.Blocked) nextSteps.Add(report.Blocker);
            if (dryRun && changes.Count > 0)
                nextSteps.Add("Call Convai.ConfigureBodyLanguage again with dryRun false to apply these changes.");
            if (!dryRun) nextSteps.Add("Run Convai.DiagnoseBodyLanguage to confirm the character is ready.");

            return Response(
                true,
                dryRun ? "Previewed the Convai Body Language setup." : "Configured Convai Body Language.",
                new
                {
                    dryRun,
                    complete = report.IsWorking,
                    changes,
                    notes,
                    blockers = report.State == BodyLanguageReadiness.Blocked
                        ? new[] { new { code = "BODY_LANGUAGE_SETUP_RIG", message = report.Blocker } }
                        : Array.Empty<object>(),
                    readiness = new
                    {
                        state = report.State.ToString(),
                        isWorking = report.IsWorking,
                        blocker = report.Blocker
                    },
                    personality = report.IsPresent
                        ? DescribePersonality(report)
                        : new
                        {
                            profileName = string.Empty,
                            profileAssetPath = string.Empty,
                            usingSdkDefaults = true,
                            createProfileMenuPath = CreateProfileMenuPath,
                            expressiveness = (object)null,
                            settings = Array.Empty<object>(),
                            message = "No Body Language component on this character yet."
                        },
                    availablePersonalities = DescribeAvailablePersonalities(),
                    characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    componentInstanceId = ConvaiMcpEntityRef.ToToolId(controller),
                    affectedInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps
                });
        }

        private static bool TryResolvePersonality(
            string path, out ConvaiBodyLanguageProfile personality, out string error)
        {
            personality = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return true;

            personality = AssetDatabase.LoadAssetAtPath<ConvaiBodyLanguageProfile>(path);
            if (personality != null) return true;

            error = $"No Body Language Profile exists at '{path}'. Create one with " +
                    $"{CreateProfileMenuPath}, or leave personalityAssetPath empty to use the SDK defaults.";
            return false;
        }

        private static object[] DescribeAvailablePersonalities()
        {
            string[] guids = AssetDatabase.FindAssets("t:ConvaiBodyLanguageProfile");
            var described = new List<object>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var profile = AssetDatabase.LoadAssetAtPath<ConvaiBodyLanguageProfile>(path);
                if (profile == null) continue;

                described.Add(new
                {
                    name = profile.name,
                    assetPath = path,
                    expressivenessPreset = profile.ExpressivenessPreset.ToString(),
                    headline = BodyLanguageSetupService.HeadlineOf(profile)
                });
            }

            return described.ToArray();
        }

        private static Dictionary<ConvaiBodyLanguageProfile, List<string>> GatherPersonalityUsage()
        {
            var usage = new Dictionary<ConvaiBodyLanguageProfile, List<string>>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    ConvaiBodyLanguageController[] controllers =
                        roots[r].GetComponentsInChildren<ConvaiBodyLanguageController>(true);
                    for (int c = 0; c < controllers.Length; c++)
                    {
                        ConvaiBodyLanguageProfile profile =
                            BodyLanguageSetupService.ResolveAssignedProfile(controllers[c]);
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

        // ------------------------------------------------------------------ diagnose helpers

        private static object[] DescribeChecks(BodyLanguagePreflight preflight)
        {
            IReadOnlyList<BodyLanguageCheck> checks = preflight.Checks;
            if (checks == null) return Array.Empty<object>();

            var described = new object[checks.Count];
            for (int i = 0; i < checks.Count; i++)
            {
                described[i] = new
                {
                    label = checks[i].Label,
                    detail = checks[i].Detail,
                    state = checks[i].State.ToString(),
                    suggestedFix = BodyLanguageSetupService.DescribeFix(checks[i].Fix) ?? string.Empty
                };
            }

            return described;
        }

        /// <summary>
        ///     What this character's skeleton offers, as the documentation's own rig table states it.
        ///     A projection of the preflight rows, never a second probe.
        /// </summary>
        private static object DescribeRig(BodyLanguagePreflight preflight) => new
        {
            spine = FindDetail(preflight, BodyLanguageSetupService.CheckRig),
            torso = FindDetail(preflight, BodyLanguageSetupService.CheckTorso),
            shoulders = FindDetail(preflight, BodyLanguageSetupService.CheckShoulders),
            stance = FindDetail(preflight, BodyLanguageSetupService.CheckStance),
            armsAndHands = FindDetail(preflight, BodyLanguageSetupService.CheckHands),
            message =
                "A missing optional bone is a legitimate rig, not a fault — the behaviour that needs " +
                "it simply stays off. Only the Spine can stop the module."
        };

        private static string FindDetail(BodyLanguagePreflight preflight, string checkId)
        {
            IReadOnlyList<BodyLanguageCheck> checks = preflight.Checks;
            if (checks == null) return string.Empty;

            for (int i = 0; i < checks.Count; i++)
                if (string.Equals(checks[i].Id, checkId, StringComparison.Ordinal))
                    return checks[i].Detail;

            return string.Empty;
        }

        private static object DescribePersonality(ConvaiBodyLanguageReport report)
        {
            ConvaiBodyLanguageProfile effective = report.EffectiveProfile;
            return new
            {
                profileName = report.AssignedProfile != null ? report.AssignedProfile.name : string.Empty,
                profileAssetPath = report.AssignedProfile != null
                    ? AssetDatabase.GetAssetPath(report.AssignedProfile)
                    : string.Empty,
                usingSdkDefaults = report.UsingSdkDefaults,
                createProfileMenuPath = report.UsingSdkDefaults ? CreateProfileMenuPath : string.Empty,
                expressiveness = effective == null
                    ? null
                    : new
                    {
                        preset = effective.ExpressivenessPreset.ToString(),
                        resolved = effective.ResolveExpressiveness(),
                        message = DescribeExpressiveness(effective)
                    },
                settings = DescribeSwitches(effective),
                message = report.AssignedProfile != null
                    ? $"Tuned by the '{report.AssignedProfile.name}' Body Language Profile. Every " +
                      "amplitude and cadence is edited on that asset in the Inspector."
                    : "No Body Language Profile is assigned, so this character runs on the SDK " +
                      "defaults — which are already tuned to be visible at a normal conversational " +
                      "distance. Assign one to give it a personality of its own."
            };
        }

        private static string DescribeExpressiveness(ConvaiBodyLanguageProfile profile) =>
            profile.ExpressivenessPreset switch
            {
                ExpressivenessPreset.Subtle =>
                    "Subtle — minimal and understated. Shrugs, idle hand motion and stance " +
                    "settle-steps are absent by design at this setting, and everything else is small.",
                ExpressivenessPreset.Natural =>
                    "Natural — clearly visible at a normal two-metre conversational camera distance, " +
                    "without reading as performative.",
                ExpressivenessPreset.Expressive =>
                    "Expressive — larger, more frequent and more varied motion.",
                ExpressivenessPreset.Theatrical =>
                    "Theatrical — maximum amplitude, frequency and richness.",
                _ =>
                    "Custom — the personality's own Custom Expressiveness scalar is used instead of a " +
                    "fixed preset."
            };

        private static object[] DescribeSwitches(ConvaiBodyLanguageProfile profile)
        {
            IReadOnlyList<BodyLanguageSwitch> switches = BodyLanguageSetupService.SwitchesOf(profile);
            var described = new object[switches.Count];
            for (int i = 0; i < switches.Count; i++)
            {
                described[i] = new
                {
                    label = switches[i].Label,
                    isOn = switches[i].IsOn,
                    message = switches[i].IsOn
                        ? "On."
                        : $"Off — {switches[i].ConsequenceWhenOff}."
                };
            }

            return described;
        }

        /// <summary>
        ///     Who else moves this character's body. The whole point of stating it is that every one
        ///     of these relationships degrades silently, so a user cannot tell the difference between
        ///     "the module is broken" and "another module is holding the pose".
        /// </summary>
        private static object DescribeCoordination(BodyLanguageCoordination coordination) => new
        {
            summary = coordination.Summary,
            bodyAnimationPresent = coordination.HasBodyAnimation,
            gazePresent = coordination.HasGaze,
            emotionPresent = coordination.HasEmotion,
            lipSyncPresent = coordination.HasLipSync,
            headGestures = coordination.HeadGestures,
            gestureCues = coordination.GestureCues,
            gestureSuppression = coordination.GestureSuppression,
            speechRhythm = coordination.SpeechRhythm,
            emotion = coordination.Emotion,
            exertion = coordination.Exertion,
            note = BodyLanguageCoordination.RuntimeCaveat
        };

        /// <summary>
        ///     The direct answer to "why isn't this character moving?", cheapest cause first.
        /// </summary>
        /// <remarks>
        ///     A projection, never a new opinion: every entry restates a preflight row, a personality
        ///     switch or a live value that also appears elsewhere in the same response.
        /// </remarks>
        private static string[] BuildWhyItMightNotMove(ConvaiBodyLanguageReport report)
        {
            var reasons = new List<string>(6);

            if (report.Preflight.TryGetBlocker(out BodyLanguageCheck blocker))
                reasons.Add($"{blocker.Label}: {blocker.Detail}. {RigFixAdvice}");

            ConvaiBodyLanguageProfile profile = report.EffectiveProfile;
            if (profile != null)
            {
                if (profile.ExpressivenessPreset == ExpressivenessPreset.Subtle)
                {
                    reasons.Add(
                        "Expressiveness is Subtle on " + PersonalityName(report) + ", so the optional " +
                        "behaviours — shrugs, idle hand motion, stance settle-steps — are absent by " +
                        "design and everything else is small. Try Natural.");
                }

                IReadOnlyList<BodyLanguageSwitch> switches = BodyLanguageSetupService.SwitchesOf(profile);
                for (int i = 0; i < switches.Count; i++)
                {
                    if (switches[i].IsOn) continue;
                    reasons.Add(
                        $"{switches[i].Label} is off on {PersonalityName(report)}, so " +
                        $"{switches[i].ConsequenceWhenOff}.");
                }
            }

            IReadOnlyList<BodyLanguageCheck> checks = report.Preflight.Checks;
            if (checks != null)
            {
                for (int i = 0; i < checks.Count; i++)
                {
                    if (checks[i].State != BodyLanguageCheckState.Optional) continue;
                    if (string.Equals(checks[i].Id, BodyLanguageSetupService.CheckCoordination,
                            StringComparison.Ordinal)) continue;
                    if (string.Equals(checks[i].Id, BodyLanguageSetupService.CheckCharacter,
                            StringComparison.Ordinal)) continue;
                    reasons.Add($"{checks[i].Label}: {checks[i].Detail}.");
                }
            }

            if (report.Coordination.HasBodyAnimation)
                reasons.Add(report.Coordination.GestureSuppression);

            AddRuntimeReasons(report.Controller, reasons);

            if (reasons.Count == 0)
            {
                reasons.Add(
                    "Nothing is stopping this character moving — press Play and it will breathe, " +
                    "shift its weight and gesture as it talks.");
            }

            return reasons.ToArray();
        }

        private static void AddRuntimeReasons(ConvaiBodyLanguageController controller, List<string> reasons)
        {
            if (!EditorApplication.isPlaying || controller == null || !controller.isActiveAndEnabled) return;

            BodyLanguageSnapshot snapshot = controller.CaptureSnapshot();
            if (snapshot == null) return;

            if (snapshot.IsInert)
                reasons.Add("The module is inert right now — see the Console for the rig error it logged once.");

            if (snapshot.GesticulationSuppression != GestureSuppression.None)
            {
                reasons.Add(
                    $"Right now this character is under {Humanize(snapshot.GesticulationSuppression.ToString())} " +
                    "gesture suppression, so " + (snapshot.GesticulationSuppression == GestureSuppression.FullBody
                        ? "its posture and breathing have faded to zero and every cue is refused."
                        : "its posture and gesticulation are reduced — head-beats and breathing " +
                          "deliberately stay at full weight."));
            }

            if (snapshot.MasterWeight < 0.05f && !snapshot.IsInert)
            {
                reasons.Add(
                    "The posture and breath master weight is near zero right now, so nothing it " +
                    "computes reaches the bones yet — it ramps in on enable rather than snapping.");
            }

            if (snapshot.DialogueState is DialogueState.Reacting or DialogueState.Interrupted
                or DialogueState.Settling)
            {
                reasons.Add(
                    $"The character is {snapshot.DialogueState} right now, and weight shifts do not " +
                    "schedule in that state.");
            }
        }

        private static string PersonalityName(ConvaiBodyLanguageReport report) =>
            report.AssignedProfile != null
                ? $"the '{report.AssignedProfile.name}' personality"
                : "the SDK defaults";

        private static object DescribeRuntime(ConvaiBodyLanguageController controller, bool include)
        {
            if (!include || !EditorApplication.isPlaying || controller == null ||
                !controller.isActiveAndEnabled) return null;

            BodyLanguageSnapshot snapshot = controller.CaptureSnapshot();
            if (snapshot == null) return null;

            var trace = new List<object>(snapshot.RecentTrace.Count);
            for (int i = 0; i < snapshot.RecentTrace.Count; i++)
            {
                BodyLanguageTraceEntry entry = snapshot.RecentTrace[i];
                trace.Add(new { time = entry.Time, message = entry.Message });
            }

            return new
            {
                isInert = snapshot.IsInert,
                dialogueState = snapshot.DialogueState.ToString(),
                personality = snapshot.ProfileName,
                masterWeight = snapshot.MasterWeight,
                posture = new
                {
                    opennessTarget = snapshot.PostureOpennessTarget,
                    opennessCurrent = snapshot.PostureOpennessCurrent,
                    leanTarget = snapshot.PostureLeanTarget,
                    leanCurrent = snapshot.PostureLeanCurrent,
                    tensionTarget = snapshot.PostureTensionTarget,
                    tensionCurrent = snapshot.PostureTensionCurrent
                },
                breathing = new
                {
                    rateCpm = snapshot.BreathRateCpm,
                    depth = snapshot.BreathDepth,
                    phase = snapshot.BreathPhase,
                    duckedAgainstIdleClip = snapshot.BreathDuckFactor
                },
                stanceAndSway = new
                {
                    weightShiftLateral = snapshot.StanceLateral,
                    weightShiftCentimeters = snapshot.StanceLateralCentimeters,
                    isShifting = snapshot.StanceIsShifting,
                    feetStayPlanted = snapshot.LegCompensationActive,
                    swaySagittal = snapshot.SwaySagittal,
                    swayLateral = snapshot.SwayLateral
                },
                gesticulation = new
                {
                    suppression = snapshot.GesticulationSuppression.ToString(),
                    suppressionMessage = snapshot.GesticulationSuppression switch
                    {
                        GestureSuppression.UpperBody =>
                            "Posture and gesticulation are reduced; head-beats and breathing stay at " +
                            "full weight, and semantic gesture cues are refused.",
                        GestureSuppression.FullBody =>
                            "Posture and breathing have faded to zero and every gesture cue is refused.",
                        _ => "Nothing is ducking this character right now."
                    },
                    usingMotionBudget = snapshot.UsingMotionBudget,
                    upperBodyOccupancy = snapshot.UpperBodyOccupancy,
                    cadence = snapshot.GesticulationStatisticalCadenceActive
                        ? "Randomized fallback cadence — nothing is publishing live speech energy."
                        : "Riding the character's live speech energy.",
                    posturePulse = snapshot.GesticulationPosturePulseValue,
                    lastGestureCue = snapshot.LastGestureCueKind.ToString(),
                    lastGestureCueAccepted = snapshot.LastGestureCueAccepted,
                    proceduralGestureRunning = snapshot.ProceduralGestureFallbackActive
                },
                headGestures = new
                {
                    isPlaying = snapshot.HeadGestureIsPlaying,
                    progress = snapshot.HeadGestureProgress,
                    consumerCount = snapshot.HeadGestureConsumerCount,
                    selfActuating = snapshot.HeadGestureFallbackActive,
                    message = snapshot.HeadGestureConsumerCount > 0
                        ? "A head-gesture consumer is composing these — Gaze, when it is present."
                        : "Nothing is consuming head gestures, so Body Language is moving the head " +
                          "and neck itself at conservative limits."
                },
                listeningAndFidgets = new
                {
                    leanIn = snapshot.ListeningLeanIn,
                    stillness = snapshot.ListeningStillnessFactor,
                    wantsTiltHold = snapshot.ListeningWantsTiltHold,
                    fidgetWeightShift = snapshot.FidgetWeightShift
                },
                reactions = new
                {
                    flinch = snapshot.ReactionFlinch,
                    amusementBounce = snapshot.ReactionBounce
                },
                expressiveness = new
                {
                    resolved = snapshot.Expressiveness,
                    amplitudeGain = snapshot.AmplitudeGain,
                    frequencyGain = snapshot.FrequencyGain,
                    richnessGain = snapshot.RichnessGain
                },
                cameraDistanceScale = snapshot.CameraLodScale,
                recentTrace = trace
            };
        }

        private static string[] BuildNextSteps(ConvaiBodyLanguageReport report)
        {
            var steps = new List<string>(4);

            if (report.Preflight.TryGetBlocker(out BodyLanguageCheck blocker))
            {
                steps.Add($"{blocker.Detail}. {RigFixAdvice}");
                return steps.ToArray();
            }

            if (report.UsingSdkDefaults)
            {
                steps.Add(
                    "This character works as it is. To shape it, create a personality with " +
                    $"{CreateProfileMenuPath} and assign it with Convai.ConfigureBodyLanguage — or " +
                    "run Convai.InspectBodyLanguagePersonalities to see the ones this project has.");
            }

            if (!EditorApplication.isPlaying)
            {
                steps.Add(
                    "Press Play and run this again to see the live posture, breathing and weight " +
                    "shifts, and what any other module is doing to the body.");
            }

            if (steps.Count == 0)
                steps.Add("Nothing needs doing — this character is breathing, shifting its weight and gesturing.");

            return steps.ToArray();
        }

        // ------------------------------------------------------------------ shared

        /// <summary>"UpperBody" as the documentation spells it: "Upper Body".</summary>
        private static string Humanize(string pascalCase)
        {
            var builder = new System.Text.StringBuilder(pascalCase.Length + 4);
            for (int i = 0; i < pascalCase.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascalCase[i])) builder.Append(' ');
                builder.Append(pascalCase[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        ///     One finding, addressed two ways: <paramref name="affectedId" /> points at the thing
        ///     that is wrong (usually the Body Language component), while the suggested arguments
        ///     must carry the <em>character</em> id, because that is what
        ///     <c>Convai.ConfigureBodyLanguage</c> takes. An assistant that follows a suggestion built
        ///     from the component id gets INVALID_CHARACTER.
        /// </summary>
        private static object Issue(
            string code,
            string severity,
            string message,
            string evidence,
            long affectedId,
            long characterId,
            bool fixable) =>
            ConvaiMcpResponses.Issue(code, severity, message, evidence, affectedId, fixable, ConfigureTool,
                new { characterInstanceId = characterId, dryRun = true });

        private static object Response(bool success, string message, object data) =>
            ConvaiMcpResponses.Envelope(success, message, data);

        private static object StandardSchema() => ConvaiMcpResponses.StandardResponseSchema(true);
    }

    /// <summary>The same three tools, for Unity's in-editor assistant.</summary>
    public static class ConvaiBodyLanguageAssistantTools
    {
        [AgentTool(
            "Add Convai Body Language to a character and give it an existing personality. Never creates an asset.",
            "Convai.ConfigureBodyLanguage")]
        public static object ConfigureBodyLanguage(
            long characterInstanceId,
            string personalityAssetPath = "",
            bool assignDefaultPersonality = false,
            bool dryRun = true) =>
            ConvaiBodyLanguageMcpTools.Configure(new ConfigureBodyLanguageRequest
            {
                CharacterInstanceId = characterInstanceId,
                PersonalityAssetPath = personalityAssetPath,
                AssignDefaultPersonality = assignDefaultPersonality,
                DryRun = dryRun
            });

        [AgentTool(
            "Explain what a Convai character's body is doing and why it might not be moving.",
            "Convai.DiagnoseBodyLanguage")]
        public static object DiagnoseBodyLanguage(long characterInstanceId = 0, bool includeRuntimeState = true) =>
            ConvaiBodyLanguageMcpTools.Diagnose(new DiagnoseBodyLanguageRequest
            {
                CharacterInstanceId = characterInstanceId,
                IncludeRuntimeState = includeRuntimeState
            });

        [AgentTool(
            "List the Convai Body Language personalities this project has, and which characters use them.",
            "Convai.InspectBodyLanguagePersonalities")]
        public static object InspectBodyLanguagePersonalities(string[] folderPaths = null) =>
            ConvaiBodyLanguageMcpTools.InspectPersonalities(new InspectBodyLanguagePersonalitiesRequest
            {
                FolderPaths = folderPaths ?? Array.Empty<string>()
            });
    }
}
