using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Convai.Runtime.Actions;

namespace Convai.Editor.Actions
{
    /// <summary>How the Insights table orders its per-action rows.</summary>
    internal enum ConvaiActionsInsightsSort
    {
        /// <summary>Most failed-or-declined runs first (the default — problems float to the top).</summary>
        MostFailed = 0,

        /// <summary>Most total runs first.</summary>
        MostUsed = 1,

        /// <summary>Alphabetical by action name.</summary>
        Name = 2
    }

    /// <summary>One action's aggregated usage for the current session.</summary>
    internal sealed class ConvaiActionsInsightsRow
    {
        internal string ActionName = string.Empty;

        /// <summary>Completed runs recorded for this action (in-flight steps are not counted yet).</summary>
        internal int RunCount;

        internal int SucceededCount;

        /// <summary>Failed, timed-out, or canceled runs.</summary>
        internal int FailedCount;

        /// <summary>Runs the character's scene behavior declined (includes disabled-action declines).</summary>
        internal int UnhandledCount;

        internal double AverageDurationMs;
        internal double MaxDurationMs;

        /// <summary>Most recent failure reason/message; empty when every run succeeded.</summary>
        internal string LastFailureReason = string.Empty;

        /// <summary>Clock time (collector seconds) of the most recent run's start.</summary>
        internal double LastUsedTime;

        /// <summary>Outcome of the most recent completed run.</summary>
        internal ConvaiActionExecutionStatus LastStatus;

        /// <summary>Duration of the most recent completed run, in milliseconds.</summary>
        internal double LastDurationMs;

        internal int FailedOrUnhandledCount => FailedCount + UnhandledCount;
    }

    /// <summary>
    ///     Pure aggregation behind the Actions Editor window's Insights section: folds
    ///     <see cref="ConvaiActionsSessionLog" /> step records into per-action usage
    ///     rows and renders the shareable Markdown report. No <c>UnityEditor</c>/GUI dependency, so
    ///     every rule here is unit-testable. Explicitly editor-only diagnostics — nothing runs or
    ///     ships at runtime, and nothing leaves the machine.
    /// </summary>
    internal static class ConvaiActionsInsightsModel
    {
        /// <summary>
        ///     Aggregates every completed step in <paramref name="log" /> into one row per action
        ///     name (case-insensitive), sorted per <paramref name="sort" />. Ties fall back to run
        ///     count, then name, so the order is deterministic.
        /// </summary>
        internal static List<ConvaiActionsInsightsRow> Build(
            ConvaiActionsSessionLog log,
            ConvaiActionsInsightsSort sort = ConvaiActionsInsightsSort.MostFailed)
        {
            var byName = new Dictionary<string, ConvaiActionsInsightsRow>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ConvaiActionsInsightsRow>();
            if (log == null)
                return rows;

            IReadOnlyList<ConvaiActionsSessionLog.BatchRecord> batches = log.Batches;
            for (int b = 0; b < batches.Count; b++)
            {
                List<ConvaiActionsSessionLog.StepRecord> steps = batches[b].Steps;
                for (int s = 0; s < steps.Count; s++)
                    Accumulate(byName, rows, steps[s]);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                ConvaiActionsInsightsRow row = rows[i];
                if (row.RunCount > 0)
                    row.AverageDurationMs /= row.RunCount;
            }

            Sort(rows, sort);
            return rows;
        }

