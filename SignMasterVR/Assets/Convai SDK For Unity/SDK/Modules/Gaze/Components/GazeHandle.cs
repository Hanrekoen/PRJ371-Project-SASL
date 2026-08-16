using System.Threading.Tasks;

namespace Convai.Modules.Gaze.Components
{
    /// <summary>Options for a scripted <see cref="ConvaiGazeController.GazeAt(UnityEngine.Transform, GazeOptions)" /> request.</summary>
    public struct GazeOptions
    {
        /// <summary>
        ///     Priority among scripted requests (higher wins; recency breaks ties).
        ///     Scripted requests always outrank automatic targets.
        /// </summary>
        public int Priority;

        /// <summary>
        ///     Hold duration in seconds measured from the request. Values &lt;= 0 hold until
        ///     <see cref="GazeHandle.Release" /> is called.
        /// </summary>
        public float HoldSeconds;

        /// <summary>
        ///     Engagement override in <c>(0, 1]</c>. Values &lt;= 0 use the current dialogue
        ///     state's engagement — note that Idle defaults to 0, so pass an explicit value
        ///     (e.g. 1) when the gaze must land regardless of conversation state.
        /// </summary>
        public float Engagement;

        /// <summary>Whether this request may trigger a full-body turn toward the target.</summary>
        public bool AllowBodyTurn;
    }

    /// <summary>
    ///     Live handle for one scripted gaze request. Await <see cref="Settled" /> to know
    ///     when the character is visibly looking at the target (action executors gate on
    ///     this before proceeding), await <see cref="Completion" /> for the end of the hold,
    ///     and call <see cref="Release" /> to end the request early. All operations are
    ///     idempotent and cancellation-safe.
    /// </summary>
    /// <summary>Why a gaze request never reached its target.</summary>
    /// <remarks>
    ///     <see cref="GazeHandle.Settled" /> answering <c>false</c> covers outcomes that mean
    ///     opposite things. A look that was pushed aside by something else went wrong; a glance the
    ///     character deliberately did not take, because it is holding eye contact with the person it
    ///     is talking to, went exactly right. Reporting both as "something took the character's
    ///     attention" turns the second into a failure that aborts whatever came after it.
    /// </remarks>
    public enum GazeOutcome
    {
        /// <summary>The gaze request is live, or it arrived on its target.</summary>
        Taken = 0,

        /// <summary>
        ///     The request ended before the gaze arrived — released, expired, superseded by a
        ///     higher-priority look, or its target was destroyed.
        /// </summary>
        Interrupted = 1,

        /// <summary>
        ///     The character is deliberately holding eye contact and this glance was folded into
        ///     it rather than taken, because
        ///     <see cref="ConvaiGazeController.LockBlocksGlances" /> is on. Nothing went wrong:
        ///     the character chose the person over the thing.
        /// </summary>
        HeldEyeContactInstead = 2
    }

    public sealed class GazeHandle
    {
        private readonly ConvaiGazeController _owner;
        private readonly TaskCompletionSource<bool> _settled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int EntryId { get; }

        /// <summary>Display name of the requested target (diagnostics).</summary>
        public string TargetName { get; }

        /// <summary>Whether the request is still live (not released, expired, or superseded by destroy).</summary>
        public bool IsActive { get; internal set; } = true;

        /// <summary>
        ///     Completes with <c>true</c> once gaze is aligned on the target, or <c>false</c>
        ///     when the request ended before alignment (released, expired, target destroyed).
        ///     Alignment requires three consecutive post-expression frames inside a two-axis
        ///     contact tolerance. Head-only rigs use head-facing alignment so they never hang.
        /// </summary>
        public Task<bool> Settled => _settled.Task;

        /// <summary>Completes when the request ends (hold elapsed, released, or target lost).</summary>
        public Task Completion => _completion.Task;

        /// <summary>
        ///     What became of the request. <see cref="GazeOutcome.Taken" /> until something says
        ///     otherwise, so a live or arrived request never reads as a problem.
        /// </summary>
        public GazeOutcome Outcome { get; internal set; } = GazeOutcome.Taken;

        internal GazeHandle(ConvaiGazeController owner, int entryId, string targetName)
        {
            _owner = owner;
            EntryId = entryId;
            TargetName = targetName;
        }

        /// <summary>Ends the request. Safe to call multiple times and from cancellation callbacks.</summary>
        public void Release() => _owner?.ReleaseGaze(this);

        internal void MarkSettled(bool aligned) => _settled.TrySetResult(aligned);

        internal void MarkCompleted()
        {
            // Only a request that never arrived has an outcome to explain. One that settled on its
            // target and is now simply over stays Taken — ending is not a failure mode.
            if (!_settled.Task.IsCompleted || _settled.Task.Result == false)
            {
                if (Outcome == GazeOutcome.Taken)
                    Outcome = GazeOutcome.Interrupted;
            }

            _settled.TrySetResult(false);
            _completion.TrySetResult(true);
            IsActive = false;
        }
    }
}
