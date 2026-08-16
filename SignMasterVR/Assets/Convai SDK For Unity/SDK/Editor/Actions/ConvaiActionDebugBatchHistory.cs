using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using UnityEditor;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Pure batch-history ring buffer: tracks the last <see cref="MaxEntries" /> dispatched
    ///     batches with per-step timing, status, and failure reason. No GUI/EditorWindow dependency,
    ///     so it is directly unit-testable.
    /// </summary>
    /// <remarks>
    ///     Orphaned: its only consumer — a per-project debug window — was removed because the
    ///     Actions Editor's Live mode already covers the same
    ///     "recent batches" need via <see cref="ConvaiActionsSessionLog" />'s Timeline (which
    ///     additionally survives Play-mode exit as Session Review). Left in place deliberately — it
    ///     is a tested, GUI-free type — rather than deleted on this pass's own judgement; a follow-up
    ///     change should either wire it to a new consumer or remove it (and
    ///     <c>ConvaiActionDebugBatchHistoryTests</c>) together.
    /// </remarks>
    internal sealed class ConvaiActionDebugBatchHistory
    {
        internal const int MaxEntries = 10;

        private readonly List<BatchEntry> _history = new();
        private readonly Func<double> _clock;
        private BatchEntry _current;
        private double _currentStepStartTime;

        /// <summary>
        ///     Creates a batch-history tracker. <paramref name="clock" /> defaults to
        ///     <see cref="EditorApplication.timeSinceStartup" />; tests inject a deterministic clock
        ///     instead.
        /// </summary>
        internal ConvaiActionDebugBatchHistory(Func<double> clock = null) =>
            _clock = clock ?? (() => EditorApplication.timeSinceStartup);

        /// <summary>Completed batches, oldest first, capped at <see cref="MaxEntries" />.</summary>
        internal IReadOnlyList<BatchEntry> Entries => _history;

        /// <summary>The in-progress batch, or null between batches.</summary>
        internal BatchEntry Current => _current;

        /// <summary>
        ///     Call on <c>OnStepStarted</c>: starts a new in-progress batch when
        ///     <paramref name="batchIndex" /> differs from the current one, and records the step
        ///     start time.
        /// </summary>
        internal void BeginStep(int batchIndex)
        {
            if (_current == null || _current.BatchIndex != batchIndex)
                _current = new BatchEntry(batchIndex);

            _currentStepStartTime = _clock();
        }

        /// <summary>
        ///     Call on <c>OnStepCompleted</c>: records one step's outcome and duration since the
        ///     matching <see cref="BeginStep" /> call. No-ops if no batch is in progress (e.g. a
        ///     stray completion with no matching start).
        /// </summary>
        internal void RecordStep(string actionName, ConvaiActionExecutionStatus status, ConvaiActionFailureReason failureReason)
        {
            if (_current == null)
                return;

            double durationMs = (_clock() - _currentStepStartTime) * 1000.0;
            _current.Steps.Add(new StepEntry(actionName, status, failureReason, durationMs));
        }

        /// <summary>
        ///     Call on <c>OnBatchCompleted</c>/<c>OnBatchAborted</c>: moves the in-progress batch
        ///     into <see cref="Entries" /> (trimming to <see cref="MaxEntries" />) and clears it.
        ///     No-ops if no batch is in progress.
        /// </summary>
        internal void FinalizeBatch(bool aborted)
        {
            if (_current == null)
                return;

            _current.Aborted = aborted;
            _history.Add(_current);
            while (_history.Count > MaxEntries)
                _history.RemoveAt(0);

            _current = null;
        }

        /// <summary>One ring-buffer entry: a batch index plus its recorded steps.</summary>
        internal sealed class BatchEntry
        {
            internal BatchEntry(int batchIndex) => BatchIndex = batchIndex;

            internal int BatchIndex { get; }
            internal bool Aborted { get; set; }
            internal List<StepEntry> Steps { get; } = new();
        }

        /// <summary>One recorded step within a <see cref="BatchEntry" />.</summary>
        internal readonly struct StepEntry
        {
            internal StepEntry(
                string actionName,
                ConvaiActionExecutionStatus status,
                ConvaiActionFailureReason failureReason,
                double durationMs)
            {
                ActionName = actionName;
                Status = status;
                FailureReason = failureReason;
                DurationMs = durationMs;
            }

            internal string ActionName { get; }
            internal ConvaiActionExecutionStatus Status { get; }
            internal ConvaiActionFailureReason FailureReason { get; }
            internal double DurationMs { get; }
        }
    }
}
