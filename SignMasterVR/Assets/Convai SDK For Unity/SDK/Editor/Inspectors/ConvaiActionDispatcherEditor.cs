using Convai.Editor.Actions;
using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Actions;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai, beginner-first inspector for <see cref="ConvaiActionDispatcher" />: a header
    ///     with a live status pill, a one-line explainer, friendly policy
    ///     fields with a plain-language explanation under each selected value, a collapsed Events
    ///     card, and a Live Activity section that shows the current action and queue depth in Play
    ///     Mode. All fields go through the <see cref="SerializedObject" /> pipeline, so Undo, prefab
    ///     overrides, and revert behave exactly like the default inspector.
    /// </summary>
    /// <remarks>
    ///     Built on <see cref="ConvaiInspectorEditor" /> (Convai header/status-chip/purpose via its
    ///     declared hooks) but owns its own <see cref="OnInspectorGUI" /> so every policy field can
    ///     carry a bespoke explanation instead of the framework's generic per-field auto layout —
    ///     every serialized field of the component is drawn exactly once, by this editor.
    /// </remarks>
    [CustomEditor(typeof(ConvaiActionDispatcher))]
    internal sealed class ConvaiActionDispatcherEditor : ConvaiInspectorEditor
    {
        private const string EventsSectionStateKey = "Convai.Inspector.ConvaiActionDispatcher.EventsExpanded";

        private const string RunSectionGlyph = Glyphs.Run;
        private const string BehaviorSectionGlyph = Glyphs.Profile;
        private const string EventsSectionGlyph = Glyphs.Events;
        private const string LiveSectionGlyph = Glyphs.Live;

        private static readonly string[] EventPropertyNames =
        {
            "_onBatchStarted",
            "_onStepStarted",
            "_onStepSucceeded",
            "_onStepFailed",
            "_onStepUnhandled",
            "_onStepCompleted",
            "_onBatchCompleted",
            "_onBatchAborted"
        };

        private SerializedProperty _batchPolicy;
        private SerializedProperty _failurePolicy;
        private SerializedProperty _speechGateTimeout;
        private SerializedProperty _stepTimeout;
        private SerializedProperty _cancelOnUserSpeech;
        private SerializedProperty _enablePerformanceReactions;
        private SerializedProperty[] _eventProperties;
        private bool _eventsExpanded;

        protected override string Title => ConvaiActionsEditorStrings.DispatcherTitle.text;

        protected override string Purpose => ConvaiActionsEditorStrings.DispatcherIntro.text;

        protected override GUIContent StatusChip
        {
            get
            {
                var dispatcher = (ConvaiActionDispatcher)target;
                if (EditorApplication.isPlaying)
                {
                    return dispatcher.IsProcessingLive
                        ? ConvaiActionsEditorStrings.DispatcherChipPerforming
                        : ConvaiActionsEditorStrings.DispatcherChipIdle;
                }

                return dispatcher.enabled
                    ? ConvaiActionsEditorStrings.DispatcherChipReady
                    : ConvaiActionsEditorStrings.DispatcherChipDisabled;
            }
        }

        protected override Color StatusChipTint
        {
            get
            {
                var dispatcher = (ConvaiActionDispatcher)target;
                if (EditorApplication.isPlaying)
                    return dispatcher.IsProcessingLive ? Theme.StatusReady : Theme.TextMuted;

                return dispatcher.enabled ? Theme.StatusReady : Theme.StatusWarn;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            _batchPolicy = serializedObject.FindProperty("_batchPolicy");
            _failurePolicy = serializedObject.FindProperty("_failurePolicy");
            _speechGateTimeout = serializedObject.FindProperty("_speechGateTimeoutSeconds");
            _stepTimeout = serializedObject.FindProperty("_defaultStepTimeoutSeconds");
            _cancelOnUserSpeech = serializedObject.FindProperty("_cancelOnUserSpeech");
            _enablePerformanceReactions = serializedObject.FindProperty("_enablePerformanceReactions");

            _eventProperties = new SerializedProperty[EventPropertyNames.Length];
            for (int i = 0; i < EventPropertyNames.Length; i++)
                _eventProperties[i] = serializedObject.FindProperty(EventPropertyNames[i]);

            _eventsExpanded = SessionState.GetBool(EventsSectionStateKey, false);
        }

        protected override void OnDisable()
        {
            SessionState.SetBool(EventsSectionStateKey, _eventsExpanded);
            base.OnDisable();
        }

        /// <summary>Keeps the Live Activity panel updating while the scene plays.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        protected override void DrawHeaderExtras()
        {
            var dispatcher = (ConvaiActionDispatcher)target;
            if (!dispatcher.enabled)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.DispatcherDisabledBody, Theme.BodyWrapped);
                Theme.EndPanel(8f);
            }

            DrawRunCard();
            DrawBehaviorCard();
            DrawEventsCard();
        }

        /// <summary>
        ///     Every serialized field of the dispatcher is drawn in <see cref="DrawHeaderExtras" /> with its
        ///     own plain-language explanation, so the base's generic per-field section renderer is
        ///     intentionally not used. The body slot instead carries the Play-mode-only placeholder,
        ///     since <see cref="DrawLiveSection" /> runs only while playing.
        /// </summary>
        protected override void DrawBody()
        {
            if (!EditorApplication.isPlaying)
                DrawLiveOfflineCard();
        }

        private void DrawRunCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(RunSectionGlyph, ConvaiActionsEditorStrings.DispatcherRunSectionTitle);

            DrawField(_batchPolicy, ConvaiActionsEditorStrings.DispatcherWhileBusyField);
            DrawFieldHint(ConvaiActionDispatcherPolicyExplanations.ExplainBatchPolicy(
                (ConvaiActionBatchPolicy)_batchPolicy.intValue));

            DrawField(_failurePolicy, ConvaiActionsEditorStrings.DispatcherFailureField);
            DrawFieldHint(ConvaiActionDispatcherPolicyExplanations.ExplainFailurePolicy(
                (ConvaiActionBatchFailurePolicy)_failurePolicy.intValue));

            DrawField(_speechGateTimeout, ConvaiActionsEditorStrings.DispatcherSpeechGateField);
            if (_speechGateTimeout.floatValue < 0f)
                _speechGateTimeout.floatValue = 0f;

            DrawField(_stepTimeout, ConvaiActionsEditorStrings.DispatcherStepTimeoutField);
            if (_stepTimeout.floatValue < 0f)
                _stepTimeout.floatValue = 0f;
            DrawFieldHint(ConvaiActionsEditorStrings.ExplainStepTimeout(_stepTimeout.floatValue));

            Theme.EndCard();
        }

        /// <summary>
        ///     Draws a serialized field via the rect-based <see cref="EditorGUI.PropertyField(Rect, SerializedProperty, GUIContent, bool)" />,
        ///     which skips <see cref="HeaderAttribute" /> decorators — the runtime component's
        ///     [Header] groups would otherwise render duplicate raw headings inside the themed cards.
        /// </summary>
        private static void DrawField(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUI.GetPropertyHeight(property, label, true);
            Rect rect = EditorGUILayout.GetControlRect(true, height);
            EditorGUI.PropertyField(rect, property, label, true);
        }

        /// <summary>Plain-language hint under a policy dropdown, indented to the field column.</summary>
        private static void DrawFieldHint(string hint)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 4f);
                GUILayout.Label(new GUIContent(hint), Theme.MutedWrapped);
            }

            EditorGUILayout.Space(6f);
        }

        private void DrawBehaviorCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(BehaviorSectionGlyph, ConvaiActionsEditorStrings.DispatcherBehaviorSectionTitle);
            DrawField(_cancelOnUserSpeech, ConvaiActionsEditorStrings.DispatcherCancelOnSpeechField);
            EditorGUILayout.Space(2f);
            DrawField(_enablePerformanceReactions, ConvaiActionsEditorStrings.DispatcherReactionsField);
            Theme.EndCard();
        }

        private void DrawEventsCard()
        {
            Theme.BeginCard();

            _eventsExpanded = Theme.SectionHeaderRow(
                EventsSectionGlyph, ConvaiActionsEditorStrings.DispatcherEventsSectionTitle,
                _eventsExpanded, Theme.AccentRule);

            if (_eventsExpanded)
            {
                for (int i = 0; i < _eventProperties.Length; i++)
                {
                    if (_eventProperties[i] != null)
                        DrawField(_eventProperties[i], null);
                }
            }

            Theme.EndCard();
        }

        private static void DrawLiveOfflineCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(LiveSectionGlyph, ConvaiActionsEditorStrings.DispatcherLiveSectionTitle);
            GUILayout.Label(ConvaiActionsEditorStrings.DispatcherLiveOffline, Theme.MutedWrapped);
            Theme.EndCard(4f);
        }

        protected override void DrawLiveSection()
        {
            var dispatcher = (ConvaiActionDispatcher)target;

            Theme.BeginCard();
            Theme.SectionHeader(LiveSectionGlyph, ConvaiActionsEditorStrings.DispatcherLiveSectionTitle);

            bool performing = dispatcher.IsProcessingLive;
            string currentAction = dispatcher.CurrentActionDisplayNameLive;
            GUIContent stateLine = performing && !string.IsNullOrEmpty(currentAction)
                ? ConvaiActionsEditorStrings.BuildDispatcherPerforming(currentAction)
                : performing
                    ? ConvaiActionsEditorStrings.DispatcherChipPerforming
                    : ConvaiActionsEditorStrings.DispatcherLiveIdle;

            Theme.BeginPanel(performing ? Theme.StatusReady : (Color?)null);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect dotRect = GUILayoutUtility.GetRect(14f, 16f, GUILayout.Width(14f));
                Theme.StatusDot(dotRect, performing ? Theme.StatusReady : Theme.TextMuted, performing);
                GUILayout.Space(2f);
                GUILayout.Label(stateLine, Theme.BodyWrapped);
            }

            Theme.EndPanel(4f);
            GUILayout.Label(
                ConvaiActionsEditorStrings.BuildDispatcherQueueSummary(
                    dispatcher.PendingBatchCountLive, dispatcher.StartedBatchCountLive),
                Theme.MicroLabel);

            Theme.EndCard(4f);
        }
    }
}
