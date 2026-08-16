using System.Threading.Tasks;
using Convai.Domain.Embodiment.Interfaces;

namespace Convai.Modules.BodyLanguage.Components
{
    /// <summary>
    ///     Live handle for one scripted <see cref="ConvaiBodyLanguageController.PulseGesture" />
    ///     request. Await <see cref="Completion" /> to know when the cue's dispatch outcome is
    ///     known (accepted for performance, or refused/substituted) — see
    ///     <see cref="ConvaiBodyLanguageController.PulseGesture" /> for the exact contract. Call
    ///     <see cref="Release" /> to give up interest early. All operations are idempotent and
    ///     never throw.
    /// </summary>
    /// <remarks>
    ///     Mirrors <c>Convai.Modules.Gaze.Components.GazeHandle</c>'s recipe: a
    ///     <see cref="TaskCompletionSource{TResult}" /> with
    ///     <see cref="TaskCreationOptions.RunContinuationsAsynchronously" />, resolved with
    ///     <c>TrySetResult</c> so a double release/complete never throws.
    /// </remarks>
    public sealed class GestureCueHandle
    {
        private readonly ConvaiBodyLanguageController _owner;
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int RequestId { get; }

        /// <summary>The requested cue kind.</summary>
        public GestureCueKind Kind { get; }

        /// <summary>
        ///     Whether the request is still awaiting its dispatch outcome. Always <c>false</c>
        ///     immediately for a refused or substituted cue. The handle tracks the dispatch
        ///     outcome, not the resulting clip through to its visual end — see
        ///     <see cref="ConvaiBodyLanguageController.PulseGesture" />.
        /// </summary>
        public bool IsActive { get; internal set; } = true;

        /// <summary>
        ///     Completes as soon as the cue's dispatch outcome is known (accepted for
        ///     performance, or refused/substituted). Does NOT track the clip to its visual end.
        /// </summary>
        public Task Completion => _completion.Task;

        internal GestureCueHandle(ConvaiBodyLanguageController owner, int requestId, GestureCueKind kind)
        {
            _owner = owner;
            RequestId = requestId;
            Kind = kind;
        }

        /// <summary>Gives up interest in this request. Safe to call multiple times.</summary>
        public void Release() => _owner?.ReleaseGestureCueHandle(this);

        internal void MarkCompleted()
        {
            _completion.TrySetResult(true);
            IsActive = false;
        }
    }
}
