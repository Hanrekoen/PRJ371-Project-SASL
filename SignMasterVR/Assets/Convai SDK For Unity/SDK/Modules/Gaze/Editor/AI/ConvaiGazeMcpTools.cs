using System;
using System.Collections.Generic;
using System.Text;
using Convai.Editor.AI;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Core;
using Convai.Modules.Gaze.Core.Diagnostics;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Providers;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Modules.Gaze.Editor.AI
{
    /// <summary>A point in space, as an assistant most naturally writes one.</summary>
    public sealed class GazePoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        internal Vector3 ToVector3() => new(X, Y, Z);
    }

    /// <summary>
    ///     Input for <c>Convai.ConfigureGaze</c>. Every tuning field is optional, and omitting one
    ///     leaves that setting exactly as the project authored it.
    /// </summary>
    public sealed class ConfigureGazeRequest
    {
        public long CharacterInstanceId { get; set; }
        public GazeEyeContactMode? EyeContactMode { get; set; }
        public GazeFocusFidelity? FocusFidelity { get; set; }
        public long? PlayerAnchorInstanceId { get; set; }
        public bool? ClearPlayerAnchorOverride { get; set; }
        public GazeAnchorAimMode? PlayerAnchorAimMode { get; set; }
        public GazePoint PlayerAnchorAimOffset { get; set; }
        public GazeBodyTurnStyle? BodyTurnStyle { get; set; }
        public bool? AllowScriptedOverrides { get; set; }
        public bool? LockBlocksGlances { get; set; }
        public bool? AutoCreatePlayerAnchor { get; set; }
        public string ProfileAssetPath { get; set; }
        public string[] Capabilities { get; set; }
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.DiagnoseGaze</c>.</summary>
    public sealed class DiagnoseGazeRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeRuntimeState { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.MarkGazeTarget</c>.</summary>
    public sealed class MarkGazeTargetRequest
    {
        public long[] GameObjectInstanceIds { get; set; } = Array.Empty<long>();
        public int? Priority { get; set; }
        public float? BaseRelevance { get; set; }
        public float? MaxDistance { get; set; }
        public float? FullRelevanceDistance { get; set; }
        public GazePoint AimOffset { get; set; }
        public bool Remove { get; set; }
        public bool DryRun { get; set; } = true;
    }

    /// <summary>
    ///     Convai Gaze exposed through Unity's official MCP server: put gaze on a character, tune
    ///     how it makes eye contact, mark what is worth looking at, and answer "why isn't this
    ///     character looking at me?".
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every verdict in every response comes from <see cref="GazeSetupService" /> and
    ///         <see cref="GazeSetupTroubleshooter" /> — the same code the component inspector and the
    ///         Gaze editor window draw. This class contains no check of its own, so an assistant and
    ///         the editor cannot describe the same character differently.
    ///     </para>
    ///     <para>
    ///         Adding the component is the one step performed here rather than in the service. The
    ///         service deliberately starts from a controller that already exists, because Add
    ///         Component is the gesture every Unity user knows — but an assistant has no Add
    ///         Component button, so this layer performs that single step and then hands off.
    ///     </para>
    ///     <para>
    ///         Nothing here creates or edits an asset. When a character would benefit from a Gaze
    ///         Profile and the project has none, the response says so and names the menu path that
    ///         creates one.
    ///     </para>
    /// </remarks>
    public static class ConvaiGazeMcpTools
    {
        private const string ConfigureTool = "Convai.ConfigureGaze";
        private const string DiagnoseTool = "Convai.DiagnoseGaze";
        private const string MarkTargetTool = "Convai.MarkGazeTarget";

        private const string AddGazeMenuPath = "Add Component → Convai → Embodiment → Gaze";
        private const string CreateProfileMenuPath = "Assets → Create → Convai → Embodiment → Gaze Profile";
        private const string MarkTargetMenuPath = "Convai → Gaze → Target";

        /// <summary>Priority the player anchor publishes at; a target above it outranks the player.</summary>
        private const int PlayerAnchorPriority = 10;

        // ------------------------------------------------------------------ configure

        [McpTool(
            ConfigureTool,
            "Adds Convai Gaze to a character and tunes how it makes eye contact — who it treats as the player, how strongly it commits, how it turns its body, and which optional extras it has. Previews by default. Assigns an existing Gaze Profile if the project has one; never creates or edits an asset. Omitted settings are left unchanged.",
            "Configure Convai Gaze",
            Groups = new[] { "convai", "gaze" },
            EnabledByDefault = true)]
        public static object Configure(JObject input) => Configure(input?.ToObject<ConfigureGazeRequest>());

        public static object Configure(ConfigureGazeRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "Gaze can only be configured in Edit Mode.", new { code = "PLAY_MODE_ACTIVE" });

            request ??= new ConfigureGazeRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            if (!TryResolveProfile(request.ProfileAssetPath, out ConvaiGazeProfile profile, out error))
                return Response(false, error, new
                {
                    code = "INVALID_PROFILE",
                    createProfileMenuPath = CreateProfileMenuPath
                });

            if (!TryResolveCapabilities(request.Capabilities, out List<GazeCapabilityId> requested, out error))
                return Response(false, error, new
                {
                    code = "INVALID_CAPABILITY",
                    validCapabilities = Enum.GetNames(typeof(GazeCapabilityId))
                });

            if (!TryResolveAnchor(request, out Transform anchor, out error))
                return Response(false, error, new { code = "INVALID_ANCHOR" });

            ConvaiGazeController controller = character.GetComponentInChildren<ConvaiGazeController>(true);
            bool hadController = controller != null;
            Transform root = hadController
                ? GazeSetupService.ResolveRoot(controller)
                : character.transform;

            // Omitted means "leave capabilities alone" on a character that already has gaze, and
            // "give a first-time user the recommended pair" on one that does not. Passing the
            // current set is how "leave alone" is expressed to a service whose contract is a set.
            IReadOnlyList<GazeCapabilityId> wanted = requested ??
                (hadController
                    ? CurrentCapabilities(root)
                    : GazeSetupOptions.RecommendedCapabilities);

            List<FieldWrite> fields = BuildFieldWrites(request, anchor, profile);
            var changes = new List<string>(8);
            var notes = new List<string>(4);

            if (!hadController) changes.Add("Add the Gaze component to this character");
            PlanSetupChanges(controller, changes);
            PlanCapabilityChanges(root, wanted, changes);
            PlanFieldChanges(controller, fields, changes);

            if (request.DryRun)
                return ConfigureResponse(true, character, controller, changes, notes, root, wanted);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                if (controller == null)
                {
                    controller = Undo.AddComponent<ConvaiGazeController>(character.gameObject);
                    root = GazeSetupService.ResolveRoot(controller);
                }

                GazeSetupResult result = GazeSetupService.Apply(
                    controller, new GazeSetupOptions { Capabilities = wanted, AssignProfile = false });
                notes.AddRange(result.Notes);

                if (fields.Count > 0)
                {
                    Undo.RecordObject(controller, "Configure Convai Gaze");
                    var serialized = new SerializedObject(controller);
                    for (int i = 0; i < fields.Count; i++)
                    {
                        SerializedProperty property = serialized.FindProperty(fields[i].Property);
                        if (property == null)
                        {
                            notes.Add($"This character's Gaze component has no {fields[i].Label} setting to write.");
                            continue;
                        }

                        fields[i].Write(property);
                    }

                    serialized.ApplyModifiedProperties();
                }

                EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                Undo.SetCurrentGroupName("Configure Convai Gaze");
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return Response(false, exception.Message, new { code = "AUTHORING_FAILED" });
            }

            return ConfigureResponse(false, character, controller, changes, notes, root, wanted);
        }

        [McpSchema(ConfigureTool)]
        public static object ConfigureSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character to configure. 0 uses the only one in the active scene."),
                ["eyeContactMode"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "How strongly the character commits to the player. Natural follows the profile's " +
                    "per-state behaviour; Speaking Focus locks on only while it is talking; " +
                    "Conversation Lock never looks away during a conversation; Always Lock never " +
                    "looks away at all. Omit to leave unchanged.",
                    Enum.GetNames(typeof(GazeEyeContactMode))),
                ["focusFidelity"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "Precision while locked on. Social keeps subtle eye life; Exact suppresses " +
                    "look-aways entirely. Omit to leave unchanged.",
                    Enum.GetNames(typeof(GazeFocusFidelity))),
                ["playerAnchorInstanceId"] = ConvaiMcpResponses.OptionalIntegerProperty(
                    "The transform this character should treat as the player (Player Anchor " +
                    "Override) — for split-screen, multiplayer, or cutscene rigs. Omit to leave " +
                    "unchanged; empty means the main camera."),
                ["clearPlayerAnchorOverride"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Clear Player Anchor Override so the character goes back to watching the main camera."),
                ["playerAnchorAimMode"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "Where on the player the character aims (Aim Point). Omit to leave unchanged.",
                    Enum.GetNames(typeof(GazeAnchorAimMode))),
                ["playerAnchorAimOffset"] = PointSchema(
                    "Anchor-local aim point, used when Aim Point is Local Offset."),
                ["bodyTurnStyle"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "How the character turns to face something behind it. Stepping Turn plays the " +
                    "body's own turn animation; Smooth Rotation turns the character directly and " +
                    "needs no clips. Omit to leave unchanged.",
                    Enum.GetNames(typeof(GazeBodyTurnStyle))),
                ["allowScriptedOverrides"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Let a scripted look-at request take over during Exact focus (Allow Scripted Overrides)."),
                ["lockBlocksGlances"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "While locked on the player, absorb brief glances so nothing pulls the eyes away " +
                    "(Lock Blocks Glances)."),
                ["autoCreatePlayerAnchor"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Create a Gaze Player Anchor at runtime when the character has no target " +
                    "provider (Auto Create Player Anchor)."),
                ["profileAssetPath"] = ConvaiMcpResponses.StringProperty(
                    "Project path of an existing Gaze Profile to give this character a personality. " +
                    "Never creates one — if the project has none, the response names the menu path " +
                    "that does."),
                ["capabilities"] = ConvaiMcpResponses.ArrayProperty(
                    "The optional extras this character should end up with — the exact set, so a " +
                    "capability left out is removed. Omit the field entirely to leave the extras " +
                    "alone on a character that already has gaze.",
                    ConvaiMcpResponses.StringEnumSchema(Enum.GetNames(typeof(GazeCapabilityId)))),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the changes without touching the scene.", true)
            },
            "characterInstanceId");

        [McpOutputSchema(ConfigureTool)]
        public static object ConfigureOutput() => StandardSchema();

        // ------------------------------------------------------------------ diagnose

        [McpTool(
            DiagnoseTool,
            "Explains why a character is or is not looking at the player: which head and eye bones resolved, whether the rig faces the right way, what it treats as the player and which setting decided that, which personality is in use, which optional extras it has, and live gaze state in Play Mode. Read-only.",
            "Diagnose Convai Gaze",
            Groups = new[] { "convai", "gaze", "validation" },
            EnabledByDefault = true)]
        public static object Diagnose(JObject input) => Diagnose(input?.ToObject<DiagnoseGazeRequest>());

        public static object Diagnose(DiagnoseGazeRequest request)
        {
            request ??= new DiagnoseGazeRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            long characterId = ConvaiMcpEntityRef.ToToolId(character.gameObject);
            ConvaiGazeController controller = character.GetComponentInChildren<ConvaiGazeController>(true);
            if (controller == null)
            {
                return Response(true, $"'{character.gameObject.name}' has no Gaze component yet.", new
                {
                    present = false,
                    ready = false,
                    isWorking = false,
                    characterInstanceId = characterId,
                    issues = new[]
                    {
                        Issue("GAZE_COMPONENT_MISSING", "Error",
                            "This character has no Gaze component, so it will never look at the " +
                            $"player. Add it with {AddGazeMenuPath} — that is the only required step.",
                            character.gameObject.name, characterId, characterId, true)
                    },
                    nextSteps = new[]
                    {
                        $"Add Gaze to '{character.gameObject.name}' — {AddGazeMenuPath}, or call Convai.ConfigureGaze.",
                        "Then run Convai.DiagnoseGaze again to confirm the rig can drive it."
                    }
                });
            }

            var serialized = new SerializedObject(controller);
            SerializedProperty profileProperty = serialized.FindProperty("profile");
            bool autoCreateAnchor = serialized.FindProperty("autoCreatePlayerAnchor")?.boolValue ?? true;

            GazePreflight preflight = GazeSetupService.Inspect(controller);
            GazeSetupInput input = GazeSetupTroubleshooter.GatherFrom(controller, profileProperty, autoCreateAnchor);
            var findings = new List<GazeSetupFinding>(8);
            GazeSetupTroubleshooter.Evaluate(in input, findings);

            GazeAnchorReport anchor = GazeSetupService.InspectAnchor(controller);
            GazeFacingReport facing = GazeSetupService.InspectFacing(controller);
            Transform root = GazeSetupService.ResolveRoot(controller);
            ConvaiGazeProfile profile = GazeSetupService.ResolveAssignedProfile(controller);
            long controllerId = ConvaiMcpEntityRef.ToToolId(controller);

            var issues = new List<object>(findings.Count);
            for (int i = 0; i < findings.Count; i++)
            {
                GazeSetupFinding finding = findings[i];
                if (finding.Severity == GazeSetupSeverity.Ok) continue;
                issues.Add(Issue(
                    IssueCode(finding.Title),
                    finding.Severity.ToString(),
                    finding.Message,
                    finding.Title,
                    controllerId,
                    characterId,
                    finding.Fix != GazeFixId.None));
            }

            return Response(
                true,
                preflight.IsFunctional
                    ? $"Convai Gaze is working on '{character.gameObject.name}'."
                    : $"Convai Gaze cannot run on '{character.gameObject.name}' yet.",
                new
                {
                    present = true,
                    ready = preflight.IsReady,
                    isWorking = preflight.IsFunctional,
                    characterInstanceId = characterId,
                    componentInstanceId = controllerId,
                    checks = DescribeChecks(preflight),
                    issues,
                    rig = DescribeRig(in input),
                    facing = new
                    {
                        state = facing.State.ToString(),
                        angleDegrees = facing.Measured ? (float?)facing.AngleDegrees : null,
                        message = facing.Detail
                    },
                    watches = DescribeAnchor(anchor, autoCreateAnchor),
                    personality = new
                    {
                        profileName = profile != null ? profile.name : string.Empty,
                        profileAssetPath = profile != null ? AssetDatabase.GetAssetPath(profile) : string.Empty,
                        usingSdkDefaults = profile == null,
                        createProfileMenuPath = profile == null ? CreateProfileMenuPath : string.Empty,
                        message = profile != null
                            ? $"Tuned by the '{profile.name}' Gaze Profile."
                            : "No Gaze Profile is assigned, so the character runs on the SDK defaults — " +
                              "which work. Assign one to give it a personality of its own."
                    },
                    eyeContact = DescribeEyeContact(controller),
                    capabilities = DescribeCapabilities(root),
                    sceneTargets = DescribeSceneTargets(),
                    runtime = DescribeRuntime(controller, request.IncludeRuntimeState),
                    nextSteps = BuildNextSteps(preflight, findings, anchor, profile)
                });
        }

        [McpSchema(DiagnoseTool)]
        public static object DiagnoseSchema() => ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character to diagnose. 0 uses the only one in the active scene.", 0),
                ["includeRuntimeState"] = ConvaiMcpResponses.BooleanProperty(
                    "Include what the character is looking at right now. Play Mode only.", true)
            });

        [McpOutputSchema(DiagnoseTool)]
        public static object DiagnoseOutput() => StandardSchema();

        // ------------------------------------------------------------------ mark target

        [McpTool(
            MarkTargetTool,
            "Marks scene objects as worth looking at, so Convai characters glance at them — a painting, a screen, a prop. Previews by default. Adds a component to the objects named; never creates an asset.",
            "Mark Convai Gaze Target",
            Groups = new[] { "convai", "gaze" },
            EnabledByDefault = true)]
        public static object MarkTarget(JObject input) => MarkTarget(input?.ToObject<MarkGazeTargetRequest>());

        public static object MarkTarget(MarkGazeTargetRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "Gaze targets can only be marked in Edit Mode.",
                    new { code = "PLAY_MODE_ACTIVE" });

            request ??= new MarkGazeTargetRequest();
            long[] ids = request.GameObjectInstanceIds ?? Array.Empty<long>();
            if (ids.Length == 0)
                return Response(false, "Name at least one GameObject to mark as a gaze target.", new
                {
                    code = "NO_TARGETS",
                    requiredInputs = new[] { "gameObjectInstanceIds" }
                });

            var targets = new List<GameObject>(ids.Length);
            foreach (long id in ids)
            {
                if (!ConvaiMcpResolvers.TryHost(id, null, true, out GameObject host, out string hostError))
                    return Response(false, hostError, new { code = "INVALID_TARGET" });
                if (!targets.Contains(host)) targets.Add(host);
            }

            var warnings = new List<string>(2);
            if (!request.Remove && request.Priority is > PlayerAnchorPriority)
            {
                warnings.Add(
                    $"Priority {request.Priority} is above the player's {PlayerAnchorPriority}, so " +
                    "characters will look at this object instead of the player during a conversation.");
            }

            var results = new List<object>(targets.Count);
            if (request.DryRun)
            {
                foreach (GameObject target in targets)
                    results.Add(PreviewTarget(target, request));
                return MarkResponse(true, results, warnings, request.Remove);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                foreach (GameObject target in targets)
                    results.Add(ApplyTarget(target, request));

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.SetCurrentGroupName(request.Remove ? "Unmark Convai Gaze Targets" : "Mark Convai Gaze Targets");
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return Response(false, exception.Message, new { code = "AUTHORING_FAILED" });
            }

            return MarkResponse(false, results, warnings, request.Remove);
        }

        [McpSchema(MarkTargetTool)]
        public static object MarkTargetSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["gameObjectInstanceIds"] = ConvaiMcpResponses.ArrayProperty(
                    "The GameObjects to mark as worth looking at.",
                    ConvaiMcpResponses.IntegerSchema()),
                ["priority"] = ConvaiMcpResponses.OptionalIntegerProperty(
                    "How important this object is. The player counts as 10, so the default 5 means " +
                    "the player still wins during a conversation; above 10 the character looks here instead."),
                ["baseRelevance"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "How interesting the object is when the character is close to it, from 0 to 1."),
                ["maxDistance"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "Metres beyond which characters stop noticing the object."),
                ["fullRelevanceDistance"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "Metres within which the object is at its most interesting."),
                ["aimOffset"] = PointSchema(
                    "Where on the object the eyes should land, relative to it — the top of a " +
                    "painting, the centre of a screen."),
                ["remove"] = ConvaiMcpResponses.BooleanProperty(
                    "Unmark these objects instead, so characters stop noticing them.", false),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the changes without touching the scene.", true)
            },
            "gameObjectInstanceIds");

        [McpOutputSchema(MarkTargetTool)]
        public static object MarkTargetOutput() => StandardSchema();

        // ------------------------------------------------------------------ configure helpers

        /// <summary>One optional setting: how to tell whether it already holds, and how to write it.</summary>
        /// <remarks>
        ///     Preview and apply walk this same list, so a previewed change and an applied one cannot
        ///     drift apart — the failure mode where "previews by default" quietly stops meaning
        ///     anything.
        /// </remarks>
        private readonly struct FieldWrite
        {
            internal FieldWrite(
                string property, string label, Predicate<SerializedProperty> holds, Action<SerializedProperty> write)
            {
                Property = property;
                Label = label;
                Holds = holds;
                Write = write;
            }

            internal string Property { get; }
            internal string Label { get; }
            internal Predicate<SerializedProperty> Holds { get; }
            internal Action<SerializedProperty> Write { get; }
        }

        private static List<FieldWrite> BuildFieldWrites(
            ConfigureGazeRequest request, Transform anchor, ConvaiGazeProfile profile)
        {
            var fields = new List<FieldWrite>(10);

            if (request.EyeContactMode.HasValue)
            {
                int value = (int)request.EyeContactMode.Value;
                fields.Add(new FieldWrite("eyeContactMode",
                    $"Eye Contact: {Humanize(request.EyeContactMode.Value.ToString())}",
                    property => property.enumValueIndex == value,
                    property => property.enumValueIndex = value));
            }

            if (request.FocusFidelity.HasValue)
            {
                int value = (int)request.FocusFidelity.Value;
                fields.Add(new FieldWrite("focusFidelity",
                    $"Focus fidelity: {request.FocusFidelity.Value}",
                    property => property.enumValueIndex == value,
                    property => property.enumValueIndex = value));
            }

            if (request.PlayerAnchorInstanceId.HasValue || request.ClearPlayerAnchorOverride == true)
            {
                fields.Add(new FieldWrite("playerAnchorOverride",
                    anchor != null
                        ? $"Player Anchor Override: '{anchor.name}'"
                        : "Player Anchor Override: cleared, back to the main camera",
                    property => (Transform)property.objectReferenceValue == anchor,
                    property => property.objectReferenceValue = anchor));
            }

            if (request.PlayerAnchorAimMode.HasValue)
            {
                int value = (int)request.PlayerAnchorAimMode.Value;
                fields.Add(new FieldWrite("playerAnchorAimMode",
                    $"Aim Point: {Humanize(request.PlayerAnchorAimMode.Value.ToString())}",
                    property => property.enumValueIndex == value,
                    property => property.enumValueIndex = value));
            }

            if (request.PlayerAnchorAimOffset != null)
            {
                Vector3 value = request.PlayerAnchorAimOffset.ToVector3();
                fields.Add(new FieldWrite("playerAnchorAimOffset",
                    $"Aim offset: {value}",
                    property => property.vector3Value == value,
                    property => property.vector3Value = value));
            }

            if (request.BodyTurnStyle.HasValue)
            {
                int value = (int)request.BodyTurnStyle.Value;
                fields.Add(new FieldWrite("bodyTurnStyle",
                    $"Body turns: {Humanize(request.BodyTurnStyle.Value.ToString())}",
                    property => property.enumValueIndex == value,
                    property => property.enumValueIndex = value));
            }

            if (request.AllowScriptedOverrides.HasValue)
            {
                bool value = request.AllowScriptedOverrides.Value;
                fields.Add(new FieldWrite("allowScriptedOverridesDuringExactFocus",
                    $"Allow Scripted Overrides: {OnOff(value)}",
                    property => property.boolValue == value,
                    property => property.boolValue = value));
            }

            if (request.LockBlocksGlances.HasValue)
            {
                bool value = request.LockBlocksGlances.Value;
                fields.Add(new FieldWrite("lockBlocksGlances",
                    $"Lock Blocks Glances: {OnOff(value)}",
                    property => property.boolValue == value,
                    property => property.boolValue = value));
            }

            if (request.AutoCreatePlayerAnchor.HasValue)
            {
                bool value = request.AutoCreatePlayerAnchor.Value;
                fields.Add(new FieldWrite("autoCreatePlayerAnchor",
                    $"Auto Create Player Anchor: {OnOff(value)}",
                    property => property.boolValue == value,
                    property => property.boolValue = value));
            }

            if (profile != null)
            {
                fields.Add(new FieldWrite("profile",
                    $"Personality: '{profile.name}'",
                    property => (ConvaiGazeProfile)property.objectReferenceValue == profile,
                    property => property.objectReferenceValue = profile));
            }

            return fields;
        }

        /// <summary>
        ///     What the setup service will repair, taken from its own preflight rather than
        ///     re-derived — a preview that predicts the service by copying its rules is the second
        ///     source of truth this whole design exists to avoid.
        /// </summary>
        private static void PlanSetupChanges(ConvaiGazeController controller, List<string> changes)
        {
            if (controller == null)
            {
                changes.Add("Set the character up: pick who it watches");
                return;
            }

            GazePreflight preflight = GazeSetupService.Inspect(controller);
            if (preflight.Checks == null) return;

            for (int i = 0; i < preflight.Checks.Count; i++)
            {
                GazeCheck check = preflight.Checks[i];
                if (check.State != GazeCheckState.Fixable) continue;

                string label = GazeSetupService.DescribeFix(check.Fix);
                if (label != null) changes.Add($"{label} ({check.Label.ToLowerInvariant()})");
            }
        }

        private static void PlanCapabilityChanges(
            Transform root, IReadOnlyList<GazeCapabilityId> wanted, List<string> changes)
        {
            foreach (GazeCapabilityId id in (GazeCapabilityId[])Enum.GetValues(typeof(GazeCapabilityId)))
            {
                bool want = Contains(wanted, id);
                bool have = GazeCapabilities.IsPresentUnder(root, GazeCapabilities.ProviderTypeOf(id));
                if (want == have) continue;
                changes.Add(want
                    ? $"Turn on \"{GazeCapabilities.DisplayNameOf(id)}\""
                    : $"Turn off \"{GazeCapabilities.DisplayNameOf(id)}\"");
            }
        }

        private static void PlanFieldChanges(
            ConvaiGazeController controller, List<FieldWrite> fields, List<string> changes)
        {
            SerializedObject serialized = controller != null ? new SerializedObject(controller) : null;
            for (int i = 0; i < fields.Count; i++)
            {
                SerializedProperty property = serialized?.FindProperty(fields[i].Property);
                if (property != null && fields[i].Holds(property)) continue;
                changes.Add($"Set {fields[i].Label}");
            }
        }

        private static object ConfigureResponse(
            bool dryRun,
            ConvaiCharacter character,
            ConvaiGazeController controller,
            List<string> changes,
            List<string> notes,
            Transform root,
            IReadOnlyList<GazeCapabilityId> wanted)
        {
            GazePreflight preflight = GazeSetupService.Inspect(controller);
            preflight.TryGetBlocker(out GazeCheck blocker);

            // A character with no controller has no preflight rows, and an empty preflight reads as
            // "nothing is blocked". Saying a bare character is ready would be the exact failure this
            // module's setup card exists to avoid, in the other direction.
            bool isWorking = controller != null && preflight.IsFunctional;
            bool isReady = controller != null && preflight.IsReady;

            // Said on the preview as well as the apply. These tools never author an asset, so the
            // character will still have no profile afterwards either way, and a preview that omits
            // what the apply will tell you is a preview you cannot act on.
            if (GazeSetupService.ResolveAssignedProfile(controller) == null)
            {
                notes.Add(
                    "This character has no Gaze Profile, so it runs on the SDK defaults — which " +
                    $"work. Create one with {CreateProfileMenuPath} to tune its personality.");
            }

            var nextSteps = new List<string>(3);
            if (blocker.State == GazeCheckState.Blocked)
            {
                // The blocker says what is wrong; the check also knows what fixes it. Reporting only
                // the first leaves a reader who cannot already diagnose it with nothing to do, which
                // Diagnose gets right and this did not.
                string fix = GazeSetupService.DescribeFix(blocker.Fix);
                nextSteps.Add(string.IsNullOrEmpty(fix)
                    ? blocker.Detail
                    : $"{blocker.Detail} — press {fix} on the Gaze component, or run Convai.DiagnoseGaze for the full rig report.");
            }
            if (dryRun && changes.Count > 0)
                nextSteps.Add("Call Convai.ConfigureGaze again with dryRun false to apply these changes.");
            if (!dryRun) nextSteps.Add("Run Convai.DiagnoseGaze to confirm the character is ready.");

            return Response(
                true,
                dryRun ? "Previewed the Convai Gaze setup." : "Configured Convai Gaze.",
                new
                {
                    dryRun,
                    complete = isWorking,
                    changes,
                    notes,
                    blockers = blocker.State == GazeCheckState.Blocked
                        ? new[] { new { code = IssueCode(blocker.Label), message = blocker.Detail } }
                        : Array.Empty<object>(),
                    readiness = new
                    {
                        isWorking,
                        ready = isReady,
                        blocker = blocker.State == GazeCheckState.Blocked ? blocker.Detail : string.Empty
                    },
                    capabilities = DescribeCapabilities(root),
                    requestedCapabilities = Names(wanted),
                    characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    componentInstanceId = ConvaiMcpEntityRef.ToToolId(controller),
                    affectedInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps
                });
        }

        private static bool TryResolveProfile(string path, out ConvaiGazeProfile profile, out string error)
        {
            profile = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return true;

            profile = AssetDatabase.LoadAssetAtPath<ConvaiGazeProfile>(path);
            if (profile != null) return true;

            error = $"No Gaze Profile exists at '{path}'. Create one with {CreateProfileMenuPath}, " +
                    "or leave profileAssetPath empty to use the SDK defaults.";
            return false;
        }

        private static bool TryResolveCapabilities(
            string[] requested, out List<GazeCapabilityId> capabilities, out string error)
        {
            capabilities = null;
            error = string.Empty;
            if (requested == null) return true;

            capabilities = new List<GazeCapabilityId>(requested.Length);
            foreach (string name in requested)
            {
                if (!Enum.TryParse(name, true, out GazeCapabilityId id))
                {
                    error = $"'{name}' is not a Convai Gaze capability.";
                    capabilities = null;
                    return false;
                }

                if (!capabilities.Contains(id)) capabilities.Add(id);
            }

            return true;
        }

        private static bool TryResolveAnchor(ConfigureGazeRequest request, out Transform anchor, out string error)
        {
            anchor = null;
            error = string.Empty;
            if (request.ClearPlayerAnchorOverride == true) return true;
            if (!request.PlayerAnchorInstanceId.HasValue) return true;

            long id = request.PlayerAnchorInstanceId.Value;
            if (id == 0) return true;

            if (!ConvaiMcpResolvers.TryHost(id, null, true, out GameObject host, out error)) return false;
            anchor = host.transform;
            return true;
        }

        private static IReadOnlyList<GazeCapabilityId> CurrentCapabilities(Transform root)
        {
            var present = new List<GazeCapabilityId>(GazeCapabilities.Count);
            foreach (GazeCapabilityId id in (GazeCapabilityId[])Enum.GetValues(typeof(GazeCapabilityId)))
                if (GazeCapabilities.IsPresentUnder(root, GazeCapabilities.ProviderTypeOf(id)))
                    present.Add(id);
            return present;
        }

        // ------------------------------------------------------------------ diagnose helpers

        private static object[] DescribeChecks(GazePreflight preflight)
        {
            if (preflight.Checks == null) return Array.Empty<object>();

            var checks = new object[preflight.Checks.Count];
            for (int i = 0; i < preflight.Checks.Count; i++)
            {
                GazeCheck check = preflight.Checks[i];
                checks[i] = new
                {
                    label = check.Label,
                    detail = check.Detail,
                    state = check.State.ToString(),
                    suggestedFix = GazeSetupService.DescribeFix(check.Fix) ?? string.Empty
                };
            }

            return checks;
        }

        private static object DescribeRig(in GazeSetupInput input)
        {
            GazeEyeBackend backend = GazeSetupTroubleshooter.ResolveEyeBackend(in input);
            return new
            {
                isHumanoid = input.IsHumanoid,
                hasAnimator = input.HasAnimator,
                headBone = BoneName(input.Bones.Head),
                neckBone = BoneName(input.Bones.Neck),
                leftEyeBone = BoneName(input.Bones.LeftEye),
                rightEyeBone = BoneName(input.Bones.RightEye),
                bonesFoundByName = input.Bones.InferredFromNames,
                rigBindingCount = input.RigBindingCount,
                eyeLookShapeCount = input.EyeLookShapeCount,
                eyeBackend = backend switch
                {
                    GazeEyeBackend.EyeBones => "Eye bones",
                    GazeEyeBackend.EyeLookBlendshapes => "EyeLook blendshapes",
                    _ => "Head only"
                },
                eyeBackendMessage = backend switch
                {
                    GazeEyeBackend.EyeBones => "The eyes are driven by their own bones — full fidelity.",
                    GazeEyeBackend.EyeLookBlendshapes =>
                        "No eye bones resolved, so the EyeLook blendshapes drive the eyes.",
                    _ => "Neither eye bones nor a complete set of EyeLook blendshapes resolved, so " +
                         "the eyes follow the head. Map LeftEye and RightEye on the character's " +
                         "Character Rig for full eye motion."
                }
            };
        }

        private static object DescribeAnchor(GazeAnchorReport anchor, bool autoCreateAnchor) => new
        {
            resolvedBy = anchor.Source.ToString(),
            anchorName = anchor.Anchor != null ? anchor.Anchor.name : string.Empty,
            anchorInstanceId = ConvaiMcpEntityRef.ToToolId(anchor.Anchor != null ? anchor.Anchor.gameObject : null),
            mainCameraTagged = anchor.MainCameraTagged,
            playerAnchorPresent = anchor.ProviderPresent,
            playerAnchorExplicitTarget = anchor.ProviderExplicitAnchor != null
                ? anchor.ProviderExplicitAnchor.name
                : string.Empty,
            autoCreatePlayerAnchor = autoCreateAnchor,
            detail = anchor.Detail,
            message = anchor.Source switch
            {
                GazeAnchorSource.PlayerAnchorOverride =>
                    $"This character watches '{anchor.Anchor.name}' because Player Anchor Override is " +
                    "set on its Gaze component. Clear that field to go back to the main camera.",
                GazeAnchorSource.PlayerAnchorProvider =>
                    $"This character watches '{anchor.Anchor.name}' because the Player Anchor on it " +
                    "carries that Explicit Anchor. Clear the Explicit Anchor to go back to the main camera.",
                GazeAnchorSource.MainCamera =>
                    $"This character watches '{anchor.Anchor.name}', the camera tagged MainCamera. Set " +
                    "Player Anchor Override on the Gaze component to point it somewhere else.",
                GazeAnchorSource.GuessedCamera =>
                    "No camera is tagged MainCamera, so the character will guess one at runtime — " +
                    $"probably '{anchor.Anchor.name}', which may be the wrong one. Tag your view " +
                    "camera as MainCamera, or set Player Anchor Override on the Gaze component.",
                _ => "Nothing in this scene resolves as the player yet. Add a camera and tag it " +
                     "MainCamera, or set Player Anchor Override on the Gaze component."
            }
        };

        private static object DescribeEyeContact(ConvaiGazeController controller) => new
        {
            mode = controller.EyeContactMode.ToString(),
            modeMessage = controller.EyeContactMode switch
            {
                GazeEyeContactMode.Natural =>
                    "Natural — the Gaze Profile's per-state behaviour decides how much eye contact " +
                    "the character makes, and it ignores the player while idle.",
                GazeEyeContactMode.SpeakingFocus =>
                    "Speaking Focus — the character locks onto the player only while it is talking.",
                GazeEyeContactMode.ConversationLock =>
                    "Conversation Lock — the character never looks away during a conversation, and " +
                    "keeps its ambient life while idle.",
                _ => "Always Lock — the character never looks away, idle included."
            },
            focusFidelity = controller.FocusFidelity.ToString(),
            aimPoint = controller.PlayerAnchorAimMode.ToString(),
            lockBlocksGlances = controller.LockBlocksGlances,
            allowScriptedOverrides = controller.AllowScriptedOverridesDuringExactFocus
        };

        private static object[] DescribeCapabilities(Transform root)
        {
            var infos = new List<GazeCapabilityInfo>(GazeCapabilities.Count);
            GazeCapabilities.Evaluate(root, infos);

            var described = new object[infos.Count];
            for (int i = 0; i < infos.Count; i++)
            {
                described[i] = new
                {
                    id = infos[i].Id.ToString(),
                    name = infos[i].DisplayName,
                    description = infos[i].Description,
                    present = infos[i].IsPresent
                };
            }

            return described;
        }

        private static object DescribeSceneTargets()
        {
            ConvaiGazeTarget[] all = ConvaiObjectFind.All<ConvaiGazeTarget>(FindObjectsInactive.Include);
            var names = new List<string>(all.Length);
            for (int i = 0; i < all.Length; i++)
                if (all[i].gameObject.scene == SceneManager.GetActiveScene())
                    names.Add(all[i].gameObject.name);

            return new
            {
                count = names.Count,
                names,
                message = names.Count > 0
                    ? "Characters glance at these while idle; the player still wins during a conversation."
                    : "Nothing in this scene is marked as worth looking at. Use Convai.MarkGazeTarget, " +
                      $"or add {MarkTargetMenuPath} to an object by hand.",
                markTargetMenuPath = MarkTargetMenuPath
            };
        }

        private static object DescribeRuntime(ConvaiGazeController controller, bool include)
        {
            if (!include || !EditorApplication.isPlaying || !controller.isActiveAndEnabled) return null;

            GazeSnapshot snapshot = controller.CaptureSnapshot();
            var trace = new List<object>(snapshot.RecentTrace.Count);
            for (int i = 0; i < snapshot.RecentTrace.Count; i++)
            {
                GazeTraceEntry entry = snapshot.RecentTrace[i];
                trace.Add(new { time = entry.Time, level = entry.Level.ToString(), message = entry.Message });
            }

            return new
            {
                lookingAt = snapshot.TargetName,
                targetKind = snapshot.TargetKind.ToString(),
                dialogueState = snapshot.DialogueState.ToString(),
                engagement = snapshot.PolicyEngagement,
                contactErrorDegrees = float.IsNaN(snapshot.ContactErrorDegrees)
                    ? (float?)null
                    : snapshot.ContactErrorDegrees,
                lockedOn = snapshot.FocusActive,
                lostItsAnchor = snapshot.FocusDegraded,
                turningBody = snapshot.IsReorienting,
                nodding = snapshot.IsNodding,
                blinkWeight = snapshot.BlinkWeight,
                playerAttention = snapshot.PlayerAttention < 0f ? (float?)null : snapshot.PlayerAttention,
                recentTrace = trace
            };
        }

        private static string[] BuildNextSteps(
            GazePreflight preflight,
            List<GazeSetupFinding> findings,
            GazeAnchorReport anchor,
            ConvaiGazeProfile profile)
        {
            var steps = new List<string>(4);
            if (preflight.TryGetBlocker(out GazeCheck blocker)) steps.Add(blocker.Detail);

            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity != GazeSetupSeverity.Error) continue;
                if (!steps.Contains(findings[i].Message)) steps.Add(findings[i].Message);
            }

            if (anchor.Source == GazeAnchorSource.GuessedCamera)
                steps.Add("Tag your view camera as MainCamera so every character agrees on where the player is.");

            if (profile == null)
                steps.Add($"Create a Gaze Profile with {CreateProfileMenuPath} to tune this character's personality.");

            if (steps.Count == 0) steps.Add("Nothing needs doing — press Play and the character will look at you.");
            return steps.ToArray();
        }

        // ------------------------------------------------------------------ mark-target helpers

        private static object PreviewTarget(GameObject target, MarkGazeTargetRequest request)
        {
            var existing = target.GetComponent<ConvaiGazeTarget>();
            string action = request.Remove
                ? existing != null ? "Removed" : "NotMarked"
                : existing == null
                    ? "Added"
                    : WouldUpdate(existing, request) ? "Updated" : "AlreadyMarked";

            return new
            {
                instanceId = ConvaiMcpEntityRef.ToToolId(target),
                name = target.name,
                action
            };
        }

        private static object ApplyTarget(GameObject target, MarkGazeTargetRequest request)
        {
            var existing = target.GetComponent<ConvaiGazeTarget>();
            if (request.Remove)
            {
                if (existing == null)
                    return new { instanceId = ConvaiMcpEntityRef.ToToolId(target), name = target.name, action = "NotMarked" };

                Undo.DestroyObjectImmediate(existing);
                return new { instanceId = ConvaiMcpEntityRef.ToToolId(target), name = target.name, action = "Removed" };
            }

            bool added = existing == null;
            if (added) existing = Undo.AddComponent<ConvaiGazeTarget>(target);

            bool updated = WouldUpdate(existing, request);
            if (updated)
            {
                Undo.RecordObject(existing, "Mark Convai Gaze Target");
                // Through the public properties, so the component's own clamping — a full-relevance
                // distance can never exceed the max — applies exactly as it does in the inspector.
                if (request.MaxDistance.HasValue) existing.MaxDistance = request.MaxDistance.Value;
                if (request.Priority.HasValue) existing.Priority = request.Priority.Value;
                if (request.BaseRelevance.HasValue) existing.BaseRelevance = request.BaseRelevance.Value;
                if (request.FullRelevanceDistance.HasValue)
                    existing.FullRelevanceDistance = request.FullRelevanceDistance.Value;
                if (request.AimOffset != null) existing.AimOffset = request.AimOffset.ToVector3();
                EditorUtility.SetDirty(existing);
            }

            return new
            {
                instanceId = ConvaiMcpEntityRef.ToToolId(target),
                name = target.name,
                action = added ? "Added" : updated ? "Updated" : "AlreadyMarked"
            };
        }

        private static bool WouldUpdate(ConvaiGazeTarget target, MarkGazeTargetRequest request)
        {
            if (target == null) return true;
            if (request.Priority.HasValue && target.Priority != request.Priority.Value) return true;
            if (request.BaseRelevance.HasValue &&
                !Mathf.Approximately(target.BaseRelevance, Mathf.Clamp01(request.BaseRelevance.Value))) return true;
            if (request.MaxDistance.HasValue &&
                !Mathf.Approximately(target.MaxDistance, Mathf.Max(0f, request.MaxDistance.Value))) return true;
            if (request.FullRelevanceDistance.HasValue &&
                !Mathf.Approximately(target.FullRelevanceDistance, request.FullRelevanceDistance.Value)) return true;
            return request.AimOffset != null && target.AimOffset != request.AimOffset.ToVector3();
        }

        private static object MarkResponse(bool dryRun, List<object> results, List<string> warnings, bool remove)
        {
            string verb = remove ? "unmark" : "mark";
            return Response(
                true,
                dryRun ? $"Previewed the gaze targets to {verb}." : $"Finished — gaze targets {verb}ed.",
                new
                {
                    dryRun,
                    results,
                    warnings,
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps = dryRun
                        ? new[] { $"Call Convai.MarkGazeTarget again with dryRun false to {verb} these objects." }
                        : new[] { "Run Convai.DiagnoseGaze on a character to see the targets it can now notice." }
                });
        }

        // ------------------------------------------------------------------ shared

        /// <summary>
        ///     A stable issue code derived from the finding's own title, so the codes an assistant
        ///     sees stay a projection of the one finding engine rather than a second table that has
        ///     to be kept in step with it.
        /// </summary>
        private static string IssueCode(string title)
        {
            if (string.IsNullOrEmpty(title)) return "GAZE_ISSUE";

            var builder = new StringBuilder("GAZE_", title.Length + 6);
            for (int i = 0; i < title.Length; i++)
                builder.Append(char.IsLetterOrDigit(title[i]) ? char.ToUpperInvariant(title[i]) : '_');
            return builder.ToString();
        }

        private static string BoneName(Transform bone) => bone != null ? bone.name : string.Empty;

        private static string OnOff(bool value) => value ? "on" : "off";

        /// <summary>"ConversationLock" as the dropdown spells it: "Conversation Lock".</summary>
        private static string Humanize(string pascalCase)
        {
            var builder = new StringBuilder(pascalCase.Length + 4);
            for (int i = 0; i < pascalCase.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascalCase[i])) builder.Append(' ');
                builder.Append(pascalCase[i]);
            }

            return builder.ToString();
        }

        private static string[] Names(IReadOnlyList<GazeCapabilityId> capabilities)
        {
            if (capabilities == null) return Array.Empty<string>();
            var names = new string[capabilities.Count];
            for (int i = 0; i < capabilities.Count; i++) names[i] = capabilities[i].ToString();
            return names;
        }

        private static bool Contains(IReadOnlyList<GazeCapabilityId> list, GazeCapabilityId id)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == id) return true;
            return false;
        }

        private static object PointSchema(string description) => new
        {
            type = "object",
            description,
            properties = new Dictionary<string, object>
            {
                ["x"] = ConvaiMcpResponses.NumberSchema(0),
                ["y"] = ConvaiMcpResponses.NumberSchema(0),
                ["z"] = ConvaiMcpResponses.NumberSchema(0)
            },
            additionalProperties = false
        };

        /// <summary>
        ///     One finding, addressed two ways: <paramref name="affectedId" /> points at the thing
        ///     that is wrong (usually the Gaze component), while the suggested arguments must carry
        ///     the <em>character</em> id, because that is what <c>Convai.ConfigureGaze</c> takes. An
        ///     assistant that follows a suggestion built from the component id gets INVALID_CHARACTER.
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
    public static class ConvaiGazeAssistantTools
    {
        [AgentTool(
            "Add Convai Gaze to a character and tune how it makes eye contact. Never creates an asset.",
            "Convai.ConfigureGaze")]
        public static object ConfigureGaze(
            long characterInstanceId,
            string eyeContactMode = null,
            string focusFidelity = null,
            long playerAnchorInstanceId = 0,
            bool clearPlayerAnchorOverride = false,
            string bodyTurnStyle = null,
            string profileAssetPath = "",
            string[] capabilities = null,
            bool dryRun = true) =>
            ConvaiGazeMcpTools.Configure(new ConfigureGazeRequest
            {
                CharacterInstanceId = characterInstanceId,
                EyeContactMode = ParseEnum<GazeEyeContactMode>(eyeContactMode),
                FocusFidelity = ParseEnum<GazeFocusFidelity>(focusFidelity),
                PlayerAnchorInstanceId = playerAnchorInstanceId != 0 ? playerAnchorInstanceId : null,
                ClearPlayerAnchorOverride = clearPlayerAnchorOverride ? true : null,
                BodyTurnStyle = ParseEnum<GazeBodyTurnStyle>(bodyTurnStyle),
                ProfileAssetPath = profileAssetPath,
                Capabilities = capabilities,
                DryRun = dryRun
            });

        [AgentTool(
            "Explain why a Convai character is or is not looking at the player.",
            "Convai.DiagnoseGaze")]
        public static object DiagnoseGaze(long characterInstanceId = 0, bool includeRuntimeState = true) =>
            ConvaiGazeMcpTools.Diagnose(new DiagnoseGazeRequest
            {
                CharacterInstanceId = characterInstanceId,
                IncludeRuntimeState = includeRuntimeState
            });

        [AgentTool(
            "Mark scene objects as worth looking at, so Convai characters glance at them.",
            "Convai.MarkGazeTarget")]
        public static object MarkGazeTarget(
            long[] gameObjectInstanceIds, bool remove = false, bool dryRun = true) =>
            ConvaiGazeMcpTools.MarkTarget(new MarkGazeTargetRequest
            {
                GameObjectInstanceIds = gameObjectInstanceIds ?? Array.Empty<long>(),
                Remove = remove,
                DryRun = dryRun
            });

        private static T? ParseEnum<T>(string value) where T : struct, Enum =>
            !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out T parsed) ? parsed : null;
    }
}
