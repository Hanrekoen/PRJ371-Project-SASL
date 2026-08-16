using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Editor.AI;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Components;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Modules.BodyAnimation.Editor.AI
{
    /// <summary>
    ///     Input for <c>Convai.ConfigureBodyAnimation</c>. Every tuning field is optional, and
    ///     omitting one leaves that setting exactly as the project authored it.
    /// </summary>
    public sealed class ConfigureBodyAnimationRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeMovement { get; set; } = true;
        public string ProfileAssetPath { get; set; }
        public string AnimationSetAssetPath { get; set; }
        public string ConfigAssetPath { get; set; }
        public LocomotionSpeedProfile? SpeedProfile { get; set; }
        public float? AutoJogDistanceMeters { get; set; }
        public float? MinJogDistanceMeters { get; set; }
        public float? AccelerationMetersPerSecondSquared { get; set; }
        public float? RotationDegreesPerSecond { get; set; }
        public bool DryRun { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.DiagnoseBodyAnimation</c>.</summary>
    public sealed class DiagnoseBodyAnimationRequest
    {
        public long CharacterInstanceId { get; set; }
        public bool IncludeRuntimeState { get; set; } = true;
    }

    /// <summary>Input for <c>Convai.InspectBodyAnimationContent</c>.</summary>
    public sealed class InspectBodyAnimationContentRequest
    {
        public long CharacterInstanceId { get; set; }
        public string AnimationSetAssetPath { get; set; }
    }

    /// <summary>Input for <c>Convai.TuneBodyAnimationPersonality</c>.</summary>
    public sealed class TuneBodyAnimationPersonalityRequest
    {
        public long CharacterInstanceId { get; set; }
        public CharacterDemeanor? Archetype { get; set; }
        public float? HowExpressive { get; set; }
        public float? HowCalm { get; set; }
        public bool? KeepsBusyWhenAlone { get; set; }
        public float? HowOftenSeconds { get; set; }
        public bool MakeConfigUnique { get; set; }
        public bool DryRun { get; set; } = true;
    }

    /// <summary>
    ///     Convai Body Animation exposed through Unity's official MCP server: put body animation on
    ///     a character, see what its animation content can actually perform, tune how it moves and
    ///     how it comes across, and answer "why isn't this character doing anything?".
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every verdict in every response comes from <see cref="BodyAnimationSetupService" />
    ///         and <see cref="BodyAnimationTroubleshooter" /> — the same code the component
    ///         inspector and the Body Animation Editor window draw. This class contains no check of
    ///         its own, so an assistant and the editor cannot describe the same character
    ///         differently.
    ///     </para>
    ///     <para>
    ///         Adding the component is the one step performed here rather than in the service. The
    ///         service deliberately starts from a controller that already exists, because Add
    ///         Component is the gesture every Unity user knows — but an assistant has no Add
    ///         Component button, so this layer performs that single step and then hands off.
    ///     </para>
    ///     <para>
    ///         Nothing here creates an animation set, tags a clip, bakes clip measurements,
    ///         generates a mask, or touches an Animator Controller. Where one of those is the
    ///         answer, the response names the window mode or menu path that performs it.
    ///         <c>Convai.TuneBodyAnimationPersonality</c> is the single exception to writing on
    ///         disk, and only ever <em>duplicates</em> a config that already exists — see its own
    ///         remarks.
    ///     </para>
    /// </remarks>
    public static class ConvaiBodyAnimationMcpTools
    {
        private const string ConfigureTool = "Convai.ConfigureBodyAnimation";
        private const string DiagnoseTool = "Convai.DiagnoseBodyAnimation";
        private const string InspectContentTool = "Convai.InspectBodyAnimationContent";
        private const string TuneTool = "Convai.TuneBodyAnimationPersonality";

        private const string AddComponentMenuPath = "Add Component → Convai → Embodiment → Body Animation";
        private const string EditorWindowMenuPath = "Convai → Body Animation Editor";
        private const string MeasureClipsMenuPath = "Convai → Body Animation → Analyze Selected Animation Set";

        // ------------------------------------------------------------------ configure

        [McpTool(
            ConfigureTool,
            "Adds Convai Body Animation to a character and sets it up — the shipped animation content, whether the character can walk, and how it moves. Previews by default. Assigns existing content assets; never creates, edits or measures an asset, and never touches an Animator Controller. Omitted settings are left unchanged.",
            "Configure Convai Body Animation",
            Groups = new[] { "convai", "body-animation" },
            EnabledByDefault = true)]
        public static object Configure(JObject input) =>
            Configure(input?.ToObject<ConfigureBodyAnimationRequest>());

        public static object Configure(ConfigureBodyAnimationRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "Body animation can only be configured in Edit Mode.",
                    new { code = "PLAY_MODE_ACTIVE" });

            request ??= new ConfigureBodyAnimationRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            if (!TryResolveAsset(request.ProfileAssetPath, "Body Animation Profile",
                    out ConvaiBodyAnimationProfile profile, out error) ||
                !TryResolveAsset(request.AnimationSetAssetPath, "Animation Set",
                    out ConvaiBodyAnimationSet set, out error) ||
                !TryResolveAsset(request.ConfigAssetPath, "Body Animation Config",
                    out ConvaiBodyAnimationConfig config, out error))
            {
                return Response(false, error, new
                {
                    code = "INVALID_ASSET",
                    createMenuPath = "Assets → Create → Convai → Embodiment",
                    editorWindowMenuPath = EditorWindowMenuPath
                });
            }

            ConvaiBodyAnimationController controller =
                character.GetComponentInChildren<ConvaiBodyAnimationController>(true);

            // The rig verdict comes from the setup service either way — through the controller when
            // one exists, and through the same method on the bare Animator when one does not. A
            // character that cannot host the module is told so before anything is written to it,
            // including the component itself.
            BodyAnimationCheck rig = controller != null
                ? FindCheck(BodyAnimationSetupService.Inspect(controller), BodyAnimationSetupService.CheckRig)
                : BodyAnimationSetupService.InspectRig(
                    character.GetComponentInChildren<Animator>(true));

            if (rig.State == BodyAnimationCheckState.Blocked)
            {
                return Response(true, $"Convai Body Animation cannot run on '{character.gameObject.name}' yet.",
                    new
                    {
                        dryRun = request.DryRun,
                        complete = false,
                        changes = Array.Empty<string>(),
                        notes = Array.Empty<string>(),
                        blockers = new[]
                        {
                            new { code = "BODY_ANIMATION_RIG_BLOCKED", message = $"{rig.Label}: {rig.Detail}." }
                        },
                        readiness = new
                        {
                            state = controller != null
                                ? BodyAnimationReadiness.Blocked.ToString()
                                : BodyAnimationReadiness.NotInstalled.ToString(),
                            isWorking = false,
                            blocker = $"{rig.Label}: {rig.Detail}."
                        },
                        characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                        componentInstanceId = ConvaiMcpEntityRef.ToToolId(controller),
                        affectedInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                        sceneDirty = SceneManager.GetActiveScene().isDirty,
                        sceneSaved = false,
                        nextSteps = new[]
                        {
                            $"{rig.Label}: {rig.Detail}.",
                            "Nothing was changed on this character. Fix the rig, then call " +
                            "Convai.ConfigureBodyAnimation again."
                        }
                    });
            }

            List<FieldWrite> contentWrites = BuildContentWrites(profile, set, config);
            List<FieldWrite> movementWrites = BuildMovementWrites(request);

            var changes = new List<string>(8);
            var notes = new List<string>(4);

            if (controller == null) changes.Add("Add the Body Animation component to this character");
            PlanSetupChanges(controller, request.IncludeMovement, changes);
            PlanFieldChanges(controller, contentWrites, changes);
            PlanMovementChanges(controller, request, movementWrites, changes, notes);

            if (request.DryRun)
                return ConfigureResponse(true, character, controller, changes, notes);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                if (controller == null)
                    controller = Undo.AddComponent<ConvaiBodyAnimationController>(character.gameObject);

                // Content assignment first: the setup service only assigns the shipped default when
                // nothing is resolved, so an explicitly named asset must already be in place or the
                // service would add the default on top of it.
                WriteFields(controller, contentWrites, "Configure Convai Body Animation", notes);

                BodyAnimationSetupResult result = BodyAnimationSetupService.Apply(
                    controller, new BodyAnimationSetupOptions { IncludeMovement = request.IncludeMovement });
                notes.AddRange(result.Notes);

                ConvaiNavMeshLocomotion locomotion = FindLocomotion(controller);
                if (movementWrites.Count > 0)
                {
                    if (locomotion != null)
                        WriteFields(locomotion, movementWrites, "Configure Convai Body Animation", notes);
                    else
                        notes.Add(
                            "Movement settings were requested, but this character has no movement " +
                            "component, so they were not written. Call again with includeMovement true.");
                }

                EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
                Undo.SetCurrentGroupName("Configure Convai Body Animation");
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
                ["includeMovement"] = ConvaiMcpResponses.BooleanProperty(
                    "Add movement so the character can walk, jog, turn and stop. Leave it off for a " +
                    "receptionist or a seated character — everything else works in place.", true),
                ["profileAssetPath"] = ConvaiMcpResponses.StringProperty(
                    "Project path of an existing Body Animation Profile, which bundles an Animation " +
                    "Set and a Config. Never creates one — omit it and the SDK's shipped content is " +
                    "assigned instead."),
                ["animationSetAssetPath"] = ConvaiMcpResponses.StringProperty(
                    "Project path of an existing Animation Set, for a character that is not using a " +
                    "profile. Never creates one."),
                ["configAssetPath"] = ConvaiMcpResponses.StringProperty(
                    "Project path of an existing Body Animation Config, for a character that is not " +
                    "using a profile. Never creates one."),
                ["speedProfile"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "Whether a move walks or jogs. Auto decides per destination from how far it is; " +
                    "Walk and Jog force one gait for every move. Omit to leave unchanged.",
                    Enum.GetNames(typeof(LocomotionSpeedProfile))),
                ["autoJogDistanceMeters"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "Auto profile only: destinations farther than this are jogged to, nearer ones " +
                    "walked. Omit to leave unchanged."),
                ["minJogDistanceMeters"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "Legs shorter than this always walk, even on the Jog profile — jogging needs " +
                    "room to accelerate, cruise and plant a stop. Omit to leave unchanged."),
                ["accelerationMetersPerSecondSquared"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "How fast the character gets up to speed. Human-like ramps sit around 3–5; " +
                    "Unity's own default of 8 reads as a lurch. Omit to leave unchanged."),
                ["rotationDegreesPerSecond"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "Turn rate while following a path. Turning on the spot is animation-driven and " +
                    "ignores this. Omit to leave unchanged."),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the changes without touching the scene.", true)
            },
            "characterInstanceId");

        [McpOutputSchema(ConfigureTool)]
        public static object ConfigureOutput() => StandardSchema();

        // ------------------------------------------------------------------ diagnose

        [McpTool(
            DiagnoseTool,
            "Explains what a character's body animation is actually doing: whether it is set up at all, whether the rig can drive it, which animation content it resolved, which behaviours work and which are inert because this character has no clips for them, how the rig's scale is calibrated, and live state in Play Mode. Read-only.",
            "Diagnose Convai Body Animation",
            Groups = new[] { "convai", "body-animation", "validation" },
            EnabledByDefault = true)]
        public static object Diagnose(JObject input) =>
            Diagnose(input?.ToObject<DiagnoseBodyAnimationRequest>());

        public static object Diagnose(DiagnoseBodyAnimationRequest request)
        {
            request ??= new DiagnoseBodyAnimationRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            long characterId = ConvaiMcpEntityRef.ToToolId(character.gameObject);
            ConvaiBodyAnimationReport report = ConvaiBodyAnimationReport.For(character.gameObject);

            if (!report.IsPresent)
            {
                return Response(true, $"'{character.gameObject.name}' has no Body Animation component yet.",
                    new
                    {
                        present = false,
                        readiness = new
                        {
                            state = BodyAnimationReadiness.NotInstalled.ToString(),
                            isWorking = false,
                            blocker = string.Empty
                        },
                        characterInstanceId = characterId,
                        issues = new[]
                        {
                            Issue("BODY_ANIMATION_COMPONENT_MISSING", "Error",
                                "This character has no Body Animation component, so it will stand " +
                                $"perfectly still — no idle, no talking gestures. Add it with " +
                                $"{AddComponentMenuPath}, which is the only required step.",
                                character.gameObject.name, characterId, characterId, true)
                        },
                        nextSteps = new[]
                        {
                            $"Add Body Animation to '{character.gameObject.name}' — {AddComponentMenuPath}, " +
                            "or call Convai.ConfigureBodyAnimation.",
                            "Then run Convai.DiagnoseBodyAnimation again to confirm the rig can drive it."
                        }
                    });
            }

            long componentId = ConvaiMcpEntityRef.ToToolId(report.Controller);
            BodyAnimationReadiness state = report.State;

            return Response(
                true,
                state switch
                {
                    BodyAnimationReadiness.Working =>
                        $"Convai Body Animation is working on '{character.gameObject.name}'.",
                    BodyAnimationReadiness.NeedsContent =>
                        $"Convai Body Animation is on '{character.gameObject.name}' but has no " +
                        "animation content, so the character stays still.",
                    _ => $"Convai Body Animation cannot run on '{character.gameObject.name}' yet."
                },
                new
                {
                    present = true,
                    readiness = new
                    {
                        state = state.ToString(),
                        isWorking = report.IsWorking,
                        blocker = report.Blocker,
                        message = ReadinessMessage(state, report.Preflight)
                    },
                    characterInstanceId = characterId,
                    componentInstanceId = componentId,
                    checks = DescribeChecks(report.Preflight),
                    issues = DescribeIssues(report, componentId, characterId),
                    rig = DescribeRig(report),
                    content = DescribeContentSummary(report),
                    personality = DescribePersonality(report),
                    movement = DescribeMovement(report),
                    features = DescribeFeatures(report),
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
                    "Include what the character is animating right now. Play Mode only.", true)
            });

        [McpOutputSchema(DiagnoseTool)]
        public static object DiagnoseOutput() => StandardSchema();

        // ------------------------------------------------------------------ inspect content

        [McpTool(
            InspectContentTool,
            "Lists what a character's Animation Set can actually perform — the idle, talk, listen and think pools, every action and gesture with the names you can play it by, walking coverage, and pointing directions. Read-only. Call this before writing code that uses PlayAction.",
            "Inspect Convai Body Animation Content",
            Groups = new[] { "convai", "body-animation" },
            EnabledByDefault = true)]
        public static object InspectContent(JObject input) =>
            InspectContent(input?.ToObject<InspectBodyAnimationContentRequest>());

        public static object InspectContent(InspectBodyAnimationContentRequest request)
        {
            request ??= new InspectBodyAnimationContentRequest();

            ConvaiBodyAnimationSet set;
            string resolvedVia;
            int usedBy = 0;

            if (!string.IsNullOrWhiteSpace(request.AnimationSetAssetPath))
            {
                set = AssetDatabase.LoadAssetAtPath<ConvaiBodyAnimationSet>(request.AnimationSetAssetPath);
                if (set == null)
                    return Response(false,
                        $"No Animation Set exists at '{request.AnimationSetAssetPath}'. Create one with " +
                        $"the Create Animation Set wizard in {EditorWindowMenuPath}.",
                        new { code = "INVALID_ASSET" });

                resolvedVia = "Animation Set asset path";
            }
            else
            {
                if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                        out ConvaiCharacter character, out string error))
                    return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

                ConvaiBodyAnimationReport report = ConvaiBodyAnimationReport.For(character.gameObject);
                if (!report.IsPresent)
                    return Response(true, $"'{character.gameObject.name}' has no Body Animation component yet.",
                        new
                        {
                            hasContent = false,
                            nextSteps = new[]
                            {
                                $"Add Body Animation to '{character.gameObject.name}' — {AddComponentMenuPath}, " +
                                "or call Convai.ConfigureBodyAnimation."
                            }
                        });

                set = report.Set;
                resolvedVia = report.Input.HasProfileAsset ? "Profile" : "Animation Set field";
                if (set != null) usedBy = BodyAnimationUsage.CountUsing(set);

                if (set == null)
                    return Response(true,
                        $"'{character.gameObject.name}' has Body Animation but no animation content assigned.",
                        new
                        {
                            hasContent = false,
                            nextSteps = new[]
                            {
                                "Call Convai.ConfigureBodyAnimation to assign the animation content " +
                                "this project has, or — if it has none yet — import the Convai " +
                                "samples, or build your own with the Create Animation Set wizard in " +
                                $"{EditorWindowMenuPath}."
                            }
                        });
            }

            int filledSlots = ConvaiBodyAnimationReport.CountFilledLocomotionSlots(set);
            var setIssues = new List<BodyAnimationFinding>(8);
            set.CollectFindings(setIssues);

            return Response(
                true,
                $"'{set.DisplayName}' authors {set.Actions.Count} action(s) and fills " +
                $"{filledSlots} of {BodyAnimationContentCoverage.TotalSlots} locomotion slots.",
                new
                {
                    hasContent = true,
                    animationSetName = set.DisplayName,
                    animationSetAssetPath = AssetDatabase.GetAssetPath(set),
                    resolvedVia,
                    usedByCharacterCount = usedBy,
                    pools = DescribePools(set),
                    actions = DescribeActions(set),
                    locomotion = DescribeLocomotionCoverage(set, filledSlots),
                    pointing = DescribePointing(set),
                    upperBodyMask = DescribeMask(set),
                    issues = DescribeSetIssues(setIssues),
                    nextSteps = BuildContentNextSteps(set, setIssues)
                });
        }

        [McpSchema(InspectContentTool)]
        public static object InspectContentSchema() => ConvaiMcpResponses.ClosedObjectSchemaWithoutRequired(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character whose animation content to list. 0 uses the only one in " +
                    "the active scene. Ignored when animationSetAssetPath is given.", 0),
                ["animationSetAssetPath"] = ConvaiMcpResponses.StringProperty(
                    "Project path of an Animation Set to inspect directly, with no character " +
                    "involved — for comparing two sets, or reading one before assigning it.")
            });

        [McpOutputSchema(InspectContentTool)]
        public static object InspectContentOutput() => StandardSchema();

        // ------------------------------------------------------------------ tune personality

        /// <summary>
        ///     Tunes the character's personality, making the config its own first.
        /// </summary>
        /// <remarks>
        ///     A Body Animation Config can be shared by many characters at once, so writing to the
        ///     one a character happens to resolve would silently retune every other character using
        ///     it. This tool therefore copies the shared config for the named character before
        ///     changing anything — the same <em>Make Unique For This Character</em> command the
        ///     inspector offers, through the same code. It duplicates an asset that already exists;
        ///     it never authors one from scratch, and it is the only tool in this module that
        ///     writes to disk at all.
        /// </remarks>
        [McpTool(
            TuneTool,
            "Tunes how expressive and how calm a character is, and whether it keeps busy when alone. Because one Body Animation Config can be shared by many characters, this makes a private copy for the named character before changing anything, so tuning one character never changes another. Previews by default and needs explicit consent before copying.",
            "Tune Convai Body Animation Personality",
            Groups = new[] { "convai", "body-animation" },
            EnabledByDefault = true)]
        public static object Tune(JObject input) =>
            Tune(input?.ToObject<TuneBodyAnimationPersonalityRequest>());

        public static object Tune(TuneBodyAnimationPersonalityRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Response(false, "Personality can only be tuned in Edit Mode.",
                    new { code = "PLAY_MODE_ACTIVE" });

            request ??= new TuneBodyAnimationPersonalityRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string error))
                return Response(false, error, new { code = ConvaiMcpResolvers.CharacterErrorCode });

            ConvaiBodyAnimationReport report = ConvaiBodyAnimationReport.For(character.gameObject);
            if (!report.IsPresent)
                return Response(false,
                    $"'{character.gameObject.name}' has no Body Animation component, so there is no " +
                    "personality to tune.",
                    new { code = "BODY_ANIMATION_COMPONENT_MISSING", addComponentMenuPath = AddComponentMenuPath });

            ConvaiBodyAnimationConfig config = report.Config;
            if (config == null)
                return Response(false,
                    $"'{character.gameObject.name}' has no Body Animation Config, so it runs on the " +
                    "SDK's built-in defaults and there is no asset to tune. Setup does not create one " +
                    "on its own, so no character owns settings it never changed: press Give This " +
                    "Character Its Own Settings on the Body Animation component's Personality " +
                    "section, or assign a Config to the component yourself.",
                    new
                    {
                        code = "NO_CONFIG_ASSET",
                        // Deliberately not the configure tool. It leaves the config empty by design,
                        // so naming it here sent the caller to something that could never help.
                        inspectorAction = "Body Animation → Personality → Give This Character Its Own Settings",
                        createAssetMenuPath = "Create → Convai → Embodiment → Body Animation Config"
                    });

            if (!TryResolveArchetype(request.Archetype, out BodyAnimationArchetype archetype, out bool hasArchetype))
                return Response(false, $"'{request.Archetype}' is not a Convai body animation archetype.",
                    new
                    {
                        code = "INVALID_ARCHETYPE",
                        validArchetypes = ArchetypeNames()
                    });

            // The same ownership rule the inspector's own notice reads, so the tool and the editor
            // cannot disagree about whether a config is safe to tune in place.
            BodyAnimationPersonality.DescribeOwnership(
                config, out bool shipsWithSdk, out int sharedBy, out bool willCopy);
            List<string> otherCharacters = sharedBy > 1
                ? BodyAnimationUsage.NamesOfOthersUsing(config, report.Controller)
                : new List<string>(0);

            List<string> changes = PlanPersonalityChanges(config, request, hasArchetype, archetype);
            var warnings = new List<string>(2);

            if (request.KeepsBusyWhenAlone == true &&
                !report.Input.FeatureAvailability.AmbientActivities.HasContent)
            {
                warnings.Add(
                    "Keeps busy when alone was turned on, but this character's animation set tags no " +
                    "action as Ambient, so it has nothing to perform yet. Tag an activity clip in " +
                    $"{EditorWindowMenuPath}'s Content mode.");
            }


            if (request.DryRun)
            {
                return Response(true, "Previewed the personality change.", new
                {
                    dryRun = true,
                    changes,
                    warnings,
                    current = DescribePersonalityValues(config),
                    sharing = new
                    {
                        configName = config.name,
                        configAssetPath = AssetDatabase.GetAssetPath(config),
                        sharedByCharacterCount = sharedBy,
                        otherCharacters,
                        shipsWithSdk,
                        willCopyConfig = willCopy,
                        message = shipsWithSdk
                            ? "This is the animation config the SDK ships, so it is not tuned in " +
                              $"place — a copy will be made for '{character.gameObject.name}' under " +
                              "Assets. Editing the shipped one would change the default every future " +
                              "character inherits, and the next package update would overwrite it."
                            : willCopy
                                ? $"This config is shared by {sharedBy} characters in the open scenes. A " +
                                  $"private copy will be made for '{character.gameObject.name}' first, so " +
                                  "the others are left exactly as they are."
                                : "Only this character uses this config, so it is tuned in place and no " +
                                  "copy is needed."
                    },
                    characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps = willCopy
                        ? new[]
                        {
                            "Call Convai.TuneBodyAnimationPersonality again with dryRun false and " +
                            "makeConfigUnique true to copy the config and apply these values."
                        }
                        : new[]
                        {
                            "Call Convai.TuneBodyAnimationPersonality again with dryRun false to apply " +
                            "these values."
                        }
                });
            }

            if (willCopy && !request.MakeConfigUnique)
            {
                return Response(false,
                    shipsWithSdk
                        ? $"'{config.name}' is the animation config the SDK ships, so it is not " +
                          "edited in place — every character that uses it would change, and the " +
                          "next package update would overwrite the change."
                        : $"'{config.name}' is shared by {sharedBy} characters in the open scenes, so " +
                          "changing it would retune all of them.",
                    new
                    {
                        code = "CONFIG_SHARED_CONSENT_REQUIRED",
                        configName = config.name,
                        configAssetPath = AssetDatabase.GetAssetPath(config),
                        sharedByCharacterCount = sharedBy,
                        shipsWithSdk,
                        otherCharacters,
                        nextSteps = new[]
                        {
                            "Call again with makeConfigUnique true to give " +
                            $"'{character.gameObject.name}' its own copy of the config and tune only " +
                            "that character.",
                            "The other characters keep the shared config exactly as it is."
                        }
                    });
            }

            var notes = new List<string>(3);
            string configAssetPath = AssetDatabase.GetAssetPath(config);
            string profileAssetPath = string.Empty;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                if (willCopy)
                {
                    if (!BodyAnimationPersonality.TryMakeUnique(config, report.Controller,
                            out BodyAnimationMakeUniqueResult copy))
                    {
                        Undo.RevertAllDownToGroup(group);
                        return Response(false, copy.FailureReason, new { code = "MAKE_UNIQUE_FAILED" });
                    }

                    config = copy.Config;
                    configAssetPath = copy.ConfigAssetPath;
                    profileAssetPath = copy.ProfileAssetPath;
                    notes.Add(shipsWithSdk
                        ? $"Copied the SDK's shipped config to '{configAssetPath}' for " +
                          $"'{character.gameObject.name}' alone, leaving the shipped one untouched."
                        : $"Copied the shared config to '{configAssetPath}' for " +
                          $"'{character.gameObject.name}' alone.");
                    if (!string.IsNullOrEmpty(profileAssetPath))
                        notes.Add(
                            $"Copied its profile to '{profileAssetPath}' too, so the character " +
                            "actually reads the new config.");
                }

                if (hasArchetype) BodyAnimationPersonality.Apply(config, archetype);
                WritePersonalityValues(config, request);

                Undo.SetCurrentGroupName("Tune Convai Body Animation Personality");
                Undo.CollapseUndoOperations(group);
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(group);
                return Response(false, exception.Message, new { code = "AUTHORING_FAILED" });
            }

            return Response(true, "Tuned this character's personality.", new
            {
                dryRun = false,
                changes,
                notes,
                warnings,
                current = DescribePersonalityValues(config),
                sharing = new
                {
                    configName = config.name,
                    configAssetPath,
                    profileAssetPath,
                    sharedByCharacterCount = BodyAnimationPersonality.CountCharactersUsing(config),
                    copiedForThisCharacter = willCopy,
                    message = shipsWithSdk
                        ? "This character now has its own config under Assets; the SDK's shipped one " +
                          "is untouched, so other characters and the next package update are unaffected."
                        : willCopy
                            ? "This character now has its own config; the characters that shared the " +
                              "old one are untouched."
                            : "Only this character uses this config, so nothing else changed."
                },
                characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                affectedInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                sceneDirty = SceneManager.GetActiveScene().isDirty,
                sceneSaved = false,
                nextSteps = new[]
                {
                    "Enter Play Mode and talk to the character to feel the change.",
                    "Run Convai.DiagnoseBodyAnimation to confirm the values that landed."
                }
            });
        }

        [McpSchema(TuneTool)]
        public static object TuneSchema() => ConvaiMcpResponses.ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = ConvaiMcpResponses.IntegerProperty(
                    "The Convai Character to tune. 0 uses the only one in the active scene."),
                ["archetype"] = ConvaiMcpResponses.OptionalStringEnumProperty(
                    "A named personality preset, which writes a documented combination of the values " +
                    "below in one step. Composed is a receptionist or clerk; Warm is the SDK default; " +
                    "Energetic is a tour guide or host; Reserved is nearly still. Omit to leave the " +
                    "current values alone.",
                    ArchetypeNames()),
                ["howExpressive"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "How large and how frequent talking gestures are, from 0 to 2. 0 is nearly " +
                    "still, 1 is the default, 2 is maximally lively. Omit to leave unchanged."),
                ["howCalm"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "How long the character holds a pose and how gently it settles between them, " +
                    "from 0 to 2. Higher reads as more composed and deliberate. Omit to leave unchanged."),
                ["keepsBusyWhenAlone"] = ConvaiMcpResponses.OptionalBooleanProperty(
                    "Whether the character performs small activities on its own after a while of " +
                    "silence. It plays clips tagged Ambient, so it does nothing until the animation " +
                    "set has one. Omit to leave unchanged."),
                ["howOftenSeconds"] = ConvaiMcpResponses.OptionalNumberProperty(
                    "Average seconds between those activities. Omit to leave unchanged."),
                ["makeConfigUnique"] = ConvaiMcpResponses.BooleanProperty(
                    "Consent to giving this character its own copy of the config. Required before " +
                    "applying when the config is shared by more than one character; a preview always " +
                    "tells you whether it will be needed.", false),
                ["dryRun"] = ConvaiMcpResponses.BooleanProperty(
                    "Preview the change, and whether a copy is needed, without writing anything.", true)
            },
            "characterInstanceId");

        [McpOutputSchema(TuneTool)]
        public static object TuneOutput() => StandardSchema();

        // ------------------------------------------------------------------ configure helpers

        /// <summary>One optional setting: how to tell whether it already holds, and how to write it.</summary>
        /// <remarks>
        ///     Preview and apply walk this same list, so a previewed change and an applied one
        ///     cannot drift apart — the failure mode where "previews by default" quietly stops
        ///     meaning anything.
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

        private static List<FieldWrite> BuildContentWrites(
            ConvaiBodyAnimationProfile profile, ConvaiBodyAnimationSet set, ConvaiBodyAnimationConfig config)
        {
            var fields = new List<FieldWrite>(3);

            if (profile != null)
            {
                fields.Add(new FieldWrite("profile",
                    $"Animation content: the '{profile.name}' profile",
                    property => (ConvaiBodyAnimationProfile)property.objectReferenceValue == profile,
                    property => property.objectReferenceValue = profile));
            }

            if (set != null)
            {
                fields.Add(new FieldWrite("_animationSet",
                    $"Animation Set: '{set.name}'",
                    property => (ConvaiBodyAnimationSet)property.objectReferenceValue == set,
                    property => property.objectReferenceValue = set));
            }

            if (config != null)
            {
                fields.Add(new FieldWrite("_config",
                    $"Config: '{config.name}'",
                    property => (ConvaiBodyAnimationConfig)property.objectReferenceValue == config,
                    property => property.objectReferenceValue = config));
            }

            return fields;
        }

        private static List<FieldWrite> BuildMovementWrites(ConfigureBodyAnimationRequest request)
        {
            var fields = new List<FieldWrite>(5);

            if (request.SpeedProfile.HasValue)
            {
                int value = (int)request.SpeedProfile.Value;
                fields.Add(new FieldWrite("_speedProfile",
                    $"Speed Profile: {request.SpeedProfile.Value}",
                    property => property.enumValueIndex == value,
                    property => property.enumValueIndex = value));
            }

            AddFloat(fields, request.AutoJogDistanceMeters, "_autoJogDistance", "Auto Jog Distance", "m");
            AddFloat(fields, request.MinJogDistanceMeters, "_minJogDistance", "Min Jog Distance", "m");
            AddFloat(fields, request.AccelerationMetersPerSecondSquared, "_acceleration", "Acceleration", "m/s²");
            AddFloat(fields, request.RotationDegreesPerSecond, "_rotationDegreesPerSecond",
                "Rotation Degrees Per Second", "°/s");

            return fields;
        }

        private static void AddFloat(
            List<FieldWrite> fields, float? requested, string property, string label, string unit)
        {
            if (!requested.HasValue) return;

            float value = requested.Value;
            fields.Add(new FieldWrite(property, $"{label}: {value}{unit}",
                candidate => Mathf.Approximately(candidate.floatValue, value),
                candidate => candidate.floatValue = value));
        }

        /// <summary>
        ///     What the setup service will repair, taken from its own preflight rather than
        ///     re-derived — a preview that predicts the service by copying its rules is the second
        ///     source of truth this whole design exists to avoid.
        /// </summary>
        private static void PlanSetupChanges(
            ConvaiBodyAnimationController controller, bool includeMovement, List<string> changes)
        {
            if (controller == null)
            {
                // There is no controller to run a preflight against yet, so this is the one branch
                // that has to answer for the service — and it must answer the same question, not
                // assume content exists. Promising to assign content that is not in the project is
                // exactly the second source of truth described above.
                changes.Add(BodyAnimationSetupService.TryLoadDefaultProfile() != null
                    ? "Assign the animation content in this project (idle, talk, gestures, pointing, walking)"
                    : "Add the component only — this project has no animation clips yet, so there is "
                      + "none to assign");
                if (includeMovement) changes.Add("Add movement so the character can walk, jog, turn and stop");
                return;
            }

            BodyAnimationPreflight preflight = BodyAnimationSetupService.Inspect(controller);
            IReadOnlyList<BodyAnimationCheck> checks = preflight.Checks;
            if (checks == null) return;

            for (int i = 0; i < checks.Count; i++)
            {
                BodyAnimationCheck check = checks[i];
                if (check.State != BodyAnimationCheckState.Fixable) continue;
                changes.Add($"Set up {check.Label.ToLowerInvariant()} — {check.Detail}");
            }

            if (includeMovement && FindLocomotion(controller) == null)
                changes.Add("Add movement so the character can walk, jog, turn and stop");
        }

        private static void PlanFieldChanges(
            ConvaiBodyAnimationController controller, List<FieldWrite> fields, List<string> changes)
        {
            SerializedObject serialized = controller != null ? new SerializedObject(controller) : null;
            for (int i = 0; i < fields.Count; i++)
            {
                SerializedProperty property = serialized?.FindProperty(fields[i].Property);
                if (property != null && fields[i].Holds(property)) continue;
                changes.Add($"Set {fields[i].Label}");
            }
        }

        private static void PlanMovementChanges(
            ConvaiBodyAnimationController controller,
            ConfigureBodyAnimationRequest request,
            List<FieldWrite> fields,
            List<string> changes,
            List<string> notes)
        {
            if (fields.Count == 0) return;

            ConvaiNavMeshLocomotion locomotion = controller != null ? FindLocomotion(controller) : null;
            if (locomotion == null && !request.IncludeMovement)
            {
                notes.Add(
                    "Movement settings were named, but this character has no movement component and " +
                    "includeMovement is off, so they cannot be written. Set includeMovement to true.");
                return;
            }

            SerializedObject serialized = locomotion != null ? new SerializedObject(locomotion) : null;
            for (int i = 0; i < fields.Count; i++)
            {
                SerializedProperty property = serialized?.FindProperty(fields[i].Property);
                if (property != null && fields[i].Holds(property)) continue;
                changes.Add($"Set {fields[i].Label}");
            }
        }

        private static void WriteFields(
            UnityEngine.Object target, List<FieldWrite> fields, string undoName, List<string> notes)
        {
            if (fields.Count == 0) return;

            Undo.RecordObject(target, undoName);
            var serialized = new SerializedObject(target);
            for (int i = 0; i < fields.Count; i++)
            {
                SerializedProperty property = serialized.FindProperty(fields[i].Property);
                if (property == null)
                {
                    notes.Add($"This character has no {fields[i].Label} setting to write.");
                    continue;
                }

                fields[i].Write(property);
            }

            serialized.ApplyModifiedProperties();
        }

        private static object ConfigureResponse(
            bool dryRun,
            ConvaiCharacter character,
            ConvaiBodyAnimationController controller,
            List<string> changes,
            List<string> notes)
        {
            ConvaiBodyAnimationReport report = ConvaiBodyAnimationReport.For(controller);
            BodyAnimationReadiness state = report.State;

            var nextSteps = new List<string>(3);
            if (!string.IsNullOrEmpty(report.Blocker)) nextSteps.Add(report.Blocker);
            if (dryRun && changes.Count > 0)
                nextSteps.Add(
                    "Call Convai.ConfigureBodyAnimation again with dryRun false to apply these changes.");
            if (!dryRun && state == BodyAnimationReadiness.NeedsContent)
            {
                // The setup that just ran did everything it could. Sending the caller to the
                // diagnostic, or to a content listing with no content behind it, would confirm what
                // this response already said instead of moving anything forward.
                nextSteps.Add(
                    "Give this character animation clips: import the Convai samples, or build an "
                    + $"Animation Set from your own clip folder with Create Animation Set in {EditorWindowMenuPath}.");
                nextSteps.Add(
                    "Then call Convai.ConfigureBodyAnimation again to assign it, or drop the set on "
                    + "the component's Animation Set field.");
            }
            else if (!dryRun)
            {
                nextSteps.Add("Run Convai.DiagnoseBodyAnimation to confirm the character is ready.");
                nextSteps.Add(
                    "Run Convai.InspectBodyAnimationContent to see the gestures this character can play " +
                    "and the names PlayAction takes.");
            }

            return Response(
                true,
                dryRun ? "Previewed the Convai Body Animation setup." : "Configured Convai Body Animation.",
                new
                {
                    dryRun,
                    complete = state == BodyAnimationReadiness.Working,
                    changes,
                    notes,
                    blockers = string.IsNullOrEmpty(report.Blocker)
                        ? Array.Empty<object>()
                        : new object[] { new { code = "BODY_ANIMATION_BLOCKED", message = report.Blocker } },
                    readiness = new
                    {
                        state = state.ToString(),
                        isWorking = report.IsWorking,
                        blocker = report.Blocker,
                        message = ReadinessMessage(state, report.Preflight)
                    },
                    checks = DescribeChecks(report.Preflight),
                    movement = DescribeMovement(report),
                    characterInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    componentInstanceId = ConvaiMcpEntityRef.ToToolId(controller),
                    affectedInstanceId = ConvaiMcpEntityRef.ToToolId(character.gameObject),
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps
                });
        }

        private static bool TryResolveAsset<T>(string path, string label, out T asset, out string error)
            where T : UnityEngine.Object
        {
            asset = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return true;

            asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return true;

            error = $"No {label} exists at '{path}'. This tool never creates one — create it yourself, " +
                    $"or leave the path empty to use the SDK's shipped content.";
            return false;
        }

        private static BodyAnimationCheck FindCheck(BodyAnimationPreflight preflight, string id)
        {
            IReadOnlyList<BodyAnimationCheck> checks = preflight.Checks;
            if (checks == null) return default;

            for (int i = 0; i < checks.Count; i++)
                if (string.Equals(checks[i].Id, id, StringComparison.Ordinal))
                    return checks[i];

            return default;
        }

        private static ConvaiNavMeshLocomotion FindLocomotion(ConvaiBodyAnimationController controller)
        {
            if (controller == null) return null;

            ConvaiNavMeshLocomotion locomotion = controller.GetComponentInParent<ConvaiNavMeshLocomotion>(true);
            return locomotion != null
                ? locomotion
                : controller.GetComponentInChildren<ConvaiNavMeshLocomotion>(true);
        }

        // ------------------------------------------------------------------ diagnose helpers

        /// <summary>
        ///     Why the character is in this state, and the one thing to do about it.
        /// </summary>
        /// <remarks>
        ///     The needs-content answer depends on whether the project has content to assign, which
        ///     is why the preflight is passed in rather than the state alone. Telling a caller to
        ///     run the configure tool when there is nothing for it to assign sends it back to the
        ///     tool that just said so.
        /// </remarks>
        private static string ReadinessMessage(
            BodyAnimationReadiness state, BodyAnimationPreflight preflight) => state switch
        {
            BodyAnimationReadiness.NotInstalled =>
                "This character has no Body Animation component, so it stands perfectly still.",
            BodyAnimationReadiness.Blocked =>
                "The component is here, but something about the rig or the project stops it running. " +
                "Fix the blocker below — no amount of content will help until it is resolved.",
            BodyAnimationReadiness.NeedsContent => preflight.NeedsContent
                ? "The component is here and the rig is fine, but this project has no animation clips "
                  + "yet, so there is nothing to play. Import the Convai samples, or build an "
                  + "Animation Set from your own clip folder with Create Animation Set in the Body "
                  + "Animation editor."
                : "The component is here and the rig is fine, but no animation content is assigned, "
                  + "so there is nothing to play. Call Convai.ConfigureBodyAnimation to assign the "
                  + "content this project has.",
            _ =>
                "Set up and running. Any behaviour listed below as needing content is a missing clip " +
                "on this character's animation set, not a setup problem."
        };

        private static object[] DescribeChecks(BodyAnimationPreflight preflight)
        {
            IReadOnlyList<BodyAnimationCheck> checks = preflight.Checks;
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

        private static object[] DescribeIssues(
            in ConvaiBodyAnimationReport report, long componentId, long characterId)
        {
            List<BodyAnimationTroubleshooterFinding> findings = report.Findings;
            var issues = new List<object>(findings.Count);

            for (int i = 0; i < findings.Count; i++)
            {
                BodyAnimationTroubleshooterFinding finding = findings[i];
                if (finding.Severity == BodyAnimationTroubleshooterSeverity.Ok) continue;

                issues.Add(Issue(
                    ConvaiBodyAnimationReport.IssueCode(finding.Id),
                    finding.Severity.ToString(),
                    finding.Message,
                    finding.Title,
                    componentId,
                    characterId,
                    CanBeFixedNow(finding.Fix)));
            }

            return issues.ToArray();
        }

        /// <summary>
        ///     Whether a finding's repair can actually run right now, rather than merely existing.
        /// </summary>
        /// <remarks>
        ///     <c>autoFixable</c> is a promise to an unattended caller, so it has to answer for the
        ///     project as it stands. Assigning default content is the one repair whose success
        ///     depends on something outside this character: with no animation content anywhere, it
        ///     has nothing to assign and would report a note instead of a change.
        /// </remarks>
        private static bool CanBeFixedNow(BodyAnimationFixId fix) => fix switch
        {
            BodyAnimationFixId.None => false,
            BodyAnimationFixId.AssignDefaultContent => BodyAnimationSetupService.TryLoadDefaultProfile() != null,
            _ => true
        };

        private static object DescribeRig(in ConvaiBodyAnimationReport report)
        {
            BodyAnimationTroubleshooterInput input = report.Input;
            bool calibrated = !Mathf.Approximately(input.RigMotionScale, 1f);

            return new
            {
                hasAnimator = input.HasAnimator,
                isHumanoid = input.IsHumanoid,
                animatorName = report.Animator != null ? report.Animator.name : string.Empty,
                avatarName = report.Animator != null && report.Animator.avatar != null
                    ? report.Animator.avatar.name
                    : string.Empty,
                motionScale = input.RigMotionScale,
                calibrated,
                applyRootMotion = input.ApplyRootMotion,
                hasAnimatorController = input.HasAnimatorController,
                message = !input.HasAnimator
                    ? "No Animator was found on this character, so body animation cannot run at all. " +
                      "Add an Animator with a Humanoid avatar."
                    : !input.IsHumanoid
                        ? "This character's avatar is not Humanoid, so body animation cannot map its " +
                          "bones. Set the model's Rig → Animation Type to Humanoid in its import settings."
                        : calibrated
                            ? $"This rig measures {input.RigMotionScale:F2}x the animation content's " +
                              "reference scale, so walk and jog speeds and stop distances are " +
                              "calibrated automatically — nothing to do. If the feet still slide, " +
                              $"re-measure the clips with {MeasureClipsMenuPath}."
                            : "This rig matches the animation content's reference scale, so no speed " +
                              "correction is needed."
            };
        }

        private static object DescribeContentSummary(in ConvaiBodyAnimationReport report)
        {
            ConvaiBodyAnimationSet set = report.Set;
            if (set == null)
            {
                return new
                {
                    hasContent = false,
                    message = "No Animation Set is resolved, so this character has nothing to play. " +
                              "Call Convai.ConfigureBodyAnimation to assign the animation content " +
                              "this project has, or import the Convai samples if it has none yet."
                };
            }

            BodyAnimationFeatureAvailability availability = report.Input.FeatureAvailability;
            int filled = ConvaiBodyAnimationReport.CountFilledLocomotionSlots(set);

            return new
            {
                hasContent = true,
                animationSetName = set.DisplayName,
                animationSetAssetPath = AssetDatabase.GetAssetPath(set),
                resolvedVia = report.Input.HasProfileAsset ? "Profile" : "Animation Set field",
                summary =
                    $"Idle {availability.IdleVariantCount}, Talk {availability.TalkVariantCount}, " +
                    $"Listen {set.Listens.Count}, Think {set.Thinks.Count}, " +
                    $"Actions {set.Actions.Count}, Locomotion {filled}/{BodyAnimationContentCoverage.TotalSlots}",
                message = "Call Convai.InspectBodyAnimationContent for the full inventory, including " +
                          "the action names PlayAction takes."
            };
        }

        private static object DescribePersonality(in ConvaiBodyAnimationReport report)
        {
            ConvaiBodyAnimationConfig config = report.Config;
            if (config == null)
            {
                return new
                {
                    usingSdkDefaults = true,
                    message = "No Body Animation Config is assigned, so this character runs on the " +
                              "SDK's built-in defaults — which work. Assign one to give it a " +
                              "personality of its own."
                };
            }

            int sharedBy = BodyAnimationPersonality.CountCharactersUsing(config);
            return new
            {
                usingSdkDefaults = false,
                configName = config.name,
                configAssetPath = AssetDatabase.GetAssetPath(config),
                howExpressive = config.GestureLiveliness,
                howCalm = config.Calmness,
                keepsBusyWhenAlone = config.EnableAmbientActivities,
                howOftenSeconds = config.AmbientIntervalSeconds,
                archetype = MatchingArchetypeName(config),
                sharedByCharacterCount = sharedBy,
                message = sharedBy > 1
                    ? $"This config is shared by {sharedBy} characters in the open scenes, so changing " +
                      "it changes all of them. Convai.TuneBodyAnimationPersonality copies it for this " +
                      "character first."
                    : "Only this character uses this config, so it can be tuned freely."
            };
        }

        private static object DescribeMovement(in ConvaiBodyAnimationReport report)
        {
            ConvaiNavMeshLocomotion locomotion = FindLocomotion(report.Controller);
            if (locomotion == null)
            {
                return new
                {
                    present = false,
                    message = "This character has no movement component, so it idles, talks, gestures " +
                              "and points in place. That is a complete setup — plenty of characters " +
                              "should never move. Call Convai.ConfigureBodyAnimation with " +
                              "includeMovement true to let it walk."
                };
            }

            var serialized = new SerializedObject(locomotion);
            bool controllerPresent = report.IsPresent;

            return new
            {
                present = true,
                speedProfile = ((LocomotionSpeedProfile)serialized.FindProperty("_speedProfile").enumValueIndex)
                    .ToString(),
                autoJogDistanceMeters = serialized.FindProperty("_autoJogDistance").floatValue,
                minJogDistanceMeters = serialized.FindProperty("_minJogDistance").floatValue,
                accelerationMetersPerSecondSquared = serialized.FindProperty("_acceleration").floatValue,
                rotationDegreesPerSecond = serialized.FindProperty("_rotationDegreesPerSecond").floatValue,
                componentWalkSpeed = locomotion.WalkSpeed,
                componentJogSpeed = locomotion.JogSpeed,
                speedSource = controllerPresent
                    ? "The animation content's measured clip speeds. The component's own Walk Speed " +
                      "and Jog Speed are overridden while Body Animation runs, which is what keeps " +
                      "the feet from sliding — so tuning them here would have no effect."
                    : "The component's own Walk Speed and Jog Speed, since no Body Animation " +
                      "controller is present to override them.",
                message = "Walking, jogging, animated turns and stops are available. A scene with no " +
                          "baked NavMesh still idles, talks and gestures — only movement needs one."
            };
        }

        private static object[] DescribeFeatures(in ConvaiBodyAnimationReport report)
        {
            List<ConvaiBodyAnimationFeature> features = ConvaiBodyAnimationFeatures.Describe(in report);
            var described = new object[features.Count];

            for (int i = 0; i < features.Count; i++)
            {
                described[i] = new
                {
                    name = features[i].Name,
                    state = features[i].State.ToString(),
                    enabled = features[i].Enabled,
                    hasContent = features[i].HasContent,
                    settingLabel = features[i].SettingLabel,
                    message = features[i].Message
                };
            }

            return described;
        }

        private static object DescribeRuntime(in ConvaiBodyAnimationReport report, bool include)
        {
            ConvaiBodyAnimationController controller = report.Controller;
            if (!include || !EditorApplication.isPlaying || controller == null ||
                !controller.isActiveAndEnabled)
                return null;

            BodyAnimationSnapshot snapshot = controller.CaptureSnapshot();

            var layers = new List<object>(snapshot.Layers.Count);
            for (int i = 0; i < snapshot.Layers.Count; i++)
            {
                BodyAnimationLayerSnapshot layer = snapshot.Layers[i];
                layers.Add(new
                {
                    name = layer.Name,
                    state = layer.State,
                    clip = layer.Clip,
                    weight = layer.FinalWeight
                });
            }

            var trace = new List<object>(snapshot.RecentTrace.Count);
            for (int i = 0; i < snapshot.RecentTrace.Count; i++)
            {
                trace.Add(new
                {
                    time = snapshot.RecentTrace[i].Time,
                    level = snapshot.RecentTrace[i].Level.ToString(),
                    message = snapshot.RecentTrace[i].Message
                });
            }

            return new
            {
                isRuntimeBuilt = controller.IsRuntimeBuilt,
                animationSetName = snapshot.SetName,
                dialogueState = snapshot.DialogueState.ToString(),
                speechEnergy = snapshot.SpeechEnergy,
                movementState = snapshot.LocomotionState,
                agentSpeed = snapshot.AgentSpeed,
                animationSpeed = snapshot.AnimationSpeed,
                footSlide = Mathf.Abs(snapshot.AgentSpeed - snapshot.AnimationSpeed),
                currentAction = controller.CurrentActionName,
                animationSetSwapPending = controller.IsAnimationSetSwapPending,
                layers,
                recentTransitions = trace,
                transitionLogMessage = trace.Count > 0
                    ? string.Empty
                    : "The transition log is empty because Trace Verbosity is Off, which is how it " +
                      "ships — a character that walks and talks transitions constantly, and a console " +
                      "full of routine play-by-play is where a real warning goes unread. Raise it to " +
                      "State on the one character you are diagnosing, and put it back afterwards. " +
                      "Everything else above reports regardless."
            };
        }

        private static string[] BuildNextSteps(in ConvaiBodyAnimationReport report)
        {
            var steps = new List<string>(5);

            if (!string.IsNullOrEmpty(report.Blocker)) steps.Add(report.Blocker);

            List<BodyAnimationTroubleshooterFinding> findings = report.Findings;
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity != BodyAnimationTroubleshooterSeverity.Error) continue;
                if (!steps.Contains(findings[i].Message)) steps.Add(findings[i].Message);
            }

            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].Severity != BodyAnimationTroubleshooterSeverity.Warning) continue;
                if (!steps.Contains(findings[i].Message)) steps.Add(findings[i].Message);
            }

            if (steps.Count == 0)
            {
                steps.Add(
                    "Nothing needs doing — press Play and the character idles, and gestures while it talks.");
                steps.Add(
                    "Run Convai.InspectBodyAnimationContent to see the gestures it can play by name.");
            }

            return steps.ToArray();
        }

        // ------------------------------------------------------------------ content helpers

        private static object DescribePools(ConvaiBodyAnimationSet set)
        {
            int additiveTwins = ConvaiBodyAnimationReport.CountTalkEntriesWithAdditiveClip(set);

            return new
            {
                idle = new
                {
                    count = ConvaiBodyAnimationReport.VariantClipNames(set.Idles).Length,
                    clipNames = ConvaiBodyAnimationReport.VariantClipNames(set.Idles),
                    message = set.HasAnyIdle
                        ? "The character has something to stand and breathe in."
                        : "No valid idle clip, so the character will not animate at all. Every set " +
                          "needs at least one looping idle."
                },
                talk = new
                {
                    count = ConvaiBodyAnimationReport.VariantClipNames(set.Talks).Length,
                    clipNames = ConvaiBodyAnimationReport.VariantClipNames(set.Talks),
                    withAdditiveClip = additiveTwins,
                    message = !set.HasAnyTalk
                        ? "No valid talk clip, so the character stays in its idle pose while speaking."
                        : additiveTwins > 0
                            ? $"{additiveTwins} talk entries carry an Additive Clip, so Moving Talk " +
                              "Mode's Auto setting layers gestures over the walk cycle for those."
                            : "No talk entry carries an Additive Clip, so Moving Talk Mode's Auto " +
                              "setting uses the softened override while walking. A complete, intended " +
                              "behaviour — bake additive twins if walk-and-talk matters to your scene."
                },
                listen = new
                {
                    count = ConvaiBodyAnimationReport.VariantClipNames(set.Listens).Length,
                    clipNames = ConvaiBodyAnimationReport.VariantClipNames(set.Listens),
                    message = set.HasAnyListen
                        ? "The character holds a listening pose while the player speaks."
                        : "No Listen entries, so listening acting is inactive and the layer releases " +
                          "to idle instead. The SDK's shipped set authors none."
                },
                think = new
                {
                    count = ConvaiBodyAnimationReport.VariantClipNames(set.Thinks).Length,
                    clipNames = ConvaiBodyAnimationReport.VariantClipNames(set.Thinks),
                    message = set.HasAnyThink
                        ? "The character holds a thinking pose while it works out a reply."
                        : "No Think entries, so thinking acting is inactive and the layer releases to " +
                          "idle instead. The SDK's shipped set authors none."
                }
            };
        }

        private static object[] DescribeActions(ConvaiBodyAnimationSet set)
        {
            IReadOnlyList<ActionEntry> actions = set.Actions;
            var described = new List<object>(actions.Count);

            for (int i = 0; i < actions.Count; i++)
            {
                ActionEntry action = actions[i];
                if (action == null) continue;

                described.Add(new
                {
                    name = action.ActionName,
                    aliases = action.Aliases,
                    isValid = action.IsValid,
                    clipName = action.Clip != null ? action.Clip.name : string.Empty,
                    cue = action.Cue.ToString(),
                    ambient = action.Ambient,
                    maskMode = action.MaskMode.ToString(),
                    loopMode = action.LoopMode.ToString(),
                    interruptible = action.Interruptible,
                    suspendsLocomotion = action.SuspendsLocomotion,
                    allowConversationOverlays = action.AllowConversationOverlays
                });
            }

            return described.ToArray();
        }

        private static object DescribeLocomotionCoverage(ConvaiBodyAnimationSet set, int filledSlots)
        {
            SerializedProperty locomotion = BodyAnimationContentCoverage.LocomotionPropertyOf(set);
            var cells = new List<object>(10);

            AppendCoverageRow(cells, locomotion,
                BodyAnimationEditorStrings.LocomotionRowWalk, BodyAnimationContentCoverage.WalkCells);
            AppendCoverageRow(cells, locomotion,
                BodyAnimationEditorStrings.LocomotionRowJog, BodyAnimationContentCoverage.JogCells);

            return new
            {
                filledSlots,
                totalSlots = BodyAnimationContentCoverage.TotalSlots,
                coverage = cells,
                clipsMissingMeasurement = CountClipsMissingMeasurement(set),
                message = BodyAnimationFixes.DescribeLocomotionCoverage(set),
                measureClipsMenuPath = MeasureClipsMenuPath
            };
        }

        private static void AppendCoverageRow(
            List<object> cells,
            SerializedProperty locomotion,
            string rowLabel,
            LocomotionCoverageCell[] row)
        {
            for (int i = 0; i < row.Length; i++)
            {
                LocomotionCoverageCell cell = row[i];
                int total = cell.Slots?.Length ?? 0;
                int filled = BodyAnimationContentCoverage.CountFilled(locomotion, cell);

                cells.Add(new
                {
                    row = rowLabel,
                    column = cell.ColumnLabel,
                    filled,
                    total,
                    message = total == 0
                        ? cell.DisabledFeatureText
                        : filled == 0
                            ? cell.DisabledFeatureText
                            : filled == total
                                ? "Fully covered."
                                : $"{filled} of {total} filled — the rest blend instead."
                });
            }
        }

        private static int CountClipsMissingMeasurement(ConvaiBodyAnimationSet set)
        {
            var assigned = new List<(string slot, LocomotionClip clip)>();
            set.Locomotion.CollectAssigned(assigned);

            int missing = 0;
            for (int i = 0; i < assigned.Count; i++)
            {
                LocomotionClip clip = assigned[i].clip;
                if (clip?.Metadata == null || !clip.Metadata.HasSpeed) missing++;
            }

            return missing;
        }

        private static object DescribePointing(ConvaiBodyAnimationSet set) => new
        {
            directionCount = set.Pointing.Entries.Count,
            message = set.Pointing.HasAny
                ? "PointAt picks the authored direction closest to the target."
                : "No pointing directions are authored, so PointAt has nothing to play. Add them in " +
                  $"{EditorWindowMenuPath}'s Content mode."
        };

        private static object DescribeMask(ConvaiBodyAnimationSet set) => new
        {
            assigned = set.UpperBodyMask != null,
            maskName = set.UpperBodyMask != null ? set.UpperBodyMask.name : string.Empty,
            message = set.UpperBodyMask != null
                ? "Talking, pointing and upper-body gestures blend over the legs instead of replacing " +
                  "the whole skeleton."
                : "No upper-body overlay mask. If this set authors talk, pointing or upper-body " +
                  "gestures, those layers would drive the full skeleton and fight walking. The Body " +
                  "Animation Editor's setup checklist can generate a standard mask for you."
        };

        private static object[] DescribeSetIssues(List<BodyAnimationFinding> findings)
        {
            var described = new object[findings.Count];
            for (int i = 0; i < findings.Count; i++)
            {
                described[i] = new
                {
                    code = ConvaiBodyAnimationReport.IssueCode(findings[i].Id),
                    message = findings[i].Message
                };
            }

            return described;
        }

        private static string[] BuildContentNextSteps(
            ConvaiBodyAnimationSet set, List<BodyAnimationFinding> issues)
        {
            var steps = new List<string>(4);

            for (int i = 0; i < issues.Count; i++)
                if (!steps.Contains(issues[i].Message))
                    steps.Add(issues[i].Message);

            if (CountClipsMissingMeasurement(set) > 0)
                steps.Add(
                    "Some locomotion clips have not been measured, which is the usual cause of sliding " +
                    $"feet. Run {MeasureClipsMenuPath}, or the Measure Clips button in " +
                    $"{EditorWindowMenuPath}'s Content mode.");

            if (steps.Count == 0)
                steps.Add(
                    "This content is healthy. Play any action above by its name or an alias with " +
                    "PlayAction on the Body Animation component.");

            return steps.ToArray();
        }

        // ------------------------------------------------------------------ personality helpers

        private static string[] ArchetypeNames()
        {
            BodyAnimationArchetype[] archetypes = BodyAnimationPersonality.Archetypes;
            var names = new string[archetypes.Length];
            for (int i = 0; i < archetypes.Length; i++) names[i] = archetypes[i].Demeanor.ToString();
            return names;
        }

        private static bool TryResolveArchetype(
            CharacterDemeanor? requested, out BodyAnimationArchetype archetype, out bool hasArchetype)
        {
            archetype = default;
            hasArchetype = false;
            if (!requested.HasValue) return true;

            BodyAnimationArchetype[] archetypes = BodyAnimationPersonality.Archetypes;
            for (int i = 0; i < archetypes.Length; i++)
            {
                if (archetypes[i].Demeanor != requested.Value) continue;
                archetype = archetypes[i];
                hasArchetype = true;
                return true;
            }

            return false;
        }

        private static string MatchingArchetypeName(ConvaiBodyAnimationConfig config)
        {
            BodyAnimationArchetype[] archetypes = BodyAnimationPersonality.Archetypes;
            for (int i = 0; i < archetypes.Length; i++)
                if (BodyAnimationPersonality.Matches(config, archetypes[i]))
                    return archetypes[i].Name;

            return "Custom";
        }

        private static List<string> PlanPersonalityChanges(
            ConvaiBodyAnimationConfig config,
            TuneBodyAnimationPersonalityRequest request,
            bool hasArchetype,
            BodyAnimationArchetype archetype)
        {
            var changes = new List<string>(4);

            if (hasArchetype && !BodyAnimationPersonality.Matches(config, archetype))
                changes.Add($"Apply the {archetype.Name} archetype ({archetype.Description})");

            if (request.HowExpressive.HasValue &&
                !Mathf.Approximately(config.GestureLiveliness, request.HowExpressive.Value))
                changes.Add($"Set How expressive to {request.HowExpressive.Value}");

            if (request.HowCalm.HasValue && !Mathf.Approximately(config.Calmness, request.HowCalm.Value))
                changes.Add($"Set How calm to {request.HowCalm.Value}");

            if (request.KeepsBusyWhenAlone.HasValue &&
                config.EnableAmbientActivities != request.KeepsBusyWhenAlone.Value)
                changes.Add(
                    $"Turn Keeps busy when alone {(request.KeepsBusyWhenAlone.Value ? "on" : "off")}");

            if (request.HowOftenSeconds.HasValue &&
                !Mathf.Approximately(config.AmbientIntervalSeconds, request.HowOftenSeconds.Value))
                changes.Add($"Set How often to {request.HowOftenSeconds.Value}s");

            return changes;
        }

        /// <summary>
        ///     Writes through the config's serialized fields so its own <c>OnValidate</c> clamping
        ///     applies exactly as it does when a user drags the inspector slider.
        /// </summary>
        private static void WritePersonalityValues(
            ConvaiBodyAnimationConfig config, TuneBodyAnimationPersonalityRequest request)
        {
            var serialized = new SerializedObject(config);
            bool changed = false;

            changed |= SetFloat(serialized, "_gestureLiveliness", request.HowExpressive);
            changed |= SetFloat(serialized, "_calmness", request.HowCalm);
            changed |= SetFloat(serialized, "_ambientIntervalSeconds", request.HowOftenSeconds);

            if (request.KeepsBusyWhenAlone.HasValue)
            {
                SerializedProperty property = serialized.FindProperty("_enableAmbientActivities");
                if (property != null)
                {
                    property.boolValue = request.KeepsBusyWhenAlone.Value;
                    changed = true;
                }
            }

            if (!changed) return;

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
        }

        private static bool SetFloat(SerializedObject serialized, string field, float? value)
        {
            if (!value.HasValue) return false;

            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) return false;

            property.floatValue = value.Value;
            return true;
        }

        private static object DescribePersonalityValues(ConvaiBodyAnimationConfig config) => new
        {
            howExpressive = config.GestureLiveliness,
            howCalm = config.Calmness,
            keepsBusyWhenAlone = config.EnableAmbientActivities,
            howOftenSeconds = config.AmbientIntervalSeconds,
            archetype = MatchingArchetypeName(config)
        };

        // ------------------------------------------------------------------ shared

        /// <summary>
        ///     One finding, addressed two ways: <paramref name="affectedId" /> points at the thing
        ///     that is wrong (usually the Body Animation component), while the suggested arguments
        ///     must carry the <em>character</em> id, because that is what
        ///     <c>Convai.ConfigureBodyAnimation</c> takes. An assistant that follows a suggestion
        ///     built from the component id gets INVALID_CHARACTER.
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

    /// <summary>The same four tools, for Unity's in-editor assistant.</summary>
    public static class ConvaiBodyAnimationAssistantTools
    {
        [AgentTool(
            "Add Convai Body Animation to a character and set it up. Never creates or measures an asset.",
            "Convai.ConfigureBodyAnimation")]
        public static object ConfigureBodyAnimation(
            long characterInstanceId,
            bool includeMovement = true,
            string profileAssetPath = "",
            string animationSetAssetPath = "",
            string configAssetPath = "",
            string speedProfile = null,
            bool dryRun = true) =>
            ConvaiBodyAnimationMcpTools.Configure(new ConfigureBodyAnimationRequest
            {
                CharacterInstanceId = characterInstanceId,
                IncludeMovement = includeMovement,
                ProfileAssetPath = profileAssetPath,
                AnimationSetAssetPath = animationSetAssetPath,
                ConfigAssetPath = configAssetPath,
                SpeedProfile = ParseEnum<LocomotionSpeedProfile>(speedProfile),
                DryRun = dryRun
            });

        [AgentTool(
            "Explain what a Convai character's body animation is doing, and what is inert for want of clips.",
            "Convai.DiagnoseBodyAnimation")]
        public static object DiagnoseBodyAnimation(
            long characterInstanceId = 0, bool includeRuntimeState = true) =>
            ConvaiBodyAnimationMcpTools.Diagnose(new DiagnoseBodyAnimationRequest
            {
                CharacterInstanceId = characterInstanceId,
                IncludeRuntimeState = includeRuntimeState
            });

        [AgentTool(
            "List the gestures, pools and walking coverage a Convai character's animation content provides.",
            "Convai.InspectBodyAnimationContent")]
        public static object InspectBodyAnimationContent(
            long characterInstanceId = 0, string animationSetAssetPath = "") =>
            ConvaiBodyAnimationMcpTools.InspectContent(new InspectBodyAnimationContentRequest
            {
                CharacterInstanceId = characterInstanceId,
                AnimationSetAssetPath = animationSetAssetPath
            });

        [AgentTool(
            "Tune how expressive and how calm a Convai character is, copying a shared config for it first.",
            "Convai.TuneBodyAnimationPersonality")]
        public static object TuneBodyAnimationPersonality(
            long characterInstanceId,
            string archetype = null,
            bool makeConfigUnique = false,
            bool dryRun = true) =>
            ConvaiBodyAnimationMcpTools.Tune(new TuneBodyAnimationPersonalityRequest
            {
                CharacterInstanceId = characterInstanceId,
                Archetype = ParseEnum<CharacterDemeanor>(archetype),
                MakeConfigUnique = makeConfigUnique,
                DryRun = dryRun
            });

        private static T? ParseEnum<T>(string value) where T : struct, Enum =>
            !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out T parsed) ? parsed : null;
    }
}
