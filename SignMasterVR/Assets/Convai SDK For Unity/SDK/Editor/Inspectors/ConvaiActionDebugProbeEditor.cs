using System.Collections.Generic;
using System.Text;
using Convai.Editor.Inspectors.Framework;
using Convai.Runtime.Actions;
using Convai.Editor.UI;
using UnityEditor;
using UnityEngine;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Convai editor for <see cref="ConvaiActionDebugProbe" />, framed
    ///     as "Action Activity Log": the probe's own settings via the framework renderer, and a
    ///     Play-mode Live section listing the most recent recorded activity per event category
    ///     with Clear/Copy buttons.
    /// </summary>
    /// <remarks>
    ///     The probe only keeps the single most recent occurrence of each event category (plus a
    ///     running count) rather than a full chronological history, so this is a per-category
    ///     "latest known state" feed, not a true append-only log — an honest reflection of what
    ///     the runtime component actually records (no runtime API was added to change that).
    /// </remarks>
    [CustomEditor(typeof(ConvaiActionDebugProbe))]
    [CanEditMultipleObjects]
    internal sealed class ConvaiActionDebugProbeEditor : ConvaiInspectorEditor
    {
        private const string TitleText = "Action Monitor";
        private const string SubtitleText = "Recent action activity and diagnostics";
        private const string PurposeText =
            "Watches this Convai Character's actions while you play, so you can see what happened without opening the console.";

        private static readonly GUIContent SectionTitle = new("Activity");

        private static readonly GUIContent EnterPlayModeNote = new(
            "Enter Play mode to see this Convai Character's action activity.");

        private static readonly GUIContent NoActivityYetNote = new(
            "No action activity recorded yet. Send this Convai Character an action to see it here.");

        private static readonly GUIContent ClearButton = new(
            "Clear", "Resets all recorded counts and last-seen activity back to empty.");

        private static readonly GUIContent CopyButton = new(
            "Copy", "Copies the activity below to the clipboard as text.");

        private static readonly GUIContent MultiEditNote = new("Select a single probe to see its activity.");

        private SerializedProperty _receivedBatchCountProp;
        private SerializedProperty _startedStepCountProp;
        private SerializedProperty _succeededStepCountProp;
        private SerializedProperty _failedStepCountProp;
        private SerializedProperty _unhandledStepCountProp;
        private SerializedProperty _completedStepCountProp;
        private SerializedProperty _abortedBatchCountProp;
        private SerializedProperty _lastReceivedBatchProp;
        private SerializedProperty _lastStepStartedProp;
        private SerializedProperty _lastStepSucceededProp;
        private SerializedProperty _lastUnhandledStepProp;
        private SerializedProperty _lastFailedStepDetailProp;
        private SerializedProperty _lastStepCompletedProp;
        private SerializedProperty _lastFailureReasonProp;

        protected override string Title => TitleText;
        protected override string Subtitle => SubtitleText;
        protected override string Purpose => PurposeText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _receivedBatchCountProp = serializedObject.FindProperty("_receivedBatchCount");
            _startedStepCountProp = serializedObject.FindProperty("_startedStepCount");
            _succeededStepCountProp = serializedObject.FindProperty("_succeededStepCount");
            _failedStepCountProp = serializedObject.FindProperty("_failedStepCount");
            _unhandledStepCountProp = serializedObject.FindProperty("_unhandledStepCount");
            _completedStepCountProp = serializedObject.FindProperty("_completedStepCount");
            _abortedBatchCountProp = serializedObject.FindProperty("_abortedBatchCount");
            _lastReceivedBatchProp = serializedObject.FindProperty("_lastReceivedBatch");
            _lastStepStartedProp = serializedObject.FindProperty("_lastStepStarted");
            _lastStepSucceededProp = serializedObject.FindProperty("_lastStepSucceeded");
            _lastUnhandledStepProp = serializedObject.FindProperty("_lastUnhandledStep");
            _lastFailedStepDetailProp = serializedObject.FindProperty("_lastFailedStepDetail");
            _lastStepCompletedProp = serializedObject.FindProperty("_lastStepCompleted");
            _lastFailureReasonProp = serializedObject.FindProperty("_lastFailureReason");
        }

        protected override void DrawHeaderExtras()
        {
            if (UnityEngine.Application.isPlaying)
                return;

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Live, SectionTitle);
            GUILayout.Label(EnterPlayModeNote, Theme.MutedWrapped);
            Theme.EndCard();
        }

        protected override void DrawLiveSection()
        {
            if (targets.Length != 1)
            {
                Theme.BeginCard();
                Theme.SectionHeader(Glyphs.Live, SectionTitle);
                GUILayout.Label(MultiEditNote, Theme.MutedWrapped);
                Theme.EndCard();
                return;
            }

            List<(string Label, string Text)> rows = BuildRows();

            Theme.BeginCard();
            Theme.SectionHeader(Glyphs.Live, SectionTitle);

            if (rows.Count == 0)
            {
                GUILayout.Label(NoActivityYetNote, Theme.MutedWrapped);
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    (string label, string text) = rows[i];
                    Theme.BeginPanel(null);
                    GUILayout.Label(label, Theme.MicroLabel);
                    GUILayout.Label(text, ConvaiEditorStyles.CaptionWrapped);
                    Theme.EndPanel(4f);
                }
            }

            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(ClearButton))
                ClearProbe();
            if (GUILayout.Button(CopyButton))
                GUIUtility.systemCopyBuffer = BuildCopyText(rows);
            EditorGUILayout.EndHorizontal();

            Theme.EndCard();
        }

        // Newest-ish first: the probe keeps only the latest occurrence per category (no
        // timestamps), so batch/step outcomes are listed before the batch/step start events that
        // preceded them within the same cycle. Capped well under ~30 rows by construction (one
        // category per row).
        private List<(string, string)> BuildRows()
        {
            var rows = new List<(string, string)>(7);

            AddRow(rows, "Batch Aborted", _abortedBatchCountProp, null);
            AddRow(rows, "Step Completed", _completedStepCountProp, _lastStepCompletedProp,
                _lastFailureReasonProp);
            AddRow(rows, "Step Unhandled", _unhandledStepCountProp, _lastUnhandledStepProp);
            AddRow(rows, "Step Failed", _failedStepCountProp, _lastFailedStepDetailProp);
            AddRow(rows, "Step Succeeded", _succeededStepCountProp, _lastStepSucceededProp);
            AddRow(rows, "Step Started", _startedStepCountProp, _lastStepStartedProp);
            AddRow(rows, "Batch Received", _receivedBatchCountProp, _lastReceivedBatchProp);

            return rows;
        }

        private static void AddRow(
            List<(string, string)> rows, string label, SerializedProperty countProp,
            SerializedProperty lastTextProp, SerializedProperty extraTextProp = null)
        {
            int count = countProp?.intValue ?? 0;
            if (count <= 0)
                return;

            string text = lastTextProp != null ? lastTextProp.stringValue : string.Empty;
            if (extraTextProp != null && !string.IsNullOrWhiteSpace(extraTextProp.stringValue))
                text = string.IsNullOrEmpty(text) ? extraTextProp.stringValue : $"{text} ({extraTextProp.stringValue})";

            if (string.IsNullOrEmpty(text))
                text = "(no details recorded)";

            rows.Add(($"#{count} {label}", text));
        }

        private static string BuildCopyText(List<(string Label, string Text)> rows)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                (string label, string text) = rows[i];
                builder.Append(label).Append(": ").AppendLine(text);
            }

            return builder.ToString();
        }

        private void ClearProbe()
        {
            var probe = (ConvaiActionDebugProbe)target;
            Undo.RecordObject(probe, "Clear Action Activity Log");
            probe.ResetProbeState();
            EditorUtility.SetDirty(probe);
        }
    }
}
