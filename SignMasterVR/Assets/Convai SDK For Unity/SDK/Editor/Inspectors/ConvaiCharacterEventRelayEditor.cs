using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Presentation.Events;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiCharacterEventRelay" />, framed as "Character Events":
    ///     which Convai Character the relay listens to, and the callbacks a designer wires from that
    ///     character to animation, VFX or UI without writing code.
    /// </summary>
    [CustomEditor(typeof(ConvaiCharacterEventRelay))]
    internal sealed class ConvaiCharacterEventRelayEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Character Events";
        private const string SubtitleText = "Convai Character Event Relay";

        private const string PurposeText =
            "Lets one Convai Character drive animation, VFX, UI, or other local scene reactions. " +
            "For room-wide orchestration or typed domain events, prefer ConvaiManager.Events.";

        private static readonly GUIContent TargetSection = new("Target");
        private static readonly GUIContent EventsSection = new("Events");

        private static readonly GUIContent EventsNote = new(
            "These callbacks come from the local ConvaiCharacter component. They are the right fit " +
            "for character-local reactions, not transcript history or room-wide event routing.");

        private SerializedProperty _autoResolveCharacterProp;
        private SerializedProperty _characterProp;
        private SerializedProperty _onCharacterReadyProp;
        private SerializedProperty _onEmotionChangedProp;
        private SerializedProperty _onSpeechStartedProp;
        private SerializedProperty _onSpeechStoppedProp;
        private SerializedProperty _onTranscriptReceivedProp;
        private SerializedProperty _onTurnCompletedProp;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _characterProp = serializedObject.FindProperty("_character");
            _autoResolveCharacterProp = serializedObject.FindProperty("_autoResolveCharacter");
            _onTranscriptReceivedProp = serializedObject.FindProperty("_onTranscriptReceived");
            _onSpeechStartedProp = serializedObject.FindProperty("_onSpeechStarted");
            _onSpeechStoppedProp = serializedObject.FindProperty("_onSpeechStopped");
            _onTurnCompletedProp = serializedObject.FindProperty("_onTurnCompleted");
            _onCharacterReadyProp = serializedObject.FindProperty("_onCharacterReady");
            _onEmotionChangedProp = serializedObject.FindProperty("_onEmotionChanged");
        }

        protected override void DrawBody()
        {
            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Routing, TargetSection);
            EditorGUILayout.PropertyField(_characterProp);
            EditorGUILayout.PropertyField(_autoResolveCharacterProp);
            DrawConfigurationWarning();
            Theme.EndCard();

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Events, EventsSection);
            EditorGUILayout.PropertyField(_onTranscriptReceivedProp);
            EditorGUILayout.PropertyField(_onSpeechStartedProp);
            EditorGUILayout.PropertyField(_onSpeechStoppedProp);
            EditorGUILayout.PropertyField(_onTurnCompletedProp);
            EditorGUILayout.PropertyField(_onCharacterReadyProp);
            EditorGUILayout.PropertyField(_onEmotionChangedProp);
            GUILayout.Space(4f);
            GUILayout.Label(EventsNote, Theme.MutedWrapped);
            Theme.EndCard();
        }

        private void DrawConfigurationWarning()
        {
            var relay = (ConvaiCharacterEventRelay)target;
            string warning = relay.GetConfigurationWarning();
            if (string.IsNullOrWhiteSpace(warning))
                return;

            GUILayout.Space(4f);
            WarningBox("Not wired up yet", warning);
        }
    }
}
