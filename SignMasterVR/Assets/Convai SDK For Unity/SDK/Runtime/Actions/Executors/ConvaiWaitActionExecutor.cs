using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Pauses for a few seconds and does nothing else. On its own it is rarely useful; inside
    ///     <see cref="ConvaiSequenceActionExecutor" /> it is what gives a performance its timing —
    ///     "point at the door, wait a beat, then walk to it".
    /// </summary>
    /// <remarks>
    ///     The wait is frame-wise and cancellable, and both the Inspector value and any value the
    ///     backend sends are clamped by <c>Longest allowed wait</c>, so a runaway number can never
    ///     leave the character standing still indefinitely.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Wait")]
    [ConvaiActionArchetype(
        "Wait",
        ActionName = "Wait",
        Description = "Pauses for a few seconds before the next action runs. Used to pace a sequence.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        Parameters = new[] { "seconds,Number" },
        ParameterDescriptions = new[]
        {
            "How many seconds to pause before the next action. Use a short positive number."
        })]
    public sealed class ConvaiWaitActionExecutor : ConvaiTargetedActionExecutor
    {
        [SerializeField]
        [Min(0f)]
        [Tooltip("How long to wait, in seconds. The character can ask for a different length per " +
                 "call with the 'seconds' parameter.")]
        private float _seconds = 1f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Longest allowed wait, in seconds. Applied to both the value above and anything the " +
                 "character asks for, so one bad number cannot stall the scene.")]
        private float _maxSeconds = 30f;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            float maxSeconds = Mathf.Max(0f, _maxSeconds);
            float seconds = Mathf.Clamp(GetOverride(invocation, "seconds", _seconds), 0f, maxSeconds);

            await ConvaiActionAsyncUtility.WaitSecondsAsync(seconds, cancellationToken);
            return ConvaiActionExecutionResult.Succeeded();
        }

        /// <summary>
        ///     Keeps both durations non-negative. The <c>Min</c> attributes above only constrain the
        ///     Inspector's own drag/slider UI — a script, a serialized-object edit, or a stale asset
        ///     can still write anything, so the clamp has to exist here as well.
        /// </summary>
        private void OnValidate()
        {
            _seconds = Mathf.Max(0f, _seconds);
            _maxSeconds = Mathf.Max(0f, _maxSeconds);
        }
    }
}
