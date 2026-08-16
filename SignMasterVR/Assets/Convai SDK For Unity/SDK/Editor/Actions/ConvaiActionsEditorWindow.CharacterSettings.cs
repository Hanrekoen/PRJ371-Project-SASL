using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Character Settings mode of the Actions Editor window: surfaces the picked Convai
    ///     Character's <see cref="ConvaiActionDispatcher" /> and
    ///     <see cref="ConvaiActionFeedbackRelay" /> settings inside the window instead of
    ///     requiring a trip to their own component inspectors. Deliberately edits the same
    ///     <see cref="SerializedObject" /> pipeline those component inspectors use, with
    ///     the same labels, tooltips, and plain-language explanation sentences
    ///     (<see cref="Convai.Editor.Inspectors.ConvaiActionDispatcherPolicyExplanations" /> /
    ///     <see cref="Convai.Editor.Inspectors.ConvaiActionFeedbackModeExplanations" />) — two
    ///     views, one truth; edits made here and on the component reflect each other immediately.
    /// </summary>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string SettingsRunGlyph = Glyphs.Run;
        private const string SettingsBehaviorGlyph = Glyphs.Blink;
        // "Action Feedback" and "Events" sit in the same header stack; Feedback takes the
        // hand-off mark (spoken outcomes are routed from the dispatcher into speech) so it stays
        // distinct from the Events section immediately below it.
        private const string SettingsFeedbackGlyph = Glyphs.Routing;
        private const string SettingsEventsGlyph = Glyphs.Events;

        private Vector2 _settingsScroll;

        // Bindings are cached and only rebuilt when the hierarchy changes, the picked character
        // changes, or a bound component dies — never per repaint (no per-frame component searches).
        private bool _settingsBindingsDirty = true;
        private ConvaiCharacter _settingsCharacter;

        // The config source owns the declaration of *who* runs the actions, which frames every
        // dispatcher card below it — so it is bound alongside them, from the same rebuild.
        private SerializedObject _sourceSerialized;
        private SerializedProperty _soActionExecutionMode;

        private ConvaiActionDispatcher _settingsDispatcher;
        private SerializedObject _dispatcherSerialized;
        private SerializedProperty _soBatchPolicy;
        private SerializedProperty _soFailurePolicy;
        private SerializedProperty _soSpeechGateTimeout;
        private SerializedProperty _soStepTimeout;
        private SerializedProperty _soCancelOnUserSpeech;
        private SerializedProperty _soPerformanceReactions;

        private ConvaiActionFeedbackRelay _settingsRelay;
        private SerializedObject _relaySerialized;
        private SerializedProperty _soFailureFeedbackMode;
        private SerializedProperty _soSuccessFeedbackMode;
        private SerializedProperty _soDroppedCommandFeedbackMode;
        private SerializedProperty _soFeedbackCooldown;
        private SerializedProperty _soScriptedFailureLines;

        // Labels mirror the relay's own component inspector exactly (parity: same label, same tooltip); the
        // tooltip comes from the runtime [Tooltip], captured once per rebuild — never per repaint.
        private GUIContent _relayFailureModeLabel;
        private GUIContent _relaySuccessModeLabel;
        private GUIContent _relayDroppedModeLabel;
        private GUIContent _relayCooldownLabel;

        private int _scriptedSummaryCount = -1;
        private GUIContent _scriptedSummaryContent;

        /// <summary>Hierarchy-changed hook: components may have been added, removed, or destroyed.</summary>
        private void MarkSettingsBindingsStale()
        {
            _settingsBindingsDirty = true;
            Repaint();
        }

        private void DisposeCharacterSettingsBindings()
        {
            _sourceSerialized?.Dispose();
            _sourceSerialized = null;
            _soActionExecutionMode = null;
            _dispatcherSerialized?.Dispose();
            _dispatcherSerialized = null;
            _settingsDispatcher = null;
            _relaySerialized?.Dispose();
            _relaySerialized = null;
            _settingsRelay = null;
            _settingsCharacter = null;
            _settingsBindingsDirty = true;
        }

        private void EnsureSettingsBindings()
        {
            bool dispatcherDied = _dispatcherSerialized != null && _settingsDispatcher == null;
            bool relayDied = _relaySerialized != null && _settingsRelay == null;
            if (!_settingsBindingsDirty && _settingsCharacter == _character && !dispatcherDied && !relayDied)
                return;

            DisposeCharacterSettingsBindings();
            _settingsBindingsDirty = false;
            _settingsCharacter = _character;
            if (_character == null)
                return;

            ConvaiActionConfigSource settingsSource = _character.GetActionConfigSource();
            if (settingsSource != null)
            {
                _sourceSerialized = new SerializedObject(settingsSource);
                _soActionExecutionMode = _sourceSerialized.FindProperty("_actionExecutionMode");
            }

            _settingsDispatcher = _character.GetComponentInChildren<ConvaiActionDispatcher>(true);
            if (_settingsDispatcher != null)
            {
                _dispatcherSerialized = new SerializedObject(_settingsDispatcher);
                _soBatchPolicy = _dispatcherSerialized.FindProperty("_batchPolicy");
                _soFailurePolicy = _dispatcherSerialized.FindProperty("_failurePolicy");
                _soSpeechGateTimeout = _dispatcherSerialized.FindProperty("_speechGateTimeoutSeconds");
                _soStepTimeout = _dispatcherSerialized.FindProperty("_defaultStepTimeoutSeconds");
                _soCancelOnUserSpeech = _dispatcherSerialized.FindProperty("_cancelOnUserSpeech");
                _soPerformanceReactions = _dispatcherSerialized.FindProperty("_enablePerformanceReactions");
            }

            _settingsRelay = _character.GetComponentInChildren<ConvaiActionFeedbackRelay>(true);
            if (_settingsRelay != null)
            {
                _relaySerialized = new SerializedObject(_settingsRelay);
                _soFailureFeedbackMode = _relaySerialized.FindProperty("_failureFeedbackMode");
                _soSuccessFeedbackMode = _relaySerialized.FindProperty("_successFeedbackMode");
                _soDroppedCommandFeedbackMode = _relaySerialized.FindProperty("_droppedCommandFeedbackMode");
                _soFeedbackCooldown = _relaySerialized.FindProperty("_cooldownSeconds");
                _soScriptedFailureLines = _relaySerialized.FindProperty("_scriptedFailureLines");

                _relayFailureModeLabel = new GUIContent("Failure Feedback Mode", _soFailureFeedbackMode.tooltip);
                _relaySuccessModeLabel = new GUIContent("Success Feedback Mode", _soSuccessFeedbackMode.tooltip);
                _relayDroppedModeLabel = new GUIContent(
                    "Dropped Command Feedback", _soDroppedCommandFeedbackMode.tooltip);
                _relayCooldownLabel = new GUIContent("Spoken Feedback Cooldown (Seconds)", _soFeedbackCooldown.tooltip);
                _scriptedSummaryCount = -1;
            }
        }

        private void DrawCharacterSettingsMode(ConvaiActionConfigSource source)
        {
            EnsureSettingsBindings();

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll, GUILayout.ExpandHeight(true));
                using (new EditorGUILayout.VerticalScope(Theme.PaneContent))
                {
                    float previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 232f;

                    GUILayout.Label(ConvaiActionsEditorStrings.CharacterSettingsIntro, Theme.MutedWrapped);
                    GUILayout.Space(10f);

                    bool playing = EditorApplication.isPlaying;
                    if (playing)
                    {
                        Theme.BeginPanel(Theme.StatusWarn);
                        GUILayout.Label(ConvaiActionsEditorStrings.PlayModeEditingHint, Theme.BodyWrapped);
                        Theme.EndPanel(10f);
                    }

                    DrawActionExecutionCard(playing);
                    DrawDispatcherSettingsCards(playing);
                    DrawFeedbackSettingsCard(playing);
                    DrawEventsNoteCard();

                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }

                EditorGUILayout.EndScrollView();
            }
        }

        #region Action execution

        /// <summary>
        ///     The declaration that frames every card below it: who is responsible for running the
        ///     commands this character receives. It changes no runtime behavior — it decides whether
        ///     a missing dispatcher is reported as a setup error or accepted as intended.
        /// </summary>
        private void DrawActionExecutionCard(bool playing)
        {
            if (_sourceSerialized == null || _soActionExecutionMode == null)
                return;

            _sourceSerialized.Update();
            Theme.BeginCard();
            Theme.SectionHeader(SettingsRunGlyph, ConvaiActionsEditorStrings.ExecutionModeSectionTitle);

            using (new EditorGUI.DisabledScope(playing))
                DrawSettingsField(_soActionExecutionMode, ConvaiActionsEditorStrings.ExecutionModeField);

            DrawSettingsFieldHint(
                (ConvaiActionExecutionMode)_soActionExecutionMode.intValue == ConvaiActionExecutionMode.CustomCode
                    ? ConvaiActionsEditorStrings.ExecutionModeCustomCodeHint
                    : ConvaiActionsEditorStrings.ExecutionModeDispatcherHint);

            Theme.EndCard();
            _sourceSerialized.ApplyModifiedProperties();
        }

        /// <summary>
        ///     Whether this character declares that its own project code runs the action commands,
        ///     which is what turns a missing dispatcher from a setup error into an intended choice.
        /// </summary>
        private bool UsesCustomActionCode =>
            _soActionExecutionMode != null &&
            (ConvaiActionExecutionMode)_soActionExecutionMode.intValue == ConvaiActionExecutionMode.CustomCode;

        #endregion

        #region Dispatcher settings

        private void DrawDispatcherSettingsCards(bool playing)
        {
            if (_settingsDispatcher == null || _dispatcherSerialized == null)
            {
                Theme.BeginCard();
                Theme.SectionHeader(SettingsRunGlyph, ConvaiActionsEditorStrings.DispatcherRunSectionTitle);

                // Custom Code is a complete answer to "what runs these actions", so asking for a
                // dispatcher here would be nagging the user about a decision they already made.
                if (UsesCustomActionCode)
                {
                    Theme.BeginPanel(Theme.StatusInfo);
                    GUILayout.Label(ConvaiActionsEditorStrings.SettingsCustomCodeNoDispatcherBody, Theme.BodyWrapped);
                    Theme.EndPanel(0f);
                    Theme.EndCard();
                    return;
                }

                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.SettingsMissingDispatcherBody, Theme.BodyWrapped);
                GUILayout.Space(6f);
                using (new EditorGUI.DisabledScope(playing))
                {
                    Rect addRect = GUILayoutUtility.GetRect(210f, 26f, GUILayout.Width(210f), GUILayout.Height(26f));
                    if (Theme.PrimaryButton(addRect, ConvaiActionsEditorStrings.SettingsAddDispatcherButton))
                        AddDispatcherComponent();
                }

                Theme.EndPanel(0f);
                Theme.EndCard();
                return;
            }

            // A dispatcher is present while the character declares Custom Code: both will run, and
            // that is a legitimate arrangement (custom handling layered over the shipped one), so it
            // is stated rather than flagged.
            if (UsesCustomActionCode)
            {
                Theme.BeginCard();
                Theme.SectionHeader(SettingsRunGlyph, ConvaiActionsEditorStrings.DispatcherRunSectionTitle);
                Theme.BeginPanel(Theme.StatusInfo);
                GUILayout.Label(ConvaiActionsEditorStrings.SettingsCustomCodeWithDispatcherBody, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                Theme.EndCard();
            }

            _dispatcherSerialized.Update();
            using (new EditorGUI.DisabledScope(playing))
            {
                Theme.BeginCard();
                Theme.SectionHeader(SettingsRunGlyph, ConvaiActionsEditorStrings.DispatcherRunSectionTitle);

                DrawSettingsField(_soBatchPolicy, ConvaiActionsEditorStrings.DispatcherWhileBusyField);
                DrawSettingsFieldHint(Inspectors.ConvaiActionDispatcherPolicyExplanations.ExplainBatchPolicy(
                    (ConvaiActionBatchPolicy)_soBatchPolicy.intValue));

                DrawSettingsField(_soFailurePolicy, ConvaiActionsEditorStrings.DispatcherFailureField);
                DrawSettingsFieldHint(Inspectors.ConvaiActionDispatcherPolicyExplanations.ExplainFailurePolicy(
                    (ConvaiActionBatchFailurePolicy)_soFailurePolicy.intValue));

                DrawSettingsField(_soSpeechGateTimeout, ConvaiActionsEditorStrings.DispatcherSpeechGateField);
                if (_soSpeechGateTimeout.floatValue < 0f)
                    _soSpeechGateTimeout.floatValue = 0f;

                DrawSettingsField(_soStepTimeout, ConvaiActionsEditorStrings.DispatcherStepTimeoutField);
                if (_soStepTimeout.floatValue < 0f)
                    _soStepTimeout.floatValue = 0f;
                DrawSettingsFieldHint(ConvaiActionsEditorStrings.ExplainStepTimeout(_soStepTimeout.floatValue));

                Theme.EndCard();

                Theme.BeginCard();
                Theme.SectionHeader(SettingsBehaviorGlyph, ConvaiActionsEditorStrings.DispatcherBehaviorSectionTitle);
                DrawSettingsField(_soCancelOnUserSpeech, ConvaiActionsEditorStrings.DispatcherCancelOnSpeechField);
                EditorGUILayout.Space(2f);
                DrawSettingsField(_soPerformanceReactions, ConvaiActionsEditorStrings.DispatcherReactionsField);
                Theme.EndCard();
            }

            _dispatcherSerialized.ApplyModifiedProperties();
        }

        private void AddDispatcherComponent()
        {
            if (_character == null)
                return;

            Undo.AddComponent<ConvaiActionDispatcher>(_character.gameObject);
            EditorUtility.SetDirty(_character.gameObject);
            MarkSettingsBindingsStale();
        }

        #endregion

        #region Feedback relay settings

        private void DrawFeedbackSettingsCard(bool playing)
        {
            Theme.BeginCard();
            Theme.SectionHeader(SettingsFeedbackGlyph, ConvaiActionsEditorStrings.SettingsFeedbackSectionTitle);

            if (_settingsRelay == null || _relaySerialized == null)
            {
                Theme.BeginPanel(null);
                GUILayout.Label(ConvaiActionsEditorStrings.SettingsMissingRelayBody, Theme.BodyWrapped);
                GUILayout.Space(6f);
                using (new EditorGUI.DisabledScope(playing))
                {
                    Rect addRect = GUILayoutUtility.GetRect(230f, 26f, GUILayout.Width(230f), GUILayout.Height(26f));
                    if (Theme.PrimaryButton(addRect, ConvaiActionsEditorStrings.SettingsAddRelayButton))
                        AddRelayComponent();
                }

                Theme.EndPanel(0f);
                Theme.EndCard();
                return;
            }

            _relaySerialized.Update();
            using (new EditorGUI.DisabledScope(playing))
            {
                DrawSettingsField(_soFailureFeedbackMode, _relayFailureModeLabel);
                DrawSettingsFieldHint(Inspectors.ConvaiActionFeedbackModeExplanations.Explain(
                    (ConvaiActionFeedbackMode)_soFailureFeedbackMode.intValue));

                DrawSettingsField(_soSuccessFeedbackMode, _relaySuccessModeLabel);
                DrawSettingsFieldHint(Inspectors.ConvaiActionFeedbackModeExplanations.Explain(
                    (ConvaiActionFeedbackMode)_soSuccessFeedbackMode.intValue));

                DrawSettingsField(_soDroppedCommandFeedbackMode, _relayDroppedModeLabel);
                DrawSettingsFieldHint(Inspectors.ConvaiActionFeedbackModeExplanations.Explain(
                    (ConvaiActionFeedbackMode)_soDroppedCommandFeedbackMode.intValue));

                DrawSettingsField(_soFeedbackCooldown, _relayCooldownLabel);
                if (_soFeedbackCooldown.floatValue < 0f)
                    _soFeedbackCooldown.floatValue = 0f;
            }

            _relaySerialized.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);
            GUILayout.Label(ConvaiActionsEditorStrings.SettingsScriptedLinesLabel, Theme.MicroLabel);
            GUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(BuildScriptedSummary(_soScriptedFailureLines.arraySize), Theme.MutedWrapped);
                GUILayout.Space(6f);
                Rect editRect = GUILayoutUtility.GetRect(140f, 22f, GUILayout.Width(140f), GUILayout.Height(22f));
                if (Theme.GhostButton(editRect, ConvaiActionsEditorStrings.SettingsEditRelayOnComponentButton))
                    SelectAndPing(_settingsRelay);
                GUILayout.FlexibleSpace();
            }

            Theme.EndCard();
        }

        /// <summary>
        ///     Adding the relay also pulls in a dispatcher automatically when one is missing —
        ///     <see cref="ConvaiActionFeedbackRelay" /> declares
        ///     <c>[RequireComponent(typeof(ConvaiActionDispatcher))]</c> and Unity satisfies it on add.
        /// </summary>
        private void AddRelayComponent()
        {
            if (_character == null || EditorApplication.isPlaying)
                return;

            EnsureActionFeedbackForCharacter(_character);
            ConvaiActionSetupReport.Invalidate();
            MarkSettingsBindingsStale();
        }

        /// <summary>
        ///     Adds the optional feedback bridge once, under one named Undo group. Kept as a small
        ///     test seam because both the Character Settings card and the Command-card recommendation
        ///     depend on this exact mechanical promise.
        /// </summary>
        internal static bool EnsureActionFeedbackForCharacter(ConvaiCharacter character)
        {
            if (character == null || EditorApplication.isPlaying ||
                character.GetComponentInChildren<ConvaiActionFeedbackRelay>(true) != null)
                return false;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Action Feedback");
            ConvaiActionFeedbackRelay relay = Undo.AddComponent<ConvaiActionFeedbackRelay>(character.gameObject);
            EditorUtility.SetDirty(character.gameObject);
            Undo.CollapseUndoOperations(undoGroup);
            return relay != null;
        }

        /// <summary>Cached per line count so repainting the summary row never allocates.</summary>
        private GUIContent BuildScriptedSummary(int failureLineCount)
        {
            if (_scriptedSummaryCount != failureLineCount || _scriptedSummaryContent == null)
            {
                _scriptedSummaryCount = failureLineCount;
                _scriptedSummaryContent = ConvaiActionsEditorStrings.BuildScriptedLinesSummary(failureLineCount);
            }

            return _scriptedSummaryContent;
        }

        #endregion

        #region Events note

        /// <summary>
        ///     Event wiring stays on the component inspector (UnityEvent GUIs are poor guests inside a
        ///     scrolling window pane); this card only says where to find it and takes you there.
        /// </summary>
        private void DrawEventsNoteCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(SettingsEventsGlyph, ConvaiActionsEditorStrings.DispatcherEventsSectionTitle);
            GUILayout.Label(ConvaiActionsEditorStrings.SettingsEventsNoteBody, Theme.MutedWrapped);

            if (_settingsDispatcher != null)
            {
                GUILayout.Space(6f);
                Rect selectRect = GUILayoutUtility.GetRect(190f, 22f, GUILayout.Width(190f), GUILayout.Height(22f));
                if (Theme.GhostButton(selectRect, ConvaiActionsEditorStrings.SettingsSelectDispatcherButton))
                    SelectAndPing(_settingsDispatcher);
            }

            Theme.EndCard();
        }

        private static void SelectAndPing(Component component)
        {
            if (component == null)
                return;

            Selection.activeObject = component.gameObject;
            EditorGUIUtility.PingObject(component);
        }

        #endregion

        #region Shared field helpers

        /// <summary>
        ///     Draws a serialized field via the rect-based
        ///     <see cref="EditorGUI.PropertyField(Rect, SerializedProperty, GUIContent, bool)" />,
        ///     which skips <see cref="HeaderAttribute" /> decorators — the runtime components'
        ///     [Header] groups would otherwise render duplicate raw headings inside the themed cards
        ///     (same technique as the runtime components' own Convai inspectors).
        /// </summary>
        private static void DrawSettingsField(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUI.GetPropertyHeight(property, label, true);
            Rect rect = EditorGUILayout.GetControlRect(true, height);
            EditorGUI.PropertyField(rect, property, label, true);
        }

        /// <summary>Plain-language hint under a dropdown, indented to the field column.</summary>
        private static void DrawSettingsFieldHint(string hint)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 4f);
                GUILayout.Label(hint, Theme.MutedWrapped);
            }

            EditorGUILayout.Space(6f);
        }

        #endregion
    }
}
