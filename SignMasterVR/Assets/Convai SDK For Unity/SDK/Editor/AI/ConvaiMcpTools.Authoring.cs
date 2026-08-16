using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Runtime.Room;
using Convai.Runtime.Vision.Context;
using Convai.Shared.Types;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEngine.SceneManagement;

namespace Convai.Editor.AI
{
    public static partial class ConvaiMcpTools
    {
        private const string ConfigureRoomTool = "Convai.ConfigureRoom";
        private const string ConfigurePlayerTool = "Convai.ConfigurePlayer";
        private const string ConfigureCharacterTool = "Convai.ConfigureCharacter";
        private const string SetupConversationSceneTool = "Convai.SetupConversationScene";
        private const string DiagnoseConversationTool = "Convai.DiagnoseConversation";

        [McpTool(
            ConfigureRoomTool,
            "Previews or configures a Convai room on an explicit GameObject. Uses Undo, never saves, and never changes credentials.",
            "Configure Convai Room",
            Groups = new[] { "convai", "scene" },
            EnabledByDefault = true)]
        public static object ConfigureRoom(JObject parameters) =>
            ConfigureRoom(Parse<ConvaiConfigureRoomRequest>(parameters));

        public static object ConfigureRoom(ConvaiConfigureRoomRequest request) =>
            AuthoringResponse(
                ConvaiConversationAuthoringService.ConfigureRoom(request),
                "Configured Convai room.",
                "Previewed Convai room configuration.");

