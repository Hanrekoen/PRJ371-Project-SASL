using System.Threading;
using System.Threading.Tasks;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Executes one resolved action invocation. Implement on a MonoBehaviour and assign it as a
    ///     <see cref="ConvaiActionDefinition.Executor" /> to bind gameplay code to a backend action.
    /// </summary>
    public interface IConvaiActionExecutor
    {
        /// <summary>
        ///     Runs the action. Return <see cref="ConvaiActionExecutionResult.Unhandled" /> when the component
        ///     cannot service the invocation (missing rig or peer) so the dispatcher can report it distinctly;
        ///     honor <paramref name="cancellationToken" /> for batch replacement and timeouts.
        /// </summary>
        Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken);
    }
}
