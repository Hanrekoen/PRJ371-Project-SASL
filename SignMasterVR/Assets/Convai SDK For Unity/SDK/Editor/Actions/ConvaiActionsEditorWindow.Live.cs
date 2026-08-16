using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using UnityEditor;
using UnityEngine;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Metrics = Convai.Editor.UI.ConvaiEditorTextMetrics;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Live mode of the Actions Editor window : a fourth left-rail
    ///     mode that only exists during Play mode (auto-selected on entering Play when the picked
    ///     Convai Character has a dispatcher; the previous mode is restored on exit). Read-only by
    ///     nature: Now Performing (dispatcher live telemetry + the in-flight batch), a step
    ///     timeline over <see cref="ConvaiActionsSessionCollector" />'s ring buffers, the merged
    ///     live scene-knowledge registry with register/unregister highlights, and the composed
    ///     feedback log. Timeline drawing reads only cached models rebuilt when the collector's
    ///     log version moves — never per repaint.
    /// </summary>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string LiveNowGlyph = Glyphs.Run;
        private const string LiveTimelineGlyph = Glyphs.Range;
        private const string LiveRegistryGlyph = Glyphs.Content;
        private const string LiveFeedbackGlyph = Glyphs.Events;
        private const string LiveDroppedGlyph = Glyphs.Validation;

        private const int MaxTimelineBatchesShown = 10;
        private const double LiveRepaintIntervalSeconds = 0.15d;
        private const double RegistrySnapshotIntervalSeconds = 0.5d;
        private const double RegistryHighlightSeconds = 2d;
        private const float TimelineResultColumnWidth = 112f;
        private const float TimelineDurationColumnWidth = 96f;
        private const float TimelineStepRowHeight = 30f;
        private const float FeedbackTimeColumnWidth = 76f;
        private const float FeedbackDeliveryColumnWidth = 96f;
        private const float FeedbackRowMinHeight = 30f;

        private ConvaiActionsEditorMode _modeBeforePlay = ConvaiActionsEditorMode.Actions;
        private Vector2 _liveScroll;

        // Timeline view models, rebuilt only when the collector log's version moves.
        private int _liveModelVersion = -1;
        private readonly List<LiveBatchModel> _liveTimeline = new();
        private ConvaiActionsSessionLog.BatchRecord _liveSelectedBatch;
        private int _liveSelectedStepIndex = -1;

        // Live registry snapshot (polled at a low, fixed cadence — component targets have no event).
        private readonly List<LiveRegistryRow> _liveRegistryRows = new();
        private readonly List<RemovedRegistryRow> _liveRemovedRows = new();
        private readonly Dictionary<string, double> _liveHighlightUntil = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _liveKnownKeys = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _liveSpareKeys = new(StringComparer.OrdinalIgnoreCase);
        private bool _liveRegistryPrimed;

        private double _lastLiveRepaintTime;
        private double _lastRegistrySnapshotTime;

        private sealed class LiveBatchModel
        {
            internal ConvaiActionsSessionLog.BatchRecord Record;
            internal GUIContent Header;
            internal readonly List<GUIContent> StepSubjects = new();
            internal readonly List<GUIContent> StepDurations = new();
        }

        private sealed class LiveRegistryRow
        {
            internal string Name;
            internal bool IsCharacter;
            internal bool IsAuthored;
            internal bool Available;
            internal double HighlightUntil;
        }

        private sealed class RemovedRegistryRow
        {
            internal string Name;
            internal bool IsCharacter;
            internal double Until;
        }

        #region Lifecycle hooks (wired from OnEnable/OnDisable in the main file)

        private void HandleCollectorChanged() => Repaint();

        private void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    _modeBeforePlay = _mode == ConvaiActionsEditorMode.Live ? ConvaiActionsEditorMode.Actions : _mode;
                    if (_character == null)
                        AutoSelectCharacter();
                    if (_character != null && _character.GetComponentInChildren<ConvaiActionDispatcher>(true) != null)
                        SetMode(ConvaiActionsEditorMode.Live);
                    ResetLiveCaches();
                    Repaint();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    if (_mode == ConvaiActionsEditorMode.Live)
                        SetMode(_modeBeforePlay);
                    _testRunToken = 0;
                    _testRunActive = false;
                    _testRunDefinition = null;
                    _testRunQueue.Clear();
                    MarkSettingsBindingsStale();
                    ResetLiveCaches();
                    ClearLiveRuntimeDiagnosticState();
                    Repaint();
                    break;
            }
        }

        /// <summary>
        ///     Low-frequency editor tick: only does work while Play mode runs and either the Live
        ///     mode is showing (elapsed timers, registry poll) or a test run is in flight (its
        ///     elapsed line). Idle windows cost one early-out per tick.
        /// </summary>
        private void HandleEditorUpdateForLive()
        {
            if (!EditorApplication.isPlaying)
                return;

            bool liveVisible = _mode == ConvaiActionsEditorMode.Live;
            if (!liveVisible && !HasActiveTestRun)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (liveVisible && now - _lastRegistrySnapshotTime >= RegistrySnapshotIntervalSeconds)
            {
                _lastRegistrySnapshotTime = now;
                RebuildLiveRegistry();
            }

            if (now - _lastLiveRepaintTime >= LiveRepaintIntervalSeconds)
            {
                _lastLiveRepaintTime = now;
                Repaint();
            }
        }

        /// <summary>
        ///     Keeps the collector pointed at the picked character's dispatcher/relay. Called from
        ///     OnGUI while playing; both callees are cheap no-ops when nothing changed.
        /// </summary>
        private void EnsureCollectorSubject()
        {
            if (!EditorApplication.isPlaying || _character == null)
                return;

            EnsureSettingsBindings();
            ConvaiActionsSessionCollector.SetSubject(_settingsDispatcher, _settingsRelay);
        }

        private void ResetLiveCaches()
        {
            _liveModelVersion = -1;
            _liveTimeline.Clear();
            _liveSelectedBatch = null;
            _liveSelectedStepIndex = -1;
            _liveRegistryRows.Clear();
            _liveRemovedRows.Clear();
            _liveHighlightUntil.Clear();
            _liveKnownKeys.Clear();
            _liveSpareKeys.Clear();
            _liveRegistryPrimed = false;
        }

        /// <summary>Focuses the Live timeline on a specific batch (Test Run's "Show In Timeline").</summary>
        private void SelectTimelineBatch(ConvaiActionsSessionLog.BatchRecord batch)
        {
            _liveSelectedBatch = batch;
            _liveSelectedStepIndex = batch != null && batch.Steps.Count > 0 ? 0 : -1;
        }

        #endregion

        #region Live mode drawing

        private void DrawLiveMode()
        {
            bool playing = EditorApplication.isPlaying;
            EnsureSettingsBindings();
            EnsureLiveModels();
            if (playing && !_liveRegistryPrimed)
            {
                _lastRegistrySnapshotTime = EditorApplication.timeSinceStartup;
                RebuildLiveRegistry();
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                _liveScroll = EditorGUILayout.BeginScrollView(_liveScroll, GUILayout.ExpandHeight(true));
                using (new EditorGUILayout.VerticalScope(Theme.PaneContent))
                {
                    if (playing)
                    {
                        DrawNowPlayingCard(_settingsDispatcher);
                    }
                    else
                    {
                        // Session Review: the collector's log survives Play-mode exit, so the
                        // recorded timeline, insights, and feedback stay reviewable. The live-only
                        // cards (Now Performing, live registry) are skipped — they would be stale.
                        Theme.BeginPanel(Theme.TextSecondary);
                        GUILayout.Label(ConvaiActionsEditorStrings.InsightsPostPlayBanner, Theme.BodyWrapped);
                        Theme.EndPanel(10f);
                    }

                    DrawInsightsCard();
                    DrawTimelineCard();
                    if (playing)
                        DrawLiveRegistryCard();
                    // Above the feedback log on purpose: a command that never ran produces no
                    // feedback, so this is the card that explains an otherwise empty session.
                    DrawDroppedCommandsCard();
                    DrawFeedbackCard();
                    DrawLiveAdvancedSection(_character != null ? _character.GetActionConfigSource() : null);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawNowPlayingCard(ConvaiActionDispatcher dispatcher)
        {
            Theme.BeginCard();
            Theme.SectionHeader(LiveNowGlyph, ConvaiActionsEditorStrings.LiveNowPlayingTitle);

            if (dispatcher == null)
            {
                Theme.BeginPanel(Theme.StatusWarn);
                GUILayout.Label(ConvaiActionsEditorStrings.LiveNoDispatcherBody, Theme.BodyWrapped);
                Theme.EndPanel(0f);
                Theme.EndCard();
                return;
            }

            if (!dispatcher.IsProcessingLive)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.LiveIdleBody, Theme.MutedWrapped);
            }
            else
            {
                string currentActionName = dispatcher.CurrentActionDisplayNameLive;
                if (string.IsNullOrEmpty(currentActionName))
                    GUILayout.Label(ConvaiActionsEditorStrings.LiveStartingBody, Theme.BodyWrapped);
                else
                    GUILayout.Label(ConvaiActionsEditorStrings.BuildDispatcherPerforming(currentActionName),
                        Theme.BodyWrapped);

                ConvaiActionsSessionLog.BatchRecord current = ConvaiActionsSessionCollector.Log.CurrentBatch;
                if (current != null && current.Steps.Count > 0)
                {
                    GUILayout.Space(4f);
                    Theme.BeginPanel(null);
                    for (int i = 0; i < current.Steps.Count; i++)
                        DrawInFlightStepLine(current.Steps[i]);
                    Theme.EndPanel(0f);
                }
            }

            GUILayout.Space(4f);
            GUILayout.Label(
                ConvaiActionsEditorStrings.BuildDispatcherQueueSummary(
                    dispatcher.PendingBatchCountLive, dispatcher.StartedBatchCountLive),
                Theme.MicroLabel);

            Theme.EndCard();
        }

        /// <summary>
        ///     One line of the in-flight batch. The running step's elapsed text changes every
        ///     repaint by definition, so it (and only it) is composed live.
        /// </summary>
        private void DrawInFlightStepLine(ConvaiActionsSessionLog.StepRecord step)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect dotRect = GUILayoutUtility.GetRect(14f, 16f, GUILayout.Width(14f));
                Color tint = step.Completed ? StepStatusColor(step.Status) : Theme.Accent;
                Theme.StatusDot(dotRect, tint, !step.Completed);
                GUILayout.Space(2f);

                if (step.Completed)
                {
                    GUILayout.Label(ConvaiActionsEditorStrings.BuildLiveStepLabel(
                            step.ActionName, step.TargetName,
                            ConvaiActionsEditorStrings.DescribeStepStatus(step.Status), step.DurationMs),
                        Theme.BodyWrapped);
                }
                else
                {
                    double elapsed = EditorApplication.timeSinceStartup - step.StartTime;
                    GUILayout.Label(ConvaiActionsEditorStrings.BuildTestRunRunning(step.ActionName, elapsed),
                        Theme.BodyWrapped);
                }
            }
        }

        private void DrawTimelineCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(LiveTimelineGlyph, ConvaiActionsEditorStrings.LiveTimelineTitle);

            if (_liveTimeline.Count == 0)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.LiveTimelineEmpty, Theme.MutedWrapped);
                Theme.EndCard();
                return;
            }

            GUILayout.Label(ConvaiActionsEditorStrings.LiveTimelineIntro, Theme.MutedWrapped);
            GUILayout.Space(6f);

            float actionWidth;
            using (Frame.TableHeader())
            {
                Rect header = Frame.ReserveTableHeaderRect();
                float tableWidth = Mathf.Max(460f, header.width);
                actionWidth = Mathf.Max(260f,
                    tableWidth - TimelineResultColumnWidth - TimelineDurationColumnWidth);
                Theme.TableHeaderLabel(
                    header, 28f, TimelineResultColumnWidth - 28f,
                    ConvaiActionsEditorStrings.LiveTimelineStatusColumn);
                Theme.TableHeaderLabel(
                    header, TimelineResultColumnWidth + 8f, actionWidth - 16f,
                    ConvaiActionsEditorStrings.LiveTimelineActionColumn);
                Theme.TableHeaderLabel(
                    header, TimelineResultColumnWidth + actionWidth + 8f,
                    TimelineDurationColumnWidth - 16f,
                    ConvaiActionsEditorStrings.LiveTimelineDurationColumn, right: true);
            }

            for (int i = 0; i < _liveTimeline.Count; i++)
                DrawTimelineBatch(_liveTimeline[i], actionWidth);

            DrawSelectedStepDetail();
            Theme.EndCard();
        }

        private void DrawTimelineBatch(LiveBatchModel model, float actionWidth)
        {
            Theme.BeginPanel(null);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(22f)))
            {
                GUILayout.Label(model.Header, Theme.CardName, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();

                GUIContent sourcePill = model.Record.IsTestRun
                    ? ConvaiActionsEditorStrings.LiveSourceTestRunPill
                    : ConvaiActionsEditorStrings.LiveSourceConversationPill;
                DrawInlinePill(sourcePill, model.Record.IsTestRun ? Theme.Accent : Theme.TextSecondary);

                if (model.Record.Aborted)
                {
                    GUILayout.Space(4f);
                    DrawInlinePill(ConvaiActionsEditorStrings.LiveAbortedPill, Theme.StatusWarn);
                }

            }
            Theme.EndPanel(0f);

            for (int j = 0; j < model.Record.Steps.Count; j++)
                DrawTimelineStepRow(model, j, actionWidth);

            GUILayout.Space(10f);
        }

        private void DrawTimelineStepRow(LiveBatchModel model, int stepIndex, float actionWidth)
        {
            ConvaiActionsSessionLog.StepRecord step = model.Record.Steps[stepIndex];
            using (new Frame.TableRowScope(stepIndex, TimelineStepRowHeight))
            {
                Rect row = Frame.ReserveScopeRect(TimelineStepRowHeight);
                bool selected = ReferenceEquals(_liveSelectedBatch, model.Record) &&
                                _liveSelectedStepIndex == stepIndex;
                bool hover = row.Contains(Event.current.mousePosition);
                if (selected || hover)
                    Theme.FillRounded(row, selected ? Theme.CardBgSelected : Theme.CardBgHover, 4f);

                Color statusColor = step.Completed ? StepStatusColor(step.Status) : Theme.Accent;
                Theme.StatusDot(new Rect(row.x + 8f, row.y + 6f, 14f, 18f), statusColor, selected || !step.Completed);

                string status = step.Completed
                    ? ConvaiActionsEditorStrings.DescribeStepStatus(step.Status)
                    : "Running";
                GUI.Label(new Rect(row.x + 28f, row.y + 5f, TimelineResultColumnWidth - 28f, 20f),
                    status, Theme.ReadingLabel);

                GUIContent subject = stepIndex < model.StepSubjects.Count
                    ? model.StepSubjects[stepIndex]
                    : GUIContent.none;
                GUI.Label(new Rect(row.x + TimelineResultColumnWidth + 8f, row.y + 5f,
                    actionWidth - 16f, 20f), subject, Theme.BodyWrapped);

                GUIContent duration = step.Completed
                    ? stepIndex < model.StepDurations.Count
                        ? model.StepDurations[stepIndex]
                        : GUIContent.none
                    : new GUIContent(
                        $"{Math.Max(0d, EditorApplication.timeSinceStartup - step.StartTime):0.0} s",
                        "Elapsed time for the running step.");
                GUI.Label(new Rect(row.x + TimelineResultColumnWidth + actionWidth + 8f, row.y + 5f,
                    TimelineDurationColumnWidth - 16f, 20f), duration, Theme.MicroLabelRight);

                EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
                if (GUI.Button(row, GUIContent.none, Theme.InvisibleButton))
                {
                    _liveSelectedBatch = model.Record;
                    _liveSelectedStepIndex = stepIndex;
                    Repaint();
                }
            }
        }

        private void DrawSelectedStepDetail()
        {
            if (_liveSelectedBatch == null || _liveSelectedStepIndex < 0 ||
                _liveSelectedStepIndex >= _liveSelectedBatch.Steps.Count)
                return;

            ConvaiActionsSessionLog.StepRecord step = _liveSelectedBatch.Steps[_liveSelectedStepIndex];

            GUILayout.Space(2f);
            GUILayout.Label(ConvaiActionsEditorStrings.LiveStepDetailTitle, Theme.MicroLabel);
            GUILayout.Space(2f);
            Theme.BeginPanel(step.Completed ? StepStatusColor(step.Status) : Theme.Accent);

            if (step.Completed)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.BuildLiveStepLabel(
                        step.ActionName, step.TargetName,
                        ConvaiActionsEditorStrings.DescribeStepStatus(step.Status), step.DurationMs),
                    Theme.BodyWrapped);

                if (step.Status != ConvaiActionExecutionStatus.Succeeded)
                {
                    if (step.FailureReason != ConvaiActionFailureReason.None)
                        GUILayout.Label(step.FailureReason.ToString(), Theme.BodyWrapped);
                    if (!string.IsNullOrEmpty(step.FailureMessage))
                        GUILayout.Label(step.FailureMessage, Theme.BodyWrapped);
                }
            }
            else
            {
                double elapsed = EditorApplication.timeSinceStartup - step.StartTime;
                GUILayout.Label(ConvaiActionsEditorStrings.BuildTestRunRunning(step.ActionName, elapsed),
                    Theme.BodyWrapped);
            }

            Theme.EndPanel(0f);
        }

        private void DrawLiveRegistryCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(LiveRegistryGlyph, ConvaiActionsEditorStrings.LiveRegistryTitle);

            double now = EditorApplication.timeSinceStartup;
            if (_liveRegistryRows.Count == 0 && _liveRemovedRows.Count == 0)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.LiveRegistryEmpty, Theme.MutedWrapped);
                Theme.EndCard();
                return;
            }

            for (int i = 0; i < _liveRegistryRows.Count; i++)
            {
                LiveRegistryRow row = _liveRegistryRows[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(row.Name, Theme.CardName, GUILayout.MinWidth(60f));
                    GUILayout.FlexibleSpace();

                    if (now < row.HighlightUntil)
                    {
                        DrawInlinePill(ConvaiActionsEditorStrings.LiveNewPill, Theme.Accent);
                        GUILayout.Space(4f);
                    }

                    DrawInlinePill(row.IsCharacter
                        ? ConvaiActionsEditorStrings.ScanKindCharacterPill
                        : ConvaiActionsEditorStrings.ScanKindObjectPill, Theme.TextMuted);
                    GUILayout.Space(4f);
                    DrawInlinePill(row.IsAuthored
                        ? ConvaiActionsEditorStrings.LiveAuthoredPill
                        : ConvaiActionsEditorStrings.LiveRuntimePill, Theme.TextSecondary);
                    GUILayout.Space(4f);
                    DrawInlinePill(row.Available
                        ? ConvaiActionsEditorStrings.LiveAvailablePill
                        : ConvaiActionsEditorStrings.LiveUnavailablePill,
                        row.Available ? Theme.StatusReady : Theme.StatusWarn);
                }
            }

            for (int i = 0; i < _liveRemovedRows.Count; i++)
            {
                RemovedRegistryRow removed = _liveRemovedRows[i];
                if (now > removed.Until)
                    continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(removed.Name, Theme.MutedWrapped, GUILayout.MinWidth(60f));
                    GUILayout.FlexibleSpace();
                    DrawInlinePill(removed.IsCharacter
                        ? ConvaiActionsEditorStrings.ScanKindCharacterPill
                        : ConvaiActionsEditorStrings.ScanKindObjectPill, Theme.TextMuted);
                    GUILayout.Space(4f);
                    DrawInlinePill(ConvaiActionsEditorStrings.LiveRemovedPill, Theme.StatusError);
                }
            }

            Theme.EndCard();
        }

        /// <summary>
        ///     Lists the commands that were discarded before anything could run them.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This card is the one that answers "she just ignored me". Everything else in this
        ///         view is built from dispatcher events, and a dropped command never reaches the
        ///         dispatcher — so for the failure mode that is hardest to diagnose, the window used
        ///         to be perfectly empty and perfectly unhelpful.
        ///     </para>
        ///     <para>
        ///         Newest first, and the explanation is shown in full rather than summarized: it
        ///         already ends in what to do, and truncating it would leave only the part that says
        ///         something went wrong.
        ///     </para>
        /// </remarks>
        private void DrawDroppedCommandsCard()
        {
            IReadOnlyList<ConvaiActionsSessionLog.DropRecord> drops = ConvaiActionsSessionCollector.Log.Drops;

            Theme.BeginCard();
            Theme.SectionHeader(LiveDroppedGlyph, ConvaiActionsEditorStrings.LiveDroppedTitle);

            if (drops.Count == 0)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.LiveDroppedEmpty, Theme.MutedWrapped);
                Theme.EndCard();
                return;
            }

            for (int i = drops.Count - 1; i >= 0; i--)
            {
                ConvaiActionsSessionLog.DropRecord entry = drops[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(entry.TimeLabel, Theme.MicroLabel, GUILayout.Width(56f));
                    DrawInlinePill(ConvaiActionsEditorStrings.LiveDroppedPill, Theme.StatusError);
                    GUILayout.Space(6f);
                    GUILayout.Label(entry.Explanation, Theme.BodyWrapped);
                }
            }

            Theme.EndCard();
        }

        private void DrawFeedbackCard()
        {
            Theme.BeginCard();
            Theme.SectionHeader(LiveFeedbackGlyph, ConvaiActionsEditorStrings.LiveFeedbackTitle);

            IReadOnlyList<ConvaiActionsSessionLog.FeedbackRecord> feedback = ConvaiActionsSessionCollector.Log.Feedback;
            if (feedback.Count == 0)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.LiveFeedbackEmpty, Theme.MutedWrapped);
                Theme.EndCard();
                return;
            }

            GUILayout.Label(ConvaiActionsEditorStrings.LiveFeedbackIntro, Theme.MutedWrapped);
            GUILayout.Space(6f);

            float messageWidth;
            using (Frame.TableHeader())
            {
                Rect header = Frame.ReserveTableHeaderRect();
                float tableWidth = Mathf.Max(420f, header.width);
                messageWidth = Mathf.Max(260f,
                    tableWidth - FeedbackTimeColumnWidth - FeedbackDeliveryColumnWidth);
                Theme.TableHeaderLabel(
                    header, 8f, FeedbackTimeColumnWidth - 16f,
                    ConvaiActionsEditorStrings.LiveFeedbackTimeColumn);
                Theme.TableHeaderLabel(
                    header, FeedbackTimeColumnWidth + 8f, FeedbackDeliveryColumnWidth - 16f,
                    ConvaiActionsEditorStrings.LiveFeedbackDeliveryColumn);
                Theme.TableHeaderLabel(
                    header, FeedbackTimeColumnWidth + FeedbackDeliveryColumnWidth + 8f,
                    messageWidth - 16f,
                    ConvaiActionsEditorStrings.LiveFeedbackMessageColumn);
            }

            for (int i = feedback.Count - 1; i >= 0; i--)
            {
                ConvaiActionsSessionLog.FeedbackRecord entry = feedback[i];
                float textHeight = Metrics.WrappedHeight(Theme.BodyWrapped, entry.Fact, messageWidth - 16f);
                float rowHeight = Mathf.Max(FeedbackRowMinHeight, textHeight + 10f);
                int rowIndex = feedback.Count - 1 - i;
                using (new Frame.TableRowScope(rowIndex, rowHeight))
                {
                    Rect row = Frame.ReserveScopeRect(rowHeight);
                    GUI.Label(new Rect(row.x + 8f, row.y + 5f, FeedbackTimeColumnWidth - 16f, 20f),
                        entry.TimeLabel, Theme.MicroLabel);

                    GUIContent delivery = entry.Narrated
                        ? ConvaiActionsEditorStrings.LiveNarratedPill
                        : ConvaiActionsEditorStrings.LiveSilentPill;
                    float pillWidth = Theme.PillWidth(delivery);
                    Rect pillRect = new(
                        row.x + FeedbackTimeColumnWidth + 8f,
                        row.y + Mathf.Max(5f, (rowHeight - 16f) * 0.5f),
                        Mathf.Min(pillWidth, FeedbackDeliveryColumnWidth - 16f), 16f);
                    Theme.Pill(pillRect, delivery, entry.Narrated ? Theme.Accent : Theme.TextMuted);

                    GUI.Label(new Rect(
                            row.x + FeedbackTimeColumnWidth + FeedbackDeliveryColumnWidth + 8f,
                            row.y + 5f, messageWidth - 16f, rowHeight - 10f),
                        new GUIContent(entry.Fact, ConvaiActionsEditorStrings.LiveFeedbackMessageColumn.tooltip),
                        Theme.BodyWrapped);
                }
            }

            Theme.EndCard();
        }

        private static void DrawInlinePill(GUIContent content, Color tint)
        {
            float width = Theme.PillWidth(content);
            Rect rect = GUILayoutUtility.GetRect(width, 16f, GUILayout.Width(width), GUILayout.Height(16f));
            Theme.Pill(rect, content, tint);
        }

        private static Color StepStatusColor(ConvaiActionExecutionStatus status) => status switch
        {
            ConvaiActionExecutionStatus.Succeeded => Theme.StatusReady,
            ConvaiActionExecutionStatus.Unhandled => Theme.TextMuted,
            ConvaiActionExecutionStatus.Canceled => Theme.StatusWarn,
            _ => Theme.StatusError
        };

        #endregion

        #region Cached model building

        /// <summary>Rebuilds the timeline view models only when the collector log's version moved.</summary>
        private void EnsureLiveModels()
        {
            ConvaiActionsSessionLog log = ConvaiActionsSessionCollector.Log;
            if (log.Version == _liveModelVersion)
                return;

            _liveModelVersion = log.Version;
            _liveTimeline.Clear();

            IReadOnlyList<ConvaiActionsSessionLog.BatchRecord> batches = log.Batches;
            for (int i = batches.Count - 1; i >= 0 && _liveTimeline.Count < MaxTimelineBatchesShown; i--)
            {
                ConvaiActionsSessionLog.BatchRecord record = batches[i];
                var model = new LiveBatchModel { Record = record };
                double batchDurationMs = record.Finished ? (record.EndTime - record.StartTime) * 1000d : 0d;
                model.Header = ConvaiActionsEditorStrings.BuildLiveBatchHeader(
                    Mathf.Max(0, record.BatchIndex), record.Steps.Count, batchDurationMs);

                for (int j = 0; j < record.Steps.Count; j++)
                {
                    ConvaiActionsSessionLog.StepRecord step = record.Steps[j];
                    model.StepSubjects.Add(ConvaiActionsEditorStrings.BuildLiveStepSubject(
                        step.ActionName, step.TargetName));
                    model.StepDurations.Add(step.Completed
                        ? ConvaiActionsEditorStrings.BuildLiveStepDuration(step.DurationMs)
                        : null);
                }

                _liveTimeline.Add(model);
            }
        }

        /// <summary>
        ///     Rebuilds the live registry rows from the character's merged runtime config — the
        ///     exact snapshot the dispatcher resolves against. Polled at
        ///     <see cref="RegistrySnapshotIntervalSeconds" /> (component targets register without
        ///     raising an event), never per repaint.
        /// </summary>
        private void RebuildLiveRegistry()
        {
            _liveRegistryPrimed = true;
            if (_character == null)
                return;

            ConvaiActionConfig config = _character.GetRuntimeActionConfig();
            ConvaiActionConfigSource source = _character.GetActionConfigSource();
            double now = EditorApplication.timeSinceStartup;

            var authoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
            {
                IReadOnlyList<ConvaiActionObjectDefinition> authoredObjects = source.Objects;
                for (int i = 0; authoredObjects != null && i < authoredObjects.Count; i++)
                {
                    string authoredName = authoredObjects[i]?.Name;
                    if (!string.IsNullOrWhiteSpace(authoredName))
                        authoredNames.Add(authoredName);
                }

                IReadOnlyList<ConvaiActionCharacterDefinition> authoredCharacters = source.Characters;
                for (int i = 0; authoredCharacters != null && i < authoredCharacters.Count; i++)
                {
                    string authoredName = authoredCharacters[i]?.Name;
                    if (!string.IsNullOrWhiteSpace(authoredName))
                        authoredNames.Add(authoredName);
                }
            }

            bool wasPrimed = _liveKnownKeys.Count > 0;
            HashSet<string> nextKeys = _liveSpareKeys;
            nextKeys.Clear();
            _liveRegistryRows.Clear();

            if (config != null)
            {
                for (int i = 0; i < config.Objects.Count; i++)
                {
                    ConvaiActionObjectDefinition entry = config.Objects[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    AddRegistryRow(nextKeys, authoredNames, entry.Name, false, entry.Available, wasPrimed, now);
                }

                for (int i = 0; i < config.Characters.Count; i++)
                {
                    ConvaiActionCharacterDefinition entry = config.Characters[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    AddRegistryRow(nextKeys, authoredNames, entry.Name, true, entry.Available, wasPrimed, now);
                }
            }

            // Anything known last poll but gone now gets a short "Removed" residue row.
            foreach (string key in _liveKnownKeys)
            {
                if (nextKeys.Contains(key))
                    continue;

                _liveRemovedRows.Add(new RemovedRegistryRow
                {
                    Name = key.Substring(2),
                    IsCharacter = key[0] == 'C',
                    Until = now + RegistryHighlightSeconds
                });
            }

            for (int i = _liveRemovedRows.Count - 1; i >= 0; i--)
            {
                if (now > _liveRemovedRows[i].Until)
                    _liveRemovedRows.RemoveAt(i);
            }

            // Swap the key sets (reuse both allocations across polls).
            HashSet<string> previous = _liveKnownKeys;
            _liveKnownKeys = nextKeys;
            _liveSpareKeys = previous;
        }

        private void AddRegistryRow(
            HashSet<string> nextKeys,
            HashSet<string> authoredNames,
            string name,
            bool isCharacter,
            bool available,
            bool wasPrimed,
            double now)
        {
            string trimmed = name.Trim();
            string key = (isCharacter ? "C:" : "O:") + trimmed;
            if (!nextKeys.Add(key))
                return; // Duplicate-named merged entries collapse into one row.

            if (wasPrimed && !_liveKnownKeys.Contains(key))
                _liveHighlightUntil[key] = now + RegistryHighlightSeconds;

            _liveHighlightUntil.TryGetValue(key, out double highlightUntil);
            if (highlightUntil > 0d && now > highlightUntil)
            {
                _liveHighlightUntil.Remove(key);
                highlightUntil = 0d;
            }

            _liveRegistryRows.Add(new LiveRegistryRow
            {
                Name = trimmed,
                IsCharacter = isCharacter,
                IsAuthored = authoredNames.Contains(trimmed),
                Available = available,
                HighlightUntil = highlightUntil
            });
        }

        #endregion
    }
}
