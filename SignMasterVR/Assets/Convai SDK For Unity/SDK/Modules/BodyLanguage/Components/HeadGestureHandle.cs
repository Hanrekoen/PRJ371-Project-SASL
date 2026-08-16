using System.Threading.Tasks;
using Convai.Domain.Embodiment.Interfaces;

namespace Convai.Modules.BodyLanguage.Components
{
    /// <summary>
    ///     Live handle for one scripted <see cref="ConvaiBodyLanguageController.Nod" /> request.
    ///     Await <see cref="Completion" /> to know when the head-gesture program has ended (or
    ///     was superseded/cleared), and call <see cref="Release" /> to give up interest in it
    ///     early. All operations are idempotent and never throw.
    /// </summary>
    /// <remarks>
    ///     Mirrors <c>Convai.Modules.Gaze.Components.GazeHandle</c>'s recipe: a
    ///     <see cref="TaskCompletionSource{TResult}" /> with
    ///     <see cref="TaskCreationOptions.RunContinuationsAsynchronously" />, resolved with
    ///     <c>TrySetResult</c> so a double release/complete never throws.
    /// </remarks>
    /// <summary>Why a scripted head-gesture request did not become a live program.</summary>
    /// <remarks>
    ///     The two refusals need opposite responses, so they cannot share an answer. A character
    ///     whose rig cannot nod will never nod, and saying so is the only useful reply. A character
    ///     that is mid-nod will be free in under a second, and telling its author to go and check
    ///     the rig sends them looking for a fault that is not there.
    /// </remarks>
    public enum HeadGestureRefusal
    {
        /// <summary>The request was accepted; the handle represents a live program.</summary>
        None = 0,

        /// <summary>
        ///     The character is already performing a head gesture and has one more queued behind
        ///     it. Transient — the same request a moment later normally succeeds.
        /// </summary>
        Busy = 1,

        /// <summary>
        ///     The character cannot perform head gestures at all right now: no usable rig, no
        ///     Body Language profile, or the component is disabled or not playing.
        /// </summary>
        Unavailable = 2
    }

    public sealed class HeadGestureHandle
    {
        private readonly ConvaiBodyLanguageController _owner;
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int RequestId { get; }

        /// <summary>The requested gesture kind.</summary>
        public HeadGestureKind Kind { get; }

        /// <summary>Whether the request is still live (not yet completed, superseded, or cleared).</summary>
        public bool IsActive { get; internal set; } = true;

        /// <summary>
        ///     Why the request was refused, on a handle that was never live. Always
        ///     <see cref="HeadGestureRefusal.None" /> on an accepted request, including after it
        ///     has finished — this says why a gesture never started, not that it has ended.
        /// </summary>
        public HeadGestureRefusal Refusal { get; }

        /// <summary>
        ///     Completes when the head-gesture program this handle represents ends — the
        ///     program finished playing, was superseded, or was cleared by
        ///     <see cref="ConvaiBodyLanguageController.ClearScriptedOverrides" />. A refused
        ///     request's handle is already completed at construction time.
        /// </summary>
        public Task Completion => _completion.Task;

        internal HeadGestureHandle(
            ConvaiBodyLanguageController owner,
            int requestId,
            HeadGestureKind kind,
            HeadGestureRefusal refusal = HeadGestureRefusal.None)
        {
            _owner = owner;
            RequestId = requestId;
            Kind = kind;
            Refusal = refusal;
        }

        /// <summary>Gives up interest in this request. Safe to call multiple times.</summary>
        public void Release() => _owner?.ReleaseHeadGestureHandle(this);

        internal void MarkCompleted()
        {
            _completion.TrySetResult(true);
            IsActive = false;
        }
    }
}