        /// <summary>Finds one action's aggregated row (case-insensitive), or null when it never ran.</summary>
        internal static ConvaiActionsInsightsRow FindRow(
            IReadOnlyList<ConvaiActionsInsightsRow> rows,
            string actionName)
        {
            if (rows == null || string.IsNullOrWhiteSpace(actionName))
                return null;

            string trimmed = actionName.Trim();
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].ActionName, trimmed, StringComparison.OrdinalIgnoreCase))
                    return rows[i];
            }

            return null;
        }

        /// <summary>
        ///     Renders the rows as a readable Markdown report (header, summary line, and one table
        ///     row per action) for the Insights section's Copy Report button.
        /// </summary>
        internal static string BuildMarkdownReport(
            IReadOnlyList<ConvaiActionsInsightsRow> rows,
            string characterName)
        {
            var builder = new StringBuilder(256);
            string subject = string.IsNullOrWhiteSpace(characterName) ? "Convai Character" : characterName.Trim();
            builder.Append("# Action usage — ").AppendLine(subject);
            builder.AppendLine();

            if (rows == null || rows.Count == 0)
            {
                builder.AppendLine("No actions ran this session.");
                return builder.ToString();
            }

            int totalRuns = 0;
            for (int i = 0; i < rows.Count; i++)
                totalRuns += rows[i].RunCount;

            builder
                .Append(rows.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" action(s), ")
                .Append(totalRuns.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" recorded run(s) this session.");
            builder.AppendLine();
            builder.AppendLine("| Action | Runs | Completed | Failed | Declined | Avg ms | Max ms | Last failure |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");

            for (int i = 0; i < rows.Count; i++)
            {
                ConvaiActionsInsightsRow row = rows[i];
                builder
                    .Append("| ").Append(EscapeCell(row.ActionName))
                    .Append(" | ").Append(row.RunCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(row.SucceededCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(row.FailedCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(row.UnhandledCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(row.AverageDurationMs.ToString("0", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(row.MaxDurationMs.ToString("0", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(string.IsNullOrEmpty(row.LastFailureReason) ? "—" : EscapeCell(row.LastFailureReason))
                    .AppendLine(" |");
            }

            return builder.ToString();
        }

        private static void Accumulate(
            Dictionary<string, ConvaiActionsInsightsRow> byName,
            List<ConvaiActionsInsightsRow> rows,
            ConvaiActionsSessionLog.StepRecord step)
        {
            if (step == null || !step.Completed || string.IsNullOrWhiteSpace(step.ActionName))
                return;

            string actionName = step.ActionName.Trim();
            if (!byName.TryGetValue(actionName, out ConvaiActionsInsightsRow row))
            {
                row = new ConvaiActionsInsightsRow { ActionName = actionName };
                byName[actionName] = row;
                rows.Add(row);
            }

            double durationMs = Math.Max(0d, step.DurationMs);
            row.RunCount++;
            row.AverageDurationMs += durationMs; // Divided by RunCount once aggregation finishes.
            if (durationMs > row.MaxDurationMs)
                row.MaxDurationMs = durationMs;

            switch (step.Status)
            {
                case ConvaiActionExecutionStatus.Succeeded:
                    row.SucceededCount++;
                    break;
                case ConvaiActionExecutionStatus.Unhandled:
                    row.UnhandledCount++;
                    RecordFailureReason(row, step);
                    break;
                default:
                    row.FailedCount++;
                    RecordFailureReason(row, step);
                    break;
            }

            if (step.StartTime >= row.LastUsedTime)
            {
                row.LastUsedTime = step.StartTime;
                row.LastStatus = step.Status;
                row.LastDurationMs = durationMs;
            }
        }

        private static void RecordFailureReason(
            ConvaiActionsInsightsRow row,
            ConvaiActionsSessionLog.StepRecord step)
        {
            row.LastFailureReason = step.FailureReason != ConvaiActionFailureReason.None
                ? step.FailureReason.ToString()
                : string.IsNullOrEmpty(step.FailureMessage)
                    ? step.Status.ToString()
                    : step.FailureMessage;
        }

        private static void Sort(List<ConvaiActionsInsightsRow> rows, ConvaiActionsInsightsSort sort)
        {
            switch (sort)
            {
                case ConvaiActionsInsightsSort.MostUsed:
                    rows.Sort(static (a, b) =>
                    {
                        int byRuns = b.RunCount.CompareTo(a.RunCount);
                        return byRuns != 0
                            ? byRuns
                            : string.Compare(a.ActionName, b.ActionName, StringComparison.OrdinalIgnoreCase);
                    });
                    break;

                case ConvaiActionsInsightsSort.Name:
                    rows.Sort(static (a, b) =>
                        string.Compare(a.ActionName, b.ActionName, StringComparison.OrdinalIgnoreCase));
                    break;

                default:
                    rows.Sort(static (a, b) =>
                    {
                        int byFailures = b.FailedOrUnhandledCount.CompareTo(a.FailedOrUnhandledCount);
                        if (byFailures != 0)
                            return byFailures;

                        int byRuns = b.RunCount.CompareTo(a.RunCount);
                        return byRuns != 0
                            ? byRuns
                            : string.Compare(a.ActionName, b.ActionName, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
            }
        }

        /// <summary>Keeps user text from breaking the Markdown table shape.</summary>
        private static string EscapeCell(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
