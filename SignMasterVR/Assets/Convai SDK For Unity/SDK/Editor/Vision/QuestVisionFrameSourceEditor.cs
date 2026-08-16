#if UNITY_EDITOR
using Convai.Editor.UI;
using Convai.Runtime.Vision.Sources;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Vision
{
    /// <summary>
    ///     Custom inspector for QuestVisionFrameSource.
    ///     Captures frames from the Meta Quest passthrough camera and streams them via the vision pipeline.
    ///     Sections: Quest Camera Access · Output Settings · Live Status.
    /// </summary>
    [CustomEditor(typeof(QuestVisionFrameSource))]
    internal sealed class QuestVisionFrameSourceEditor : ConvaiVisionBaseEditor
    {
        // Section IDs & icons
        private const string SectionStatusId = "LiveStatus";

        // State
        private QuestVisionFrameSource _source;

        protected override string EditorStateHostId => "QuestSourceEditor";

        // Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            _source = (QuestVisionFrameSource)target;
        }

        protected override string Title => "Quest Vision Frame Source";

        protected override string Subtitle => "Captures frames from the Quest passthrough camera";

        protected override GUIContent StatusChip
        {
            get
            {
                if (!EditorApplication.isPlaying) return ConvaiEditorChips.Ready.Content;
                return ConvaiEditorChips.Running(_source.IsCapturing).Content;
            }
        }

        protected override Color StatusChipTint
        {
            get
            {
                if (!EditorApplication.isPlaying) return ConvaiEditorChips.Ready.Tint;
                return ConvaiEditorChips.Running(_source.IsCapturing).Tint;
            }
        }

        /// <summary>Keeps the live capture readout updating while frames are flowing.</summary>
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying && _source.IsCapturing;

        protected override void DrawBody()
        {
            DrawValidation();
            DrawGeneratedSections();

            if (EditorApplication.isPlaying)
                DrawLiveStatus();
            else
                DrawStatusPlaceholder();
        }

        private void DrawValidation()
        {
            if (_source == null) return;

            SerializedProperty passthroughProp = serializedObject.FindProperty("_passthroughCameraAccess");
            if (passthroughProp != null && passthroughProp.objectReferenceValue == null)
                InfoBox(
                    "Auto-Discovery",
                    "No PassthroughCameraAccess assigned. A PassthroughCameraAccess component will be " +
                    "searched for in the scene automatically when capture starts.");
        }

        // Live Status section

        private void DrawStatusPlaceholder()
        {
            if (!DrawSection(SectionStatusId, "Live", ConvaiEditorGlyphs.Live,
                    accent: ConvaiEditorTheme.StatusInfo)) return;
            DrawSectionBody(DrawOfflinePlaceholder);
        }

        private void DrawLiveStatus()
        {
            if (!DrawSection(SectionStatusId, "Live", ConvaiEditorGlyphs.Live,
                    accent: ConvaiEditorTheme.StatusInfo)) return;

            Color bg = LiveSectionBackground(_source.IsCapturing);
            DrawSectionBody(() =>
            {
                var statusProvider = _source as IVisionFrameSourceStatusProvider;
                string label = _source.IsCapturing ? "\u25B6  CAPTURING" : "\u25CF  IDLE";
                DrawStatusRow(label, _source.IsCapturing ? ConvaiGreen : StatusIdle);

                EditorGUILayout.Space(6f);

                EditorGUILayout.BeginHorizontal();
                DrawLiveCell("Resolution",
                    $"{_source.FrameDimensions.Width}×{_source.FrameDimensions.Height}", DefaultValueColor);
                DrawLiveCell("Target FPS", $"{_source.TargetFrameRate:F0}", DefaultValueColor);
                DrawLiveCell("Frames", $"{_source.FrameCount}", DefaultValueColor);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4f);

                EditorGUILayout.BeginHorizontal();
                DrawLiveCell("Frame Ready", _source.IsFrameReady ? "Yes" : "No",
                    _source.IsFrameReady ? ConvaiGreenLight : DefaultValueColor, _source.IsFrameReady);
                string id = string.IsNullOrEmpty(_source.SourceId) ? "(none)" : _source.SourceId;
                DrawLiveCell("Identifier", id, DefaultValueColor);
                EditorGUILayout.EndHorizontal();

                if (statusProvider != null)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.BeginHorizontal();
                    DrawLiveCell("State", statusProvider.State.ToString(), DefaultValueColor);
                    DrawLiveCell("Error", statusProvider.ErrorKind.ToString(), DefaultValueColor);
                    EditorGUILayout.EndHorizontal();

                    if (!string.IsNullOrWhiteSpace(statusProvider.StatusMessage))
                    {
                        EditorGUILayout.Space(6f);
                        GUILayout.Label(statusProvider.StatusMessage, ConvaiEditorStyles.CaptionWrapped);
                    }
                }
            }, bg);
        }
    }
}
#endif
