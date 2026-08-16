using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Abstract base for <see cref="MonoBehaviour" /> executors that act on a resolved action
    ///     target through a hierarchy-resolved peer component (a controller, locomotion, or rig).
    ///     Encapsulates the skeleton shared by the shipped module executors: target validation, peer
    ///     resolution/caching, once-logged missing-peer reporting, and invocation parameter override
    ///     helpers. Implementers write only <see cref="ExecuteCoreAsync" />.
    /// </summary>
    /// <remarks>
    ///     Cancellation: the dispatcher already maps <see cref="System.OperationCanceledException" />
    ///     thrown out of <see cref="ExecuteAsync" /> to <see cref="ConvaiActionExecutionResult.TimedOut" />
    ///     / <see cref="ConvaiActionExecutionResult.Canceled" /> depending on which token tripped, so
    ///     implementers should register their handle's stop/cancel/release method against
    ///     <c>cancellationToken</c> (typically <c>using (cancellationToken.Register(handle.Stop))</c>)
    ///     and let the exception propagate rather than catching it.
    /// </remarks>
    public abstract class ConvaiTargetedActionExecutor : ConvaiActionExecutorBase
    {
        private bool _missingPeerLogged;

        // Where a peer found automatically is remembered for the rest of this run. Deliberately
        // not serialized: the author's field is their intent and must stay exactly as they left it.
        private Component _resolvedPeer;

        /// <summary>
        ///     Whether this executor requires <see cref="ConvaiActionInvocation.ResolvedTarget" /> to
        ///     carry a resolved GameObject. Target-less executors (e.g. a scripted head gesture)
        ///     override this to <c>false</c> and never touch the invocation's target; a <c>null</c>
        ///     invocation is then safe to pass through to <see cref="ExecuteCoreAsync" />.
        /// </summary>
        protected virtual bool RequiresTarget => true;

        /// <summary>
        ///     What <see cref="RequiresTarget" /> answers, for authoring-time validation.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         An action whose definition demands a target is refused by the admission stage
        ///         before the behavior is ever asked — so a behavior that says it does not need one,
        ///         bound to a definition that says it does, never runs and never explains why. The
        ///         only symptom is a drop report saying the Convai Character "named nothing", which
        ///         points at the model rather than at the mismatch.
        ///     </para>
        ///     <para>
        ///         Found in this SDK's own demo twice in one session, on two different actions, and
        ///         twelve shipped behaviors override <see cref="RequiresTarget" /> to <c>false</c> —
        ///         so the trap is available to every project. Validation needs to read the answer
        ///         the behavior actually gives, which is why this exists rather than the archetype
        ///         attribute being trusted: on one of those two actions the attribute said
        ///         <c>Object</c> while the code said <c>false</c>.
        ///     </para>
        ///     <para>
        ///         Internal, not public: it is a fact about the behavior for the SDK's own checks,
        ///         not a switch anyone should read at runtime. Override
        ///         <see cref="RequiresTarget" /> — that is the one place this is decided.
        ///     </para>
        /// </remarks>
        internal bool NeedsTargetToRun => RequiresTarget;

        /// <summary>
        ///     Sealed template flow: when <see cref="RequiresTarget" /> is true and the invocation has
        ///     no resolved target GameObject, returns <see cref="MissingTargetResult" /> without
        ///     calling <see cref="ExecuteCoreAsync" />; otherwise runs the subclass's core logic.
        /// </summary>
        public sealed override Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (RequiresTarget && ResolveTargetGameObject(invocation) == null)
                return Task.FromResult(MissingTargetResult(invocation));

            return ExecuteCoreAsync(invocation, cancellationToken);
        }

        /// <summary>
        ///     Runs the action once the target precondition declared by <see cref="RequiresTarget" />
        ///     is satisfied. Implementers resolve their own peer via <see cref="TryResolvePeer{T}" />,
        ///     run their handle-based flow, and register <paramref name="cancellationToken" /> against
        ///     the handle's stop/cancel/release method so batch replacement and timeouts unwind cleanly.
        /// </summary>
        protected abstract Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken);

        /// <summary>
        ///     Result returned when <see cref="RequiresTarget" /> is true and the invocation carries no
        ///     resolved target GameObject. Defaults to <see cref="ConvaiActionExecutionResult.Unhandled" />
        ///     with a component-prefixed message, matching every shipped targeted executor's current
        ///     behavior; override to return a <c>Failed</c> result with
        ///     <see cref="ConvaiActionFailureReason.TargetMissing" /> instead if a subclass wants a
        ///     missing target treated as a hard failure rather than a soft decline.
        /// </summary>
        protected virtual ConvaiActionExecutionResult MissingTargetResult(ConvaiActionInvocation invocation) =>
            ConvaiActionExecutionResult.Unhandled("No resolved target object.");

        /// <summary>Resolved target GameObject for this invocation, or null when absent/not required.</summary>
        protected static GameObject ResolveTargetGameObject(ConvaiActionInvocation invocation) =>
            invocation?.ResolvedTarget?.GameObjectReference;

        /// <summary>
        ///     Resolved target's interaction point for this invocation — the binding's explicit
        ///     point when authored/registered with one, otherwise its GameObject's transform, or
        ///     null when absent. Prefer this over <see cref="ResolveTargetGameObject" />'s
        ///     transform for move/point/gaze/anchor executors.
        /// </summary>
        protected static Transform ResolveTargetInteractionPoint(ConvaiActionInvocation invocation) =>
            invocation?.ResolvedTarget?.InteractionPoint;

        /// <summary>
        ///     Resolves a required peer component. <paramref name="authored" /> is the author's
        ///     explicit assignment — typically a serialized inspector field — and is only ever
        ///     read: when it is non-null it wins, and nothing is written back. When it is empty the
        ///     peer is looked up with <see cref="Component.GetComponentInParent{T}(bool)" /> first,
        ///     then <see cref="Component.GetComponentInChildren{T}(bool)" />, and remembered for the
        ///     rest of this run on the component itself rather than in the caller's field. So a
        ///     field the Inspector told the author to leave empty stays empty, in the scene and on
        ///     disk, exactly as its tooltip promises.
        /// </summary>
        protected bool TryResolvePeer<T>(ref T authored, out T peer) where T : Component
        {
            if (authored != null)
            {
                peer = authored;
                return true;
            }

            if (_resolvedPeer is T remembered && remembered != null)
            {
                peer = remembered;
                return true;
            }

            T resolved = GetComponentInParent<T>(true);
            if (resolved == null)
                resolved = GetComponentInChildren<T>(true);

            _resolvedPeer = resolved;
            peer = resolved;
            return resolved != null;
        }

        /// <summary>
        ///     Builds an <see cref="ConvaiActionExecutionResult.Unhandled" /> result for a missing peer
        ///     of type <typeparamref name="T" />, logging it once per component instance at
        ///     <see cref="LogCategory.Character" /> so setup mistakes surface without per-call spam.
        /// </summary>
        protected ConvaiActionExecutionResult UnhandledMissingPeer<T>()
        {
            string message = $"No {typeof(T).Name} found on this character.";
            if (!_missingPeerLogged)
            {
                _missingPeerLogged = true;
                ConvaiLogger.Warning($"[{GetType().Name}] {message}", LogCategory.Character);
            }

            return ConvaiActionExecutionResult.Unhandled(message);
        }

        /// <summary>
        ///     Reads a numeric invocation parameter override; falls back to
        ///     <paramref name="defaultValue" /> (the Inspector-authored default) when
        ///     <paramref name="invocation" /> is null or the parameter is absent.
        /// </summary>
        protected static float GetOverride(ConvaiActionInvocation invocation, string parameterName, float defaultValue) =>
            invocation == null ? defaultValue : invocation.GetNumber(parameterName, defaultValue);

        /// <summary>
        ///     Reads a boolean invocation parameter override; falls back to
        ///     <paramref name="defaultValue" /> (the Inspector-authored default) when
        ///     <paramref name="invocation" /> is null or the parameter is absent.
        /// </summary>
        protected static bool GetOverride(ConvaiActionInvocation invocation, string parameterName, bool defaultValue) =>
            invocation == null ? defaultValue : invocation.GetBool(parameterName, defaultValue);

        /// <summary>
        ///     Reads a string invocation parameter override; falls back to
        ///     <paramref name="defaultValue" /> (the Inspector-authored default) when
        ///     <paramref name="invocation" /> is null or the parameter is absent.
        /// </summary>
        protected static string GetOverride(ConvaiActionInvocation invocation, string parameterName, string defaultValue) =>
            invocation == null ? defaultValue : invocation.GetString(parameterName, defaultValue);
    }
}
