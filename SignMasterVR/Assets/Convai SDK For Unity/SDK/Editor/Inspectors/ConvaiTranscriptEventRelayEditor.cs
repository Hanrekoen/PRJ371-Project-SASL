using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Presentation.Events;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiTranscriptEventRelay" />, framed as "Transcript Events":
    ///     the relay's target, the filters that decide which transcript updates reach it, and the
    ///     callbacks a designer wires without code.
    /// </summary>
    [CustomEditor(typeof(ConvaiTranscriptEventRelay))]
    internal sealed class ConvaiTranscriptEventRelayEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Transcript Events";
        private const string SubtitleText = "Convai Transcript Event Relay";

        private const string PurposeText =
            "Reacts to transcripts as they arrive — keyword triggers or lightweight gameplay hooks. " +
            "For chat history use ConvaiManager.Transcripts.Subscribe; for live subtitles use SubscribeCaptions.";

        private static readonly GUIContent TargetSection = new("Target");
        private static readonly GUIContent FiltersSection = new("Filters");
        private static readonly GUIContent EventsSection = new("Events");

        private static readonly GUIContent FiltersNote = new(
            "Final Only is best for gameplay triggers. Ignore Interim Updates keeps streaming text " +
            "from firing repeated reactions while the player is still talking. Character Id Filter " +
            "takes one character's ID — the same 36-character value shown on the Convai Character " +
            "component — and relays only that character's transcripts; leave it empty to relay every " +
            "character in the scene.");

        private SerializedProperty _autoResolveManagerProp;
        private SerializedProperty _characterIdFilterProp;
        private SerializedProperty _finalOnlyProp;
        private SerializedProperty _ignoreInterimUpdatesProp;
        private SerializedProperty _managerProp;
        private SerializedProperty _onCharacterTranscriptReceivedProp;
        private SerializedProperty _onFinalCharacterTranscriptReceivedProp;
        private SerializedProperty _onFinalPlayerTranscriptReceivedProp;
        private SerializedProperty _onPlayerTranscriptReceivedProp;
        private SerializedProperty _onTranscriptReceivedProp;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _managerProp = serializedObject.FindProperty("_manager");
            _autoResolveManagerProp = serializedObject.FindProperty("_autoResolveManager");
            _finalOnlyProp = serializedObject.FindProperty("_finalOnly");
            _ignoreInterimUpdatesProp = serializedObject.FindProperty("_ignoreInterimUpdates");
            _characterIdFilterProp = serializedObject.FindProperty("_characterIdFilter");
            _onTranscriptReceivedProp = serializedObject.FindProperty("_onTranscriptReceived");
            _onCharacterTranscriptReceivedProp = serializedObject.FindProperty("_onCharacterTranscriptReceived");
            _onPlayerTranscriptReceivedProp = serializedObject.FindProperty("_onPlayerTranscriptReceived");
            _onFinalCharacterTranscriptReceivedProp =
                serializedObject.FindProperty("_onFinalCharacterTranscriptReceived");
            _onFinalPlayerTranscriptReceivedProp =
                serializedObject.FindProperty("_onFinalPlayerTranscriptReceived");
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
            Theme.SectionHeader(Glyphs.Discovery, FiltersSection);
            EditorGUILayout.PropertyField(_finalOnlyProp);
            EditorGUILayout.PropertyField(_ignoreInterimUpdatesProp);
            EditorGUILayout.PropertyField(_characterIdFilterProp);
            GUILayout.Space(4f);
            GUILayout.Label(FiltersNote, Theme.MutedWrapped);
            Theme.EndCard();

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Events, EventsSection);
            EditorGUILayout.PropertyField(_onTranscriptReceivedProp);
            EditorGUILayout.PropertyField(_onCharacterTranscriptReceivedProp);
            EditorGUILayout.PropertyField(_onPlayerTranscriptReceivedProp);
            EditorGUILayout.PropertyField(_onFinalCharacterTranscriptReceivedProp);
            EditorGUILayout.PropertyField(_onFinalPlayerTranscriptReceivedProp);
            Theme.EndCard();
        }

        private void DrawConfigurationWarning()
        {
            var relay = (ConvaiTranscriptEventRelay)target;
            string warning = relay.GetConfigurationWarning();
            if (string.IsNullOrWhiteSpace(warning))
                return;

            GUILayout.Space(4f);
            WarningBox("Not wired up yet", warning);
        }
    }
}
