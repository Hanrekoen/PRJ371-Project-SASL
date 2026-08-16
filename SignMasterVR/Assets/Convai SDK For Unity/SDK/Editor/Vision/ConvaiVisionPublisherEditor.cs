#if UNITY_EDITOR
using Convai.Modules.Vision;
using Convai.Editor.UI;
using Convai.Runtime.Vision.Publishing;
using Convai.Runtime.Vision.Sources;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Convai.Editor.Vision
{
    /// <summary>
    ///     Custom inspector for ConvaiVisionPublisher.
    ///     Sections: Frame Source · Publish Policy · Live Status.
    /// </summary>
    [CustomEditor(typeof(ConvaiVisionPublisher))]
    internal sealed class ConvaiVisionPublisherEditor : ConvaiVisionBaseEditor
    {
        // "Publishing" is kept as a local chip rather than folded into the shared "Live" entry — it
        // names the specific thing this component does (streaming frames), which is more informative
        // than the generic runtime-state word.
        private static readonly GUIContent ChipPublishing = new("Publishing", "Streaming frames to Convai.");

        private static readonly GUIContent AutoFindButton = new(
            "Auto-Find in Hierarchy", "Looks for a frame source component on this object or its children.");

        // Section IDs & icons
        private const string SectionSourceId = "FrameSource";
        private const string SectionPolicyId = "PublishPolicy";
        private const string SectionStatusId = "LiveStatus";

        private SerializedProperty _bitrateProp;
        private SerializedProperty _fpsProp;

        private SerializedProperty _frameSourceProp;

        // State
        private ConvaiVisionPublisher _publisher;
        private SerializedProperty _publishPolicyProp;

        private SerializedProperty _videoTrackNameProp;

        protected override string EditorStateHostId => "PublisherEditor";

        // Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            _publisher = (ConvaiVisionPublisher)target;
            _frameSourceProp = serializedObject.FindProperty("_frameSourceComponent");
            _publishPolicyProp = serializedObject.FindProperty("_publishPolicy");
            _videoTrackNameProp = serializedObject.FindProperty("videoTrackName");
            _fpsProp = serializedObject.FindProperty("publishFrameRateOverride");
            _bitrateProp = serializedObject.FindProperty("publishBitrateOverride");
        }

        protected override string Title => "Convai Vision Publisher";

        protected override string Subtitle => "Streams what the character sees";

        protected override GUIContent StatusChip
        {
            get
            {
                if (!EditorApplication.isPlaying) return ConvaiEditorChips.Ready.Content;
                return _publisher.IsPublishing ? ChipPublishing : ConvaiEditorChips.Idle.Content;
            }
        }

        protected override Color StatusChipTint
        {
            get
            {
                if (!EditorApplication.isPlaying) return ConvaiEditorChips.Ready.Tint;
                return _publisher.IsPublishing ? ConvaiGreen : ConvaiEditorChips.Idle.Tint;
            }
        }

        /// <summary>Keeps the live status updating while frames are flowing.</summary>
        public override bool RequiresConstantRepaint() =>
            EditorApplication.isPlaying &&
            (_publisher.IsPublishing || (_publisher.FrameSource?.IsCapturing ?? false));

        protected override void DrawBody()
        {
            DrawValidation();
            DrawFrameSourceSection();
            DrawPublishPolicySection();

            if (EditorApplication.isPlaying)
                DrawLiveStatus();
            else
                DrawStatusPlaceholder();
        }


        private void DrawValidation()
        {
            if (UnityEngine.Application.platform == RuntimePlatform.WebGLPlayer)
            {
                InfoBox(
                    "Handled Automatically On WebGL",
                    "The Unity canvas is streamed automatically, so the Frame Source field is ignored.");
                return;
            }

            // _publisher.FrameSource is a runtime backing field — null until Play.
            // Read the serialized property instead so the check works in editor mode.
            if (_frameSourceProp.objectReferenceValue == null)
            {
                WarningBox(
                    "Frame Source Required",
                    "Assign a CameraVisionFrameSource or WebcamVisionFrameSource component so the publisher knows what to stream.",
                    "Auto-Find", AutoFindSource);
            }

            List<MonoBehaviour> localSources = FindLocalFrameSources();
            if (localSources.Count > 1)
            {
                string assignedLabel = _frameSourceProp.objectReferenceValue is MonoBehaviour assignedBehaviour
                    ? DescribeFrameSource(assignedBehaviour)
                    : "none";
                InfoBox(
                    "More Than One Frame Source Here",
                    $"{localSources.Count} local frame sources were found; '{assignedLabel}' is the assigned one. " +
                    "Keeping the assignment explicit avoids ambiguity.");
            }
        }


        private void DrawFrameSourceSection()
        {
            if (!DrawSection(SectionSourceId, "Frame Source", ConvaiEditorGlyphs.Profile)) return;

            DrawSectionBody(() =>
            {
                // ObjectField: avoids the [Header("Frame Source")] decorator that
                // PropertyField would render, duplicating the section title.
                _frameSourceProp.objectReferenceValue = EditorGUILayout.ObjectField(
                    new GUIContent("Source",
                        "The component that provides video frames. " +
                        "Attach either a CameraVisionFrameSource (game camera) or a " +
                        "WebcamVisionFrameSource (physical webcam) to this field."),
                    _frameSourceProp.objectReferenceValue,
                    typeof(MonoBehaviour), true);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    Rect findRect = GUILayoutUtility.GetRect(155f, 20f, GUILayout.Width(155f));
                    if (ConvaiEditorControls.GhostButton(findRect, AutoFindButton))
                        AutoFindSource();
                }

                EditorGUILayout.Space(4f);

                // TextField: avoids the [Header("Video Settings")] decorator.
                _videoTrackNameProp.stringValue = EditorGUILayout.TextField(
                    new GUIContent("Track Name",
                        "Name of the published video track as it appears in the LiveKit room. " +
                        "Only needs to change if you publish multiple tracks simultaneously."),
                    _videoTrackNameProp.stringValue);

                Object assigned = _frameSourceProp.objectReferenceValue;
                if (assigned != null)
                {
                    EditorGUILayout.Space(2f);
                    if (assigned is MonoBehaviour assignedBehaviour)
                    {
                        GUILayout.Label(
                            $"Connected: {DescribeFrameSource(assignedBehaviour)}",
                            ConvaiEditorStyles.CaptionWrapped);
                    }
                }
            });
        }


        private void DrawPublishPolicySection()
        {
            if (!DrawSection(SectionPolicyId, "Publish Policy", ConvaiEditorGlyphs.Routing)) return;

            DrawSectionBody(() =>
            {
                // EnumPopup: avoids the [Header("Publish Policy")] decorator.
                _publishPolicyProp.enumValueIndex = (int)(VisionPublishPolicy)EditorGUILayout.EnumPopup(
                    new GUIContent("Mode",
                        "Controls when and how video is transported to the LiveKit room."),
                    (VisionPublishPolicy)_publishPolicyProp.enumValueIndex);

                var policy = (VisionPublishPolicy)_publishPolicyProp.enumValueIndex;
                EditorGUILayout.Space(2f);
                GUILayout.Label(GetPolicyDescription(policy), ConvaiEditorStyles.CaptionWrapped);

                EditorGUILayout.Space(6f);

                // Transport caps
                // These are OPTIONAL upper limits applied on top of whichever policy is active.
                // They do not replace the policy – the policy still decides the base behaviour.
                // Setting a cap to 0 means "no cap; let the policy decide."
                // They are shown for every policy including Manual, because caps still apply
                // when EnablePublishing(true) is called from code in Manual mode.
                EditorGUILayout.LabelField("Transport Caps  (0 = no cap, policy decides)",
                    ConvaiEditorStyles.CaptionWrapped);
                EditorGUILayout.Space(2f);

                _fpsProp.intValue = EditorGUILayout.IntSlider(
                    new GUIContent("Max Publish FPS",
                        "Hard upper limit on the published frame rate. " +
                        "The policy may already set a lower target — this only adds a ceiling. " +
                        "Set to 0 to leave it uncapped."),
                    _fpsProp.intValue, 0, 30);

                _bitrateProp.intValue = EditorGUILayout.IntField(
                    new GUIContent("Max Bitrate (bps)",
                        "Hard upper limit on the published bitrate in bits per second. " +
                        "The policy may already set a lower target — this only adds a ceiling. " +
                        "Set to 0 to leave it uncapped."),
                    _bitrateProp.intValue);
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

            Color bg = LiveSectionBackground(_publisher.IsPublishing);
            DrawSectionBody(() =>
            {
                string statusLabel = _publisher.IsPublishing ? "\u25B6  PUBLISHING" : "\u25CF  IDLE";
                Color statusColor = _publisher.IsPublishing ? ConvaiGreen : StatusIdle;
                string badge = _publisher.PublishPolicy.ToString();
                DrawStatusRow(statusLabel, statusColor, badge);

                EditorGUILayout.Space(6f);

                if (_publisher.FrameSource is { } src)
                {
                    EditorGUILayout.BeginHorizontal();
                    DrawLiveCell("Source",
                        src is MonoBehaviour behaviour ? behaviour.name : src.GetType().Name,
                        DefaultValueColor);
                    DrawLiveCell("Source Id",
                        string.IsNullOrWhiteSpace(src.SourceId) ? "(none)" : src.SourceId,
                        DefaultValueColor);
                    DrawLiveCell("Capturing", src.IsCapturing ? "Yes" : "No",
                        src.IsCapturing ? ConvaiGreenLight : DefaultValueColor, src.IsCapturing);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(4f);

                    EditorGUILayout.BeginHorizontal();
                    DrawLiveCell("Frame Ready", src.IsFrameReady ? "Yes" : "No",
                        src.IsFrameReady ? ConvaiGreenLight : DefaultValueColor, src.IsFrameReady);
                    DrawLiveCell("Resolution",
                        $"{src.FrameDimensions.Width}\u00D7{src.FrameDimensions.Height}", DefaultValueColor);
                    DrawLiveCell("Target FPS", $"{src.TargetFrameRate:F0}", DefaultValueColor);
                    DrawLiveCell("Frames", $"{src.FrameCount}", DefaultValueColor);
                    EditorGUILayout.EndHorizontal();

                    if (src is MonoBehaviour sourceBehaviour)
                    {
                        EditorGUILayout.Space(6f);
                        GUILayout.Label(
                            $"Live Source: {DescribeFrameSource(sourceBehaviour)}",
                            ConvaiEditorStyles.CaptionWrapped);
                    }
                }
                else
                {
                    Color previousColor = GUI.color;
                    GUI.color = ConvaiEditorTheme.TextMuted;
                    EditorGUILayout.LabelField("No frame source connected.", ConvaiEditorStyles.MicroLabel);
                    GUI.color = previousColor;
                }
            }, bg);
        }

        // Helpers

        private void AutoFindSource()
        {
            MonoBehaviour found = _publisher.GetComponentInChildren<IVisionFrameSource>(true) as MonoBehaviour
                                  ?? _publisher.GetComponentInParent<IVisionFrameSource>() as MonoBehaviour;
            if (found != null)
            {
                _frameSourceProp.objectReferenceValue = found;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private List<MonoBehaviour> FindLocalFrameSources()
        {
            var sources = new List<MonoBehaviour>();
            MonoBehaviour[] behaviours = _publisher.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IVisionFrameSource)
                    sources.Add(behaviours[i]);
            }

            return sources;
        }

        private static string DescribeFrameSource(MonoBehaviour behaviour)
        {
            if (behaviour is not IVisionFrameSource source)
                return behaviour.name;

            string sourceId = string.IsNullOrWhiteSpace(source.SourceId) ? "unlabeled" : source.SourceId;
            return $"{GetHierarchyPath(behaviour.transform)} [{sourceId}] ({behaviour.GetType().Name})";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "(unknown)";

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        private static string GetPolicyDescription(VisionPublishPolicy policy) => policy switch
        {
            VisionPublishPolicy.AutoCompatible =>
                "Publishes continuously using a balanced transport budget. " +
                "Automatically stays compatible with the backend. Best default for most projects.",
            VisionPublishPolicy.HighResponsiveness =>
                "Publishes continuously with higher visual fidelity and lower latency. " +
                "Uses more bandwidth and GPU — suitable for high-quality demo scenarios.",
            VisionPublishPolicy.LowOverhead =>
                "Publishes continuously with reduced CPU, GPU, and network usage. " +
                "Best for performance-constrained devices or background vision tasks.",
            VisionPublishPolicy.Manual =>
                "Does NOT publish automatically. " +
                "Call EnablePublishing(true) from code when you want streaming to start. " +
                "Useful when you need full control over when the video track is active.",
            _ => "Unknown policy."
        };
    }
}
#endif
