using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Modules.BodyAnimation
{
    /// <summary>Optional playback tweaks for <c>ConvaiBodyAnimationController.PlayAction</c>.</summary>
    public struct ActionPlayOptions
    {
        /// <summary>Playback speed multiplier on top of the entry's speed. ≤0 = entry default.</summary>
        public float SpeedMultiplier;

        /// <summary>
        ///     For hold-until-stopped actions: automatically request the stop after this many
        ///     seconds of the main loop. ≤0 = hold until <c>StopAction</c>/handle stop.
        /// </summary>
        public float HoldSeconds;

        /// <summary>Layer blend-in seconds override. &lt;=0 = the entry override / config default.</summary>
        public float FadeInSeconds;

        /// <summary>Layer blend-out seconds override (also used by StopImmediate). &lt;=0 = entry / config default.</summary>
        public float FadeOutSeconds;

        /// <summary>Action layer weight multiplier. Values &lt;= 0 preserve existing behavior.</summary>
        public float WeightMultiplier;
    }

    /// <summary>What an elapsed <see cref="PointingPlayOptions.HoldSeconds" /> auto-release does.</summary>
    public enum PointingReleaseStyle
    {
        /// <summary>Play the lower-arm tail before the layer fades out (default, original behavior).</summary>
        PlayTail = 0,

        /// <summary>Cross-dissolve the current pose out immediately, skipping the lower-arm tail.</summary>
        Blend = 1
    }

    /// <summary>Optional playback tweaks for <c>ConvaiBodyAnimationController.PointAt</c>.</summary>
    public struct PointingPlayOptions
    {
        /// <summary>Raise/lower speed multiplier. &lt;=0 = native (1). Hold is unaffected.</summary>
        public float Speed;

        /// <summary>Seconds to hold at the apex. &lt;=0 = hold until released (default-safe).</summary>
        public float HoldSeconds;

        /// <summary>Layer blend-in seconds. &lt;=0 = config PointingFadeSeconds.</summary>
        public float BlendInSeconds;

        /// <summary>Layer blend-out seconds. &lt;=0 = config PointingFadeSeconds.</summary>
        public float BlendOutSeconds;

        /// <summary>What an elapsed HoldSeconds auto-release does: play the lower tail, or blend out.</summary>
        public PointingReleaseStyle ReleaseStyle;

        /// <summary>Pointing layer weight multiplier. Values &lt;= 0 preserve existing behavior.</summary>
        public float WeightMultiplier;

        public static PointingPlayOptions Default => new()
        {
            Speed = 1f, HoldSeconds = -1f, BlendInSeconds = -1f, BlendOutSeconds = -1f,
            ReleaseStyle = PointingReleaseStyle.PlayTail, WeightMultiplier = 1f
        };
    }

    /// <summary>Lifecycle stages reported for every action/gesture playback.</summary>
    public enum BodyAnimationActionPhase
    {
        Started = 0,
        Ending = 1,
        Completed = 2,
        Interrupted = 3,
        Rejected = 4
    }

    /// <summary>One action lifecycle notification (mirrors the trace log).</summary>
    public readonly struct BodyAnimationActionEvent
    {
        public string ActionName { get; }
        public BodyAnimationActionPhase Phase { get; }

        public BodyAnimationActionEvent(string actionName, BodyAnimationActionPhase phase)
        {
            ActionName = actionName ?? string.Empty;
            Phase = phase;
        }

        public override string ToString() => $"action '{ActionName}' {Phase}";
    }

    /// <summary>
    ///     Live handle for a running action. Await <see cref="Completion" /> (true = played
    ///     to completion, false = interrupted/replaced) or call <see cref="Stop" /> for a
    ///     graceful finish (outro plays when the entry has one).
    /// </summary>
    public sealed class BodyAnimationActionHandle
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Action StopRequested;
        internal Action<float> StopImmediateRequested;

        public string ActionName { get; }

        /// <summary>
        ///     True when this handle represents a request that never started (runtime not
        ///     built, unknown action, etc.) — see <see cref="FailureReason" /> for why. A
        ///     failed handle is already completed: <see cref="Completion" /> resolves to
        ///     <c>false</c> immediately and <see cref="Stop" />/<see cref="StopImmediate" />
        ///     are safe no-ops. <c>PlayAction</c> never returns null, so callers detect
        ///     failure through this flag instead of a null check.
        /// </summary>
        public bool Failed { get; }

        /// <summary>Why the request failed; empty when <see cref="Failed" /> is false.</summary>
        public string FailureReason { get; }

        /// <summary>True once the action fully finished or was interrupted.</summary>
        public bool IsDone => _completion.Task.IsCompleted;

        /// <summary>Resolves true when played to completion, false when interrupted.</summary>
        public Task<bool> Completion => _completion.Task;

        internal BodyAnimationActionHandle(string actionName) : this(actionName, false, null)
        {
        }

        private BodyAnimationActionHandle(string actionName, bool failed, string failureReason)
        {
            ActionName = actionName ?? string.Empty;
            Failed = failed;
            FailureReason = failureReason ?? string.Empty;
            if (failed) _completion.TrySetResult(false);
        }

        /// <summary>Creates an already-completed, already-failed handle for a request that never started.</summary>
        internal static BodyAnimationActionHandle CreateFailed(string actionName, string failureReason) =>
            new(actionName, true, failureReason);

        /// <summary>Requests a graceful stop (loop ends, outro plays). Safe to call repeatedly.</summary>
        public void Stop()
        {
            if (IsDone) return;
            StopRequested?.Invoke();
        }

        /// <summary>Immediately stops and cross-dissolves the action out over blendOutSeconds
        /// (&lt;=0 = the action's resolved fade-out), skipping the remaining chain/outro.
        /// <see cref="Completion" /> resolves false (interrupted). Safe to call repeatedly.</summary>
        public void StopImmediate(float blendOutSeconds = -1f)
        {
            if (IsDone) return;
            StopImmediateRequested?.Invoke(blendOutSeconds);
        }

        internal void ResolveCompleted() => _completion.TrySetResult(true);
        internal void ResolveInterrupted() => _completion.TrySetResult(false);
    }

    /// <summary>
    ///     Live handle for a pointing gesture. The arm raises, holds at the apex (re-aiming
    ///     if the target moves), and lowers on <see cref="Release" /> or when the requested
    ///     hold time elapses; <see cref="Completion" /> resolves after the lower-arm tail.
    /// </summary>
    public sealed class BodyAnimationPointingHandle
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Action ReleaseRequested;
        internal Action<float> ReleaseImmediateRequested;
        internal Action<float> SpeedChangeRequested;

        /// <summary>
        ///     True when this handle represents a request that never started (runtime not
        ///     built, target/anchor null, set has no pointing content, etc.) — see
        ///     <see cref="FailureReason" /> for why. A failed handle is already completed:
        ///     <see cref="Completion" /> resolves immediately and <see cref="Release" />/
        ///     <see cref="ReleaseImmediate" /> are safe no-ops. <c>PointAt</c> never
        ///     returns null, so callers detect failure through this flag instead of a null
        ///     check.
        /// </summary>
        public bool Failed { get; }

        /// <summary>Why the request failed; empty when <see cref="Failed" /> is false.</summary>
        public string FailureReason { get; }

        internal BodyAnimationPointingHandle() : this(false, null)
        {
        }

        private BodyAnimationPointingHandle(bool failed, string failureReason)
        {
            Failed = failed;
            FailureReason = failureReason ?? string.Empty;
            if (failed) _completion.TrySetResult(true);
        }

        /// <summary>Creates an already-completed, already-failed handle for a request that never started.</summary>
        internal static BodyAnimationPointingHandle CreateFailed(string failureReason) =>
            new(true, failureReason);

        /// <summary>True once the point gesture fully finished (arm lowered).</summary>
        public bool IsDone => _completion.Task.IsCompleted;

        public Task Completion => _completion.Task;

        /// <summary>Ends the hold now; the lower-arm tail plays before completion.</summary>
        public void Release()
        {
            if (IsDone) return;
            ReleaseRequested?.Invoke();
        }

        /// <summary>Stops now and cross-dissolves the pose out over blendOutSeconds (&lt;=0 = the call/config default), skipping the lower-arm tail.</summary>
        public void ReleaseImmediate(float blendOutSeconds = -1f)
        {
            if (IsDone) return;
            ReleaseImmediateRequested?.Invoke(blendOutSeconds);
        }

        /// <summary>Live-adjust the raise/lower speed of the running gesture. No-op while holding.</summary>
        public void SetSpeed(float speed)
        {
            if (IsDone) return;
            SpeedChangeRequested?.Invoke(speed);
        }

        internal void Resolve() => _completion.TrySetResult(true);
    }

    /// <summary>Lifecycle phase of a running <c>PlayActionAt</c> request.</summary>
    public enum PlayActionAtPhase
    {
        /// <summary>MoveTo has been issued; the character is walking to the anchor's approach point.</summary>
        Approaching = 0,

        /// <summary>Arrived and settled; the root is being lerped into precise alignment with the anchor.</summary>
        Aligning = 1,

        /// <summary>The anchored action itself is playing.</summary>
        PlayingAction = 2,

        /// <summary>The request finished — the action played to its natural end.</summary>
        Completed = 3,

        /// <summary>The request was canceled (or failed) before finishing.</summary>
        Canceled = 4
    }

    /// <summary>
    ///     Live handle for a <c>ConvaiBodyAnimationController.PlayActionAt</c> request:
    ///     MoveTo the anchor → root-align (position + yaw, lerped) → play the action. Await
    ///     <see cref="Completion" /> (true = the action played to completion, false = the
    ///     request was canceled or refused at any phase) or call <see cref="Cancel" /> to
    ///     abort — idempotent, safe to call from any phase, safe to call repeatedly.
    /// </summary>
    public sealed class PlayActionAtHandle
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Action CancelRequested;

        public string ActionName { get; }

        /// <summary>Current phase of the request.</summary>
        public PlayActionAtPhase Phase { get; private set; } = PlayActionAtPhase.Approaching;

        /// <summary>
        ///     True when this handle represents a request that never started (runtime not
        ///     built, anchor null, unknown action, no locomotion, etc.) — see
        ///     <see cref="FailureReason" /> for why. A failed handle is already completed
        ///     (<see cref="Phase" /> is <see cref="PlayActionAtPhase.Canceled" />):
        ///     <see cref="Completion" /> resolves to <c>false</c> immediately and
        ///     <see cref="Cancel" /> is a safe no-op. <c>PlayActionAt</c> never returns
        ///     null, so callers detect failure through this flag instead of a null check.
        /// </summary>
        public bool Failed { get; }

        /// <summary>Why the request failed; empty when <see cref="Failed" /> is false.</summary>
        public string FailureReason { get; }

        /// <summary>True once the request finished or was canceled.</summary>
        public bool IsDone => _completion.Task.IsCompleted;

        /// <summary>Resolves true when the action played to completion, false when canceled.</summary>
        public Task<bool> Completion => _completion.Task;

        internal PlayActionAtHandle(string actionName) : this(actionName, false, null)
        {
        }

        private PlayActionAtHandle(string actionName, bool failed, string failureReason)
        {
            ActionName = actionName ?? string.Empty;
            Failed = failed;
            FailureReason = failureReason ?? string.Empty;
            if (failed)
            {
                Phase = PlayActionAtPhase.Canceled;
                _completion.TrySetResult(false);
            }
        }

        /// <summary>Creates an already-completed, already-failed handle for a request that never started.</summary>
        internal static PlayActionAtHandle CreateFailed(string actionName, string failureReason) =>
            new(actionName, true, failureReason);

        /// <summary>
        ///     Cancels the request wherever it currently is: stops locomotion during
        ///     Approaching, freezes the alignment lerp in place during Aligning, or gracefully
        ///     stops the action during PlayingAction. Idempotent — safe to call repeatedly or
        ///     after the request already finished.
        /// </summary>
        public void Cancel()
        {
            if (IsDone) return;
            CancelRequested?.Invoke();
        }

        internal void SetPhase(PlayActionAtPhase phase) => Phase = phase;
        internal void ResolveCompleted() => _completion.TrySetResult(true);
        internal void ResolveCanceled() => _completion.TrySetResult(false);
    }
}
