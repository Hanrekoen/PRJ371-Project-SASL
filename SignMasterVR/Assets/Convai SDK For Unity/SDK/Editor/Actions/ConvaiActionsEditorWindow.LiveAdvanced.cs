using System;
using System.Collections.Generic;
using System.Text;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.Logging;
using Convai.Editor.Inspectors;
using Convai.Runtime;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Facades;
using Convai.Runtime.Logging;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEditor;
using UnityEngine;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;
using Convai.Editor.UI;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Live mode's Advanced group : three collapsed-by-default cards, each covering a
    ///     capability with no equivalent anywhere else in the Actions Editor — raw command injection
    ///     (incl. the <see cref="IConvaiActionDebugPresetProvider" /> extension point), the target
    ///     resolution tester, and runtime session state + patch composer (the only editor surface
    ///     that exercises the public <see cref="ConvaiActionConfigPatch" /> API). Everything else
    ///     that window offered (event feed, batch history, target registry, feedback log, rendered
    ///     config preview) is already covered by Live mode's Timeline, Registry, and Feedback cards,
    ///     and by Scene Knowledge's "Sent To Convai" preview.
    /// </summary>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string LiveRawCommandSectionId = "LiveAdvancedRawCommand";
        private const string LiveResolutionSectionId = "LiveAdvancedResolutionTester";
        private const string LiveRuntimePatchSectionId = "LiveAdvancedRuntimePatch";
        private const string LiveRawCommandGlyph = ConvaiEditorGlyphs.Command;
        private const string LiveResolutionGlyph = ConvaiEditorGlyphs.Discovery;
        private const string LiveRuntimePatchGlyph = ConvaiEditorGlyphs.Routing;

        private string _liveRawActionName = string.Empty;
        private string _liveRawTarget = string.Empty;
        private string _liveResolutionQuery = string.Empty;
        private string _liveResolutionResultText = string.Empty;

        #region Advanced group

        private void DrawLiveAdvancedSection(ConvaiActionConfigSource source)
        {
            GUILayout.Space(6f);
            Rect divider = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            Theme.DividerLine(divider, Theme.Divider);
            Theme.GroupCaption(ConvaiActionsEditorStrings.AdvancedFoldout);
            GUILayout.Space(4f);

            DrawLiveRawCommandCard(source);
            DrawLiveResolutionTesterCard();
            DrawLiveRuntimePatchCard();
        }

        /// <summary>Shared collapsible header for a Live Advanced card, persisted like the detail pane's Advanced section.</summary>
        private static bool DrawLiveAdvancedCardHeader(string sectionId, string glyph, GUIContent title)
        {
            bool expanded = ConvaiEditorSectionState.Get(SectionStateHostId, sectionId, false);
            bool newExpanded = Theme.CollapsibleSectionHeader(glyph, title, expanded);
            if (newExpanded != expanded)
                ConvaiEditorSectionState.Set(SectionStateHostId, sectionId, newExpanded);

            return newExpanded;
        }

        #endregion

        #region Send a raw command

        private void DrawLiveRawCommandCard(ConvaiActionConfigSource source)
        {
            Theme.BeginCard();
            bool expanded = DrawLiveAdvancedCardHeader(
                LiveRawCommandSectionId, LiveRawCommandGlyph, ConvaiActionsEditorStrings.LiveRawCommandTitle);
            if (!expanded)
            {
                Theme.EndCard();
                return;
            }

            GUILayout.Label(ConvaiActionsEditorStrings.LiveRawCommandIntro, Theme.MutedWrapped);
            GUILayout.Space(6f);

            ConvaiActionDispatcher dispatcher = _settingsDispatcher;
            if (dispatcher == null)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.LiveNoDispatcherBody, Theme.BodyWrapped);
                Theme.EndPanel(0f);
            }
            else
            {
                _liveRawActionName = EditorGUILayout.TextField(
                    ConvaiActionsEditorStrings.LiveRawActionNameField, _liveRawActionName);
                _liveRawTarget = EditorGUILayout.TextField(
                    ConvaiActionsEditorStrings.LiveRawTargetField, _liveRawTarget);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_liveRawActionName)))
                    {
                        Rect sendRect = GUILayoutUtility.GetRect(90f, 24f, GUILayout.Width(90f), GUILayout.Height(24f));
                        if (Theme.GhostButton(sendRect, ConvaiActionsEditorStrings.LiveRawSendButton))
                            InjectLiveRawCommand(source, dispatcher, _liveRawActionName, _liveRawTarget);

                        GUILayout.Space(6f);
                        Rect sendFirstRect = GUILayoutUtility.GetRect(190f, 24f, GUILayout.Width(190f), GUILayout.Height(24f));
                        if (Theme.GhostButton(sendFirstRect, ConvaiActionsEditorStrings.LiveRawSendToFirstObjectButton))
                            InjectLiveRawCommandToFirstObject(source, dispatcher, _liveRawActionName);
                    }

                    GUILayout.FlexibleSpace();
                }

                IReadOnlyList<ConvaiActionDefinition> definitions = source?.Definitions;
                if (definitions != null && definitions.Count > 0)
                {
                    GUILayout.Space(8f);
                    GUILayout.Label(ConvaiActionsEditorStrings.LiveRawAuthoredActionsLabel, Theme.MicroLabel);
                    GUILayout.Space(2f);
                    DrawWrappedGhostButtonGrid(
                        definitions,
                        3,
                        22f,
                        definition => string.IsNullOrWhiteSpace(definition?.ActionName)
                            ? null
                            : new GUIContent(definition.ActionName),
                        definition => InjectLiveRawCommandToFirstObject(source, dispatcher, definition.ActionName));
                }
            }

            DrawLivePresetProviders(source, dispatcher);

            Theme.EndCard();
        }

        private void InjectLiveRawCommand(
            ConvaiActionConfigSource source, ConvaiActionDispatcher dispatcher, string actionName, string target) =>
            ConvaiActionTestRunService.Inject(dispatcher, _character, source, actionName, target);

        private void InjectLiveRawCommandToFirstObject(
            ConvaiActionConfigSource source, ConvaiActionDispatcher dispatcher, string actionName)
        {
            IReadOnlyList<ConvaiActionObjectDefinition> objects = source?.Objects;
            string target = objects != null && objects.Count > 0 ? objects[0]?.Name : null;
            InjectLiveRawCommand(source, dispatcher, actionName, target);
        }

        #endregion

        #region Presets (IConvaiActionDebugPresetProvider extension point)

        /// <summary>
        ///     Project-specific templates and one-click injections registered through
        ///     <see cref="ConvaiActionDebugPresetRegistry" /> — the retired debug window's only
        ///     consumer of this public extension point. Apply Templates edits the authored
        ///     <see cref="ConvaiActionConfigSource" /> (Edit Mode only); the preset grid injects
        ///     through the same dispatcher seam as the manual fields above.
        /// </summary>
        private void DrawLivePresetProviders(ConvaiActionConfigSource source, ConvaiActionDispatcher dispatcher)
        {
            IReadOnlyList<IConvaiActionDebugPresetProvider> providers = ConvaiActionDebugPresetRegistry.Providers;
            if (providers.Count == 0)
                return;

            GUILayout.Space(10f);
            GUILayout.Label(ConvaiActionsEditorStrings.LivePresetsLabel, Theme.MicroLabel);

            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                IConvaiActionDebugPresetProvider provider = providers[providerIndex];
                if (provider == null)
                    continue;

                GUILayout.Space(4f);
                GUILayout.Label(
                    string.IsNullOrWhiteSpace(provider.DisplayName)
                        ? ConvaiActionsEditorStrings.LivePresetsLabel.text
                        : provider.DisplayName,
                    Theme.MicroLabel);

                bool canApplyTemplates = source != null && !UnityEngine.Application.isPlaying;
                using (new EditorGUI.DisabledScope(!canApplyTemplates))
                {
                    Rect applyRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                    if (Theme.GhostButton(applyRect, ConvaiActionsEditorStrings.BuildLiveApplyTemplatesButton(provider.DisplayName)))
                        ApplyLivePresetTemplates(source, provider);
                }

                IReadOnlyList<ConvaiActionDebugInjectionPreset> presets = provider.GetInjectionPresets();
                if (presets == null || presets.Count == 0)
                    continue;

                GUILayout.Space(2f);
                using (new EditorGUI.DisabledScope(dispatcher == null))
                {
                    DrawWrappedGhostButtonGrid(
                        presets,
                        3,
                        22f,
                        preset => string.IsNullOrWhiteSpace(preset?.ActionName)
                            ? null
                            : new GUIContent(string.IsNullOrWhiteSpace(preset.Label) ? preset.ActionName : preset.Label),
                        preset => InjectLiveRawCommand(source, dispatcher, preset.ActionName, preset.Target));
                }
            }

            if (UnityEngine.Application.isPlaying)
            {
                GUILayout.Space(4f);
                GUILayout.Label(ConvaiActionsEditorStrings.LivePresetsPlayModeNotice, Theme.MutedWrapped);
            }
        }

        /// <summary>
        ///     Merges a provider's typed templates into the selected config source, exactly as the
        ///     retired debug window's Apply Templates did: preserving any already-assigned executor
        ///     reference and authored dispatch tuning on re-application, and leaving definitions the
        ///     provider does not know about untouched.
        /// </summary>
        private void ApplyLivePresetTemplates(ConvaiActionConfigSource source, IConvaiActionDebugPresetProvider provider)
        {
            if (UnityEngine.Application.isPlaying)
            {
                ConvaiLogger.Warning("Applying templates is edit-mode only.", LogCategory.Editor);
                return;
            }

            if (source == null)
            {
                ConvaiLogger.Warning("No ConvaiActionConfigSource found.", LogCategory.Editor);
                return;
            }

            IReadOnlyList<ConvaiActionDefinition> templates = provider?.BuildTemplates();
            if (templates == null || templates.Count == 0)
            {
                ConvaiLogger.Warning($"Provider '{provider?.DisplayName}' supplied no templates.", LogCategory.Editor);
                return;
            }

            Undo.RecordObject(source, $"Apply {provider.DisplayName} Action Templates");
            IReadOnlyList<ConvaiActionDefinition> existing = source.Definitions;

            var definitions = new List<ConvaiActionDefinition>(templates.Count);
            for (int i = 0; i < templates.Count; i++)
            {
                ConvaiActionDefinition template = templates[i];
                if (template == null || string.IsNullOrWhiteSpace(template.ActionName))
                    continue;

                definitions.Add(MergeLivePresetDefinition(template, FindLivePresetDefinition(existing, template.ActionName)));
            }

            CopyUnknownLivePresetDefinitions(existing, definitions);
            source.ReplaceDefinitions(definitions);
            MarkDirty(source);

            ConvaiLogger.Info($"Applied '{provider.DisplayName}' action templates.", LogCategory.Editor);
            Repaint();
        }

        private static ConvaiActionDefinition MergeLivePresetDefinition(
            ConvaiActionDefinition template, ConvaiActionDefinition previous)
        {
            ConvaiActionDefinition merged = template.Clone();
            if (previous == null)
                return merged;

            // Preserve scene wiring and authored dispatch tuning across template re-application.
            merged.Executor = previous.Executor != null ? previous.Executor : merged.Executor;
            merged.TimeoutSeconds = previous.TimeoutSeconds > 0f ? previous.TimeoutSeconds : merged.TimeoutSeconds;
            merged.FailurePolicyOverride = previous.FailurePolicyOverride != ConvaiActionFailurePolicyOverride.UseDispatcherDefault
                ? previous.FailurePolicyOverride
                : merged.FailurePolicyOverride;
            merged.WaitForBotSpeech = previous.WaitForBotSpeech || merged.WaitForBotSpeech;
            merged.DelayAfterBotSpeechSeconds = previous.DelayAfterBotSpeechSeconds > 0f
                ? previous.DelayAfterBotSpeechSeconds
                : merged.DelayAfterBotSpeechSeconds;
            return merged;
        }

        private static void CopyUnknownLivePresetDefinitions(
            IReadOnlyList<ConvaiActionDefinition> existing, List<ConvaiActionDefinition> definitions)
        {
            if (existing == null)
                return;

            for (int i = 0; i < existing.Count; i++)
            {
                ConvaiActionDefinition definition = existing[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.ActionName))
                    continue;

                if (FindLivePresetDefinition(definitions, definition.ActionName) != null)
                    continue;

                definitions.Add(definition.Clone());
            }
        }

        private static ConvaiActionDefinition FindLivePresetDefinition(
            IReadOnlyList<ConvaiActionDefinition> definitions, string actionName)
        {
            if (definitions == null || string.IsNullOrWhiteSpace(actionName))
                return null;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                if (string.Equals(definition?.ActionName, actionName, StringComparison.OrdinalIgnoreCase))
                    return definition;
            }

            return null;
        }

        #endregion

        #region Test target resolution

        private void DrawLiveResolutionTesterCard()
        {
            Theme.BeginCard();
            bool expanded = DrawLiveAdvancedCardHeader(
                LiveResolutionSectionId, LiveResolutionGlyph, ConvaiActionsEditorStrings.LiveResolutionTesterTitle);
            if (!expanded)
            {
                Theme.EndCard();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _liveResolutionQuery = EditorGUILayout.TextField(
                    ConvaiActionsEditorStrings.LiveResolutionQueryField, _liveResolutionQuery);
                GUILayout.Space(4f);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_liveResolutionQuery)))
                {
                    Rect resolveRect = GUILayoutUtility.GetRect(80f, 20f, GUILayout.Width(80f), GUILayout.Height(20f));
                    if (Theme.GhostButton(resolveRect, ConvaiActionsEditorStrings.LiveResolveButton))
                        RunLiveResolutionTest();
                }
            }

            if (!string.IsNullOrEmpty(_liveResolutionResultText))
            {
                GUILayout.Space(4f);
                Theme.BeginPanel(null);
                GUILayout.Label(_liveResolutionResultText, Theme.BodyWrapped);
                Theme.EndPanel(0f);
            }

            GUILayout.Space(6f);
            GUILayout.Label(ConvaiActionsEditorStrings.LiveResolutionExplainer, Theme.MutedWrapped);

            Theme.EndCard();
        }

        private void RunLiveResolutionTest()
        {
            ConvaiActionConfigSource source = _character != null ? _character.GetActionConfigSource() : null;
            ConvaiActionTestRunService.ResolveInjectionContext(
                _character, source, out ConvaiActionConfig actionConfig, out _);
            if (actionConfig == null)
            {
                _liveResolutionResultText = ConvaiActionsEditorStrings.LiveResolutionNoConfigResult.text;
                return;
            }

            Vector3? origin = _character != null ? _character.transform.position : (Vector3?)null;
            ConvaiResolvedActionTarget resolved = ConvaiResolvedActionTarget.Resolve(
                _liveResolutionQuery, actionConfig, (ConvaiActionTargetRequirement?)null, origin);

            string query = _liveResolutionQuery.Trim();
            _liveResolutionResultText = resolved == null
                ? ConvaiActionsEditorStrings.BuildLiveResolutionNoMatch(query).text
                : ConvaiActionsEditorStrings.BuildLiveResolutionMatched(query, resolved.Name, resolved.Kind.ToString()).text;
        }

        #endregion

        #region Runtime session state + patch composer

        private readonly HashSet<string> _liveRuntimeObservedUpdateIds = new(StringComparer.Ordinal);
        private ConvaiEvents _liveRuntimeSubscribedEvents;
        private DynamicContextUpdateResultReceived _liveRuntimeLastAck;
        private bool _liveRuntimeHasLastAck;
        private ConvaiActionPatchDraft _liveRuntimePatchDraft = new();
        private string _liveRuntimePatchStatus = string.Empty;
        private bool _liveRuntimePatchStatusIsError;

        private void DrawLiveRuntimePatchCard()
        {
            Theme.BeginCard();
            bool expanded = DrawLiveAdvancedCardHeader(
                LiveRuntimePatchSectionId, LiveRuntimePatchGlyph, ConvaiActionsEditorStrings.LiveRuntimePatchTitle);
            if (!expanded)
            {
                Theme.EndCard();
                return;
            }

            EnsureLiveRuntimeDiagnosticsSubscription();

            GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchIntro, Theme.MutedWrapped);
            GUILayout.Space(8f);

            DrawLiveRuntimeSessionState();
            GUILayout.Space(10f);
            DrawLiveRuntimePatchComposer();

            Theme.EndCard();
        }

        private void DrawLiveRuntimeSessionState()
        {
            GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimeSessionStateTitle, Theme.MicroLabel);
            GUILayout.Space(2f);

            if (!UnityEngine.Application.isPlaying)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchNeedsPlayMode, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            if (_character == null)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimeNoCharacterBody, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                return;
            }

            GUILayout.Label(
                _character.IsInConversation
                    ? ConvaiActionsEditorStrings.LiveRuntimeSessionReady
                    : ConvaiActionsEditorStrings.LiveRuntimeSessionNotReady,
                Theme.BodyWrapped);
            GUILayout.Space(4f);

            ConvaiActionConfig confirmed = _character.ActionConfig;
            string snapshot = FormatRuntimeSnapshot(
                confirmed, _character.ActionDefinitions, _character.GetRuntimeActionDefinitionCatalog());
            DrawLiveReadOnlyText(ConvaiActionsEditorStrings.LiveRuntimeConfirmedSnapshotLabel, snapshot);

            IReadOnlyList<ConvaiRuntimeActionUpdateDebugInfo> pending =
                _character.GetPendingRuntimeActionUpdateDebugInfo();
            for (int i = 0; i < pending.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(pending[i].UpdateId))
                    _liveRuntimeObservedUpdateIds.Add(pending[i].UpdateId);
            }

            GUILayout.Space(6f);
            GUILayout.Label(ConvaiActionsEditorStrings.BuildLivePendingUpdatesLabel(pending.Count), Theme.MicroLabel);
            if (pending.Count == 0)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimeNoPendingUpdates, Theme.MutedWrapped);
            }
            else
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    ConvaiRuntimeActionUpdateDebugInfo item = pending[i];
                    double ageSeconds = Math.Max(0d, (DateTime.UtcNow - item.SentAtUtc).TotalSeconds);
                    string mutation = item.MutatesActionConfig && item.MutatesTopLevelAttention
                        ? "config + attention"
                        : item.MutatesActionConfig
                            ? "config"
                            : "attention";
                    string ack = item.HasAcknowledgement
                        ? $"ACK received ({item.AcknowledgementStatus})"
                        : "waiting for ACK";
                    GUILayout.Label($"{item.UpdateId} — {mutation}; {ack}; {ageSeconds:0.0}s", Theme.MutedWrapped);
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimeLastAckLabel, Theme.MicroLabel);
            if (_liveRuntimeHasLastAck)
                DrawLiveReadOnlyText(null, FormatActionUpdateAcknowledgement(_liveRuntimeLastAck));
            else
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimeNoAckObserved, Theme.MutedWrapped);
        }

        private void DrawLiveRuntimePatchComposer()
        {
            GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchComposerTitle, Theme.MicroLabel);
            GUILayout.Space(2f);
            GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchComposerExplainer, Theme.MutedWrapped);
            GUILayout.Space(6f);

            _liveRuntimePatchDraft.IncludeActions = EditorGUILayout.ToggleLeft(
                ConvaiActionsEditorStrings.LiveRuntimePatchIncludeActions, _liveRuntimePatchDraft.IncludeActions);
            if (_liveRuntimePatchDraft.IncludeActions)
            {
                EditorGUI.indentLevel++;
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchActionsHint, Theme.MutedWrapped);
                _liveRuntimePatchDraft.ActionsText = EditorGUILayout.TextArea(
                    _liveRuntimePatchDraft.ActionsText ?? string.Empty, GUILayout.MinHeight(46f));
                EditorGUI.indentLevel--;
            }

            _liveRuntimePatchDraft.IncludeObjects = EditorGUILayout.ToggleLeft(
                ConvaiActionsEditorStrings.LiveRuntimePatchIncludeObjects, _liveRuntimePatchDraft.IncludeObjects);
            if (_liveRuntimePatchDraft.IncludeObjects)
                DrawLiveRuntimePatchObjectRows();

            _liveRuntimePatchDraft.IncludeCharacters = EditorGUILayout.ToggleLeft(
                ConvaiActionsEditorStrings.LiveRuntimePatchIncludeCharacters, _liveRuntimePatchDraft.IncludeCharacters);
            if (_liveRuntimePatchDraft.IncludeCharacters)
                DrawLiveRuntimePatchCharacterRows();

            _liveRuntimePatchDraft.IncludeNestedAttention = EditorGUILayout.ToggleLeft(
                ConvaiActionsEditorStrings.LiveRuntimePatchIncludeNestedAttention,
                _liveRuntimePatchDraft.IncludeNestedAttention);
            if (_liveRuntimePatchDraft.IncludeNestedAttention)
            {
                EditorGUI.indentLevel++;
                _liveRuntimePatchDraft.NestedAttention = EditorGUILayout.TextField(
                    ConvaiActionsEditorStrings.LiveRuntimePatchAttentionField,
                    _liveRuntimePatchDraft.NestedAttention ?? string.Empty);
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchClearsOnEmpty, Theme.MutedWrapped);
                EditorGUI.indentLevel--;
            }

            _liveRuntimePatchDraft.IncludeTopLevelAttention = EditorGUILayout.ToggleLeft(
                ConvaiActionsEditorStrings.LiveRuntimePatchIncludeTopLevelAttention,
                _liveRuntimePatchDraft.IncludeTopLevelAttention);
            if (_liveRuntimePatchDraft.IncludeTopLevelAttention)
            {
                EditorGUI.indentLevel++;
                _liveRuntimePatchDraft.TopLevelAttention = EditorGUILayout.TextField(
                    ConvaiActionsEditorStrings.LiveRuntimePatchAttentionField,
                    _liveRuntimePatchDraft.TopLevelAttention ?? string.Empty);
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchTopLevelWinsHint, Theme.MutedWrapped);
                EditorGUI.indentLevel--;
            }

            _liveRuntimePatchDraft.Reaction = (ConvaiRespondMode)EditorGUILayout.EnumPopup(
                ConvaiActionsEditorStrings.LiveRuntimePatchReactionField, _liveRuntimePatchDraft.Reaction);
            _liveRuntimePatchDraft.UpdateId = EditorGUILayout.TextField(
                ConvaiActionsEditorStrings.LiveRuntimePatchUpdateIdField, _liveRuntimePatchDraft.UpdateId ?? string.Empty);

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect loadRect = GUILayoutUtility.GetRect(104f, 22f, GUILayout.Width(104f), GUILayout.Height(22f));
                if (Theme.GhostButton(loadRect, ConvaiActionsEditorStrings.LiveRuntimePatchLoadConfirmedButton))
                    LoadConfirmedLiveRuntimePatchDraft();

                GUILayout.Space(4f);
                Rect resetRect = GUILayoutUtility.GetRect(90f, 22f, GUILayout.Width(90f), GUILayout.Height(22f));
                if (Theme.GhostButton(resetRect, ConvaiActionsEditorStrings.LiveRuntimePatchResetButton))
                    ResetLiveRuntimePatchDraft();

                GUILayout.Space(4f);
                using (new EditorGUI.DisabledScope(_character == null || !_liveRuntimePatchDraft.HasMutation))
                {
                    Rect previewRect = GUILayoutUtility.GetRect(80f, 22f, GUILayout.Width(80f), GUILayout.Height(22f));
                    if (Theme.GhostButton(previewRect, ConvaiActionsEditorStrings.LiveRuntimePatchPreviewButton))
                        PreviewLiveRuntimePatch();
                }

                GUILayout.Space(4f);
                bool sessionReady = CanSendLiveRuntimePatch();
                using (new EditorGUI.DisabledScope(!sessionReady || !_liveRuntimePatchDraft.HasMutation))
                {
                    Rect sendRect = GUILayoutUtility.GetRect(90f, 22f, GUILayout.Width(90f), GUILayout.Height(22f));
                    if (Theme.GhostButton(sendRect, ConvaiActionsEditorStrings.LiveRuntimePatchSendButton))
                        SendLiveRuntimePatch();
                }

                GUILayout.FlexibleSpace();
            }

            if (!CanSendLiveRuntimePatch())
            {
                GUILayout.Space(4f);
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchNotConnected, Theme.MutedWrapped);
            }

            if (!string.IsNullOrEmpty(_liveRuntimePatchStatus))
            {
                GUILayout.Space(4f);
                Theme.BeginPanel(_liveRuntimePatchStatusIsError ? Theme.StatusError : null);
                GUILayout.Label(_liveRuntimePatchStatus, Theme.BodyWrapped);
                Theme.EndPanel(0f);
            }
        }

        /// <summary>Whether the picked Convai Character's session can currently accept a sent patch.</summary>
        private bool CanSendLiveRuntimePatch() =>
            UnityEngine.Application.isPlaying && _character != null && _character.IsInConversation;

        private void DrawLiveRuntimePatchObjectRows()
        {
            EditorGUI.indentLevel++;
            int removeIndex = -1;
            for (int i = 0; i < _liveRuntimePatchDraft.Objects.Count; i++)
            {
                ConvaiActionObjectDefinition item = _liveRuntimePatchDraft.Objects[i] ??= new ConvaiActionObjectDefinition();
                Theme.BeginPanel(null);
                item.Name = EditorGUILayout.TextField(ConvaiActionsEditorStrings.KnownObjectNameField, item.Name ?? string.Empty);
                item.Description = EditorGUILayout.TextField(
                    ConvaiActionsEditorStrings.KnownObjectDescriptionField, item.Description ?? string.Empty);
                item.GameObjectReference = (GameObject)EditorGUILayout.ObjectField(
                    ConvaiActionsEditorStrings.LiveRuntimePatchGameObjectField, item.GameObjectReference, typeof(GameObject), true);

                Rect removeRect = GUILayoutUtility.GetRect(120f, 20f, GUILayout.Width(120f), GUILayout.Height(20f));
                if (Theme.GhostButton(removeRect, ConvaiActionsEditorStrings.LiveRuntimePatchRemoveObjectButton))
                    removeIndex = i;
                Theme.EndPanel(4f);
            }

            if (removeIndex >= 0)
                _liveRuntimePatchDraft.Objects.RemoveAt(removeIndex);

            Rect addRect = GUILayoutUtility.GetRect(110f, 22f, GUILayout.Width(110f), GUILayout.Height(22f));
            if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.LiveRuntimePatchAddObjectButton))
                _liveRuntimePatchDraft.Objects.Add(new ConvaiActionObjectDefinition());

            if (_liveRuntimePatchDraft.Objects.Count == 0)
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchNoObjectsClears, Theme.MutedWrapped);
            EditorGUI.indentLevel--;
        }

        private void DrawLiveRuntimePatchCharacterRows()
        {
            EditorGUI.indentLevel++;
            int removeIndex = -1;
            for (int i = 0; i < _liveRuntimePatchDraft.Characters.Count; i++)
            {
                ConvaiActionCharacterDefinition item =
                    _liveRuntimePatchDraft.Characters[i] ??= new ConvaiActionCharacterDefinition();
                Theme.BeginPanel(null);
                item.Name = EditorGUILayout.TextField(ConvaiActionsEditorStrings.KnownCharacterNameField, item.Name ?? string.Empty);
                item.Bio = EditorGUILayout.TextField(ConvaiActionsEditorStrings.KnownCharacterBioField, item.Bio ?? string.Empty);
                item.GameObjectReference = (GameObject)EditorGUILayout.ObjectField(
                    ConvaiActionsEditorStrings.LiveRuntimePatchGameObjectField, item.GameObjectReference, typeof(GameObject), true);

                Rect removeRect = GUILayoutUtility.GetRect(120f, 20f, GUILayout.Width(120f), GUILayout.Height(20f));
                if (Theme.GhostButton(removeRect, ConvaiActionsEditorStrings.LiveRuntimePatchRemoveCharacterButton))
                    removeIndex = i;
                Theme.EndPanel(4f);
            }

            if (removeIndex >= 0)
                _liveRuntimePatchDraft.Characters.RemoveAt(removeIndex);

            Rect addRect = GUILayoutUtility.GetRect(120f, 22f, GUILayout.Width(120f), GUILayout.Height(22f));
            if (Theme.GhostButton(addRect, ConvaiActionsEditorStrings.LiveRuntimePatchAddCharacterButton))
                _liveRuntimePatchDraft.Characters.Add(new ConvaiActionCharacterDefinition());

            if (_liveRuntimePatchDraft.Characters.Count == 0)
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimePatchNoCharactersClears, Theme.MutedWrapped);
            EditorGUI.indentLevel--;
        }

        private void LoadConfirmedLiveRuntimePatchDraft()
        {
            ConvaiActionConfigSource source = _character != null ? _character.GetActionConfigSource() : null;
            ConvaiActionConfig config = _character?.ActionConfig ?? source?.BuildActionConfig();
            if (config == null)
            {
                SetLiveRuntimePatchStatus(ConvaiActionsEditorStrings.LiveRuntimePatchNoConfigToLoad.text, isError: true);
                return;
            }

            _liveRuntimePatchDraft.Load(config);
            SetLiveRuntimePatchStatus(ConvaiActionsEditorStrings.LiveRuntimePatchLoaded.text, isError: false);
        }

        private void ResetLiveRuntimePatchDraft()
        {
            _liveRuntimePatchDraft = new ConvaiActionPatchDraft();
            SetLiveRuntimePatchStatus(ConvaiActionsEditorStrings.LiveRuntimePatchReset.text, isError: false);
        }

        private void PreviewLiveRuntimePatch()
        {
            if (!TryPreviewLiveRuntimePatch(out _, out _, out ConvaiActionConfig predicted, out string error))
            {
                SetLiveRuntimePatchStatus(ConvaiActionsEditorStrings.BuildLiveRuntimePatchRejected(error).text, isError: true);
                return;
            }

            SetLiveRuntimePatchStatus(
                ConvaiActionsEditorStrings.BuildLiveRuntimePatchPreview(FormatPredictedSnapshot(predicted)).text,
                isError: false);
        }

        private void SendLiveRuntimePatch()
        {
            if (_character == null || !_character.IsInConversation)
            {
                SetLiveRuntimePatchStatus(ConvaiActionsEditorStrings.LiveRuntimePatchNotConnected.text, isError: true);
                return;
            }

            if (!TryPreviewLiveRuntimePatch(
                    out ConvaiActionConfigPatch patch,
                    out object topLevelAttention,
                    out ConvaiActionConfig predicted,
                    out string error))
            {
                SetLiveRuntimePatchStatus(ConvaiActionsEditorStrings.BuildLiveRuntimePatchRejected(error).text, isError: true);
                return;
            }

            string updateId = string.IsNullOrWhiteSpace(_liveRuntimePatchDraft.UpdateId)
                ? $"actions-live-{Guid.NewGuid():N}"
                : _liveRuntimePatchDraft.UpdateId.Trim();
            _liveRuntimePatchDraft.UpdateId = updateId;
            _liveRuntimeObservedUpdateIds.Add(updateId);

            _character.DynamicContext.Apply(new ConvaiDynamicContextUpdate(
                text: null,
                reaction: _liveRuntimePatchDraft.Reaction,
                currentAttentionObject: topLevelAttention,
                updateId: updateId,
                actionConfig: patch));

            IReadOnlyList<ConvaiRuntimeActionUpdateDebugInfo> pending =
                _character.GetPendingRuntimeActionUpdateDebugInfo();
            bool queued = false;
            for (int i = 0; i < pending.Count; i++)
            {
                if (!string.Equals(pending[i].UpdateId, updateId, StringComparison.Ordinal))
                    continue;

                queued = true;
                break;
            }

            if (!queued)
            {
                SetLiveRuntimePatchStatus(ConvaiActionsEditorStrings.BuildLiveRuntimePatchNotQueued(updateId).text, isError: true);
                return;
            }

            SetLiveRuntimePatchStatus(
                ConvaiActionsEditorStrings.BuildLiveRuntimePatchPending(updateId, FormatPredictedSnapshot(predicted)).text,
                isError: false);
        }

        private bool TryPreviewLiveRuntimePatch(
            out ConvaiActionConfigPatch patch,
            out object topLevelAttention,
            out ConvaiActionConfig predicted,
            out string error)
        {
            patch = _liveRuntimePatchDraft.BuildActionConfigPatch();
            topLevelAttention = _liveRuntimePatchDraft.BuildTopLevelAttention();
            predicted = null;
            if (!_liveRuntimePatchDraft.HasMutation)
            {
                error = "select at least one patch or attention field";
                return false;
            }

            if (_character == null)
            {
                error = "no Convai Character selected";
                return false;
            }

            return _character.TryPreviewRuntimeActionStateUpdate(patch, topLevelAttention, out predicted, out error);
        }

        private void SetLiveRuntimePatchStatus(string message, bool isError)
        {
            _liveRuntimePatchStatus = message ?? string.Empty;
            _liveRuntimePatchStatusIsError = isError;
            Repaint();
        }

        private void EnsureLiveRuntimeDiagnosticsSubscription()
        {
            if (!UnityEngine.Application.isPlaying)
            {
                UnsubscribeLiveRuntimeDiagnostics();
                return;
            }

            ConvaiManager manager = ConvaiManager.ActiveManager;
            ConvaiEvents next = manager != null && manager.IsInitialized ? manager.Events : null;
            if (ReferenceEquals(_liveRuntimeSubscribedEvents, next))
                return;

            UnsubscribeLiveRuntimeDiagnostics();
            _liveRuntimeSubscribedEvents = next;
            if (_liveRuntimeSubscribedEvents != null)
                _liveRuntimeSubscribedEvents.OnDynamicContextUpdateResultReceived += HandleLiveRuntimeDynamicContextUpdateResult;
        }

        private void UnsubscribeLiveRuntimeDiagnostics()
        {
            if (_liveRuntimeSubscribedEvents == null)
                return;

            _liveRuntimeSubscribedEvents.OnDynamicContextUpdateResultReceived -= HandleLiveRuntimeDynamicContextUpdateResult;
            _liveRuntimeSubscribedEvents = null;
        }

        private void HandleLiveRuntimeDynamicContextUpdateResult(DynamicContextUpdateResultReceived result)
        {
            bool knownUpdate = !string.IsNullOrWhiteSpace(result.UpdateId) &&
                                _liveRuntimeObservedUpdateIds.Contains(result.UpdateId);
            bool hasActionMetadata = result.ActionConfigUpdated.HasValue ||
                                      result.ActionConfigCreated.HasValue ||
                                      result.ActionsCount.HasValue ||
                                      result.ObjectsCount.HasValue ||
                                      result.CharactersCount.HasValue ||
                                      result.ActionGenerationStrategyChanged.HasValue ||
                                      !string.IsNullOrWhiteSpace(result.ActionGenerationStrategyStatus);
            if (!knownUpdate && !hasActionMetadata)
                return;

            _liveRuntimeLastAck = result;
            _liveRuntimeHasLastAck = true;
            Repaint();
        }

        /// <summary>Clears diagnostic state on Play-mode exit (wired from Live's play-mode-state handler).</summary>
        private void ClearLiveRuntimeDiagnosticState()
        {
            UnsubscribeLiveRuntimeDiagnostics();
            _liveRuntimeObservedUpdateIds.Clear();
            _liveRuntimeLastAck = default;
            _liveRuntimeHasLastAck = false;
            _liveRuntimePatchStatus = string.Empty;
        }

        private static void DrawLiveReadOnlyText(GUIContent label, string value)
        {
            if (label != null)
            {
                GUILayout.Label(label, Theme.MicroLabel);
                GUILayout.Space(2f);
            }

            Theme.BeginPanel(null);
            if (string.IsNullOrEmpty(value))
            {
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRuntimeSnapshotEmpty, Theme.MutedWrapped);
            }
            else
            {
                string[] lines = value.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    GUILayout.Label(lines[i], Theme.MutedWrapped);
            }

            Theme.EndPanel(4f);
        }

        private static string FormatRuntimeSnapshot(
            ConvaiActionConfig config,
            IReadOnlyList<ConvaiActionDefinition> activeDefinitions,
            IReadOnlyList<ConvaiActionDefinition> catalog)
        {
            if (config == null)
                return string.Empty;

            var builder = new StringBuilder();
            builder.Append("actions (").Append(config.Actions?.Count ?? 0).Append("): ")
                .Append(JoinActions(config.Actions)).AppendLine();
            builder.Append("objects (").Append(config.Objects?.Count ?? 0).AppendLine("):");
            AppendObjectTargets(builder, config.Objects);
            builder.Append("characters (").Append(config.Characters?.Count ?? 0).AppendLine("):");
            AppendCharacterTargets(builder, config.Characters);
            builder.Append("attention: ")
                .Append(string.IsNullOrWhiteSpace(config.CurrentAttentionObject)
                    ? "<none>"
                    : config.CurrentAttentionObject)
                .AppendLine();
            builder.Append("active definitions: ").Append(activeDefinitions?.Count ?? 0).AppendLine();
            builder.Append("executable catalog: ").Append(catalog?.Count ?? 0);
            return builder.ToString();
        }

        private static void AppendObjectTargets(
            StringBuilder builder,
            IReadOnlyList<ConvaiActionObjectDefinition> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                builder.AppendLine("  <none>");
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ConvaiActionObjectDefinition target = targets[i];
                builder.Append("  ").Append(target?.Name ?? "<blank>").Append(" -> ")
                    .Append(FormatGameObjectBinding(target?.GameObjectReference)).AppendLine();
            }
        }

        private static void AppendCharacterTargets(
            StringBuilder builder,
            IReadOnlyList<ConvaiActionCharacterDefinition> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                builder.AppendLine("  <none>");
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ConvaiActionCharacterDefinition target = targets[i];
                builder.Append("  ").Append(target?.Name ?? "<blank>").Append(" -> ")
                    .Append(FormatGameObjectBinding(target?.GameObjectReference)).AppendLine();
            }
        }

        private static string FormatGameObjectBinding(GameObject gameObject) =>
            gameObject == null ? "UNBOUND" : gameObject.name;

        private static string JoinActions(IReadOnlyList<string> actions)
        {
            if (actions == null || actions.Count == 0)
                return "<none>";

            var builder = new StringBuilder();
            for (int i = 0; i < actions.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(actions[i]);
            }

            return builder.ToString();
        }

        private static string FormatPredictedSnapshot(ConvaiActionConfig predicted) =>
            predicted == null
                ? "No predicted config."
                : $"Predicted actions={predicted.Actions?.Count ?? 0}, " +
                  $"objects={predicted.Objects?.Count ?? 0}, " +
                  $"characters={predicted.Characters?.Count ?? 0}, " +
                  $"attention={predicted.CurrentAttentionObject ?? "<none>"}.";

        private static string FormatActionUpdateAcknowledgement(DynamicContextUpdateResultReceived acknowledgement) =>
            $"update_id={acknowledgement.UpdateId}\n" +
            $"status={acknowledgement.Status}; action_config_updated={FormatNullable(acknowledgement.ActionConfigUpdated)}; " +
            $"action_config_created={FormatNullable(acknowledgement.ActionConfigCreated)}\n" +
            $"counts: actions={FormatNullable(acknowledgement.ActionsCount)}, " +
            $"objects={FormatNullable(acknowledgement.ObjectsCount)}, " +
            $"characters={FormatNullable(acknowledgement.CharactersCount)}\n" +
            $"attention={acknowledgement.CurrentAttentionObject ?? "<none>"}; " +
            $"cleared={FormatNullable(acknowledgement.CurrentAttentionObjectCleared)}\n" +
            $"generation_strategy_changed={FormatNullable(acknowledgement.ActionGenerationStrategyChanged)}; " +
            $"generation_strategy_status={acknowledgement.ActionGenerationStrategyStatus ?? "<none>"}; " +
            $"prompt_rebuild={acknowledgement.PromptRebuildStatus ?? "<none>"}";

        private static string FormatNullable(bool? value) =>
            value.HasValue ? (value.Value ? "true" : "false") : "<missing>";

        private static string FormatNullable(int? value) =>
            value.HasValue ? value.Value.ToString() : "<missing>";

        #endregion

        #region Shared button grid

        /// <summary>
        ///     Draws one ghost button per item, wrapping to a new row every
        ///     <paramref name="buttonsPerRow" /> buttons. Items whose <paramref name="content" />
        ///     resolves to null/blank are skipped. Ported from the retired debug window's identical
        ///     helper, restyled onto <see cref="Theme.GhostButton(Rect, GUIContent)" /> so the grid
        ///     matches the rest of Live mode's visual language.
        /// </summary>
        private static void DrawWrappedGhostButtonGrid<T>(
            IReadOnlyList<T> items,
            int buttonsPerRow,
            float rowHeight,
            Func<T, GUIContent> content,
            Action<T> onClick)
        {
            int drawn = 0;
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < items.Count; i++)
            {
                T item = items[i];
                GUIContent label = content(item);
                if (label == null || string.IsNullOrWhiteSpace(label.text))
                    continue;

                if (drawn > 0 && drawn % buttonsPerRow == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                Rect rect = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));
                if (Theme.GhostButton(rect, label))
                    onClick(item);

                drawn++;
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
