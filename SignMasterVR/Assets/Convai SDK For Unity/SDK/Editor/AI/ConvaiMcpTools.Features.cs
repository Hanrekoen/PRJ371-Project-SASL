using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Convai.Application;
using Convai.Domain.Models;
using Convai.Runtime;
using Convai.Editor.Actions;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.Events;
using Convai.Runtime.Presentation.Views;
using Convai.Runtime.Presentation.Views.Transcript;
using Convai.Shared.Compatibility;
using Convai.Shared.Types;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Convai.Editor.AI
{
    public static partial class ConvaiMcpTools
    {
        private const string ConfigureActionsTool = "Convai.ConfigureActions";
        private const string DiagnoseActionsTool = "Convai.DiagnoseActions";
        private const string SimulateActionTool = "Convai.SimulateAction";
        private const string ConfigureTranscriptsTool = "Convai.ConfigureTranscripts";
        private const string DiagnoseTranscriptsTool = "Convai.DiagnoseTranscripts";

        [McpTool(ConfigureActionsTool, "Previews or safely upserts typed actions and explicit targets on a Convai character. Uses Undo and never saves.", "Configure Convai Actions", Groups = new[] { "convai", "actions" }, EnabledByDefault = true)]
        public static object ConfigureActions(JObject parameters) => ConfigureActions(Parse<ConvaiConfigureActionsRequest>(parameters));
        public static object ConfigureActions(ConvaiConfigureActionsRequest request) => ConvaiFeatureAuthoringService.ConfigureActions(request);
        [McpSchema(ConfigureActionsTool)]
        public static object ConfigureActionsInputSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["characterInstanceId"] = IntegerProperty("Character GameObject or component instance ID."),
                ["definitions"] = new { type = "array", description = "Typed action definitions to upsert by name.", items = ActionDefinitionSchema() },
                ["objects"] = new { type = "array", description = "Explicit actionable GameObjects to upsert by name.", items = ActionTargetSchema(false) },
                ["characters"] = new { type = "array", description = "Explicit actionable characters to upsert by name.", items = ActionTargetSchema(true) },
                ["initialAttentionObject"] = StringProperty("Optional authored object name used as initial attention."),
                ["dryRun"] = BooleanProperty("Preview without mutation.", true)
            },
            "characterInstanceId");
        [McpOutputSchema(ConfigureActionsTool)] public static object ConfigureActionsOutputSchema() => StandardResponseSchema();

        [McpTool(DiagnoseActionsTool, "Diagnoses action definitions, executors, targets, attention, dispatcher presence, and runtime availability without mutation.", "Diagnose Convai Actions", Groups = new[] { "convai", "actions", "validation" }, EnabledByDefault = true)]
        public static object DiagnoseActions(JObject parameters) => DiagnoseActions(Parse<ConvaiDiagnoseActionsRequest>(parameters));
        public static object DiagnoseActions(ConvaiDiagnoseActionsRequest request)
        {
            request ??= new ConvaiDiagnoseActionsRequest();
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, request.IncludeInactive,
                    out ConvaiCharacter character, out string failure))
                return FeatureFailure(ConvaiMcpResolvers.CharacterErrorCode, failure);
            ConvaiActionConfigSource source = character.GetComponent<ConvaiActionConfigSource>();
            ConvaiActionDispatcher dispatcher = character.GetComponent<ConvaiActionDispatcher>();
            var issues = new List<object>();
            IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics = ConvaiActionSetupReport.Validate(source);
            for (int i = 0; i < diagnostics.Count; i++)
            {
                ConvaiActionConfigDiagnostic d = diagnostics[i];
                issues.Add(ConvaiMcpResponses.Issue($"ACTION_CONFIG_{i + 1}", d.Severity.ToString(), d.Message, d.Context, ConvaiMcpEntityRef.ToToolId(character), d.Severity == ConvaiActionConfigDiagnosticSeverity.Error, ConfigureActionsTool, new { characterInstanceId = ConvaiMcpEntityRef.ToToolId(character), dryRun = true }));
            }
            if (dispatcher == null) issues.Add(ConvaiMcpResponses.Issue("ACTION_DISPATCHER_MISSING", "Error", "ConvaiActionDispatcher is missing.", character.name, ConvaiMcpEntityRef.ToToolId(character), true, ConfigureActionsTool, new { characterInstanceId = ConvaiMcpEntityRef.ToToolId(character), dryRun = true }));
            // Effective definitions (ActionSets merged + inline, auto-bound) so set-authored actions are
            // diagnosed the same way as inline ones instead of only inspecting the raw inline list.
            IReadOnlyList<ConvaiActionDefinition> effectiveDefinitions = source != null
                ? source.GetEffectiveDefinitions()
                : Array.Empty<ConvaiActionDefinition>();
            for (int i = 0; i < effectiveDefinitions.Count; i++)
                if (ConvaiActionsAuthoringDefaults.IsUnwiredPlaceholder(effectiveDefinitions[i]?.Executor))
                {
                    MonoBehaviour placeholder = effectiveDefinitions[i].Executor;
                    issues.Add(ConvaiMcpResponses.Issue("ACTION_EVENT_UNWIRED", "Warning", $"Action '{effectiveDefinitions[i].ActionName}' uses a Raise Unity Event behavior with nothing wired into it.", placeholder.name, ConvaiMcpEntityRef.ToToolId(placeholder), false, ConfigureActionsTool, new { characterInstanceId = ConvaiMcpEntityRef.ToToolId(character), dryRun = true }));
                }
            return Success(issues.Count == 0 ? "Convai actions are ready." : "Convai action diagnosis found issues.", new { ready = issues.Count == 0, issues, configuration = new { characterInstanceId = ConvaiMcpEntityRef.ToToolId(character), definitionCount = effectiveDefinitions.Count, objectCount = source?.Objects.Count ?? 0, characterCount = source?.Characters.Count ?? 0, initialAttentionObject = source?.InitialAttentionObject ?? string.Empty, dispatcherPresent = dispatcher != null, actions = DescribeActionAvailability(effectiveDefinitions), actionSets = DescribeActionSets(source) }, runtime = new { isPlaying = UnityEngine.Application.isPlaying, dispatcherEnabled = dispatcher != null && dispatcher.isActiveAndEnabled } });
        }

        /// <summary>Read surface for the availability flag: effective action names + enabled state.</summary>
        private static object[] DescribeActionAvailability(IReadOnlyList<ConvaiActionDefinition> definitions) =>
            definitions
                .Where(definition => definition != null && !string.IsNullOrWhiteSpace(definition.ActionName))
                .Select(definition => (object)new { name = definition.ActionName.Trim(), enabled = definition.Enabled })
                .ToArray();

        /// <summary>
        ///     Read-only listing of the assigned <see cref="ConvaiActionSet" /> assets and their
        ///     entries, so this tool has parity with the editor window. Set authoring itself stays an editor-window
        ///     concern; import/export shares the window's JSON schema, not this tool.
        /// </summary>
        private static object[] DescribeActionSets(ConvaiActionConfigSource source)
        {
            if (source == null || source.ActionSets == null || source.ActionSets.Count == 0)
                return Array.Empty<object>();

            var described = new List<object>(source.ActionSets.Count);
            for (int i = 0; i < source.ActionSets.Count; i++)
            {
                ConvaiActionSet actionSet = source.ActionSets[i];
                if (actionSet == null)
                    continue;

                described.Add(new
                {
                    name = actionSet.name,
                    assetPath = AssetDatabase.GetAssetPath(actionSet),
                    actions = actionSet.Definitions
                        .Where(definition => definition != null && !string.IsNullOrWhiteSpace(definition.ActionName))
                        .Select(definition => (object)new { name = definition.ActionName.Trim(), enabled = definition.Enabled })
                        .ToArray()
                });
            }

            return described.ToArray();
        }
        [McpSchema(DiagnoseActionsTool)] public static object DiagnoseActionsInputSchema() => ObjectSchema(new Dictionary<string, object> { ["characterInstanceId"] = IntegerProperty("Optional character instance ID.", 0), ["includeInactive"] = BooleanProperty("Include inactive objects.", true) });
        [McpOutputSchema(DiagnoseActionsTool)] public static object DiagnoseActionsOutputSchema() => StandardResponseSchema();

        [McpTool(SimulateActionTool, "Validates an action in Edit Mode or dispatches it through the real runtime dispatcher in Play Mode. Never changes Play Mode.", "Simulate Convai Action", Groups = new[] { "convai", "actions", "runtime" }, EnabledByDefault = true)]
        public static async Task<object> SimulateAction(JObject parameters) => await SimulateAction(Parse<ConvaiSimulateActionRequest>(parameters));
        public static async Task<object> SimulateAction(ConvaiSimulateActionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ActionName)) return FeatureFailure("ACTION_REQUIRED", "actionName is required.");
            if (!ConvaiMcpResolvers.TryCharacter(request.CharacterInstanceId, true,
                    out ConvaiCharacter character, out string failure))
                return FeatureFailure(ConvaiMcpResolvers.CharacterErrorCode, failure);
            ConvaiActionConfigSource source = character.GetComponent<ConvaiActionConfigSource>();
            // Effective definitions so set-authored actions (ActionSets, auto-bound by hint) are
            // simulatable the same way as inline ones, not just the raw inline list.
            ConvaiActionDefinition definition = source?.GetEffectiveDefinitions()
                .FirstOrDefault(item => string.Equals(item?.ActionName?.Trim(), request.ActionName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (definition == null) return FeatureFailure("ACTION_NOT_FOUND", $"Action '{request.ActionName}' is not configured.");
            if (!TryBuildSimulationParameters(definition, source, request.Parameters, out Dictionary<string, ConvaiActionParameterValue> parameters, out failure))
                return FeatureFailure("INVALID_PARAMETERS", failure);
            if (!UnityEngine.Application.isPlaying) return Success("Action payload validated. Enter Play Mode explicitly to execute it.", new { executed = false, valid = true, requiresPlayMode = true, characterInstanceId = ConvaiMcpEntityRef.ToToolId(character), actionName = definition.ActionName, target = request.Target ?? string.Empty });
            ConvaiActionDispatcher dispatcher = character.GetComponent<ConvaiActionDispatcher>();
            if (dispatcher == null || !dispatcher.isActiveAndEnabled) return FeatureFailure("DISPATCHER_UNAVAILABLE", "Active ConvaiActionDispatcher is required.");
            var completion = new TaskCompletionSource<ConvaiActionStepReport>();
            void Handler(ConvaiActionStepReport report) { if (string.Equals(report?.Invocation?.Command?.Name, request.ActionName, StringComparison.OrdinalIgnoreCase)) completion.TrySetResult(report); }
            dispatcher.OnStepCompleted.AddListener(Handler);
            try
            {
                var command = new ConvaiActionCommand(request.ActionName, request.Target)
                {
                    Parameters = parameters,
                    Enriched = true
                };
                dispatcher.EnqueueActions(new[] { command });
                Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(Mathf.Clamp(request.TimeoutSeconds, 0.1f, 60f))));
                if (finished != completion.Task) return FeatureFailure("SIMULATION_TIMEOUT", "Timed out waiting for action completion.");
                ConvaiActionStepReport report = await completion.Task;
                bool ok = report.Result.Status == ConvaiActionExecutionStatus.Succeeded;
                return Result(ok, ok ? "Action simulation succeeded." : "Action simulation completed with failure.", new { executed = true, status = report.Result.Status.ToString(), report.Message, report.FailureMessage, report.BatchAborted });
            }
            finally { dispatcher.OnStepCompleted.RemoveListener(Handler); }
        }
        [McpSchema(SimulateActionTool)] public static object SimulateActionInputSchema() => ObjectSchema(new Dictionary<string, object> { ["characterInstanceId"] = IntegerProperty("Character instance ID."), ["actionName"] = StringProperty("Configured action name."), ["target"] = StringProperty("Optional target name."), ["parameters"] = new { type = "object", description = "Optional action parameter values keyed by authored parameter name.", additionalProperties = new { type = "string" } }, ["timeoutSeconds"] = new { type = "number", description = "Completion timeout.", @default = 10f } }, "characterInstanceId", "actionName");
        [McpOutputSchema(SimulateActionTool)] public static object SimulateActionOutputSchema() => StandardResponseSchema();

        [McpTool(ConfigureTranscriptsTool, "Previews or configures the canonical transcript facade, event relay, or shipped chat UI. Never changes ConvaiSettings or exposes text.", "Configure Convai Transcripts", Groups = new[] { "convai", "transcripts" }, EnabledByDefault = true)]
        public static object ConfigureTranscripts(JObject parameters) => ConfigureTranscripts(Parse<ConvaiConfigureTranscriptsRequest>(parameters));
        public static object ConfigureTranscripts(ConvaiConfigureTranscriptsRequest request) => ConvaiFeatureAuthoringService.ConfigureTranscripts(request);
        [McpSchema(ConfigureTranscriptsTool)] public static object ConfigureTranscriptsInputSchema() => ObjectSchema(new Dictionary<string, object> { ["managerInstanceId"] = IntegerProperty("Optional manager instance ID.", 0), ["hostInstanceId"] = IntegerProperty("Optional host GameObject instance ID.", 0), ["mode"] = EnumProperty("Transcript mode.", ConvaiTranscriptToolMode.EventRelay), ["finalOnly"] = BooleanProperty("Forward final updates only.", false), ["ignoreInterim"] = BooleanProperty("Ignore interim updates.", true), ["characterIdFilter"] = StringProperty("Optional character ID filter."), ["dryRun"] = BooleanProperty("Preview without mutation.", true) });
        [McpOutputSchema(ConfigureTranscriptsTool)] public static object ConfigureTranscriptsOutputSchema() => StandardResponseSchema();

        [McpTool(DiagnoseTranscriptsTool, "Diagnoses transcript enablement, facade readiness, relays, UIs, and sanitized runtime timeline metadata.", "Diagnose Convai Transcripts", Groups = new[] { "convai", "transcripts", "validation" }, EnabledByDefault = true)]
        public static object DiagnoseTranscripts(JObject parameters) => DiagnoseTranscripts(Parse<ConvaiDiagnoseTranscriptsRequest>(parameters));
        public static object DiagnoseTranscripts(ConvaiDiagnoseTranscriptsRequest request)
        {
            request ??= new ConvaiDiagnoseTranscriptsRequest();
            if (!ConvaiMcpResolvers.TryManager(request.ManagerInstanceId, true,
                    out ConvaiManager manager, out string error))
                return FeatureFailure(ConvaiMcpResolvers.ManagerErrorCode, error);
            var issues = new List<object>();
            if (ConvaiSettings.Instance == null || !ConvaiSettings.Instance.TranscriptSystemEnabled) issues.Add(ConvaiMcpResponses.Issue("TRANSCRIPTS_DISABLED", "Error", "Transcript System is disabled in Convai project settings.", "ConvaiSettings.TranscriptSystemEnabled=false", ConvaiMcpEntityRef.ToToolId(manager), false, ConfigureTranscriptsTool, new { dryRun = true }));
            ConvaiTranscriptEventRelay[] relays = ConvaiObjectFind.All<ConvaiTranscriptEventRelay>(FindObjectsInactive.Include).Where(x => x.gameObject.scene == SceneManager.GetActiveScene()).ToArray();
            int uiCount = CountShippedTranscriptUis(SceneManager.GetActiveScene());
            object timeline = new { available = false, cursor = 0L, activeTurnCount = 0, committedTurnCount = 0, textIncluded = false, turns = Array.Empty<object>() };
            if (UnityEngine.Application.isPlaying)
            {
                try
                {
                    TranscriptTimeline value = manager.Transcripts.CurrentTimeline;
                    timeline = new
                    {
                        available = true,
                        cursor = value.Cursor,
                        activeTurnCount = value.ActiveTurns.Count,
                        committedTurnCount = value.CommittedTurns.Count,
                        textIncluded = request.IncludeText,
                        turns = request.IncludeText
                            ? DescribeTranscriptTurns(value.ActiveTurns.Concat(value.CommittedTurns))
                            : Array.Empty<object>()
                    };
                }
                catch (InvalidOperationException exception) { issues.Add(ConvaiMcpResponses.Issue("TRANSCRIPT_FACADE_UNAVAILABLE", "Warning", exception.Message, "ConvaiManager.Transcripts", ConvaiMcpEntityRef.ToToolId(manager), false, DiagnoseTranscriptsTool, new { managerInstanceId = ConvaiMcpEntityRef.ToToolId(manager) })); }
            }
            return Success(issues.Count == 0 ? "Convai transcripts are ready." : "Convai transcript diagnosis found issues.", new { ready = issues.Count == 0, issues, configuration = new { managerInstanceId = ConvaiMcpEntityRef.ToToolId(manager), transcriptSystemEnabled = ConvaiSettings.Instance != null && ConvaiSettings.Instance.TranscriptSystemEnabled, relayCount = relays.Length, transcriptUiCount = uiCount, transcriptUiCountNote = "Counts shipped SDK transcript UIs (ConvaiTranscriptDisplay, ChatTranscriptUI); sample UIs such as SubtitleTranscriptUI are not counted." }, runtime = timeline, includeTextRequested = request.IncludeText });
        }
        [McpSchema(DiagnoseTranscriptsTool)] public static object DiagnoseTranscriptsInputSchema() => ObjectSchema(new Dictionary<string, object> { ["managerInstanceId"] = IntegerProperty("Optional manager instance ID.", 0), ["includeText"] = BooleanProperty("Include transcript text only when explicitly requested.", false) });
        [McpOutputSchema(DiagnoseTranscriptsTool)] public static object DiagnoseTranscriptsOutputSchema() => StandardResponseSchema();

        internal static int CountShippedTranscriptUis(Scene scene)
        {
            int transcriptDisplayCount = ConvaiObjectFind.All<ConvaiTranscriptDisplay>(FindObjectsInactive.Include)
                .Count(item => item != null && item.gameObject.scene == scene);
            int chatTranscriptUiCount = ConvaiObjectFind.All<ChatTranscriptUI>(FindObjectsInactive.Include)
                .Count(item => item != null && item.gameObject.scene == scene);

            return transcriptDisplayCount + chatTranscriptUiCount;
        }

        internal static object FeatureFailure(string code, string message) => Failure(code, message, new { });
        internal static object FeatureAuthoring(bool dryRun, bool complete, IReadOnlyList<string> changes, IReadOnlyList<string> blocked, IReadOnlyList<string> warnings, Component target) => Success(dryRun ? "Previewed Convai feature configuration." : "Configured Convai feature.", new { dryRun, complete, changes, blockedSteps = blocked, requiredInputs = blocked, warnings, affectedInstanceId = ConvaiMcpEntityRef.ToToolId(target), sceneDirty = SceneManager.GetActiveScene().isDirty, sceneSaved = false });
        private static bool TryBuildSimulationParameters(
            ConvaiActionDefinition definition,
            ConvaiActionConfigSource source,
            IReadOnlyDictionary<string, string> supplied,
            out Dictionary<string, ConvaiActionParameterValue> values,
            out string error)
        {
            values = new Dictionary<string, ConvaiActionParameterValue>(StringComparer.OrdinalIgnoreCase);
            supplied ??= new Dictionary<string, string>();
            var definitions = new Dictionary<string, ConvaiActionParameterDefinition>(StringComparer.OrdinalIgnoreCase);
            if (definition.Parameters != null)
                for (int i = 0; i < definition.Parameters.Count; i++)
                {
                    ConvaiActionParameterDefinition parameter = definition.Parameters[i];
                    if (parameter != null && !string.IsNullOrWhiteSpace(parameter.Name))
                        definitions[parameter.Name.Trim()] = parameter;
                }

            foreach (KeyValuePair<string, string> pair in supplied)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !definitions.TryGetValue(pair.Key.Trim(), out ConvaiActionParameterDefinition parameter))
                {
                    error = $"Unknown parameter '{pair.Key}'.";
                    return false;
                }
                if (!TryCoerceParameter(pair.Value, parameter, source, out ConvaiActionParameterValue value, out error))
                    return false;
                values[parameter.Name.Trim()] = value;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryCoerceParameter(
            string raw,
            ConvaiActionParameterDefinition definition,
            ConvaiActionConfigSource source,
            out ConvaiActionParameterValue value,
            out string error)
        {
            // Read exactly the way production reads a value the Convai Character sent, quotes and
            // all. A simulator stricter than the thing it simulates is worse than no simulator: it
            // rejects input that works and sends whoever trusted it looking for a fault that is not
            // there.
            //
            // Including the vocabulary, which this call used to omit — so a target legitimately
            // called '- Special' or '{Annex}' simulated as unreachable while working perfectly in a
            // conversation, which is the precise failure the comment above warns about. The
            // resolution view is the same one the runtime resolves against.
            string text = ConvaiActionWireText.Clean(raw, source?.BuildRuntimeResolutionConfig());
            ConvaiActionParameterType type = definition.Type;
            ConvaiActionParameterReference reference = FindReference(text, source);
            if (type == ConvaiActionParameterType.Auto)
            {
                if (reference != null) type = ConvaiActionParameterType.Reference;
                else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) type = ConvaiActionParameterType.Number;
                else if (TryParseBool(text, out _)) type = ConvaiActionParameterType.Bool;
                else type = ConvaiActionParameterType.String;
            }

            value = new ConvaiActionParameterValue
            {
                Type = type,
                RawValue = text,
                StringValue = text,
                ResolvedReference = reference,
                IsConstraintMatch = true
            };
            switch (type)
            {
                case ConvaiActionParameterType.Reference when reference == null:
                    error = $"Parameter '{definition.Name}' does not match an authored object or character.";
                    return false;
                case ConvaiActionParameterType.Number:
                    if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                    {
                        error = $"Parameter '{definition.Name}' requires a number.";
                        return false;
                    }
                    value.NumberValue = number;
                    break;
                case ConvaiActionParameterType.Bool:
                    if (!TryParseBool(text, out bool boolean))
                    {
                        error = $"Parameter '{definition.Name}' requires true/false, yes/no, or 1/0.";
                        return false;
                    }
                    value.BoolValue = boolean;
                    break;
                case ConvaiActionParameterType.Choice:
                    bool matches = definition.Choices != null && definition.Choices.Any(choice =>
                        string.Equals(choice?.Trim(), text, StringComparison.OrdinalIgnoreCase));
                    value.IsConstraintMatch = matches;
                    if (!matches)
                    {
                        error = $"Parameter '{definition.Name}' must match an authored choice.";
                        return false;
                    }
                    break;
            }

            error = string.Empty;
            return true;
        }

        private static ConvaiActionParameterReference FindReference(string value, ConvaiActionConfigSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(value)) return null;
            for (int i = 0; i < source.Objects.Count; i++)
                if (string.Equals(source.Objects[i]?.Name?.Trim(), value, StringComparison.OrdinalIgnoreCase))
                    return new ConvaiActionParameterReference(source.Objects[i].Name.Trim(), ConvaiActionTargetKind.Object);
            for (int i = 0; i < source.Characters.Count; i++)
                if (string.Equals(source.Characters[i]?.Name?.Trim(), value, StringComparison.OrdinalIgnoreCase))
                    return new ConvaiActionParameterReference(source.Characters[i].Name.Trim(), ConvaiActionTargetKind.Character);
            return null;
        }

        private static bool TryParseBool(string value, out bool result)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || value == "1")
            {
                result = true;
                return true;
            }
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) || value == "0")
            {
                result = false;
                return true;
            }
            result = false;
            return false;
        }

        private static object[] DescribeTranscriptTurns(IEnumerable<TranscriptTurn> turns) => turns
            .OrderBy(turn => turn.RoomSequence)
            .Select(turn => (object)new
            {
                turnId = turn.Id,
                turn.ResponseId,
                turn.RoomSequence,
                participantKind = turn.Speaker.Type.ToString(),
                participantId = turn.Speaker.Id,
                participantName = turn.Speaker.DisplayName,
                state = turn.State.ToString(),
                turn.DisplayText,
                turn.WasInterrupted
            })
            .ToArray();

        private static object ActionDefinitionSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["name"] = StringProperty("Canonical backend action name."),
                ["description"] = StringProperty("Grounding description."),
                ["parameters"] = new { type = "array", description = "Ordered typed parameters.", items = ActionParameterSchema() },
                ["targetRequirement"] = EnumProperty("Required target kind.", ConvaiActionTargetRequirement.None),
                ["executorInstanceId"] = IntegerProperty("Existing IConvaiActionExecutor component instance ID; zero creates an unwired UnityEvent placeholder.", 0),
                ["timeoutSeconds"] = new { type = "number", description = "Per-step timeout; zero disables it.", minimum = 0, @default = 0 },
                ["waitForBotSpeech"] = BooleanProperty("Wait for bot speech before the first action.", false),
                ["delayAfterBotSpeechSeconds"] = new { type = "number", description = "Delay after speech gate release.", minimum = 0, @default = 0 },
                ["enabled"] = BooleanProperty("Whether the character knows about and offers this action; omit to leave the authored value unchanged.", true)
            },
            "name");

        private static object ActionParameterSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["name"] = StringProperty("Wire parameter name."),
                ["description"] = StringProperty("Grounding description."),
                ["type"] = EnumProperty("Parameter coercion type.", ConvaiActionParameterType.Auto),
                ["connector"] = StringProperty("Optional connector word; first parameter must leave this empty."),
                ["choices"] = new { type = "array", description = "Allowed values for Choice parameters.", items = new { type = "string" } }
            },
            "name");

        private static object ActionTargetSchema(bool character) => ObjectSchema(
            new Dictionary<string, object>
            {
                ["name"] = StringProperty("Canonical target name."),
                ["description"] = StringProperty(character ? "Character bio." : "Object grounding description."),
                ["gameObjectInstanceId"] = IntegerProperty("Active-scene GameObject instance ID.")
            },
            "name", "gameObjectInstanceId");
    }
}
