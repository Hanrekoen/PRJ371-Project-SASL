using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Frame = Convai.Editor.UI.ConvaiEditorFrame;
using Glyphs = Convai.Editor.UI.ConvaiEditorGlyphs;
using Metrics = Convai.Editor.UI.ConvaiEditorTextMetrics;
using Theme = Convai.Editor.UI.ConvaiEditorTheme;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Insights section of the Actions Editor window : per-action
    ///     usage aggregated from <see cref="ConvaiActionsSessionCollector" />'s session log by
    ///     <see cref="ConvaiActionsInsightsModel" />. Lives in the Live view during Play mode and
    ///     survives Play-mode exit as the "Session Review" mode (the collector's log deliberately
    ///     outlives the session), plus a compact per-action last-run strip in the detail pane.
    ///     All view models are rebuilt only when the collector log's version moves — never per
    ///     repaint. Editor-only diagnostics; nothing leaves the machine.
    /// </summary>
    internal sealed partial class ConvaiActionsEditorWindow
    {
        private const string InsightsGlyph = Glyphs.Capture;
        private const float InsightsViewportHeight = 196f;
        private const float InsightsActionColumnWidth = 220f;
        private const float InsightsTimingColumnWidth = 124f;
        private const float InsightsRowHeight = 48f;

        private sealed class InsightsRowView
        {
            internal GUIContent Action;
            internal GUIContent Outcomes;
            internal GUIContent Durations;
            internal GUIContent LastFailure;
            internal bool Healthy;
        }

        // Rebuilt only when the collector log's version or the sort selection changes.
        private int _insightsVersion = -1;
        private ConvaiActionsInsightsSort _insightsSort = ConvaiActionsInsightsSort.MostFailed;
        private List<ConvaiActionsInsightsRow> _insightsRows = new();
        private readonly List<InsightsRowView> _insightsRowViews = new();
        private Vector2 _insightsScroll;

        // Detail-pane last-run strip cache (per log version + action name).
        private int _detailHistoryVersion = -1;
        private string _detailHistoryActionName = string.Empty;
        private GUIContent _detailHistoryContent;

        /// <summary>Rebuilds the insights rows and their cached row contents when stale.</summary>
        private void EnsureInsightsModels()
        {
            ConvaiActionsSessionLog log = ConvaiActionsSessionCollector.Log;
            if (log.Version == _insightsVersion)
                return;

            _insightsVersion = log.Version;
            _insightsRows = ConvaiActionsInsightsModel.Build(log, _insightsSort);
            _insightsRowViews.Clear();

            for (int i = 0; i < _insightsRows.Count; i++)
            {
                ConvaiActionsInsightsRow row = _insightsRows[i];
                var view = new InsightsRowView
                {
                    Action = new GUIContent(row.ActionName, "The action name recorded in this session."),
                    Outcomes = ConvaiActionsEditorStrings.BuildInsightsOutcomes(
                        row.RunCount, row.SucceededCount, row.FailedCount, row.UnhandledCount),
                    Durations = ConvaiActionsEditorStrings.BuildInsightsDurations(
                        row.AverageDurationMs, row.MaxDurationMs),
                    Healthy = row.FailedOrUnhandledCount == 0
                };

                if (!string.IsNullOrEmpty(row.LastFailureReason))
                    view.LastFailure = ConvaiActionsEditorStrings.BuildInsightsLastFailure(row.LastFailureReason);

                _insightsRowViews.Add(view);
            }
        }

        /// <summary>The Insights card: sort pills, per-action rows, and the Copy Report button.</summary>
        private void DrawInsightsCard()
        {
            EnsureInsightsModels();

            Theme.BeginCard();
            Theme.SectionHeader(InsightsGlyph, ConvaiActionsEditorStrings.InsightsCardTitle);

            if (_insightsRowViews.Count == 0)
            {
                GUILayout.Label(ConvaiActionsEditorStrings.InsightsEmpty, Theme.MutedWrapped);
                Theme.EndCard();
                return;
            }

            GUILayout.Label(ConvaiActionsEditorStrings.InsightsIntro, Theme.MutedWrapped);
            GUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUIContent orderLabel = ConvaiActionsEditorStrings.InsightsOrderLabel;
                float orderWidth = Metrics.Width(Theme.MicroLabel, orderLabel);
                Rect orderRect = GUILayoutUtility.GetRect(
                    orderWidth, 20f, GUILayout.Width(orderWidth), GUILayout.Height(20f));
                GUI.Label(orderRect, orderLabel, Theme.MicroLabel);
                GUILayout.Space(6f);
                DrawInsightsSortPill(ConvaiActionsInsightsSort.MostFailed, ConvaiActionsEditorStrings.InsightsSortMostFailed);
                GUILayout.Space(4f);
                DrawInsightsSortPill(ConvaiActionsInsightsSort.MostUsed, ConvaiActionsEditorStrings.InsightsSortMostUsed);
                GUILayout.Space(4f);
                DrawInsightsSortPill(ConvaiActionsInsightsSort.Name, ConvaiActionsEditorStrings.InsightsSortByName);
                GUILayout.FlexibleSpace();

                float copyWidth = Theme.GhostButtonWidth(ConvaiActionsEditorStrings.InsightsCopyReportButton);
                Rect copyRect = GUILayoutUtility.GetRect(copyWidth, 20f, GUILayout.Width(copyWidth), GUILayout.Height(20f));
                if (Theme.GhostButton(copyRect, ConvaiActionsEditorStrings.InsightsCopyReportButton))
                    CopyInsightsReport();
            }

            GUILayout.Label(ConvaiActionsEditorStrings.BuildInsightsOrderExplanation(_insightsSort), Theme.MicroLabel);
            GUILayout.Space(4f);

            float contentHeight = _insightsRowViews.Count * InsightsRowHeight;
            bool needsScrollbar = contentHeight > InsightsViewportHeight;
            float actionWidth;
            float outcomeWidth;
            using (Frame.TableHeader())
            {
                Rect header = Frame.ReserveTableHeaderRect();
                float tableWidth = Mathf.Max(460f, header.width - (needsScrollbar ? 15f : 0f));
                actionWidth = Mathf.Clamp(tableWidth * 0.28f, 150f, InsightsActionColumnWidth);
                outcomeWidth = Mathf.Max(190f,
                    tableWidth - actionWidth - InsightsTimingColumnWidth);
                Theme.TableHeaderLabel(
                    header, 28f, actionWidth - 28f, ConvaiActionsEditorStrings.InsightsActionColumn);
                Theme.TableHeaderLabel(
                    header, actionWidth + 8f, outcomeWidth - 16f,
                    ConvaiActionsEditorStrings.InsightsOutcomeColumn);
                Theme.TableHeaderLabel(
                    header, actionWidth + outcomeWidth + 8f, InsightsTimingColumnWidth - 16f,
                    ConvaiActionsEditorStrings.InsightsTimingColumn, right: true);
            }

            float viewportHeight = Mathf.Min(InsightsViewportHeight, contentHeight + 2f);
            _insightsScroll = EditorGUILayout.BeginScrollView(
                _insightsScroll, false, contentHeight > InsightsViewportHeight,
                GUILayout.Height(viewportHeight));
            for (int i = 0; i < _insightsRowViews.Count; i++)
            {
                InsightsRowView view = _insightsRowViews[i];
                using (new Frame.TableRowScope(i, InsightsRowHeight))
                {
                    Rect rowRect = Frame.ReserveScopeRect(InsightsRowHeight);
                    Rect dotRect = new(rowRect.x + 8f, rowRect.y + 14f, 14f, 18f);
                    Theme.StatusDot(dotRect, view.Healthy ? Theme.StatusReady : Theme.StatusWarn, !view.Healthy);

                    Rect actionRect = new(rowRect.x + 28f, rowRect.y + 5f,
                        actionWidth - 28f, InsightsRowHeight - 10f);
                    GUI.Label(actionRect, view.Action, Theme.BodyWrapped);

                    float outcomeX = rowRect.x + actionWidth;
                    Rect outcomeRect = new(outcomeX + 8f, rowRect.y + 5f,
                        outcomeWidth - 16f, view.LastFailure == null ? InsightsRowHeight - 10f : 20f);
                    GUI.Label(outcomeRect, view.Outcomes, Theme.BodyWrapped);
                    if (view.LastFailure != null)
                    {
                        Rect failureRect = new(outcomeX + 8f, rowRect.y + 25f,
                            outcomeWidth - 16f, 18f);
                        GUI.Label(failureRect, view.LastFailure, Theme.MutedWrapped);
                    }

                    Rect timingRect = new(outcomeX + outcomeWidth + 8f, rowRect.y + 5f,
                        InsightsTimingColumnWidth - 16f, InsightsRowHeight - 10f);
                    GUI.Label(timingRect, view.Durations, Theme.MicroLabelRight);
                }
            }
            EditorGUILayout.EndScrollView();

            Theme.EndCard();
        }

        private void DrawInsightsSortPill(ConvaiActionsInsightsSort sort, GUIContent content)
        {
            float width = Theme.PillWidth(content) + 14f;
            Rect rect = GUILayoutUtility.GetRect(width, 20f, GUILayout.Width(width), GUILayout.Height(20f));
            bool selected = _insightsSort == sort;
            bool clicked = selected ? Theme.PrimaryButton(rect, content) : Theme.GhostButton(rect, content);
            if (!clicked || selected)
                return;

            _insightsSort = sort;
            _insightsVersion = -1; // Same data, different order — force a view rebuild.
            Repaint();
        }

        private void CopyInsightsReport()
        {
            EditorGUIUtility.systemCopyBuffer = ConvaiActionsInsightsModel.BuildMarkdownReport(
                _insightsRows, _character != null ? _character.name : null);
            ShowNotification(ConvaiActionsEditorStrings.InsightsReportCopied, 2.5d);
        }

        /// <summary>
        ///     Compact per-action history strip under the detail-pane header: the selected action's
        ///     last recorded run (outcome + duration) and this-session run count. Drawn only when
        ///     the session log actually recorded a run for the action.
        /// </summary>
        private void DrawDetailHistoryStrip(ConvaiActionRow row)
        {
            string actionName = row.Definition?.ActionName;
            if (string.IsNullOrWhiteSpace(actionName))
                return;

            ConvaiActionsSessionLog log = ConvaiActionsSessionCollector.Log;
            if (log.Version != _detailHistoryVersion ||
                !string.Equals(_detailHistoryActionName, actionName, StringComparison.OrdinalIgnoreCase))
            {
                EnsureInsightsModels();
                _detailHistoryVersion = log.Version;
                _detailHistoryActionName = actionName;
                ConvaiActionsInsightsRow insights = ConvaiActionsInsightsModel.FindRow(_insightsRows, actionName);
                _detailHistoryContent = insights == null
                    ? null
                    : ConvaiActionsEditorStrings.BuildActionLastRunStrip(
                        ConvaiActionsEditorStrings.DescribeStepStatus(insights.LastStatus),
                        insights.LastDurationMs,
                        insights.RunCount);
            }

            if (_detailHistoryContent == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(ConvaiActionsEditorStrings.DetailHistoryLabel, Theme.MicroLabel);
                GUILayout.Space(6f);
                GUILayout.Label(_detailHistoryContent, Theme.MutedWrapped);
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(6f);
        }
    }
}
