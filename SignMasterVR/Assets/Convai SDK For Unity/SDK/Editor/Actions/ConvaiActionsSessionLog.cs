using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;

namespace Convai.Editor.Actions
{
    /// <summary>
    ///     Pure ring-buffer aggregation behind <see cref="ConvaiActionsSessionCollector" />:
    ///     per-batch and per-step records of one Play-mode session, plus the composed
    ///     feedback log and test-run correlation. No <c>UnityEditor</c>/GUI dependency (the clock is
    ///     injected), so all aggregation logic is unit-testable; the collector owns event wiring and
    ///     the Live view owns drawing. Ring buffers are hard-capped (<see cref="MaxBatches" /> /
    ///     <see cref="MaxTotalSteps" /> / <see cref="MaxFeedbackEntries" />) so an arbitrarily long
    ///     session can never grow editor memory unbounded.
    /// </summary>
    internal sealed class ConvaiActionsSessionLog
    {
        internal const int MaxBatches = 50;
        internal const int MaxTotalSteps = 500;
        internal const int MaxFeedbackEntries = 50;
        internal const int MaxDropEntries = 50;

        private readonly Func<double> _clock;
        private readonly List<BatchRecord> _batches = new();
        private readonly List<FeedbackRecord> _feedback = new();
        private readonly List<DropRecord> _drops = new();
        private readonly Queue<PendingTestRun> _pendingTestRuns = new();
        private int _nextTestRunToken = 1;
        private int _totalSteps;

        /// <summary>Creates a log with an injectable clock (seconds; tests supply a deterministic one).</summary>
        internal ConvaiActionsSessionLog(Func<double> clock) => _clock = clock ?? (() => 0d);

        /// <summary>Monotonic change counter — consumers rebuild cached view models only when this moves.</summary>
        internal int Version { get; private set; }

        /// <summary>Recorded batches, oldest first (the last entry may still be in progress).</summary>
        internal IReadOnlyList<BatchRecord> Batches => _batches;

        /// <summary>The in-progress batch, or null between batches.</summary>
        internal BatchRecord CurrentBatch { get; private set; }

        /// <summary>Composed feedback entries, oldest first, capped at <see cref="MaxFeedbackEntries" />.</summary>
        internal IReadOnlyList<FeedbackRecord> Feedback => _feedback;

        /// <summary>
        ///     Commands that never ran, oldest first, capped at <see cref="MaxDropEntries" />.
        /// </summary>
        /// <remarks>
        ///     Kept separately from <see cref="Batches" /> because a dropped command never becomes a
        ///     batch: it is discarded before the dispatcher hears about it, which is exactly why this
        ///     window used to show nothing at all for the failure mode hardest to diagnose without it.
        /// </remarks>
        internal IReadOnlyList<DropRecord> Drops => _drops;

        /// <summary>
        ///     Announces that a test run whose first step is <paramref name="firstActionName" /> is
        ///     about to be enqueued. The next batch whose first step matches that name is tagged as
        ///     a test run and carries the returned token, so the Test Run panel can find its own
        ///     batch among conversation traffic.
        /// </summary>
        internal int ExpectTestRun(string firstActionName)
        {
            int token = _nextTestRunToken++;
            _pendingTestRuns.Enqueue(new PendingTestRun(token, firstActionName ?? string.Empty));
            return token;
        }

        /// <summary>Call on the dispatcher's batch-started event.</summary>
        internal void OnBatchStarted()
        {
            CurrentBatch = new BatchRecord { BatchIndex = -1, StartTime = _clock() };
            _batches.Add(CurrentBatch);
            TrimRings();
            Touch();
        }

