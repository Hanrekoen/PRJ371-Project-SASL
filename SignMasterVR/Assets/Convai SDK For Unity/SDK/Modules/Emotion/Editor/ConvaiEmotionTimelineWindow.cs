using System;
using System.Collections.Generic;
using Convai.Editor.UI;
using Convai.Modules.Emotion.Components;
using UnityEditor;
using UnityEngine;

namespace Convai.Modules.Emotion.Editor
{
    /// <summary>
    ///     Profiler-style play-mode window plotting a selected character's emotion life over
    ///     time: per-label output scores, the resting mood score (emphasized), and vertical
    ///     markers for <see cref="ConvaiEmotionController.DominantEmotionChanged" />/
    ///     <see cref="ConvaiEmotionController.MoodChanged" /> transitions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Backed by an <see cref="EmotionTimelineRecorder" />. Samples are pulled from
    ///         <see cref="ConvaiEmotionController.Current" /> on the editor update loop (never a
    ///         bespoke per-frame hook on the character), throttled by the recorder's own sampling
    ///         interval. Markers are captured by subscribing to the controller's emotion and mood
    ///         change events on <c>Start</c> and
    ///         unsubscribing on <c>Stop</c>, target change, window close, and play-mode exit — no
    ///         subscription is ever left dangling on a stale controller.
    ///     </para>
    ///     <para>
    ///         Zero overhead when closed; while open, it only samples/repaints while a recording
    ///         is active. Degrades gracefully when the target is destroyed or disabled mid-
    ///         recording: the recording simply stops, no exceptions, no log spam.
    ///     </para>
    /// </remarks>
    internal sealed class ConvaiEmotionTimelineWindow : EditorWindow
    {
        private const float ChartHeight = 220f;
        private const float RepaintInterval = 0.1f;
        private const float MinWindowSeconds = 5f;
        private const float MaxWindowSeconds = 60f;
        private const float DefaultWindowSeconds = 20f;

        // Properties, not static readonly fields: a captured colour freezes at first access and
        // would keep the dark-skin palette after the user switches Unity to the Light skin.
        private static Color ChartBackground => ConvaiEditorTheme.InnerBg;
        private static Color GridColor => ConvaiEditorTheme.Divider;
        private static Color MoodLineColor => ConvaiEditorTheme.StatusWarn;
        private static Color DominantMarkerColor => ConvaiEditorTheme.Fade(ConvaiEditorTheme.StatusInfo, 0.6f);
        private static Color MoodMarkerColor => ConvaiEditorTheme.Fade(ConvaiEditorTheme.StatusError, 0.6f);

        private static readonly GUIContent WindowTitleContent = new("Emotion Timeline");
        private static readonly GUIContent WindowSubtitleContent = new("Live character emotion");

        /// <summary>
        ///     Opens the timeline, already watching <paramref name="target" /> when one is given.
        /// </summary>
        /// <remarks>
        ///     Reached from the Emotion editor window's <em>Live</em> mode rather than from the
        ///     Convai menu. The timeline plots one character's emotion life during Play Mode, so
        ///     it is only meaningful once a character is picked — which the Live mode has already
        ///     done — and a top-level menu row put a profiler in front of users who were still
        ///     setting a character up.
        /// </remarks>
        internal static void Open(ConvaiEmotionController target = null)
        {
            var window = GetWindow<ConvaiEmotionTimelineWindow>(false, "Emotion Timeline", true);
            window.minSize = new Vector2(480f, 420f);
            if (target != null) window.SetTarget(target);
        }

        private readonly EmotionTimelineRecorder _recorder = new();

        private ConvaiEmotionController _target;
        private ConvaiEmotionController _subscribedTarget;
        private Color[] _labelColors = Array.Empty<Color>();
        private Vector3[] _pointBuffer;
        private float _windowSeconds = DefaultWindowSeconds;
        private double _nextRepaintTime;
        private Vector2 _legendScroll;

