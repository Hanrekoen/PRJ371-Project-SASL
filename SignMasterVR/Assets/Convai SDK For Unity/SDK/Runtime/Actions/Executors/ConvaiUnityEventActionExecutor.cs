using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Runs whatever you wire into its event when the character performs the action, then reports
    ///     success. This is the no-code path: anything your scene can already do from a button — open
    ///     a door, start a timeline, award a point — becomes something the character can do, without
    ///     writing an Action Behavior of your own.
    /// </summary>
    /// <remarks>
    ///     Write your own Action Behavior instead when the action needs to read parameters, take
    ///     time, be cancelled, or report why it could not run — a Unity event cannot express any of
    ///     those. See <c>Documentation~/ACTIONS-EXTENDING.md</c>.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Raise Unity Event")]
    [ConvaiActionArchetype(
        "Raise Unity Event",
        ActionName = "Raise Unity Event",
        Description = "Runs the scene logic wired into this behavior's event, then succeeds. " +
                      "The no-code way to let the character trigger something your scene already does.",
        TargetRequirement = ConvaiActionTargetRequirement.None)]
    public sealed class ConvaiUnityEventActionExecutor : ConvaiActionExecutorBase
    {
        /// <summary>
        ///     Name of the serialized event field. Editor and assistant tooling reflects on this to
        ///     detect a placeholder behavior nobody has wired up yet; keeping the literal in one place
        ///     means renaming the field cannot silently break that detection.
        /// </summary>
        internal const string EventFieldName = "_onExecute";

        [SerializeField]
        [Tooltip("Runs once each time the character performs this action. Drop in any scene object " +
                 "and pick the method to call — the same way you would wire a button.")]
        private UnityEvent _onExecute;

        /// <summary>Raises the authored event and completes immediately with success.</summary>
        public override Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            _onExecute?.Invoke();
            return Task.FromResult(ConvaiActionExecutionResult.Succeeded());
        }
    }
}