        /// <summary>Call on the dispatcher's step-started event.</summary>
        internal void OnStepStarted(int batchIndex, int stepIndex, string actionName, string targetName)
        {
            // A stray step with no preceding batch-started (subscription raced the batch) still
            // records instead of being dropped.
            if (CurrentBatch == null)
                OnBatchStarted();

            if (CurrentBatch.BatchIndex < 0)
                CurrentBatch.BatchIndex = batchIndex;

            if (stepIndex == 0 && _pendingTestRuns.Count > 0 &&
                string.Equals(_pendingTestRuns.Peek().FirstActionName, actionName ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                PendingTestRun pending = _pendingTestRuns.Dequeue();
                CurrentBatch.IsTestRun = true;
                CurrentBatch.TestRunToken = pending.Token;
            }

            CurrentBatch.Steps.Add(new StepRecord
            {
                ActionName = actionName ?? string.Empty,
                TargetName = targetName ?? string.Empty,
                StartTime = _clock()
            });
            _totalSteps++;
            TrimRings();
            Touch();
        }

        /// <summary>Call on the dispatcher's step-completed event.</summary>
        internal void OnStepCompleted(
            ConvaiActionExecutionStatus status,
            ConvaiActionFailureReason failureReason,
            string failureMessage)
        {
            StepRecord step = FindLastRunningStep();
            if (step == null)
                return;

            step.EndTime = _clock();
            step.Completed = true;
            step.Status = status;
            step.FailureReason = failureReason;
            step.FailureMessage = failureMessage ?? string.Empty;
            Touch();
        }

        /// <summary>Call on the dispatcher's batch-completed/batch-aborted events.</summary>
        internal void OnBatchFinished(bool aborted)
        {
            if (CurrentBatch == null)
                return;

            CurrentBatch.Finished = true;
            CurrentBatch.Aborted = aborted;
            CurrentBatch.EndTime = _clock();
            CurrentBatch = null;
            Touch();
        }

        /// <summary>Call on the feedback relay's composed event (time label pre-formatted by the caller).</summary>
        internal void OnFeedback(string timeLabel, string fact, bool narrated)
        {
            _feedback.Add(new FeedbackRecord(timeLabel ?? string.Empty, fact ?? string.Empty, narrated));
            while (_feedback.Count > MaxFeedbackEntries)
                _feedback.RemoveAt(0);
            Touch();
        }

        /// <summary>Call when the response filter reports commands it dropped before they could run.</summary>
        internal void OnCommandDropped(string timeLabel, string actionName, string requestedTarget, string explanation)
        {
            _drops.Add(new DropRecord(
                timeLabel ?? string.Empty,
                actionName ?? string.Empty,
                requestedTarget ?? string.Empty,
                explanation ?? string.Empty));
            while (_drops.Count > MaxDropEntries)
                _drops.RemoveAt(0);
            Touch();
        }

        /// <summary>Resets everything (fresh Play-mode session).</summary>
        internal void Clear()
        {
            _batches.Clear();
            _feedback.Clear();
            _drops.Clear();
            _pendingTestRuns.Clear();
            CurrentBatch = null;
            _totalSteps = 0;
            Touch();
        }

        /// <summary>Finds the batch tagged with a test-run token, newest first; null when not (yet) recorded.</summary>
        internal BatchRecord FindByToken(int token)
        {
            if (token <= 0)
                return null;

            for (int i = _batches.Count - 1; i >= 0; i--)
            {
                if (_batches[i].IsTestRun && _batches[i].TestRunToken == token)
                    return _batches[i];
            }

            return null;
        }

        private StepRecord FindLastRunningStep()
        {
            BatchRecord batch = CurrentBatch ?? (_batches.Count > 0 ? _batches[^1] : null);
            if (batch == null)
                return null;

            for (int i = batch.Steps.Count - 1; i >= 0; i--)
            {
                if (!batch.Steps[i].Completed)
                    return batch.Steps[i];
            }

            return null;
        }

        private void TrimRings()
        {
            while (_batches.Count > 0 &&
                   (_batches.Count > MaxBatches || (_totalSteps > MaxTotalSteps && _batches.Count > 1)))
            {
                BatchRecord oldest = _batches[0];
                if (ReferenceEquals(oldest, CurrentBatch) && _batches.Count == 1)
                    break; // Never drop the only, still-running batch.

                _totalSteps -= oldest.Steps.Count;
                _batches.RemoveAt(0);
                if (ReferenceEquals(oldest, CurrentBatch))
                    CurrentBatch = null;
            }
        }

        private void Touch() => Version++;

        /// <summary>One recorded batch: its dispatcher index, timing, source, and steps.</summary>
        internal sealed class BatchRecord
        {
            internal int BatchIndex;
            internal double StartTime;
            internal double EndTime;
            internal bool Finished;
            internal bool Aborted;

            /// <summary>True when this batch was started from the editor's Test Run panel.</summary>
            internal bool IsTestRun;

            /// <summary>The <see cref="ExpectTestRun" /> token when <see cref="IsTestRun" />; 0 otherwise.</summary>
            internal int TestRunToken;

            internal List<StepRecord> Steps { get; } = new();
        }

        /// <summary>One recorded step: names, timing, and outcome.</summary>
        internal sealed class StepRecord
        {
            internal string ActionName;
            internal string TargetName;
            internal double StartTime;
            internal double EndTime;
            internal bool Completed;
            internal ConvaiActionExecutionStatus Status;
            internal ConvaiActionFailureReason FailureReason;
            internal string FailureMessage = string.Empty;

            /// <summary>Step duration in milliseconds; only meaningful once <see cref="Completed" />.</summary>
            internal double DurationMs => (EndTime - StartTime) * 1000d;
        }

        /// <summary>One composed-feedback entry.</summary>
        internal readonly struct FeedbackRecord
        {
            internal FeedbackRecord(string timeLabel, string fact, bool narrated)
            {
                TimeLabel = timeLabel;
                Fact = fact;
                Narrated = narrated;
            }

            internal string TimeLabel { get; }
            internal string Fact { get; }
            internal bool Narrated { get; }
        }

        /// <summary>One command that was discarded before it could run.</summary>
        internal readonly struct DropRecord
        {
            internal DropRecord(string timeLabel, string actionName, string requestedTarget, string explanation)
            {
                TimeLabel = timeLabel;
                ActionName = actionName;
                RequestedTarget = requestedTarget;
                Explanation = explanation;
            }

            internal string TimeLabel { get; }

            /// <summary>The action that was asked for, empty when the entry could not be read at all.</summary>
            internal string ActionName { get; }

            /// <summary>What it asked to act on, empty when it named nothing.</summary>
            internal string RequestedTarget { get; }

            /// <summary>The whole story in one sentence, ending in what to do about it.</summary>
            internal string Explanation { get; }
        }

        private readonly struct PendingTestRun
        {
            internal PendingTestRun(int token, string firstActionName)
            {
                Token = token;
                FirstActionName = firstActionName;
            }

            internal int Token { get; }
            internal string FirstActionName { get; }
        }
    }
}