        private void OnEnable()
        {
            _pointBuffer ??= new Vector3[_recorder.SampleCapacity];
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            // Window close: unsubscribe from whatever controller we were listening to. Do not
            // touch recorder history — closing the window should not discard a completed capture.
            StopRecordingAndUnsubscribe();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode) return;

            // Play-mode exit: the controller is about to be torn down with the scene, so stop
            // cleanly and clear — a recording made in the session that just ended is stale state
            // for the next one.
            StopRecordingAndUnsubscribe();
            _recorder.Clear();
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (!_recorder.IsRecording) return;

            if (!UnityEngine.Application.isPlaying || _target == null)
            {
                // Target destroyed/disabled or play mode exited mid-recording (B5): stop cleanly,
                // no throw, no error spam.
                StopRecordingAndUnsubscribe();
                Repaint();
                return;
            }

            _recorder.Record(Time.time, _target.Current);

            if (EditorApplication.timeSinceStartup >= _nextRepaintTime)
            {
                _nextRepaintTime = EditorApplication.timeSinceStartup + RepaintInterval;
                Repaint();
            }
        }

        private void OnGUI()
        {
            ConvaiEditorTheme.WindowHero(position.width, WindowTitleContent, WindowSubtitleContent);

            DrawTargetControls();

            if (!UnityEngine.Application.isPlaying)
            {
                ConvaiEditorFrame.InfoBox(
                    "Not Running", "Enter Play Mode and pick a character to record.");
                return;
            }

            DrawLifecycleControls();

            if (_recorder.SampleCount == 0)
            {
                ConvaiEditorFrame.InfoBox(
                    _recorder.IsRecording ? "Recording" : "Nothing Recorded Yet",
                    _recorder.IsRecording
                        ? "Waiting for the first sample."
                        : "Press Start to begin recording this character's emotion life.");
                return;
            }

            Rect chartRect = GUILayoutUtility.GetRect(Mathf.Max(100f, position.width - 12f), ChartHeight);
            DrawChart(chartRect);
            DrawLegend();
        }

        private void DrawTargetControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var newTarget = (ConvaiEmotionController)EditorGUILayout.ObjectField(
                    "Character", _target, typeof(ConvaiEmotionController), true);
                SetTarget(newTarget);

