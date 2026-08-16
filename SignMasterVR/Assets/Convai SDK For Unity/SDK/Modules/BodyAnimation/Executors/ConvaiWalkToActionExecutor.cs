using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.BodyAnimation.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Executors
{
    /// <summary>
    ///     The character walks to the thing the action names, finding its way around whatever is in
    ///     the room. It stops a comfortable distance short rather than walking into the object, and
    ///     the walk itself — starts, turns, and the slow-down into the stop — comes from the
    ///     character's own locomotion, so it reads as walking rather than sliding.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Needs a baked NavMesh. Without a path the action fails with
    ///         <see cref="ConvaiActionFailureReason.PathBlocked" /> and names the destination, which
    ///         is almost always either an unbaked floor or a target standing off the mesh.
    ///     </para>
    ///     <para>
    ///         The character looks where it is going on its own: the dispatcher tells its other
    ///         systems what this step is acting on, and gaze turns toward it while the walk starts.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Walk To Target")]
    [ConvaiActionArchetype(
        "Walk To Target",
        ActionName = "Walk To",
        Description = "Walk to the named place, object, or person and stop at a natural " +
                      "conversational distance. Use this when the character is asked to approach " +
                      "or go to a specific target.",
        FeaturedDescription = "Walk to a named place, object, or person and stop nearby.",
        TargetRequirement = ConvaiActionTargetRequirement.Either,
        RequiredPeerHint = "ConvaiNavMeshLocomotion",
        TimeoutSeconds = 45f,
        FeaturedOrder = 1)]
    public sealed class ConvaiWalkToActionExecutor : ConvaiCharacterActionExecutor<ConvaiNavMeshLocomotion>
    {
        [SerializeField]
        [Min(0f)]
        [Tooltip("How far short of the target to stop, in metres. Stopping right on top of something " +
                 "is what makes a character look like it is standing inside the furniture.")]
        private float _arriveDistance = 1f;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiNavMeshLocomotion locomotion,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            GameObject targetObject = ResolveTargetGameObject(invocation);
            if (targetObject == null)
            {
                // Walking is one of the few verbs that is meaningless without a destination, so the
                // message names the two things that actually cause this: an action authored to take
                // no target, or a character naming a target the scene never registered.
                return ConvaiActionExecutionResult.Unhandled(
                    "This action has nowhere to walk to. Walking always needs a target — set the " +
                    "action's Target to Object, Character, or Either, and register the thing to walk to.");
            }

            float arriveDistance = Mathf.Max(0f, GetOverride(invocation, "arriveDistance", _arriveDistance));
            Transform destination = ResolveTargetInteractionPoint(invocation) ?? targetObject.transform;
            Vector3 stopAt = ResolveStandingSpot(locomotion.transform.position, destination.position, arriveDistance);

            var arrival = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void HandleMoveEnded(bool arrived) => arrival.TrySetResult(arrived);
            locomotion.MoveEnded += HandleMoveEnded;

            // What this walk is about, so the character checks on it as it goes instead of either
            // ignoring it or staring at it the whole way. Cleared in the finally: the subject
            // belongs to this walk, not to whatever the character does next.
            locomotion.SetTravelSubject(destination.position);

            try
            {
                if (!locomotion.MoveTo(stopAt))
                {
                    return ConvaiActionExecutionResult.Failed(
                        $"There is no way to walk to '{targetObject.name}'. Check that the floor has a " +
                        "baked NavMesh and that the target stands on it.",
                        ConvaiActionFailureReason.PathBlocked);
                }

                using (cancellationToken.Register(locomotion.Stop))
                {
                    bool arrived = await arrival.Task;
                    cancellationToken.ThrowIfCancellationRequested();

                    return arrived
                        ? ConvaiActionExecutionResult.Succeeded()
                        : ConvaiActionExecutionResult.Failed(
                            $"The walk to '{targetObject.name}' was stopped before the character got there.",
                            ConvaiActionFailureReason.Interrupted);
                }
            }
            finally
            {
                locomotion.MoveEnded -= HandleMoveEnded;
                locomotion.ClearTravelSubject();
            }
        }

        /// <summary>
        ///     Where to actually stand: short of the destination along the line from the character to
        ///     it, on the ground plane. Height is dropped from the approach on purpose — a target
        ///     mounted on a wall should still be approached across the floor, not aimed at through it.
        /// </summary>
        internal static Vector3 ResolveStandingSpot(Vector3 from, Vector3 destination, float arriveDistance)
        {
            Vector3 approach = destination - from;
            approach.y = 0f;

            return approach.magnitude > arriveDistance
                ? destination - approach.normalized * arriveDistance
                : destination;
        }

        private void OnValidate() => _arriveDistance = Mathf.Max(0f, _arriveDistance);
    }
}
