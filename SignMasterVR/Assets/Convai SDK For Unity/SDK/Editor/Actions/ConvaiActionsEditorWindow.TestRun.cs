using System;
using System.Collections.Generic;
using Convai.Editor.Inspectors;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     "Try It" card of the Actions Editor window's detail pane.
    ///     Edit mode: a conversation-free preview — a resolution dry-run through the real runtime
    ///     ladder (<see cref="ConvaiActionEditTimeResolver" />), with a focused empty state when the
    ///     action has nothing to target. Play mode: a real test run — target picker over the live merged registry,
    ///     typed parameter inputs, and injection through the one shared dispatcher seam
    ///     (<see cref="ConvaiActionTestRunService" /> — the same path backend commands take), with
    ///     an inline result read back from <see cref="ConvaiActionsSessionCollector" />. An ordered
    ///     run list rehearses several actions as one sequential batch.
    /// </summary>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string TestRunGlyph = Glyphs.Animator;
        private const string UnnamedValueLabel = "(unnamed value)";

        // ── Play-mode input state (rebuilt when the selected action changes) ────────────
        private ConvaiActionDefinition _testRunDefinition;
        private string _testRunTargetName = string.Empty;
        private readonly List<string> _testRunParameterTexts = new();
        private GUIContent[] _testRunParameterLabels = Array.Empty<GUIContent>();
        private GUIContent[][] _testRunChoiceOptions = Array.Empty<GUIContent[]>();

        private static GUIContent[] s_testRunBoolOptions;

        // ── Ordered run list ────────────────────────────────────────────────────────────
        private readonly List<QueuedTestRun> _testRunQueue = new();

        // ── Result correlation (collector-tagged batch) ────────────────────────────────
        private int _testRunToken;
        private bool _testRunActive;
        private int _testRunResultVersion = -1;
        private readonly List<GUIContent> _testRunResultLines = new();
        private bool _testRunResultHealthy = true;
        private bool _testRunResultFinished;

        // ── Edit-mode dry run ───────────────────────────────────────────────────────────
        private string _dryRunPhrase = string.Empty;
        private ConvaiActionDefinition _dryRunDefinition;
        private GUIContent _dryRunResult;
        private bool _dryRunMatched;

        private readonly struct QueuedTestRun
        {
            internal QueuedTestRun(string displayName, ConvaiActionCommand command)
            {
                DisplayName = displayName;
                Command = command;
            }

            internal string DisplayName { get; }
            internal ConvaiActionCommand Command { get; }
        }

        /// <summary>True while an injected test run has not finished yet (drives repaint ticking).</summary>
        private bool HasActiveTestRun => _testRunActive;

        private void DrawTestRunCard(
            ConvaiActionConfigSource source,
            ConvaiActionRow row)
        {
            Theme.BeginCard();
            Theme.SectionHeader(TestRunGlyph, ConvaiActionsEditorStrings.TestRunCardTitle);

            if (EditorApplication.isPlaying)
                DrawPlayModeTestRun(source, row);
            else
                DrawEditModePreview(source, row);

            Theme.EndCard();
        }

        #region Edit mode — target dry run

        private void DrawEditModePreview(
            ConvaiActionConfigSource source,
            ConvaiActionRow row)
        {
            ConvaiActionTargetRequirement requirement = row.Definition.TargetRequirement;
            if (requirement == ConvaiActionTargetRequirement.None)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.TestRunNoTargetEditMode, Theme.MutedWrapped);
                return;
            }

            GUILayout.Label(ConvaiActionsEditorStrings.TestRunEditModeIntro, Theme.MutedWrapped);
            GUILayout.Space(8f);

            if (!ConvaiActionConfigValidator.HasTargetForRequirement(
                    source, requirement, ConvaiActionSetupReport.CachedSceneTargets()))
            {
                Theme.WarningBox(
                    ConvaiActionsEditorStrings.TestRunMissingTargetTitle,
                    ConvaiActionsEditorStrings.BuildTestRunMissingTargetMessage(requirement),
                    ConvaiActionsEditorStrings.BuildAddTargetButton(requirement),
                    () => BeginTargetSetup(source, requirement));
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _dryRunPhrase = EditorGUILayout.TextField(ConvaiActionsEditorStrings.DryRunPhraseField, _dryRunPhrase);
                GUILayout.Space(4f);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_dryRunPhrase)))
                {
                    Rect checkRect = GUILayoutUtility.GetRect(64f, 20f, GUILayout.Width(64f), GUILayout.Height(20f));
                    if (Theme.GhostButton(checkRect, ConvaiActionsEditorStrings.DryRunCheckButton))
                        RunDryRun(source, row.Definition);
                }
            }

            if (_dryRunResult != null && ReferenceEquals(_dryRunDefinition, row.Definition))
            {
                GUILayout.Space(4f);
                Theme.BeginPanel(_dryRunMatched ? Theme.StatusReady : Theme.StatusWarn);
                GUILayout.Label(_dryRunResult, Theme.BodyWrapped);
                Theme.EndPanel(0f);
            }

        }

        private void RunDryRun(ConvaiActionConfigSource source, ConvaiActionDefinition definition)
        {
            _dryRunDefinition = definition;
            _dryRunMatched = false;

            ConvaiActionConfig config = ConvaiActionEditTimeResolver.BuildCandidateConfig(
                _character, source, ConvaiActionSetupReport.CachedSceneTargets());
            if (!ConvaiActionEditTimeResolver.HasUsableTarget(config, definition.TargetRequirement))
            {
                _dryRunResult = new GUIContent(
                    ConvaiActionsEditorStrings.BuildTestRunMissingTargetMessage(definition.TargetRequirement));
                return;
            }

            Vector3? origin = _character != null ? _character.transform.position : (Vector3?)null;
            ConvaiActionEditTimeResolver.DryRunResult result = ConvaiActionEditTimeResolver.Resolve(
                _dryRunPhrase, config, definition?.TargetRequirement ?? ConvaiActionTargetRequirement.Either, origin);

            if (!result.Matched)
            {
                _dryRunResult = ConvaiActionsEditorStrings.BuildDryRunNoMatch(_dryRunPhrase.Trim());
                return;
            }

            string kindLabel = result.Kind == ConvaiActionTargetKind.Character
                ? ConvaiActionsEditorStrings.ScanKindCharacterPill.text
                : ConvaiActionsEditorStrings.ScanKindObjectPill.text;
            string stepDescription = ConvaiActionTargetPhraseMatcher.Describe(
                new ConvaiActionTargetPhraseMatcher.MatchResult(result.Step, result.MatchedText));
            _dryRunResult = ConvaiActionsEditorStrings.BuildDryRunMatched(result.TargetName, kindLabel, stepDescription);
            _dryRunMatched = true;
        }

        #endregion

        #region Play mode — test run

        private void DrawPlayModeTestRun(ConvaiActionConfigSource source, ConvaiActionRow row)
        {
            EnsureSettingsBindings();
            ConvaiActionDispatcher dispatcher = _settingsDispatcher;
            if (dispatcher == null)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.TestRunNeedsDispatcher, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            ConvaiActionDefinition definition = row.Definition;
            EnsureTestRunInputs(definition);

            if (definition.TargetRequirement != ConvaiActionTargetRequirement.None)
            {
                ConvaiActionConfig runtimeConfig = _character != null ? _character.GetRuntimeActionConfig() : null;
                if (!ConvaiActionEditTimeResolver.HasUsableTarget(runtimeConfig, definition.TargetRequirement))
                {
                    Theme.WarningBox(
                        ConvaiActionsEditorStrings.TestRunMissingTargetTitle,
                        ConvaiActionsEditorStrings.TestRunNoTargetPlayMode.text);
                    return;
                }

                DrawTestRunTargetPicker(definition);
            }

            List<ConvaiActionParameterDefinition> parameters = definition.Parameters;
            if (parameters != null && parameters.Count > 0)
            {
                GUILayout.Space(4f);
                GUILayout.Label(ConvaiActionsEditorStrings.TestRunParametersLabel, Theme.MicroLabel);
                GUILayout.Space(2f);
                for (int i = 0; i < parameters.Count; i++)
                    DrawTestRunParameterField(i, parameters[i]);
            }

            // Availability-aware affordance: a disabled action still gets a test path, but an
            // explicit one — "Run Anyway" bypasses the availability check for this one injected
            // command only (nothing is toggled or overridden persistently).
            bool actionAvailable = IsTestRunActionAvailable(definition);
            if (!actionAvailable)
            {
                GUILayout.Space(6f);
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.TestRunDisabledWarning, Theme.BodyWrapped);
                Theme.EndPanel(0f);
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect runRect = GUILayoutUtility.GetRect(120f, 26f, GUILayout.Width(120f), GUILayout.Height(26f));
                if (actionAvailable)
                {
                    if (Theme.PrimaryButton(runRect, ConvaiActionsEditorStrings.TestRunButton))
                        RunTestNow(dispatcher, source, definition);
                }
                else if (Theme.PrimaryButton(runRect, ConvaiActionsEditorStrings.TestRunRunAnywayButton))
                {
                    RunTestNow(dispatcher, source, definition, bypassAvailability: true);
                }

                GUILayout.Space(8f);
                Rect queueRect = GUILayoutUtility.GetRect(120f, 26f, GUILayout.Width(120f), GUILayout.Height(26f));
                if (Theme.GhostButton(queueRect, ConvaiActionsEditorStrings.TestRunAddToListButton))
                    AddToRunList(source, row, definition);

                GUILayout.FlexibleSpace();
            }

            DrawTestRunQueue(dispatcher);
            DrawTestRunResult();

            GUILayout.Space(6f);
            GUILayout.Label(ConvaiActionsEditorStrings.TestRunSpeechGateNote, Theme.MutedWrapped);
        }

        private void EnsureTestRunInputs(ConvaiActionDefinition definition)
        {
            if (ReferenceEquals(_testRunDefinition, definition))
                return;

            _testRunDefinition = definition;
            _testRunTargetName = string.Empty;
            _testRunParameterTexts.Clear();

            List<ConvaiActionParameterDefinition> parameters = definition?.Parameters;
            int count = parameters?.Count ?? 0;
            _testRunParameterLabels = count == 0 ? Array.Empty<GUIContent>() : new GUIContent[count];
            _testRunChoiceOptions = count == 0 ? Array.Empty<GUIContent[]>() : new GUIContent[count][];

            for (int i = 0; i < count; i++)
            {
                _testRunParameterTexts.Add(string.Empty);
                ConvaiActionParameterDefinition parameter = parameters[i];
                string label = string.IsNullOrWhiteSpace(parameter?.Name) ? UnnamedValueLabel : parameter.Name;
                string tooltip = string.IsNullOrWhiteSpace(parameter?.Description)
                    ? ConvaiActionsEditorStrings.TestRunParametersLabel.tooltip
                    : parameter.Description;
                _testRunParameterLabels[i] = new GUIContent(label, tooltip);

                if (parameter?.Type != ConvaiActionParameterType.Choice)
                    continue;

                List<string> choices = parameter.Choices;
                int choiceCount = choices?.Count ?? 0;
                var options = new GUIContent[choiceCount + 1];
                options[0] = ConvaiActionsEditorStrings.TestRunChoiceNotSet;
                for (int c = 0; c < choiceCount; c++)
                    options[c + 1] = new GUIContent(choices[c] ?? string.Empty,
                        ConvaiActionsEditorStrings.TestRunParametersLabel.tooltip);
                _testRunChoiceOptions[i] = options;
            }
        }

        private void DrawTestRunTargetPicker(ConvaiActionDefinition definition)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(ConvaiActionsEditorStrings.TestRunTargetField,
                    GUILayout.Width(EditorGUIUtility.labelWidth));
                GUIContent choice = string.IsNullOrEmpty(_testRunTargetName)
                    ? ConvaiActionsEditorStrings.TestRunTargetNotSet
                    : ConvaiActionsEditorStrings.BuildInitialAttentionChoice(_testRunTargetName);
                Rect choiceRect = GUILayoutUtility.GetRect(220f, 22f, GUILayout.Width(220f), GUILayout.Height(22f));
                if (Theme.GhostButton(choiceRect, choice))
                    ShowTestRunTargetMenu(definition.TargetRequirement);
                GUILayout.FlexibleSpace();
            }
        }

        private void ShowTestRunTargetMenu(ConvaiActionTargetRequirement requirement)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(ConvaiActionsEditorStrings.TestRunChoiceNotSet.text),
                string.IsNullOrEmpty(_testRunTargetName), () =>
                {
                    _testRunTargetName = string.Empty;
                    Repaint();
                });

            int added = 0;
            ConvaiActionConfig config = _character != null ? _character.GetRuntimeActionConfig() : null;
            if (config != null)
            {
                bool includeObjects = requirement is ConvaiActionTargetRequirement.Object or ConvaiActionTargetRequirement.Either;
                bool includeCharacters = requirement is ConvaiActionTargetRequirement.Character or ConvaiActionTargetRequirement.Either;

                if (includeObjects)
                {
                    for (int i = 0; i < config.Objects.Count; i++)
                        added += AddTargetMenuItem(menu, config.Objects[i]?.Name, config.Objects[i]?.Available ?? false);
                }

                if (includeCharacters)
                {
                    for (int i = 0; i < config.Characters.Count; i++)
                        added += AddTargetMenuItem(menu, config.Characters[i]?.Name, config.Characters[i]?.Available ?? false);
                }
            }

            if (added == 0)
                menu.AddDisabledItem(new GUIContent(ConvaiActionsEditorStrings.TestRunNoTargetsAvailable.text));

            menu.ShowAsContext();
        }

        private int AddTargetMenuItem(GenericMenu menu, string name, bool available)
        {
            if (!available || string.IsNullOrWhiteSpace(name))
                return 0;

            string trimmed = name.Trim();
            menu.AddItem(new GUIContent(trimmed),
                string.Equals(_testRunTargetName, trimmed, StringComparison.OrdinalIgnoreCase), () =>
                {
                    _testRunTargetName = trimmed;
                    Repaint();
                });
            return 1;
        }

        private void DrawTestRunParameterField(int index, ConvaiActionParameterDefinition parameter)
        {
            if (parameter == null || index >= _testRunParameterTexts.Count)
                return;

            GUIContent label = index < _testRunParameterLabels.Length
                ? _testRunParameterLabels[index]
                : GUIContent.none;
            string text = _testRunParameterTexts[index];

            switch (parameter.Type)
            {
                case ConvaiActionParameterType.Bool:
                {
                    GUIContent[] options = BoolOptions();
                    int current = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? 1
                        : string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                    int next = EditorGUILayout.Popup(label, current, options);
                    if (next != current)
                        _testRunParameterTexts[index] = next == 1 ? "true" : next == 2 ? "false" : string.Empty;
                    break;
                }

                case ConvaiActionParameterType.Choice:
                {
                    GUIContent[] options = index < _testRunChoiceOptions.Length ? _testRunChoiceOptions[index] : null;
                    if (options == null || options.Length <= 1)
                    {
                        _testRunParameterTexts[index] = EditorGUILayout.TextField(label, text);
                        break;
                    }

                    int current = 0;
                    for (int i = 1; i < options.Length; i++)
                    {
                        if (string.Equals(options[i].text, text, StringComparison.OrdinalIgnoreCase))
                        {
                            current = i;
                            break;
                        }
                    }

                    int next = EditorGUILayout.Popup(label, current, options);
                    if (next != current)
                        _testRunParameterTexts[index] = next == 0 ? string.Empty : options[next].text;
                    break;
                }

                case ConvaiActionParameterType.Number:
                {
                    _testRunParameterTexts[index] = EditorGUILayout.TextField(label, text);
                    if (!ConvaiActionTestRunModel.IsNumberTextValid(_testRunParameterTexts[index]))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(EditorGUIUtility.labelWidth);
                            GUILayout.Label(ConvaiActionsEditorStrings.TestRunInvalidNumberHint, Theme.MutedWrapped);
                        }
                    }

                    break;
                }

                default:
                    _testRunParameterTexts[index] = EditorGUILayout.TextField(label, text);
                    break;
            }
        }

        private static GUIContent[] BoolOptions() =>
            s_testRunBoolOptions ??= new[]
            {
                ConvaiActionsEditorStrings.TestRunChoiceNotSet,
                ConvaiActionsEditorStrings.TestRunBoolTrue,
                ConvaiActionsEditorStrings.TestRunBoolFalse
            };

        private ConvaiActionCommand BuildTestRunCommand(ConvaiActionConfigSource source, ConvaiActionDefinition definition)
        {
            ConvaiActionTestRunService.ResolveInjectionContext(_character, source,
                out ConvaiActionConfig actionConfig,
                out IReadOnlyList<ConvaiActionDefinition> definitions);
            return ConvaiActionTestRunModel.BuildCommand(
                definition, _testRunTargetName, _testRunParameterTexts, actionConfig, definitions);
        }

        /// <summary>
        ///     Whether the selected action is currently available on the live character (runtime
        ///     override over the authored Offer This Action flag). Play mode only.
        /// </summary>
        private bool IsTestRunActionAvailable(ConvaiActionDefinition definition)
        {
            if (definition == null)
                return false;

            if (_character != null && !string.IsNullOrWhiteSpace(definition.ActionName))
                return _character.Actions.IsActionAvailable(definition.ActionName);

            return definition.Enabled;
        }

        private void RunTestNow(
            ConvaiActionDispatcher dispatcher,
            ConvaiActionConfigSource source,
            ConvaiActionDefinition definition,
            bool bypassAvailability = false)
        {
            ConvaiActionCommand command = BuildTestRunCommand(source, definition);
            if (command == null)
                return;

            // Scoped strictly to this injected command: the dispatcher skips its availability
            // check for it, and nothing about the action's authored or runtime state changes.
            if (bypassAvailability)
                command.BypassAvailability = true;

            BeginTrackedRun(command.Name);
            ConvaiActionTestRunService.EnqueueBatch(dispatcher, new[] { command });
        }

        private void AddToRunList(ConvaiActionConfigSource source, ConvaiActionRow row, ConvaiActionDefinition definition)
        {
            ConvaiActionCommand command = BuildTestRunCommand(source, definition);
            if (command == null)
                return;

            string displayName = string.IsNullOrEmpty(command.Target)
                ? row.DisplayName
                : $"{row.DisplayName} → {command.Target}";
            _testRunQueue.Add(new QueuedTestRun(displayName, command));
        }

        private void DrawTestRunQueue(ConvaiActionDispatcher dispatcher)
        {
            if (_testRunQueue.Count == 0)
                return;

            GUILayout.Space(8f);
            GUILayout.Label(ConvaiActionsEditorStrings.TestRunListTitle, Theme.MicroLabel);
            GUILayout.Space(2f);

            Theme.BeginPanel(null);
            int removeIndex = -1;
            for (int i = 0; i < _testRunQueue.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{i + 1}.", Theme.MicroLabel, GUILayout.Width(20f));
                    GUILayout.Label(_testRunQueue[i].DisplayName, Theme.BodyWrapped);
                    GUILayout.FlexibleSpace();
                    Rect removeRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f));
                    if (Theme.IconButton(removeRect, ConvaiActionsEditorStrings.TestRunRemoveFromListButton))
                        removeIndex = i;
                }
            }

            if (removeIndex >= 0)
                _testRunQueue.RemoveAt(removeIndex);

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect runAllRect = GUILayoutUtility.GetRect(150f, 24f, GUILayout.Width(150f), GUILayout.Height(24f));
                if (Theme.PrimaryButton(runAllRect, ConvaiActionsEditorStrings.TestRunRunAllButton))
                    RunAllInOrder(dispatcher);

                GUILayout.Space(8f);
                Rect clearRect = GUILayoutUtility.GetRect(60f, 24f, GUILayout.Width(60f), GUILayout.Height(24f));
                if (Theme.GhostButton(clearRect, ConvaiActionsEditorStrings.TestRunClearListButton))
                    _testRunQueue.Clear();

                GUILayout.FlexibleSpace();
            }

            Theme.EndPanel(0f);
        }

        private void RunAllInOrder(ConvaiActionDispatcher dispatcher)
        {
            if (_testRunQueue.Count == 0)
                return;

            var commands = new ConvaiActionCommand[_testRunQueue.Count];
            for (int i = 0; i < _testRunQueue.Count; i++)
                commands[i] = _testRunQueue[i].Command;

            BeginTrackedRun(commands[0].Name);
            ConvaiActionTestRunService.EnqueueBatch(dispatcher, commands);
        }

        private void BeginTrackedRun(string firstActionName)
        {
            _testRunToken = ConvaiActionsSessionCollector.Log.ExpectTestRun(firstActionName);
            _testRunActive = true;
            _testRunResultVersion = -1;
            Repaint();
        }

        private void DrawTestRunResult()
        {
            if (_testRunToken == 0)
                return;

            ConvaiActionsSessionLog log = ConvaiActionsSessionCollector.Log;
            ConvaiActionsSessionLog.BatchRecord batch = log.FindByToken(_testRunToken);

            GUILayout.Space(8f);
            if (batch == null)
            {
                Theme.BeginPanel(null);
                GUILayout.Label(ConvaiActionsEditorStrings.TestRunWaitingForStart, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            EnsureTestRunResultLines(log, batch);

            Color? tint = _testRunResultFinished ? (_testRunResultHealthy ? Theme.StatusReady : Theme.StatusWarn) : (Color?)null;
            Theme.BeginPanel(tint);

            for (int i = 0; i < _testRunResultLines.Count; i++)
                GUILayout.Label(_testRunResultLines[i], Theme.BodyWrapped);

            // The in-flight step's elapsed line changes every repaint, so it is composed live.
            if (!batch.Finished && batch.Steps.Count > 0)
            {
                ConvaiActionsSessionLog.StepRecord running = batch.Steps[batch.Steps.Count - 1];
                if (!running.Completed)
                {
                    double elapsed = EditorApplication.timeSinceStartup - running.StartTime;
                    GUILayout.Label(
                        ConvaiActionsEditorStrings.BuildTestRunRunning(running.ActionName, elapsed),
                        Theme.BodyWrapped);
                }
            }

            if (batch.Finished)
            {
                GUILayout.Space(4f);
                Rect timelineRect = GUILayoutUtility.GetRect(130f, 20f, GUILayout.Width(130f), GUILayout.Height(20f));
                if (Theme.GhostButton(timelineRect, ConvaiActionsEditorStrings.TestRunShowTimelineButton))
                {
                    SelectTimelineBatch(batch);
                    SetMode(ConvaiActionsEditorMode.Live);
                }
            }

            Theme.EndPanel(0f);
        }

        /// <summary>Rebuilds the cached, finished-step result lines only when the collector log changed.</summary>
        private void EnsureTestRunResultLines(ConvaiActionsSessionLog log, ConvaiActionsSessionLog.BatchRecord batch)
        {
            if (log.Version == _testRunResultVersion)
                return;

            _testRunResultVersion = log.Version;
            _testRunResultLines.Clear();
            _testRunResultHealthy = true;
            _testRunResultFinished = batch.Finished;
            _testRunActive = !batch.Finished;

            for (int i = 0; i < batch.Steps.Count; i++)
            {
                ConvaiActionsSessionLog.StepRecord step = batch.Steps[i];
                if (!step.Completed)
                    continue;

                if (step.Status != ConvaiActionExecutionStatus.Succeeded)
                    _testRunResultHealthy = false;

                string reason = step.Status == ConvaiActionExecutionStatus.Succeeded
                    ? string.Empty
                    : step.FailureReason != ConvaiActionFailureReason.None
                        ? step.FailureReason.ToString()
                        : step.FailureMessage;
                _testRunResultLines.Add(ConvaiActionsEditorStrings.BuildTestRunStepOutcome(
                    step.ActionName,
                    ConvaiActionsEditorStrings.DescribeStepStatus(step.Status),
                    step.DurationMs,
                    reason));
            }
        }

        #endregion
    }
}
