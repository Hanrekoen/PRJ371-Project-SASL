using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.AI;
using Convai.Editor.Embodiment.Setup;
using Convai.Modules.Embodiment.Components;
using Convai.Modules.Embodiment.Presets;
using Convai.Runtime.Animation;
using Convai.Runtime.Components;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Editor.Embodiment.AI
{
    /// <summary>Input for <c>Convai.ConfigureEmbodiment</c>.</summary>
    public sealed class ConfigureEmbodimentRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool SetUpRig { get; set; } = true;
        public string[] Capabilities { get; set; } = Array.Empty<string>();
        public string PresetAssetPath { get; set; } = string.Empty;
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.DiagnoseEmbodiment</c>.</summary>
    public sealed class DiagnoseEmbodimentRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeCapabilities { get; set; } = true;
        public bool IncludeRuntimeState { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.InspectEmbodimentPresets</c>.</summary>
    public sealed class InspectEmbodimentPresetsRequest
    {
        public string[] FolderPaths { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    ///     Convai Embodiment exposed through Unity's official MCP server: set a character up, see
    ///     which expressive features it actually has, and find out what is stopping the rest.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the layer an assistant reaches for first, before it knows which feature it
    ///         needs. <c>Convai.DiagnoseEmbodiment</c> answers "what is this character and what can it
    ///         do" in one call, and hands back the per-feature tool to call next.
    ///     </para>
    ///     <para>
    ///         Every verdict comes from <see cref="EmbodimentRigSetupService" />,
    ///         <see cref="EmbodimentModuleCatalog" />, <see cref="EmbodimentPresetTroubleshooter" />
    ///         or the feature's own registered surveyor — the same code every Convai editor surface
    ///         draws. This class contains no check of its own, so an assistant and the editor cannot
    ///         describe the same character differently.
    ///     </para>
    ///     <para>
    ///         Nothing here creates or edits an asset. Presets and settings assets are authored by the
    ///         user; when one is missing, the response names the menu path that makes it.
    ///     </para>
    /// </remarks>
    public static class ConvaiEmbodimentMcpTools
    {
        private const string ConfigureTool = "Convai.ConfigureEmbodiment";
        private const string DiagnoseTool = "Convai.DiagnoseEmbodiment";
        private const string PresetsTool = "Convai.InspectEmbodimentPresets";

        private const string AddCharacterAdvice =
            "Add Convai Character to the model's root with Add Component → Convai → Convai Character, " +
            "or call Convai.ConfigureCharacter. Every expressive feature hangs off it.";

        // ------------------------------------------------------------------ configure

        [McpTool(
            ConfigureTool,
            "Sets a Convai character up so it can come alive: works out its rig now instead of at runtime, adds the expressive features you name — whichever of Gaze, Emotion, Body Animation, Body Language and Conversation Flow this project has installed — and assigns an Embodiment Preset the project already has. Previews by default. Never creates or edits an asset, and never tunes a feature; each feature has its own Configure tool for that.",
            "Set Up Convai Character Embodiment",
            Groups = new[] { "convai", "embodiment" },
            EnabledByDefault = true)]
        public static object Configure(JObject input) =>
            Configure(input?.ToObject<ConfigureEmbodimentRequest>());

        public static object Configure(ConfigureEmbodimentRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false,
                    "Embodiment can only be set up in Edit Mode. Leave Play Mode and call this "
                    + "again — or call Convai.DiagnoseEmbodiment, which reads a character in either "
                    + "mode and reports what it is doing right now.",
                    new
                    {
                        code = "PLAY_MODE_ACTIVE",
                        nextSteps = new[]
                        {
                            "Leave Play Mode, then call Convai.ConfigureEmbodiment again.",
                            "To read the character without leaving Play Mode, call Convai.DiagnoseEmbodiment."
                        }
                    });

            request ??= new ConfigureEmbodimentRequest();

            if (!TryResolveCharacter(request.CharacterInstanceId, out ConvaiCharacter character,
                    out object failure))
                return failure;

            if (!TryResolveCapabilities(request.Capabilities, out List<EmbodimentModuleDescriptor> wanted,
                    out string unknown))
            {
                return Response(false,
                    $"'{unknown}' is not a Convai expressive feature this project has installed. "
                    + "Name one from validCapabilities below, or leave capabilities empty to set up "
                    + "the rig alone.",
                    new
                    {
                        code = "UNKNOWN_CAPABILITY",
                        requiredInputs = new[] { "capabilities" },
                        nextSteps = new[]
                        {
                            "Call again with a name from validCapabilities.",
                            "A feature missing from that list is not installed in this project; "
                            + "install its module before naming it."
                        },
                        validCapabilities = DescribeCatalog()
                    });
            }

            ConvaiEmbodimentPreset preset = null;
            if (!string.IsNullOrWhiteSpace(request.PresetAssetPath))
            {
                preset = AssetDatabase.LoadAssetAtPath<ConvaiEmbodimentPreset>(request.PresetAssetPath);
                if (preset == null)
                {
                    return Response(false,
                        $"'{request.PresetAssetPath}' is not an Embodiment Preset asset.",
                        new
                        {
                            code = "INVALID_PRESET",
                            requiredInputs = new[] { "presetAssetPath" },
                            createPresetMenuPath = ConvaiEmbodimentReport.CreatePresetMenuPath,
                            nextSteps = new[]
                            {
                                "Run Convai.InspectEmbodimentPresets to see the presets this project has.",
                                "A preset is optional — every feature also works from the settings " +
                                "assigned on its own component."
                            }
                        });
                }
            }

            GameObject root = character.gameObject;
            ConvaiEmbodimentReport before = ConvaiEmbodimentReport.For(character);

            var changes = new List<string>(6);
            var notes = new List<string>(4);
            var capabilityPlan = new List<(EmbodimentModuleDescriptor Descriptor, bool Add)>(wanted.Count);

            bool willSetUpRig = request.SetUpRig && !before.Rig.HasBlocker;
            if (request.SetUpRig && before.Rig.HasBlocker)
            {
                notes.Add("The rig could not be set up: " + FirstBlockerMessage(before.Rig));
            }
            else if (willSetUpRig)
            {
                changes.Add(before.RigBinding == null
                    ? "Add the Character Rig component and work out this character's bones and face meshes"
                    : "Work out this character's bones and face meshes again");
            }

            for (int i = 0; i < wanted.Count; i++)
            {
                EmbodimentModuleDescriptor descriptor = wanted[i];
                bool present = root.GetComponentInChildren(descriptor.ControllerType, true) != null;
                capabilityPlan.Add((descriptor, !present));

                if (present)
                {
                    // Conversation Flow has no tools of its own, so naming one would be worse than
                    // saying nothing — the note stops at the fact rather than pointing nowhere.
                    string configureTool = ConvaiEmbodimentCapabilityTools.Configure(descriptor.ModuleId);
                    notes.Add(string.IsNullOrEmpty(configureTool)
                        ? $"{descriptor.DisplayName} is already on this character, so it was left alone."
                        : $"{descriptor.DisplayName} is already on this character, so it was left "
                          + $"alone. Use {configureTool} to change its settings.");
                }
                else
                    changes.Add($"Add {descriptor.DisplayName}");
            }

            bool assignPreset = preset != null && before.Preset != preset;
            if (assignPreset)
            {
                changes.Add(before.PresetBinding == null
                    ? $"Add the Preset component and assign '{preset.name}'"
                    : $"Assign the '{preset.name}' preset");
            }
            else if (preset != null)
            {
                notes.Add($"The '{preset.name}' preset is already assigned to this character.");
            }

            if (request.DryRun)
            {
                // Everything below `changes` is measured from the character as it stands, because
                // nothing has been done to it yet. Saying so is the difference between a preview and
                // a prediction a caller cannot tell apart from one.
                notes.Add(
                    "This is a preview. 'changes' is what applying would do; 'rig', 'preset', " +
                    "'complete' and 'nextSteps' describe the character as it is now. Call again " +
                    "with dryRun false to apply.");
                return ConfigureResponse(true, character, changes, notes, capabilityPlan, preset);
            }

            if (changes.Count == 0)
                return ConfigureResponse(false, character, changes, notes, capabilityPlan, preset);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                // The rig service owns rig setup, including naming and collapsing its own undo step.
                // Running it first and setting the group name afterwards leaves one collapsed entry
                // with this tool's name on it.
                if (willSetUpRig)
                {
                    EmbodimentRigSetupResult result = EmbodimentRigSetupService.Apply(root);
                    if (!string.IsNullOrEmpty(result.Summary)) notes.Add(result.Summary);
                }

                for (int i = 0; i < capabilityPlan.Count; i++)
                {
                    if (!capabilityPlan[i].Add) continue;
                    Undo.AddComponent(root, capabilityPlan[i].Descriptor.ControllerType);
                }

                if (assignPreset)
                {
                    ConvaiEmbodimentPresetBinding binding = before.PresetBinding != null
                        ? before.PresetBinding
                        : Undo.AddComponent<ConvaiEmbodimentPresetBinding>(root);
                    Undo.RecordObject(binding, "Set Up Convai Embodiment");
                    var serialized = new SerializedObject(binding);
                    serialized.FindProperty("preset").objectReferenceValue = preset;
                    serialized.ApplyModifiedProperties();
                }

                EditorSceneManager.MarkSceneDirty(root.scene);
                Undo.SetCurrentGroupName("Set Up Convai Embodiment");
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return Response(false,
                    $"Setting this character up failed, and every change was rolled back: {exception.Message}",
                    new
                    {
                        code = "AUTHORING_FAILED",
                        nextSteps = new[]
                        {
                            "The character is as it was — nothing was left half-applied.",
                            "Call Convai.DiagnoseEmbodiment to see what state it is in, then try again."
                        }
                    });
            }

            return ConfigureResponse(false, character, changes, notes, capabilityPlan, preset);
        }

        [McpSchema(ConfigureTool)]
        public static object ConfigureSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character to set up. 0 uses the only one in the active scene."),
                ["setUpRig"] = ConvaiMcpResponses.BooleanProperty(
                    "Add the Character Rig component and work out the character's bones and face " +
                    "meshes now. Convai does this automatically at runtime either way; doing it now " +
                    "is how the result becomes visible and correctable.", true),
                ["capabilities"] = ConvaiMcpResponses.ArrayProperty(
                    "Which expressive features to add, by the name a user sees — 'Gaze', 'Emotion', " +
                    "'Body Animation', 'Body Language', 'Conversation Flow'. Only features this " +
                    "project has installed can be named; call this tool with an unknown one and the " +
                    "error lists what is available. Features already on the character are left " +
                    "alone. Adds the component only; use the feature's own Configure tool to tune it.",
                    ConvaiMcpResponses.StringSchema()),
                ["presetAssetPath"] = ConvaiMcpResponses.StringProperty(
                    "Project path of an existing Embodiment Preset to assign, which hands one " +
                    "settings asset to each feature at once. Never creates one. Presets are optional."),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the changes without touching the scene.", true)
            },
            "characterInstanceId");

        [McpOutputSchema(ConfigureTool)]
        public static object ConfigureOutput() => StandardSchema();

        // ------------------------------------------------------------------- diagnose

        [McpTool(
            DiagnoseTool,
            "Surveys one Convai character end to end: whether its rig is understood, which expressive features it has, which of them will actually do something and which are blocked or inert and why, which settings assets they run on, its Embodiment Preset, and — in Play Mode — what it is doing right now. Call this before any other Convai embodiment tool; it names the per-feature tool to call next. Read-only.",
            "Diagnose Convai Character Embodiment",
            Groups = new[] { "convai", "embodiment", "validation" },
            EnabledByDefault = true)]
        public static object Diagnose(JObject input) =>
            Diagnose(input?.ToObject<DiagnoseEmbodimentRequest>());

        public static object Diagnose(DiagnoseEmbodimentRequest request)
        {
            request ??= new DiagnoseEmbodimentRequest();

            if (!TryResolveCharacter(request.CharacterInstanceId, out ConvaiCharacter character,
                    out object failure))
                return failure;

            ConvaiEmbodimentReport report = ConvaiEmbodimentReport.For(character);
            long characterId = ConvaiMcpEntityRef.ToToolId(character.gameObject);

            var issues = new List<object>(8);
            CollectRigIssues(report, characterId, issues);
            CollectCapabilityIssues(report, characterId, issues);
            CollectPresetIssues(report, characterId, issues);

            return Response(true,
                report.Readiness == ConvaiCapabilityReadiness.Working
                    ? $"'{character.gameObject.name}' is set up."
                    : $"'{character.gameObject.name}': {report.Summary}",
                new
                {
                    characterInstanceId = characterId,
                    characterName = character.gameObject.name,
                    characterId = character.CharacterId,
                    readiness = report.Readiness.ToString(),
                    summary = report.Summary,
                    rig = DescribeRig(report),
                    capabilities = DescribeCapabilities(report, request.IncludeCapabilities),
                    preset = DescribePreset(report),
                    runtime = request.IncludeRuntimeState ? DescribeRuntime(report) : null,
                    issues,
                    nextSteps = BuildNextSteps(report)
                });
        }

        [McpSchema(DiagnoseTool)]
        public static object DiagnoseSchema() => ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerSchema(0),
                ["includeCapabilities"] = ConvaiMcpResponses.BooleanSchema(true),
                ["includeRuntimeState"] = ConvaiMcpResponses.BooleanSchema(true)
            });

        [McpOutputSchema(DiagnoseTool)]
        public static object DiagnoseOutput() => StandardSchema();

        // -------------------------------------------------------------------- presets

        [McpTool(
            PresetsTool,
            "Lists the Embodiment Presets this project has and whether each one is valid, so a preset can be assigned to a character without guessing which assets exist. Also lists every expressive feature a preset can carry and the menu path that creates its settings asset. Read-only, and never creates or edits an asset.",
            "Inspect Convai Embodiment Presets",
            Groups = new[] { "convai", "embodiment" },
            EnabledByDefault = true)]
        public static object InspectPresets(JObject input) =>
            InspectPresets(input?.ToObject<InspectEmbodimentPresetsRequest>());

        public static object InspectPresets(InspectEmbodimentPresetsRequest request)
        {
            request ??= new InspectEmbodimentPresetsRequest();

            string[] folders = request.FolderPaths != null && request.FolderPaths.Length > 0
                ? request.FolderPaths
                : null;

            string[] guids;
            try
            {
                guids = folders == null
                    ? AssetDatabase.FindAssets("t:ConvaiEmbodimentPreset")
                    : AssetDatabase.FindAssets("t:ConvaiEmbodimentPreset", folders);
            }
            catch (Exception exception)
            {
                return Response(false,
                    $"One of the folder paths could not be searched: {exception.Message}",
                    new
                    {
                        code = "INVALID_FOLDER",
                        requiredInputs = new[] { "folderPaths" },
                        nextSteps = new[]
                        {
                            "Give project-relative folder paths, for example 'Assets/Convai'.",
                            "Leave folderPaths empty to search the whole project."
                        }
                    });
            }

            var presets = new List<object>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var preset = AssetDatabase.LoadAssetAtPath<ConvaiEmbodimentPreset>(path);
                if (preset == null) continue;

                EmbodimentSetupReport report = EmbodimentPresetTroubleshooter.Evaluate(preset);
                presets.Add(new
                {
                    assetPath = path,
                    name = preset.name,
                    presetId = preset.PresetId,
                    status = report.HeaderStatus,
                    entryCount = preset.ProfileSlots?.Count ?? 0,
                    entries = DescribePresetEntries(preset),
                    findings = DescribeFindings(report)
                });
            }

            return Response(true,
                presets.Count == 0
                    ? "This project has no Embodiment Presets."
                    : presets.Count == 1
                        ? "Found 1 Embodiment Preset."
                        : $"Found {presets.Count} Embodiment Presets.",
                new
                {
                    presets,
                    capabilities = DescribeCatalog(),
                    createPresetMenuPath = ConvaiEmbodimentReport.CreatePresetMenuPath,
                    addPresetComponentMenuPath = ConvaiEmbodimentReport.AddPresetComponentMenuPath,
                    nextSteps = presets.Count == 0
                        ? new[]
                        {
                            "A preset is optional. Every feature works from the settings asset " +
                            "assigned on its own component, and from Convai's built-in defaults when " +
                            "there is none.",
                            $"Create one with {ConvaiEmbodimentReport.CreatePresetMenuPath} if several " +
                            "characters should share one personality."
                        }
                        : new[]
                        {
                            "Assign one with Convai.ConfigureEmbodiment and presetAssetPath.",
                            "One preset can be shared by any number of characters that should behave alike."
                        }
                });
        }

        [McpSchema(PresetsTool)]
        public static object InspectPresetsSchema() => ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
            new Dictionary<string, object>
            {
                ["folderPaths"] = ConvaiMcpResponses.ArrayProperty(
                    "Project folders to search, e.g. 'Assets/Characters'. Omit to search the whole project.",
                    ConvaiMcpResponses.StringSchema())
            });

        [McpOutputSchema(PresetsTool)]
        public static object InspectPresetsOutput() => StandardSchema();

        // -------------------------------------------------------------- shared reading

        /// <summary>
        ///     Resolves the character, and when it cannot, says what to do about it. The shared
        ///     resolver's message assumes the reader knows what a <c>ConvaiCharacter</c> is; someone
        ///     asking an assistant to set a character up usually does not.
        /// </summary>
        private static bool TryResolveCharacter(
            long instanceId, out ConvaiCharacter character, out object failure)
        {
            if (ConvaiMcpResolvers.TryCharacter(instanceId, true, out character, out string error))
            {
                failure = null;
                return true;
            }

            if (instanceId != 0 &&
                ConvaiMcpEntityRef.TryResolve(instanceId, out GameObject target) &&
                target != null &&
                target.scene == SceneManager.GetActiveScene())
            {
                failure = Response(false,
                    $"'{target.name}' has no Convai Character component, so Convai will not drive it.",
                    new
                    {
                        code = "NOT_A_CONVAI_CHARACTER",
                        requiredInputs = new[] { "characterInstanceId" },
                        advice = AddCharacterAdvice,
                        nextSteps = new[]
                        {
                            AddCharacterAdvice,
                            "Then call this tool again with that object's instance id."
                        }
                    });
                return false;
            }

            failure = Response(false, error, new
            {
                code = ConvaiMcpResolvers.CharacterErrorCode,
                requiredInputs = new[] { "characterInstanceId" },
                nextSteps = new[]
                {
                    "Run Convai.InspectScene to list the Convai characters in the open scenes and " +
                    "their instance ids."
                }
            });
            return false;
        }

        /// <summary>
        ///     Matches requested feature names against the catalog, accepting either the label a user
        ///     reads ("Body Animation") or the stable id ("convai.body-animation").
        /// </summary>
        private static bool TryResolveCapabilities(
            string[] requested, out List<EmbodimentModuleDescriptor> resolved, out string unknown)
        {
            resolved = new List<EmbodimentModuleDescriptor>(requested?.Length ?? 0);
            unknown = string.Empty;
            if (requested == null) return true;

            for (int i = 0; i < requested.Length; i++)
            {
                string name = requested[i];
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!TryMatchCapability(name.Trim(), out EmbodimentModuleDescriptor descriptor))
                {
                    unknown = name;
                    return false;
                }

                // Compared by id, not by struct equality: the default ValueType comparison would
                // reflect over every field, including two Types and five strings, to answer a
                // question the id already answers.
                bool duplicate = false;
                for (int seen = 0; seen < resolved.Count; seen++)
                {
                    if (!string.Equals(resolved[seen].ModuleId, descriptor.ModuleId,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    duplicate = true;
                    break;
                }

                if (!duplicate) resolved.Add(descriptor);
            }

            return true;
        }

        private static bool TryMatchCapability(string name, out EmbodimentModuleDescriptor descriptor)
        {
            if (EmbodimentModuleCatalog.TryGet(name, out descriptor)) return true;

            IReadOnlyList<EmbodimentModuleDescriptor> all = EmbodimentModuleCatalog.Modules;
            for (int i = 0; i < all.Count; i++)
            {
                if (!string.Equals(all[i].DisplayName, name, StringComparison.OrdinalIgnoreCase)) continue;
                descriptor = all[i];
                return true;
            }

            descriptor = default;
            return false;
        }

        // ------------------------------------------------------------ response shaping

        private static object ConfigureResponse(
            bool dryRun,
            ConvaiCharacter character,
            List<string> changes,
            List<string> notes,
            List<(EmbodimentModuleDescriptor Descriptor, bool Add)> plan,
            ConvaiEmbodimentPreset preset)
        {
            ConvaiEmbodimentReport after = ConvaiEmbodimentReport.For(character);
            var capabilities = new List<object>(plan.Count);
            for (int i = 0; i < plan.Count; i++)
            {
                EmbodimentModuleDescriptor descriptor = plan[i].Descriptor;
                capabilities.Add(new
                {
                    moduleId = descriptor.ModuleId,
                    name = descriptor.DisplayName,
                    action = plan[i].Add ? dryRun ? "WouldAdd" : "Added" : "AlreadyPresent",
                    configureTool = ConvaiEmbodimentCapabilityTools.Configure(descriptor.ModuleId),
                    diagnoseTool = ConvaiEmbodimentCapabilityTools.Diagnose(descriptor.ModuleId)
                });
            }

            return Response(true,
                dryRun
                    ? "Previewed Convai embodiment setup."
                    : changes.Count == 0
                        ? "Convai embodiment setup was already satisfied."
                        : "Set up Convai embodiment on this character.",
                new
                {
                    dryRun,
                    complete = !after.Rig.HasBlocker,
                    changes,
                    notes,
                    warnings = Array.Empty<string>(),
                    requiredInputs = Array.Empty<string>(),
                    rig = new
                    {
                        status = after.Rig.HeaderStatus,
                        characterRigComponentPresent = after.RigBinding != null
                    },
                    capabilities,
                    preset = new
                    {
                        assetPath = after.Preset != null ? AssetDatabase.GetAssetPath(after.Preset) : string.Empty,
                        requested = preset != null ? preset.name : string.Empty,
                        findings = DescribeFindings(after.PresetReport)
                    },
                    affectedInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps = BuildNextSteps(after)
                });
        }

        private static object DescribeRig(in ConvaiEmbodimentReport report)
        {
            StandardRigBinding binding = report.RigBinding;
            return new
            {
                status = report.Rig.HeaderStatus,
                characterRigComponentPresent = binding != null,
                hasAnimator = report.Animator != null,
                isHumanoid = report.Animator != null && report.Animator.isHuman,
                faceConvention = binding != null ? binding.DetectedConvention.ToString() : string.Empty,
                faceConventionConfidence = binding != null ? binding.DetectionConfidence : 0f,
                faceMeshCount = binding?.FacialMeshes?.Count ?? 0,
                bones = new
                {
                    head = BoneName(binding, StandardBone.Head),
                    leftEye = BoneName(binding, StandardBone.LeftEye),
                    rightEye = BoneName(binding, StandardBone.RightEye)
                },
                findings = DescribeFindings(report.Rig)
            };
        }

        private static string BoneName(StandardRigBinding binding, StandardBone bone) =>
            binding != null && binding.TryGetBone(bone, out Transform resolved) && resolved != null
                ? resolved.name
                : string.Empty;

        private static object[] DescribeCapabilities(in ConvaiEmbodimentReport report, bool includeFindings)
        {
            IReadOnlyList<ConvaiEmbodimentCapability> capabilities = report.Capabilities;
            var described = new object[capabilities.Count];
            for (int i = 0; i < capabilities.Count; i++)
            {
                ConvaiEmbodimentCapability capability = capabilities[i];
                described[i] = new
                {
                    moduleId = capability.Descriptor.ModuleId,
                    name = capability.Descriptor.DisplayName,
                    description = capability.Descriptor.Description,
                    readiness = capability.Readiness.ToString(),
                    summary = capability.Summary,
                    blocker = capability.Blocker,
                    withoutIt = capability.Descriptor.Absence,
                    settingsAsset = capability.SettingsAssetPath,
                    componentInstanceId = capability.Component != null
                        ? ConvaiMcpEntityRef.ToToolId(capability.Component)
                        : 0L,
                    addComponentMenuPath = capability.AddComponentMenuPath,
                    createSettingsMenuPath = capability.Descriptor.CreateProfileMenuPath,
                    configureTool = capability.ConfigureTool,
                    diagnoseTool = capability.DiagnoseTool,
                    findings = includeFindings ? DescribeFindings(capability.Findings) : null
                };
            }

            return described;
        }

        private static object DescribePreset(in ConvaiEmbodimentReport report) => new
        {
            componentPresent = report.PresetBinding != null,
            present = report.HasPreset,
            assetPath = report.Preset != null ? AssetDatabase.GetAssetPath(report.Preset) : string.Empty,
            name = report.Preset != null ? report.Preset.name : string.Empty,
            status = report.PresetBinding != null ? report.PresetReport.HeaderStatus : string.Empty,
            findings = DescribeFindings(report.PresetReport),
            createPresetMenuPath = ConvaiEmbodimentReport.CreatePresetMenuPath,
            note = "A preset is optional. Each feature also reads the settings assigned on its own component."
        };

        /// <summary>
        ///     What the character is doing right now, read through the same service the Convai
        ///     Convai editor surfaces show while a scene is playing. <c>null</c> outside Play Mode, which is the honest
        ///     answer rather than a block of zeroes.
        /// </summary>
        private static object DescribeRuntime(in ConvaiEmbodimentReport report)
        {
            if (!UnityEngine.Application.isPlaying || report.Root == null) return null;

            EmbodimentLiveState live = EmbodimentLiveStateService.Read(report.Root);
            if (!live.HasConversationFlow && !live.HasEmotion) return null;

            DialogueStateReading conversation = live.Conversation;
            var emotions = new List<object>(Mathf.Min(5, live.Emotions.Count));
            for (int i = 0; i < live.Emotions.Count && i < 5; i++)
                emotions.Add(new { label = live.Emotions[i].Label, score = live.Emotions[i].Score });

            return new
            {
                conversationState = live.HasConversationFlow ? conversation.Primary.ToString() : string.Empty,
                blendingTo = live.HasConversationFlow ? conversation.BlendTo.ToString() : string.Empty,
                blendWeight = live.HasConversationFlow ? conversation.BlendWeight : 0f,
                timeInStateSeconds = live.HasConversationFlow ? conversation.TimeInState : 0f,
                energy = live.HasConversationFlow ? conversation.EnergyLevel : 0f,
                topEmotions = emotions
            };
        }

        private static object[] DescribePresetEntries(ConvaiEmbodimentPreset preset)
        {
            IReadOnlyList<EmbodimentProfileSlot> slots = preset.ProfileSlots;
            if (slots == null) return Array.Empty<object>();

            var entries = new object[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                EmbodimentProfileSlot slot = slots[i];
                entries[i] = new
                {
                    capability = slot == null
                        ? string.Empty
                        : EmbodimentModuleCatalog.DescribeModule(slot.ModuleId),
                    moduleId = slot?.ModuleId ?? string.Empty,
                    settingsAsset = slot?.Profile != null ? AssetDatabase.GetAssetPath(slot.Profile) : string.Empty
                };
            }

            return entries;
        }

        /// <summary>Every feature a preset can carry, and where its settings asset comes from.</summary>
        private static object[] DescribeCatalog()
        {
            IReadOnlyList<EmbodimentModuleDescriptor> all = EmbodimentModuleCatalog.Modules;
            var described = new object[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                described[i] = new
                {
                    moduleId = all[i].ModuleId,
                    name = all[i].DisplayName,
                    description = all[i].Description,
                    withoutIt = all[i].Absence,
                    settingsAssetType = all[i].ProfileType?.Name ?? string.Empty,
                    createSettingsMenuPath = all[i].CreateProfileMenuPath
                };
            }

            return described;
        }

        private static object[] DescribeFindings(in EmbodimentSetupReport report)
        {
            IReadOnlyList<EmbodimentFinding> findings = report.Findings;
            var described = new object[findings.Count];
            for (int i = 0; i < findings.Count; i++)
            {
                described[i] = new
                {
                    id = findings[i].Id,
                    severity = findings[i].Severity.ToString(),
                    title = findings[i].Title,
                    message = findings[i].Message
                };
            }

            return described;
        }

        private static object[] DescribeFindings(IReadOnlyList<ConvaiModuleSurveyFinding> findings)
        {
            var described = new object[findings.Count];
            for (int i = 0; i < findings.Count; i++)
            {
                described[i] = new
                {
                    severity = findings[i].Severity.ToString(),
                    title = findings[i].Title,
                    message = findings[i].Message
                };
            }

            return described;
        }

        // -------------------------------------------------------------------- issues

        private static void CollectRigIssues(
            in ConvaiEmbodimentReport report, long characterId, List<object> issues)
        {
            IReadOnlyList<EmbodimentFinding> findings = report.Rig.Findings;
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity < EmbodimentFindingSeverity.Warning) continue;

                issues.Add(ConvaiMcpResponses.Issue(
                    "EMBODIMENT_RIG_" + Normalize(findings[i].Id),
                    findings[i].Severity.ToString(),
                    findings[i].Message,
                    findings[i].Title,
                    characterId,
                    findings[i].CanFix,
                    ConfigureTool,
                    new { characterInstanceId = characterId, setUpRig = true, dryRun = true }));
            }
        }

        private static void CollectCapabilityIssues(
            in ConvaiEmbodimentReport report, long characterId, List<object> issues)
        {
            IReadOnlyList<ConvaiEmbodimentCapability> capabilities = report.Capabilities;
            for (int i = 0; i < capabilities.Count; i++)
            {
                ConvaiEmbodimentCapability capability = capabilities[i];

                // An absent feature is a choice, not a fault: a character that should not gesture is
                // correctly configured without Body Animation. So an absent feature raises nothing —
                // but everything on the character does, including a feature that works and still has
                // something worth acting on.
                if (!capability.IsPresent) continue;

                long affected = capability.Component != null
                    ? ConvaiMcpEntityRef.ToToolId(capability.Component)
                    : characterId;
                string tool = string.IsNullOrEmpty(capability.DiagnoseTool)
                    ? DiagnoseTool
                    : capability.DiagnoseTool;
                string blocker = string.IsNullOrEmpty(capability.Blocker)
                    ? capability.Summary
                    : capability.Blocker;

                if (!capability.IsWorking)
                {
                    issues.Add(ConvaiMcpResponses.Issue(
                        $"EMBODIMENT_CAPABILITY_{Normalize(capability.Descriptor.ModuleId)}_" +
                        capability.Readiness.ToString().ToUpperInvariant(),
                        capability.Readiness == ConvaiCapabilityReadiness.Blocked ? "Error" : "Warning",
                        blocker,
                        capability.Descriptor.DisplayName,
                        affected, false, tool,
                        new { characterInstanceId = characterId }));
                }

                // A feature can be working and still be worth acting on — a rig that resolved only
                // one eye, say. Dropping those because the readiness word was Working is how a real
                // warning becomes invisible to the one tool an assistant calls first.
                IReadOnlyList<ConvaiModuleSurveyFinding> findings = capability.Findings;
                for (int f = 0; f < findings.Count; f++)
                {
                    if (findings[f].Severity < ConvaiModuleFindingSeverity.Warning) continue;

                    // The readiness row above already said this, in the feature's own words.
                    if (!capability.IsWorking &&
                        string.Equals(findings[f].Message, blocker, StringComparison.Ordinal)) continue;

                    issues.Add(ConvaiMcpResponses.Issue(
                        $"EMBODIMENT_CAPABILITY_{Normalize(capability.Descriptor.ModuleId)}_" +
                        Normalize(findings[f].Title),
                        findings[f].Severity.ToString(),
                        findings[f].Message,
                        $"{capability.Descriptor.DisplayName} — {findings[f].Title}",
                        affected, false, tool,
                        new { characterInstanceId = characterId }));
                }
            }
        }

        private static void CollectPresetIssues(
            in ConvaiEmbodimentReport report, long characterId, List<object> issues)
        {
            if (report.PresetBinding == null) return;

            IReadOnlyList<EmbodimentFinding> findings = report.PresetReport.Findings;
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity < EmbodimentFindingSeverity.Warning) continue;

                issues.Add(ConvaiMcpResponses.Issue(
                    "EMBODIMENT_PRESET_" + Normalize(findings[i].Id),
                    findings[i].Severity.ToString(),
                    findings[i].Message,
                    findings[i].Title,
                    ConvaiMcpEntityRef.ToToolId(report.PresetBinding),
                    findings[i].CanFix,
                    PresetsTool,
                    new { }));
            }
        }

        /// <summary>
        ///     A finding id or module id turned into a stable issue code — <c>rig.bone-missing.Head</c>
        ///     becomes <c>RIG_BONE_MISSING_HEAD</c>. Derived rather than tabulated, so the codes an
        ///     assistant sees stay a projection of the one finding engine.
        /// </summary>
        private static string Normalize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "ISSUE";

            var builder = new System.Text.StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char character = id[i];
                builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_');
            }

            return builder.ToString();
        }

        private static string FirstBlockerMessage(in EmbodimentSetupReport report)
        {
            IReadOnlyList<EmbodimentFinding> findings = report.Findings;
            for (int i = 0; i < findings.Count; i++)
                if (findings[i].Severity == EmbodimentFindingSeverity.Error)
                    return findings[i].Message;

            return string.Empty;
        }

        /// <summary>
        ///     What to do next, cheapest and most-blocking first. Written for someone who has not read
        ///     our documentation: every step names a component, a menu path or a tool.
        /// </summary>
        private static string[] BuildNextSteps(in ConvaiEmbodimentReport report)
        {
            var steps = new List<string>(5);

            if (report.Rig.HasBlocker)
            {
                steps.Add(FirstBlockerMessage(report.Rig));
                return steps.ToArray();
            }

            IReadOnlyList<ConvaiEmbodimentCapability> capabilities = report.Capabilities;

            for (int i = 0; i < capabilities.Count; i++)
            {
                if (capabilities[i].Readiness != ConvaiCapabilityReadiness.Blocked) continue;
                steps.Add($"{capabilities[i].Descriptor.DisplayName}: {capabilities[i].Blocker}");
            }

            for (int i = 0; i < capabilities.Count; i++)
            {
                if (capabilities[i].Readiness != ConvaiCapabilityReadiness.Inert) continue;
                steps.Add($"{capabilities[i].Descriptor.DisplayName}: {capabilities[i].Blocker}");
            }

            if (report.Rig.WorstSeverity == EmbodimentFindingSeverity.Warning)
            {
                steps.Add(
                    "Check the rig findings above. A face rig Convai could not recognize confidently " +
                    "is the usual cause of \"expression does nothing\" — set the convention manually " +
                    "on the Character Rig component, or supply a Custom Rig Convention Map.");
            }

            // A feature that works and still has a warning is the case a "nothing is blocking this
            // character" summary would hide.
            for (int i = 0; i < capabilities.Count; i++)
            {
                if (!capabilities[i].IsPresent || !capabilities[i].IsWorking) continue;

                IReadOnlyList<ConvaiModuleSurveyFinding> findings = capabilities[i].Findings;
                for (int f = 0; f < findings.Count; f++)
                {
                    if (findings[f].Severity < ConvaiModuleFindingSeverity.Warning) continue;
                    steps.Add($"{capabilities[i].Descriptor.DisplayName}: {findings[f].Message}");
                }
            }

            int present = 0;
            for (int i = 0; i < capabilities.Count; i++)
                if (capabilities[i].IsPresent) present++;

            if (present == 0)
            {
                steps.Add(
                    "This character has no expressive features yet. Add the ones you want with " +
                    "Convai.ConfigureEmbodiment, or Add Component → Convai → Embodiment. Each one is " +
                    "optional and works on its own.");
            }

            if (steps.Count == 0)
            {
                steps.Add(
                    "Nothing is blocking this character. Tune an individual feature with its own " +
                    "Configure tool, or open Convai → Troubleshooter to see what is still missing.");
            }

            return steps.ToArray();
        }

        private static object Response(bool success, string message, object data) =>
            ConvaiMcpResponses.Envelope(success, message, data);

        private static object StandardSchema() => ConvaiMcpResponses.StandardResponseSchema(true);
    }

    /// <summary>Unity Assistant wrappers for the Convai Embodiment tools.</summary>
    public static class ConvaiEmbodimentAssistantTools
    {
        [AgentTool(
            "Set a Convai character up: work out its rig, add the expressive features you name, and assign an existing Embodiment Preset. Never creates an asset.",
            "Convai.ConfigureEmbodiment")]
        public static object ConfigureEmbodiment(
            long characterInstanceId,
            bool setUpRig = true,
            string[] capabilities = null,
            string presetAssetPath = "",
            bool dryRun = true) =>
            ConvaiEmbodimentMcpTools.Configure(new ConfigureEmbodimentRequest
            {
                CharacterInstanceId = characterInstanceId,
                SetUpRig = setUpRig,
                Capabilities = capabilities ?? Array.Empty<string>(),
                PresetAssetPath = presetAssetPath,
                DryRun = dryRun
            });

        [AgentTool(
            "Survey one Convai character: its rig, which expressive features it has, which are working, blocked or inert and why, its preset, and its live state in Play Mode.",
            "Convai.DiagnoseEmbodiment")]
        public static object DiagnoseEmbodiment(
            long characterInstanceId = 0,
            bool includeCapabilities = true,
            bool includeRuntimeState = true) =>
            ConvaiEmbodimentMcpTools.Diagnose(new DiagnoseEmbodimentRequest
            {
                CharacterInstanceId = characterInstanceId,
                IncludeCapabilities = includeCapabilities,
                IncludeRuntimeState = includeRuntimeState
            });

        [AgentTool(
            "List the Embodiment Presets this project has and whether each one is valid.",
            "Convai.InspectEmbodimentPresets")]
        public static object InspectEmbodimentPresets(string[] folderPaths = null) =>
            ConvaiEmbodimentMcpTools.InspectPresets(new InspectEmbodimentPresetsRequest
            {
                FolderPaths = folderPaths ?? Array.Empty<string>()
            });
    }
}
