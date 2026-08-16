using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Base class for an Action Behavior that can only work through one specific component on the
    ///     character — a controller, a locomotion driver, or a rig. It owns the whole "find that
    ///     component, remember it, and say something useful when it is missing" flow, so a behavior
    ///     built on it starts at the interesting part: what to do once the component is there.
    /// </summary>
    /// <typeparam name="TPeer">
    ///     The component this behavior needs on the character (for example a gaze controller or a
    ///     NavMesh locomotion driver). It is looked for on this GameObject's parents first, then its
    ///     children, matching how Convai character hierarchies are usually laid out.
    /// </typeparam>
    /// <remarks>
    ///     <para>
    ///         A missing component is reported as <see cref="ConvaiActionExecutionResult.Unhandled" />
    ///         — a soft decline, not a failure — and logged exactly once per component instance, so a
    ///         setup mistake surfaces the first time it matters without filling the console on every
    ///         later call.
    ///     </para>
    ///     <para>
    ///         Derive from <see cref="ConvaiTargetedActionExecutor" /> instead when the behavior acts
    ///         on the resolved target and needs nothing special from the character, and from
    ///         <see cref="ConvaiActionExecutorBase" /> when you want full manual control.
    ///     </para>
    /// </remarks>
    public abstract class ConvaiCharacterActionExecutor<TPeer> : ConvaiTargetedActionExecutor
        where TPeer : Component
    {
        [SerializeField]
        [Tooltip("The component on the character this action works through. Leave this empty to have " +
                 "it found automatically on this character — assign it only when the character has " +
                 "more than one and you need a specific one.")]
        private TPeer _characterComponent;

        /// <summary>
        ///     Runs the action with the character component already resolved. Register
        ///     <paramref name="cancellationToken" /> against whatever handle the call returns, so a
        ///     replaced batch or a timeout unwinds the behavior instead of leaving it running.
        /// </summary>
        /// <param name="characterComponent">The resolved component; never null.</param>
        /// <param name="invocation">The action invocation being serviced.</param>
        /// <param name="cancellationToken">Cancels the action.</param>
        protected abstract Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            TPeer characterComponent,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken);

        /// <summary>
        ///     Resolves the character component once, then hands off to
        ///     <see cref="ExecuteCoreAsync(TPeer,ConvaiActionInvocation,CancellationToken)" />. Sealed
        ///     so the resolve-and-report step cannot be skipped by a subclass.
        /// </summary>
        protected sealed override Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (!TryResolvePeer(ref _characterComponent, out TPeer characterComponent))
                return Task.FromResult(UnhandledMissingPeer<TPeer>());

            return ExecuteCoreAsync(characterComponent, invocation, cancellationToken);
        }
    }
}
