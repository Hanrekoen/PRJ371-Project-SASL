using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Presentation.Events;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiSessionEventRelay" />, framed as "Session Events": the
    ///     relay's target, the connection lifecycle callbacks a designer wires without code, and the
    ///     detailed payload callbacks kept apart from them.
    /// </summary>
    [CustomEditor(typeof(ConvaiSessionEventRelay))]
    internal sealed class ConvaiSessionEventRelayEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Session Events";
        private const string SubtitleText = "Convai Session Event Relay";

        private const string PurposeText =
            "Gives designers no-code hooks for connection, reconnection, and runtime error flows. " +
            "For typed C# integrations, use ConvaiManager.Events directly.";

        private static readonly GUIContent TargetSection = new("Target");
        private static readonly GUIContent LifecycleSection = new("Lifecycle Events");
        private static readonly GUIContent IdleSection = new("Idle & Background Events");
        private static readonly GUIContent DetailedSection = new("Detailed Events");

        private static readonly GUIContent IdleNote = new(
            "On User Idle Warning fires before the session times out, so you can prompt the player or " +
            "call Reset Idle Timer. On User Idle Timeout is a local deadline derived from that warning " +
            "countdown, not a transport-closed signal — confirm closure with On Disconnected or On " +
            "Session State Changed. On Runtime Background State Changed reports both the requested and " +
            "the effective policy, so a WebGL fallback is visible rather than silent.");

        private static readonly GUIContent DetailedNote = new(
            "Use On Session State Changed when you need the full transition payload. Use On Session " +
            "Error for UI, telemetry, or fallback messaging.");

        private SerializedProperty _autoResolveManagerProp;
        private SerializedProperty _managerProp;
        private SerializedProperty _onConnectedProp;
        private SerializedProperty _onDisconnectedProp;
        private SerializedProperty _onReconnectedProp;
        private SerializedProperty _onReconnectingProp;
        private SerializedProperty _onRuntimeBackgroundStateChangedProp;
        private SerializedProperty _onSessionErrorProp;
        private SerializedProperty _onSessionStateChangedProp;
        private SerializedProperty _onUsageLimitReachedProp;
        private SerializedProperty _onUserIdleTimeoutProp;
        private SerializedProperty _onUserIdleWarningProp;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _managerProp = serializedObject.FindProperty("_manager");
            _autoResolveManagerProp = serializedObject.FindProperty("_autoResolveManager");
            _onConnectedProp = serializedObject.FindProperty("_onConnected");
            _onDisconnectedProp = serializedObject.FindProperty("_onDisconnected");
            _onReconnectingProp = serializedObject.FindProperty("_onReconnecting");
            _onReconnectedProp = serializedObject.FindProperty("_onReconnected");
            _onUsageLimitReachedProp = serializedObject.FindProperty("_onUsageLimitReached");
            _onUserIdleWarningProp = serializedObject.FindProperty("_onUserIdleWarning");
            _onUserIdleTimeoutProp = serializedObject.FindProperty("_onUserIdleTimeout");
            _onRuntimeBackgroundStateChangedProp =
                serializedObject.FindProperty("_onRuntimeBackgroundStateChanged");
            _onSessionStateChangedProp = serializedObject.FindProperty("_onSessionStateChanged");
            _onSessionErrorProp = serializedObject.FindProperty("_onSessionError");
        }

        protected override void DrawBody()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Routing, TargetSection);
            EditorGUILayout.PropertyField(_managerProp);
            EditorGUILayout.PropertyField(_autoResolveManagerProp);
            DrawConfigurationWarning();
            Theme.EndCard();

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Events, LifecycleSection);
            EditorGUILayout.PropertyField(_onConnectedProp);
            EditorGUILayout.PropertyField(_onDisconnectedProp);
            EditorGUILayout.PropertyField(_onReconnectingProp);
            EditorGUILayout.PropertyField(_onReconnectedProp);
            EditorGUILayout.PropertyField(_onUsageLimitReachedProp);
            Theme.EndCard();

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Events, IdleSection);
            EditorGUILayout.PropertyField(_onUserIdleWarningProp);
            EditorGUILayout.PropertyField(_onUserIdleTimeoutProp);
            EditorGUILayout.PropertyField(_onRuntimeBackgroundStateChangedProp);
            GUILayout.Space(4f);
            GUILayout.Label(IdleNote, Theme.MutedWrapped);
            Theme.EndCard();

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Events, DetailedSection);
            EditorGUILayout.PropertyField(_onSessionStateChangedProp);
            EditorGUILayout.PropertyField(_onSessionErrorProp);
            GUILayout.Space(4f);
            GUILayout.Label(DetailedNote, Theme.MutedWrapped);
            Theme.EndCard();
        }

        private void DrawConfigurationWarning()
        {
            var relay = (ConvaiSessionEventRelay)target;
            string warning = relay.GetConfigurationWarning();
            if (string.IsNullOrWhiteSpace(warning))
                return;

            GUILayout.Space(4f);
            WarningBox("Not wired up yet", warning);
        }
    }
}
