using System.Collections.Generic;
using Convai.Application;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Shared.Compatibility;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Editor.AI
{
    /// <summary>
    ///     Convai-specific tools exposed through Unity's official MCP server.
    /// </summary>
    public static partial class ConvaiMcpTools
    {
        private const int ToolContractVersion = 4;
        private const string GetGuidanceTool = "Convai.GetGuidance";
        private const string GetProjectStatusTool = "Convai.GetProjectStatus";
        private const string InspectSceneTool = "Convai.InspectScene";
        private const string ValidateSetupTool = "Convai.ValidateSetup";
        private const string BootstrapSceneTool = "Convai.BootstrapScene";

        [McpTool(
            GetGuidanceTool,
            "Loads concise Convai SDK workflow guidance. Call before configuring a Convai feature; do not use for generic Unity operations.",
            "Get Convai Guidance",
            Groups = new[] { "convai", "resources" },
            EnabledByDefault = true)]
        public static object GetGuidance(JObject parameters) =>
            GetGuidance(Parse<ConvaiGuidanceRequest>(parameters));

        public static object GetGuidance(ConvaiGuidanceRequest request)
        {
            ConvaiGuidanceTopic topic = request?.Topic ?? ConvaiGuidanceTopic.Overview;
            Guidance guidance = BuildGuidance(topic);
            return Success(
                $"Loaded Convai guidance for {topic}.",
                new
                {
                    topic = topic.ToString(),
                    guidance.Summary,
                    guidance.Prerequisites,
                    guidance.Workflow,
                    guidance.ConvaiTools,
                    guidance.UnityTools,
                    guidance.Documentation
                });
        }

        [McpOutputSchema(GetGuidanceTool)]
        public static object GetGuidanceOutputSchema() => StandardResponseSchema();

        [McpSchema(GetGuidanceTool)]
        public static object GetGuidanceInputSchema() => EnumInputSchema(
            "topic",
            "Convai workflow topic to load.",
            ConvaiGuidanceTopic.Overview);

        [McpTool(
            GetProjectStatusTool,
            "Reads Convai SDK, Unity AI Assistant, and non-secret project configuration status. Never returns the Convai API key.",
            "Get Convai Project Status",
            Groups = new[] { "convai", "validation" },
            EnabledByDefault = true)]
        public static object GetProjectStatus(JObject _) => GetProjectStatus();

        public static object GetProjectStatus()
        {
            ConvaiSettings settings = ConvaiSettings.Instance;
            UnityEditor.PackageManager.PackageInfo assistantPackage =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpToolAttribute).Assembly);

            return Success(
                "Read Convai project status.",
                new
                {
                    sdkVersion = ConvaiSDK.Version.ToString(),
                    unityVersion = UnityEngine.Application.unityVersion,
                    assistantVersion = assistantPackage?.version ?? "unknown",
                    toolContractVersion = ToolContractVersion,
                    credentialsConfigured = settings != null && settings.HasApiKey,
                    serverUrl = settings?.ServerUrl ?? string.Empty,
                    transcriptSystemEnabled = settings != null && settings.TranscriptSystemEnabled,
                    notificationSystemEnabled = settings != null && settings.NotificationSystemEnabled,
                    backgroundPolicy = settings?.BackgroundPolicy.ToString() ?? "unknown",
                    defaultMicrophoneDeviceId = settings?.DefaultMicrophoneDeviceId ?? string.Empty,
                    connectionTimeoutSeconds = settings?.ConnectionTimeout ?? 0f,
                    isPlaying = EditorApplication.isPlaying,
                    isCompiling = EditorApplication.isCompiling,
                    packageRoot = TryGetPackageRoot()
                });
        }

        [McpOutputSchema(GetProjectStatusTool)]
        public static object GetProjectStatusOutputSchema() => StandardResponseSchema();

        [McpSchema(GetProjectStatusTool)]
        public static object GetProjectStatusInputSchema() => EmptyInputSchema();

        [McpTool(
            InspectSceneTool,
            "Inspects open scenes for Convai managers, rooms, players, and characters. Use returned instance IDs for later mutations.",
            "Inspect Convai Scene",
            Groups = new[] { "convai", "scene" },
            EnabledByDefault = true)]
        public static object InspectScene(JObject parameters) =>
            InspectScene(Parse<ConvaiSceneInspectionRequest>(parameters));

        public static object InspectScene(ConvaiSceneInspectionRequest request)
        {
            bool includeInactive = request?.IncludeInactive ?? true;
            FindObjectsInactive inactiveMode = includeInactive
                ? FindObjectsInactive.Include
                : FindObjectsInactive.Exclude;

            ConvaiManager[] managers = ConvaiObjectFind.All<ConvaiManager>(inactiveMode);
            ConvaiRoomManager[] rooms = ConvaiObjectFind.All<ConvaiRoomManager>(inactiveMode);
            ConvaiPlayer[] players = ConvaiObjectFind.All<ConvaiPlayer>(inactiveMode);
            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(inactiveMode);

            var openScenes = new List<object>(SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                openScenes.Add(new
                {
                    handle = scene.handle.GetHashCode(),
                    scene.name,
                    scene.path,
                    scene.isLoaded,
                    scene.isDirty
                });
            }

            return Success(
                "Inspected open scenes for Convai components.",
                new
                {
                    includeInactive,
                    scenes = openScenes,
                    managers = DescribeComponents(managers),
                    rooms = DescribeComponents(rooms),
                    players = DescribePlayers(players),
                    characters = DescribeCharacters(characters),
                    counts = new
                    {
                        managers = managers.Length,
                        rooms = rooms.Length,
                        players = players.Length,
                        characters = characters.Length
                    }
                });
        }

        [McpOutputSchema(InspectSceneTool)]
        public static object InspectSceneOutputSchema() => StandardResponseSchema();

        [McpSchema(InspectSceneTool)]
        public static object InspectSceneInputSchema() => BooleanInputSchema(
            "includeInactive",
            "Include disabled GameObjects and components in the inspection.",
            true);

        [McpTool(
            ValidateSetupTool,
            "Validates Convai project and scene readiness without changing assets or scenes. Call before and after Convai authoring operations.",
            "Validate Convai Setup",
            Groups = new[] { "convai", "validation" },
            EnabledByDefault = true)]
        public static object ValidateSetup(JObject parameters) =>
            ValidateSetup(Parse<ConvaiValidationRequest>(parameters));

        public static object ValidateSetup(ConvaiValidationRequest request)
        {
            ConvaiValidationScope scope = request?.Scope ?? ConvaiValidationScope.All;
            var errors = new List<string>();
            var warnings = new List<string>();
            var nextSteps = new List<string>();

            if (scope is ConvaiValidationScope.All or ConvaiValidationScope.Project)
            {
                ConvaiSettings settings = ConvaiSettings.Instance;
                if (settings == null || !settings.HasApiKey)
                {
                    warnings.Add("API key not configured (Edit > Project Settings > Convai SDK)");
                    nextSteps.Add("Configure the API key manually in Edit > Project Settings > Convai SDK.");
                }
            }

            if (scope is ConvaiValidationScope.All or ConvaiValidationScope.Scene)
            {
                ConvaiSceneSetupApi.ValidationReport sceneReport = ConvaiSceneSetupApi.ValidateCurrentScene();
                AddUnique(errors, sceneReport.Errors);
                AddUnique(warnings, sceneReport.Warnings);
                AddUnique(nextSteps, sceneReport.NextSteps);
                AddModuleFindings(errors, warnings, nextSteps);
            }

            bool success = errors.Count == 0;
            return Result(
                success,
                success ? "Convai setup validation completed." : "Convai setup validation found blocking issues.",
                new
                {
                    scope = scope.ToString(),
                    errors,
                    warnings,
                    nextSteps
                });
        }

        [McpOutputSchema(ValidateSetupTool)]
        public static object ValidateSetupOutputSchema() => StandardResponseSchema();

        [McpSchema(ValidateSetupTool)]
        public static object ValidateSetupInputSchema() => EnumInputSchema(
            "scope",
            "Validation scope.",
            ConvaiValidationScope.All);

        [McpTool(
            BootstrapSceneTool,
            "Idempotently adds the required ConvaiManager and ConvaiRoomManager to the active scene. Does not add players, characters, save the scene, or set credentials.",
            "Bootstrap Convai Scene",
            Groups = new[] { "convai", "scene" })]
        public static object BootstrapScene(JObject parameters) =>
            BootstrapScene(Parse<ConvaiBootstrapRequest>(parameters));

        public static object BootstrapScene(ConvaiBootstrapRequest request)
        {
            bool dryRun = request?.DryRun ?? false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return Failure(
                    "PLAY_MODE_ACTIVE",
                    "Scene bootstrap is available only in Edit Mode.",
                    new { dryRun });
            }

            ConvaiManager existingManager = FindFirst<ConvaiManager>();
            bool managerHasRoom = existingManager != null &&
                                  existingManager.GetComponent<ConvaiRoomManager>() != null;
            if (dryRun)
            {
                return Success(
                    "Previewed Convai scene bootstrap.",
                    new
                    {
                        dryRun = true,
                        wouldAddManager = existingManager == null,
                        wouldAddRoomManager = existingManager == null || !managerHasRoom,
                        sceneDirty = SceneManager.GetActiveScene().isDirty
                    });
            }

            ConvaiSceneSetupApi.BootstrapResult result = ConvaiSceneSetupApi.BootstrapScene();
            ConvaiManager manager = FindFirst<ConvaiManager>();
            ConvaiRoomManager room = FindFirst<ConvaiRoomManager>();

            return Success(
                result.ActionsTaken.Count == 0
                    ? "Convai scene bootstrap already satisfied."
                    : "Bootstrapped required Convai scene components.",
                new
                {
                    dryRun = false,
                    result.AddedManager,
                    result.AddedRoomManager,
                    result.ManagerObjectName,
                    managerInstanceId = ConvaiMcpEntityRef.ToToolId(manager != null ? manager.gameObject : null),
                    roomManagerInstanceId = ConvaiMcpEntityRef.ToToolId(room != null ? room.gameObject : null),
                    actionsTaken = result.ActionsTaken,
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false,
                    nextSteps = new[]
                    {
                        "Use official Unity MCP tools to choose explicit player and character GameObjects.",
                        "Run Convai.ValidateSetup after adding ConvaiPlayer and ConvaiCharacter components."
                    }
                });
        }

        [McpOutputSchema(BootstrapSceneTool)]
        public static object BootstrapSceneOutputSchema() => StandardResponseSchema();

        [McpSchema(BootstrapSceneTool)]
        public static object BootstrapSceneInputSchema() => BooleanInputSchema(
            "dryRun",
            "Preview required changes without modifying the scene.",
            false);
    }
}