                if (GUILayout.Button("Use Selection", GUILayout.Width(110f)))
                    SetTarget(ResolveFromSelection());
            }
        }

        private void DrawLifecycleControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_target == null || _recorder.IsRecording))
                {
                    if (GUILayout.Button("Start", GUILayout.Width(60f))) StartRecordingClicked();
                }

                using (new EditorGUI.DisabledScope(!_recorder.IsRecording))
                {
                    if (GUILayout.Button("Stop", GUILayout.Width(60f))) StopRecordingAndUnsubscribe();
                }

                if (GUILayout.Button("Clear", GUILayout.Width(60f))) _recorder.Clear();

                GUILayout.Space(8f);
                GUILayout.Label("Window", GUILayout.Width(50f));
                _windowSeconds = GUILayout.HorizontalSlider(_windowSeconds, MinWindowSeconds, MaxWindowSeconds,
                    GUILayout.Width(100f));
                GUILayout.Label($"{_windowSeconds:F0}s", GUILayout.Width(28f));

                GUILayout.FlexibleSpace();
                GUILayout.Label(_recorder.IsRecording ? "Recording..." : "Stopped", ConvaiEditorStyles.MicroLabel,
                    GUILayout.Width(80f));
            }
        }

        private void SetTarget(ConvaiEmotionController newTarget)
        {
            if (ReferenceEquals(newTarget, _target)) return;

            // Target change (B3/path 2): the previous target's subscription must not survive a
            // retarget, even if a recording was in progress.
            StopRecordingAndUnsubscribe();
            _target = newTarget;
        }

        private static ConvaiEmotionController ResolveFromSelection()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null) return null;

            ConvaiEmotionController controller = go.GetComponentInParent<ConvaiEmotionController>(true);
            if (controller != null) return controller;

            return go.GetComponentInChildren<ConvaiEmotionController>(true);
        }

        private void StartRecordingClicked()
        {
            if (_target == null) return;
            if (_recorder.IsRecording) StopRecordingAndUnsubscribe();

            _recorder.StartRecording(_target.Current);
            BuildLabelColors();
            SubscribeToTarget(_target);
        }

        private void StopRecordingAndUnsubscribe()
        {
            UnsubscribeFromTarget();
            _recorder.StopRecording();
        }

        private void SubscribeToTarget(ConvaiEmotionController controller)
        {
            if (controller == null) return;

            controller.DominantEmotionChanged += HandleDominantEmotionChanged;
            controller.MoodChanged += HandleMoodChanged;
            _subscribedTarget = controller;
        }

        private void UnsubscribeFromTarget()
        {
            // Unity "fake null" equality handles a destroyed-but-not-collected controller safely.
            if (_subscribedTarget == null)
            {
                _subscribedTarget = null;
                return;
            }

            _subscribedTarget.DominantEmotionChanged -= HandleDominantEmotionChanged;
            _subscribedTarget.MoodChanged -= HandleMoodChanged;
            _subscribedTarget = null;
        }

        private void HandleDominantEmotionChanged(string label, float score)
        {
            if (!_recorder.IsRecording) return;
            _recorder.AddMarker(Time.time, EmotionTimelineRecorder.MarkerKind.Dominant, label);
        }

        private void HandleMoodChanged(string label, float score)
        {
            if (!_recorder.IsRecording) return;
            _recorder.AddMarker(Time.time, EmotionTimelineRecorder.MarkerKind.Mood, label);
        }

        private void BuildLabelColors()
        {
            IReadOnlyList<string> labels = _recorder.Labels;
            _labelColors = new Color[labels.Count];
            for (int i = 0; i < labels.Count; i++)
            {
                float hue = labels.Count > 0 ? (float)i / labels.Count : 0f;
                _labelColors[i] = Color.HSVToRGB(hue, 0.65f, 0.95f);
            }
        }

        private void DrawChart(Rect rect)
        {
            EditorGUI.DrawRect(rect, ChartBackground);

            int sampleCount = _recorder.SampleCount;
            float latestTime = _recorder.GetSampleTime(sampleCount - 1);
            float earliestVisible = latestTime - _windowSeconds;

            float XFor(float t) => rect.xMin + Mathf.Clamp01((t - earliestVisible) / _windowSeconds) * rect.width;
            float YFor(float score) => rect.yMax - Mathf.Clamp01(score) * rect.height;

            for (int i = 0; i <= 4; i++)
            {
                float y = rect.yMin + i * rect.height / 4f;
                EditorGUI.DrawRect(new Rect(rect.xMin, y, rect.width, 1f), GridColor);
            }

            GUI.Label(new Rect(rect.xMin + 4f, rect.yMin + 2f, 160f, 16f), "1.0", ConvaiEditorStyles.MicroLabel);
            GUI.Label(new Rect(rect.xMin + 4f, rect.yMax - 18f, 160f, 16f), "0.0", ConvaiEditorStyles.MicroLabel);
            GUI.Label(new Rect(rect.xMax - 90f, rect.yMax - 18f, 86f, 16f), $"last {_windowSeconds:F0}s",
                ConvaiEditorStyles.MicroLabel);

            IReadOnlyList<string> labels = _recorder.Labels;
            if (_labelColors.Length != labels.Count) BuildLabelColors();

            Handles.BeginGUI();
            for (int labelIndex = 0; labelIndex < labels.Count; labelIndex++)
            {
                if (!HasNonZeroSampleInView(labelIndex, earliestVisible)) continue;
                DrawLabelSeries(rect, labelIndex, earliestVisible, XFor, YFor);
            }

            DrawMoodSeries(rect, earliestVisible, XFor, YFor);
            Handles.EndGUI();

            DrawMarkers(rect, earliestVisible, latestTime, XFor);
        }

        private bool HasNonZeroSampleInView(int labelIndex, float earliestVisible)
        {
            int count = _recorder.SampleCount;
            for (int i = 0; i < count; i++)
            {
                if (_recorder.GetSampleTime(i) < earliestVisible) continue;
                if (_recorder.GetSampleScore(i, labelIndex) > 0f) return true;
            }

            return false;
        }

        private void DrawLabelSeries(Rect rect, int labelIndex, float earliestVisible,
            Func<float, float> xFor, Func<float, float> yFor)
        {
            int pointCount = 0;
            int count = _recorder.SampleCount;
            for (int i = 0; i < count; i++)
            {
                float t = _recorder.GetSampleTime(i);
                if (t < earliestVisible) continue;

                float score = _recorder.GetSampleScore(i, labelIndex);
                _pointBuffer[pointCount++] = new Vector3(xFor(t), Mathf.Clamp(yFor(score), rect.yMin, rect.yMax), 0f);
            }

            if (pointCount < 2) return;

            Handles.color = _labelColors[labelIndex];
            Handles.DrawAAPolyLine(2f, pointCount, _pointBuffer);
        }

        private void DrawMoodSeries(Rect rect, float earliestVisible, Func<float, float> xFor, Func<float, float> yFor)
        {
            int pointCount = 0;
            int count = _recorder.SampleCount;
            for (int i = 0; i < count; i++)
            {
                float t = _recorder.GetSampleTime(i);
                if (t < earliestVisible) continue;

                float score = _recorder.GetSampleMoodScore(i);
                _pointBuffer[pointCount++] = new Vector3(xFor(t), Mathf.Clamp(yFor(score), rect.yMin, rect.yMax), 0f);
            }

            if (pointCount < 2) return;

            Handles.color = MoodLineColor;
            Handles.DrawAAPolyLine(3.5f, pointCount, _pointBuffer);
        }

        private void DrawMarkers(Rect rect, float earliestVisible, float latestTime, Func<float, float> xFor)
        {
            int count = _recorder.MarkerCount;
            for (int i = 0; i < count; i++)
            {
                EmotionTimelineMarker marker = _recorder.GetMarker(i);
                if (marker.Time < earliestVisible || marker.Time > latestTime) continue;

                float x = xFor(marker.Time);
                Color tickColor = marker.Kind == EmotionTimelineRecorder.MarkerKind.Dominant
                    ? DominantMarkerColor
                    : MoodMarkerColor;
                EditorGUI.DrawRect(new Rect(x, rect.yMin, 1f, rect.height), tickColor);

                var hitRect = new Rect(x - 4f, rect.yMin, 8f, 10f);
                string tooltip = $"{marker.Kind}: {marker.Label} @ {marker.Time:F1}s";
                GUI.Label(hitRect, new GUIContent(string.Empty, tooltip));
            }
        }

        private void DrawLegend()
        {
            IReadOnlyList<string> labels = _recorder.Labels;

            ConvaiEditorControls.GroupCaption("Legend");
            _legendScroll = EditorGUILayout.BeginScrollView(_legendScroll, GUILayout.Height(24f));
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < labels.Count; i++)
            {
                Rect swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
                EditorGUI.DrawRect(swatch, i < _labelColors.Length ? _labelColors[i] : Color.gray);
                GUILayout.Label(labels[i], ConvaiEditorStyles.MicroLabel, GUILayout.Width(70f));
            }

            Rect moodSwatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
            EditorGUI.DrawRect(moodSwatch, MoodLineColor);
            GUILayout.Label("mood (emphasized)", ConvaiEditorStyles.MicroLabel);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }
    }
}
