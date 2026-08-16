using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Presentation.Views;
using UnityEditor;
using UnityEngine;
using Chips = Convai.Editor.UI.ConvaiEditorChips;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai inspector for <see cref="ConvaiTranscriptDisplay" /> — a character-local convenience
    ///     that mirrors what the character is saying into a TextMeshPro label.
    /// </summary>
    [CustomEditor(typeof(ConvaiTranscriptDisplay))]
    internal sealed class ConvaiTranscriptDisplayEditor : ConvaiInspectorEditor
    {
        private SerializedProperty _transcriptText;

        protected override string Title => "Transcript Display";
        protected override string Subtitle => "Convai Transcript Display";

        protected override string Purpose =>
            "Mirrors this character's spoken lines into a TextMeshPro label — a quick way to show " +
            "captions without wiring up the room-wide transcript pipeline.";

        protected override string EditorStateHostId => "ConvaiTranscriptDisplayEditor";

        protected override GUIContent StatusChip => HasTranscriptText ? Chips.Ready.Content : Chips.NotSetUp.Content;
        protected override Color StatusChipTint => HasTranscriptText ? Chips.Ready.Tint : Chips.NotSetUp.Tint;

        protected override void OnEnable()
        {
            base.OnEnable();
            _transcriptText = serializedObject.FindProperty("_transcriptText");
        }

        private bool HasTranscriptText => _transcriptText.objectReferenceValue != null;
    }
}
