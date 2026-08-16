#if UNITY_EDITOR
using Convai.Editor.UI;
using Convai.Runtime.Vision.Sources;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Convai.Editor.Vision
{
    /// <summary>
    ///     Custom inspector for CameraVisionFrameSource.
    ///     Captures frames from a Unity Camera and streams them via the vision pipeline.
    ///     Sections: Capture Settings · Camera Setup · Live Status.
    /// </summary>
    [CustomEditor(typeof(CameraVisionFrameSource))]
    internal sealed class CameraVisionFrameSourceEditor : ConvaiVisionBaseEditor
    {
        // Section IDs & icons
        private const string SectionCaptureId = "CaptureSettings";
        private const string SectionCameraId = "CameraSetup";
        private const string SectionStatusId = "LiveStatus";
        private const string SectionAdvancedId = "Advanced";

        private SerializedProperty _cameraProp;
        private SerializedProperty _captureModeProp;
        private SerializedProperty _fpsProp;
        private SerializedProperty _heightProp;

        private SerializedProperty _presetProp;

        // State
        private CameraVisionFrameSource _source;
        private SerializedProperty _sourceIdProp;
        private SerializedProperty _widthProp;

        protected override string EditorStateHostId => "CameraSourceEditor";

        // Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            _source = (CameraVisionFrameSource)target;
            _presetProp = serializedObject.FindProperty("_capturePreset");
            _widthProp = serializedObject.FindProperty("_captureWidth");
            _heightProp = serializedObject.FindProperty("_captureHeight");
            _fpsProp = serializedObject.FindProperty("_targetFps");
            _captureModeProp = serializedObject.FindProperty("_cameraCaptureMode");
            _cameraProp = serializedObject.FindProperty("_targetCamera");
            _sourceIdProp = serializedObject.FindProperty("_sourceId");
        }

        protected override string Title => "Camera Vision Frame Source";

        protected override string Subtitle => "Captures frames from a game camera";

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
            DrawCaptureSection();
            DrawCameraSection();
            DrawAdvancedSection();

            if (EditorApplication.isPlaying)
                DrawLiveStatus();
            else
                DrawStatusPlaceholder();
        }


        private void DrawValidation()
        {
            CameraVisionFrameSource.CameraCaptureMode captureMode =
                (CameraVisionFrameSource.CameraCaptureMode)_captureModeProp.enumValueIndex;

            if (captureMode == CameraVisionFrameSource.CameraCaptureMode.ExplicitRenderCompatibility)
            {
                WarningBox(
                    "Backend Override Active",
                    "Explicit Render Compatibility. This path adds a bounded extra Camera.Render() at capture FPS.");
            }
            else if (captureMode == CameraVisionFrameSource.CameraCaptureMode.BuiltInHooks)
            {
                WarningBox(
                    "Backend Override Active",
                    "Built-in render hooks. Use only for SDK debugging on the built-in renderer.");
            }
            else if (captureMode == CameraVisionFrameSource.CameraCaptureMode.SrpNative)
            {
                WarningBox(
                    "Backend Override Active",
                    "SRP Native. This backend is experimental and is not part of the current production runtime flow.");
            }
            else if (GraphicsSettings.currentRenderPipeline != null &&
                     captureMode == CameraVisionFrameSource.CameraCaptureMode.Auto)
            {
                InfoBox(
                    "URP / HDRP Detected",
                    "Auto mode uses Explicit Render Compatibility until an SRP-native backend ships. " +
                    "This adds a bounded extra Camera.Render() at capture FPS.");
            }

            // Only warn about missing camera when no camera is assigned.
            // The "Use Main Camera" button lives here as the contextual fix action.
            if (_cameraProp.objectReferenceValue == null)
            {
                WarningBox(
                    "No Camera Assigned",
                    "Will fall back to Camera.main at runtime. Assign a specific camera for deterministic behaviour.",
                    "Use Main Camera",
                    () =>
                    {
                        _cameraProp.objectReferenceValue = Camera.main;
                        serializedObject.ApplyModifiedProperties();
                    });
            }
        }

        // Capture Settings section

        private void DrawCaptureSection()
        {
            if (!DrawSection(SectionCaptureId, "Capture Settings", ConvaiEditorGlyphs.Profile)) return;

            DrawSectionBody(() =>
            {
                CameraVisionFrameSource.CameraCaptureMode captureMode =
                    (CameraVisionFrameSource.CameraCaptureMode)_captureModeProp.enumValueIndex;
                string strategy = GetCaptureStrategySummary(captureMode);
                EditorGUILayout.LabelField(
                    new GUIContent("Capture Strategy",
                        "Backend internals are chosen automatically. Use the Advanced section only for SDK debugging."),
                    new GUIContent(strategy));
                EditorGUILayout.Space(2f);
                GUILayout.Label(GetCaptureStrategyDescription(captureMode), ConvaiEditorStyles.CaptionWrapped);

                EditorGUILayout.Space(6f);

                // Popup: avoids the [Header("Capture Settings")] decorator that PropertyField
                // would render, duplicating the section title.
                _presetProp.enumValueIndex = EditorGUILayout.Popup(
                    new GUIContent("Preset",
                        "Resolution and frame rate profile for capture. " +
                        "Choose Custom to enter exact values."),
                    _presetProp.enumValueIndex,
                    _presetProp.enumDisplayNames);

                int presetIndex = _presetProp.enumValueIndex;
                bool isCustom = presetIndex == 3; // CapturePreset.Custom

                // One HelpBox covers the description AND the resolution for named presets,
                // so there is never a need to show the same numbers a second time.
                EditorGUILayout.Space(2f);
                GUILayout.Label(GetPresetDescription(presetIndex), ConvaiEditorStyles.CaptionWrapped);

                // Custom fields + confirmation — shown only when the Custom preset is selected.
                // Hiding rather than disabling keeps the layout clean.
                if (isCustom)
                {
                    EditorGUILayout.Space(4f);
                    _widthProp.intValue = EditorGUILayout.IntField(
                        new GUIContent("Width", "Capture width in pixels."),
                        _widthProp.intValue);
                    _heightProp.intValue = EditorGUILayout.IntField(
                        new GUIContent("Height", "Capture height in pixels."),
                        _heightProp.intValue);
                    _fpsProp.intValue = EditorGUILayout.IntField(
                        new GUIContent("Target FPS", "Target capture frame rate."),
                        _fpsProp.intValue);

                    // Only for Custom: confirm the numbers the user typed are what they expect.
                    EditorGUILayout.Space(4f);
                    (int w, int h, int fps) = GetEffectiveCapture(presetIndex);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.LabelField(
                            new GUIContent("Will Capture At",
                                "Derived from the width, height, and FPS values above."),
                            new GUIContent($"{w}\u00D7{h}  @  {fps} fps"));
                    }
                }
            });
        }

        private void DrawAdvancedSection()
        {
            if (!DrawSection(SectionAdvancedId, "Advanced", ConvaiEditorGlyphs.Contract, defaultExpanded: false))
                return;

            DrawSectionBody(() =>
            {
                WarningBox(
                    "For SDK Debugging Only",
                    "These controls expose internal capture backends. Most projects should leave backend selection on Auto.");

                _captureModeProp.enumValueIndex = EditorGUILayout.Popup(
                    new GUIContent("Internal Backend Override",
                        "Forces how frames are captured internally. Only change this if Convai support asks you to, to help diagnose a capture issue."),
                    _captureModeProp.enumValueIndex,
                    _captureModeProp.enumDisplayNames);
            });
        }


        private void DrawCameraSection()
        {
            if (!DrawSection(SectionCameraId, "Camera Setup", ConvaiEditorGlyphs.Capture)) return;

            DrawSectionBody(() =>
            {
                // ObjectField: avoids the [Header("Camera")] decorator.
                _cameraProp.objectReferenceValue = EditorGUILayout.ObjectField(
                    new GUIContent("Target Camera",
                        "The Unity Camera whose output will be captured and streamed. " +
                        "If left empty, Camera.main is used automatically at runtime."),
                    _cameraProp.objectReferenceValue,
                    typeof(Camera), true);

                EditorGUILayout.Space(4f);

                // TextField: avoids the [Header("Debug")] decorator that PropertyField would render,
                // which would label this field "Debug" — a confusing term for an instance identifier.
                _sourceIdProp.stringValue = EditorGUILayout.TextField(
                    new GUIContent("Identifier",
                        "Optional tag to distinguish this source when multiple CameraVisionFrameSource " +
                        "components exist in the same scene. Leave empty if only one camera source is used."),
                    _sourceIdProp.stringValue);
            });
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
                    $"{_source.EffectiveWidth}\u00D7{_source.EffectiveHeight}", DefaultValueColor);
                DrawLiveCell("Target FPS", $"{_source.EffectiveFps}", DefaultValueColor);
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

        // Helpers

        /// <summary>
        ///     Computes capture dimensions from the serialized property values only.
        ///     Never reads _source.EffectiveWidth/Height/Fps — those are runtime fields
        ///     uninitialised in editor mode and always return Low Overhead defaults.
        /// </summary>
        private (int w, int h, int fps) GetEffectiveCapture(int preset) => preset switch
        {
            0 => (640, 480, 10),
            1 => (1280, 720, 15),
            2 => (1920, 1080, 30),
            3 => (_widthProp.intValue, _heightProp.intValue, _fpsProp.intValue),
            _ => (640, 480, 10)
        };

        private static string GetPresetDescription(int index) => index switch
        {
            0 =>
                "Low Overhead \u2014 640\u00D7480 @ 10 fps\nMinimises GPU and network load. Good for background vision or mobile.",
            1 =>
                "Balanced \u2014 1280\u00D7720 @ 15 fps\nGood image quality at moderate resource cost. Recommended starting point.",
            2 =>
                "High Detail \u2014 1920\u00D71080 @ 30 fps\nMaximum quality. Needs more bandwidth and GPU — use on capable hardware.",
            3 => "Custom\nEnter exact width, height, and FPS in the fields below.",
            _ => string.Empty
        };

        private static string GetCaptureStrategySummary(CameraVisionFrameSource.CameraCaptureMode captureMode)
        {
            if (captureMode != CameraVisionFrameSource.CameraCaptureMode.Auto)
                return $"Internal override: {captureMode}";

            return GraphicsSettings.currentRenderPipeline == null
                ? "Recommended (built-in render hooks)"
                : "Recommended (reliable SRP compatibility)";
        }

        private static string GetCaptureStrategyDescription(CameraVisionFrameSource.CameraCaptureMode captureMode)
        {
            if (captureMode == CameraVisionFrameSource.CameraCaptureMode.Auto)
            {
                return GraphicsSettings.currentRenderPipeline == null
                    ? "Built-in renderer projects use render-hook capture by default."
                    : "URP / HDRP projects use the reliable compatibility path by default because SRP-native capture is not yet validated for production use.";
            }

            return "A backend override is active. This is an internal debugging tool, not a normal project-level tuning control.";
        }
    }
}
#endif
