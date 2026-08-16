using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Runs several Action Behaviors one after another as a single action, so "greet the visitor"
    ///     can mean look at them, nod, and say hello — authored in the Inspector, with no code.
    ///     Every step receives the same invocation and the same cancellation, so parameters the
    ///     character sent reach all of them and stopping the action stops the whole chain.
    /// </summary>
    /// <remarks>
    ///     The sequence stops at the first step that does not succeed and reports that step's own
    ///     result, prefixed with its position — so a failure names which step failed and why, instead
    ///     of a generic "sequence failed".
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Run In Order")]
    [ConvaiActionArchetype(
        "Run In Order",
        ActionName = "Run In Order",
        Description = "Runs several Action Behaviors one after another as a single action, stopping at " +
                      "the first step that does not succeed.",
        TargetRequirement = ConvaiActionTargetRequirement.None)]
    public sealed class ConvaiSequenceActionExecutor : ConvaiTargetedActionExecutor
    {
        [SerializeField]
        [Tooltip("The Action Behaviors to run, top to bottom. Each one receives this action's own " +
                 "target and parameters. An entry that is empty, or is not an Action Behavior, stops " +
                 "the sequence and reports its position.")]
        private List<MonoBehaviour> _steps = new();

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (_steps == null || _steps.Count == 0)
                return ConvaiActionExecutionResult.Succeeded("No steps to run.");

            for (int i = 0; i < _steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                MonoBehaviour step = _steps[i];
                if (step == null || step is not IConvaiActionExecutor executor)
                {
                    return ConvaiActionExecutionResult.Failed(
                        $"Step {i + 1} is empty or is not an Action Behavior.",
                        ConvaiActionFailureReason.InvalidState);
                }

                if (ReferenceEquals(step, this))
                {
                    return ConvaiActionExecutionResult.Failed(
                        $"Step {i + 1} is this same behavior, which would run forever.",
                        ConvaiActionFailureReason.InvalidState);
                }

                ConvaiActionExecutionResult result = await executor.ExecuteAsync(invocation, cancellationToken);
                if (result.Status != ConvaiActionExecutionStatus.Succeeded)
                    return AnnotateWithPosition(result, i);
            }

            return ConvaiActionExecutionResult.Succeeded();
        }

        private static ConvaiActionExecutionResult AnnotateWithPosition(ConvaiActionExecutionResult result, int index)
        {
            int position = index + 1;
            string message = string.IsNullOrEmpty(result.Message)
                ? $"Step {position} did not succeed ({result.Status})."
                : $"Step {position}: {result.Message}";

            return result.Status switch
            {
                ConvaiActionExecutionStatus.Failed =>
                    ConvaiActionExecutionResult.Failed(message, result.FailureReason, result.Exception),
                ConvaiActionExecutionStatus.Unhandled => ConvaiActionExecutionResult.Unhandled(message),
                _ => result
            };
        }
    }
}