        [McpSchema(ConfigureRoomTool)]
        public static object ConfigureRoomInputSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["targetInstanceId"] = IntegerProperty("Target room GameObject instance ID."),
                ["configurationMode"] = EnumProperty<ConvaiToolConfigurationMode>("Configuration source.", ConvaiToolConfigurationMode.Inline),
                ["profileAssetPath"] = StringProperty("Existing ConvaiRoomManagerProfile path."),
                ["connectionType"] = EnumProperty<ConvaiConnectionType>("Inline connection type.", ConvaiConnectionType.Audio),
                ["inputMode"] = EnumProperty<ConversationInputMode>("Inline input mode.", ConversationInputMode.HandsFree),
                ["connectOnStart"] = BooleanProperty("Connect automatically on Start.", true),
                ["serverEndpoint"] = EnumProperty<ConvaiServerEndpoint>("Core-service endpoint.", ConvaiServerEndpoint.Connect),
                ["visionMode"] = EnumProperty<ConvaiVisionContextMode>("Dynamic vision policy.", ConvaiVisionContextMode.Auto),
                ["pushToTalkKey"] = StringProperty("Unity KeyCode name.", "T"),
                ["dryRun"] = BooleanProperty("Preview without mutation.", true)
            },
            "targetInstanceId");

        [McpOutputSchema(ConfigureRoomTool)]
        public static object ConfigureRoomOutputSchema() => StandardResponseSchema();

        [McpTool(
            ConfigurePlayerTool,
            "Previews or adds and configures ConvaiPlayer on an explicit GameObject, then binds one unambiguous manager. Never modifies Main Camera.",
            "Configure Convai Player",
            Groups = new[] { "convai", "scene" },
            EnabledByDefault = true)]
        public static object ConfigurePlayer(JObject parameters) =>
            ConfigurePlayer(Parse<ConvaiConfigurePlayerRequest>(parameters));

        public static object ConfigurePlayer(ConvaiConfigurePlayerRequest request) =>
            AuthoringResponse(
                ConvaiConversationAuthoringService.ConfigurePlayer(request),
                "Configured Convai player.",
                "Previewed Convai player configuration.");

        [McpSchema(ConfigurePlayerTool)]
        public static object ConfigurePlayerInputSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["targetInstanceId"] = IntegerProperty("Target player GameObject instance ID."),
                ["managerInstanceId"] = IntegerProperty("Optional manager GameObject instance ID.", 0),
                ["playerName"] = StringProperty("Player display name.", "Player"),
                ["playerId"] = StringProperty("Optional local player ID."),
                ["dryRun"] = BooleanProperty("Preview without mutation.", true)
            },
            "targetInstanceId");

        [McpOutputSchema(ConfigurePlayerTool)]
        public static object ConfigurePlayerOutputSchema() => StandardResponseSchema();

        [McpTool(
            ConfigureCharacterTool,
            "Previews or adds and configures ConvaiCharacter with recommended audio output on an explicit GameObject. Missing Character ID remains an explicit readiness blocker.",
            "Configure Convai Character",
            Groups = new[] { "convai", "scene" },
            EnabledByDefault = true)]
        public static object ConfigureCharacter(JObject parameters) =>
            ConfigureCharacter(Parse<ConvaiConfigureCharacterRequest>(parameters));

        public static object ConfigureCharacter(ConvaiConfigureCharacterRequest request) =>
            AuthoringResponse(
                ConvaiConversationAuthoringService.ConfigureCharacter(request),
                "Configured Convai character.",
                "Previewed Convai character configuration.");

        [McpSchema(ConfigureCharacterTool)]
        public static object ConfigureCharacterInputSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["targetInstanceId"] = IntegerProperty("Target character GameObject instance ID."),
                ["managerInstanceId"] = IntegerProperty("Optional manager GameObject instance ID.", 0),
                ["configurationMode"] = EnumProperty<ConvaiToolConfigurationMode>("Configuration source.", ConvaiToolConfigurationMode.Inline),
                ["profileAssetPath"] = StringProperty("Existing ConvaiCharacterProfile path."),
                ["characterId"] = StringProperty("Convai dashboard Character ID."),
                ["characterName"] = StringProperty("Character display name."),
                ["addAudioOutput"] = BooleanProperty("Ensure AudioSource and ConvaiAudioOutput.", true),
                ["dryRun"] = BooleanProperty("Preview without mutation.", true)
            },
            "targetInstanceId");

        [McpOutputSchema(ConfigureCharacterTool)]
        public static object ConfigureCharacterOutputSchema() => StandardResponseSchema();

        [McpTool(
            SetupConversationSceneTool,
            "Previews or performs end-to-end Audio conversation setup in the active scene using safe placeholders and recommended defaults. Never saves, enters Play Mode, or sets credentials.",
            "Setup Convai Conversation Scene",
            Groups = new[] { "convai", "scene" },
            EnabledByDefault = true)]
        public static object SetupConversationScene(JObject parameters) =>
            SetupConversationScene(Parse<ConvaiSetupConversationSceneRequest>(parameters));

        public static object SetupConversationScene(ConvaiSetupConversationSceneRequest request) =>
            AuthoringResponse(
                ConvaiConversationAuthoringService.SetupConversationScene(request),
                "Set up Convai conversation scene.",
                "Previewed end-to-end Convai conversation scene setup.");

        [McpSchema(SetupConversationSceneTool)]
        public static object SetupConversationSceneInputSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["managerInstanceId"] = IntegerProperty("Optional manager target instance ID.", 0),
                ["playerInstanceId"] = IntegerProperty("Optional player target instance ID.", 0),
                ["characterInstanceId"] = IntegerProperty("Optional character target instance ID.", 0),
                ["roomProfileAssetPath"] = StringProperty("Optional existing room profile path."),
                ["characterProfileAssetPath"] = StringProperty("Optional existing character profile path."),
                ["characterId"] = StringProperty("Convai dashboard Character ID."),
                ["characterName"] = StringProperty("Character display name.", "Convai Character"),
                ["playerName"] = StringProperty("Player display name.", "Player"),
                ["playerId"] = StringProperty("Optional local player ID."),
                ["inputMode"] = EnumProperty<ConversationInputMode>("Conversation input mode.", ConversationInputMode.HandsFree),
                ["connectOnStart"] = BooleanProperty("Connect automatically on Start.", true),
                ["createPlaceholders"] = BooleanProperty("Create missing standalone placeholders.", true),
                ["dryRun"] = BooleanProperty("Preview without mutation.", true)
            });

        [McpOutputSchema(SetupConversationSceneTool)]
        public static object SetupConversationSceneOutputSchema() => StandardResponseSchema();

        [McpTool(
            DiagnoseConversationTool,
            "Diagnoses active-scene Convai conversation readiness and runtime state with ranked evidence and suggested fixes. Never mutates the project or returns API keys.",
            "Diagnose Convai Conversation",
            Groups = new[] { "convai", "validation" },
            EnabledByDefault = true)]
        public static object DiagnoseConversation(JObject parameters) =>
            DiagnoseConversation(Parse<ConvaiDiagnoseConversationRequest>(parameters));

        public static object DiagnoseConversation(ConvaiDiagnoseConversationRequest request)
        {
            ConvaiConversationDiagnosis diagnosis = ConvaiConversationHealthAnalyzer.Analyze(request);
            if (!diagnosis.Success)
                return Failure(diagnosis.FailureCode, diagnosis.FailureMessage, new { });
            return Success(
                diagnosis.ReadyToRun
                    ? "Convai conversation setup is ready."
                    : "Convai conversation diagnosis found readiness issues.",
                new
                {
                    mode = diagnosis.Mode,
                    readyToRun = diagnosis.ReadyToRun,
                    issues = diagnosis.Issues.Select(issue => new
                    {
                        code = issue.Code,
                        severity = issue.Severity,
                        message = issue.Message,
                        evidence = issue.Evidence,
                        affectedInstanceId = issue.AffectedInstanceId,
                        autoFixable = issue.AutoFixable,
                        suggestedTool = issue.SuggestedTool,
                        suggestedArguments = issue.SuggestedArguments
                    }).ToArray(),
                    configuration = diagnosis.Configuration,
                    runtime = diagnosis.Runtime
                });
        }

        [McpSchema(DiagnoseConversationTool)]
        public static object DiagnoseConversationInputSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = IntegerProperty("Optional focused character instance ID.", 0),
                ["includeInactive"] = BooleanProperty("Include inactive scene objects.", true)
            });

        [McpOutputSchema(DiagnoseConversationTool)]
        public static object DiagnoseConversationOutputSchema() => StandardResponseSchema();

        private static object AuthoringResponse(ConvaiAuthoringResult result, string appliedMessage,
            string previewMessage)
        {
            if (!result.Success)
                return Failure(result.FailureCode, result.FailureMessage, new { result.DryRun });
            return Success(
                result.DryRun ? previewMessage : appliedMessage,
                new
                {
                    dryRun = result.DryRun,
                    complete = result.Complete,
                    changes = result.Changes,
                    blockedSteps = result.BlockedSteps,
                    requiredInputs = result.RequiredInputs,
                    warnings = result.Warnings,
                    managerInstanceId = result.ManagerInstanceId,
                    roomInstanceId = result.RoomInstanceId,
                    playerInstanceId = result.PlayerInstanceId,
                    characterInstanceId = result.CharacterInstanceId,
                    sceneDirty = SceneManager.GetActiveScene().isDirty,
                    sceneSaved = false
                });
        }

        private static object ObjectSchema(Dictionary<string, object> properties, params string[] required) =>
            ConvaiMcpResponses.ObjectSchema(properties, required);

        private static object IntegerProperty(string description, long? defaultValue = null) =>
            ConvaiMcpResponses.IntegerProperty(description, defaultValue);

        private static object StringProperty(string description, string defaultValue = null) =>
            ConvaiMcpResponses.StringProperty(description, defaultValue);

        private static object BooleanProperty(string description, bool defaultValue) =>
            ConvaiMcpResponses.BooleanProperty(description, defaultValue);

        private static object EnumProperty<T>(string description, T defaultValue) where T : struct, Enum =>
            ConvaiMcpResponses.EnumProperty(description, defaultValue);
    }
}
