using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.BodyAnimation.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Executors
{
    /// <summary>Leads the player to a target while waiting when the player falls behind.</summary>
    [AddComponentMenu("Convai/Actions/Lead Player To Target")]
    [ConvaiActionArchetype(
        "Lead Player To Target",
        ActionName = "Lead Player",
        Description = "Guide the player to the named destination by choosing the route, walking " +
                      "ahead, pausing when the player falls behind, and continuing when they catch up.",
        TargetRequirement = ConvaiActionTargetRequirement.Either,
        RequiredPeerHint = "ConvaiNavMeshLocomotion",
        TimeoutSeconds = 120f,
        Family = "Movement")]
    public sealed class ConvaiLeadPlayerActionExecutor : ConvaiCharacterActionExecutor<ConvaiNavMeshLocomotion>
    {
        private const float CheckIntervalSeconds = 0.1f;
        private const float ArrivalTolerance = 0.45f;

        [SerializeField, Min(0f)]
        [Tooltip("How far short of the destination the character stops.")]
        [ConvaiInspectorSection("Destination", 0)]
        private float _arriveDistance = 1.4f;

        [SerializeField, Min(1f)]
        [Tooltip("Pause the journey when the player is farther away than this distance.")]
        [ConvaiInspectorSection("Keeping Together", 10)]
        private float _waitWhenFartherThan = 4.5f;

        [SerializeField, Min(0.5f)]
        [Tooltip("Resume the journey when the player returns within this distance.")]
        [ConvaiInspectorSection("Keeping Together", 11)]
        private float _resumeWhenCloserThan = 2.8f;

        [SerializeField, Min(0f)]
        [Tooltip("Maximum time to wait before continuing to the destination without the player.")]
        [ConvaiInspectorSection("Keeping Together", 12)]
        private float _maximumWaitSeconds = 12f;

        private ConvaiNavMeshLocomotion _activeLocomotion;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiNavMeshLocomotion locomotion,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            GameObject targetObject = ResolveTargetGameObject(invocation);
            Transform destination = ResolveTargetInteractionPoint(invocation) ?? targetObject?.transform;
            if (destination == null)
            {
                return ConvaiActionExecutionResult.Unhandled(
                    "Leading requires a destination. Set the action target to Object, Character, or Either.");
            }

            Transform player = ResolvePlayer();
            if (player == null)
            {
                return ConvaiActionExecutionResult.Failed(
                    "No player found. Add a Convai Player or tag the active player camera as MainCamera.",
                    ConvaiActionFailureReason.TargetMissing);
            }

            float arriveDistance = Mathf.Max(0f, GetOverride(invocation, "arriveDistance", _arriveDistance));
            bool continuedWithoutPlayer = false;
            _activeLocomotion = locomotion;

            try
            {
                while (FlatDistance(locomotion.transform.position, destination.position) >
                       arriveDistance + ArrivalTolerance)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Vector3 stopAt = ConvaiWalkToActionExecutor.ResolveStandingSpot(
                        locomotion.transform.position,
                        destination.position,
                        arriveDistance);

                    locomotion.SetTravelSubject(destination);
                    MoveOutcome outcome = await RunLegAsync(
                        locomotion,
                        stopAt,
                        player,
                        !continuedWithoutPlayer,
                        cancellationToken);

                    if (outcome == MoveOutcome.NoPath)
                    {
                        return ConvaiActionExecutionResult.Failed(
                            $"There is no navigable path to '{targetObject.name}'. Check the baked NavMesh " +
                            "and the target's position.",
                            ConvaiActionFailureReason.PathBlocked);
                    }

                    if (outcome == MoveOutcome.Interrupted)
                    {
                        return ConvaiActionExecutionResult.Failed(
                            $"The journey to '{targetObject.name}' was interrupted.",
                            ConvaiActionFailureReason.Interrupted);
                    }

                    if (outcome == MoveOutcome.Arrived)
                        break;

                    locomotion.SetTravelSubject(player);
                    bool caughtUp = await WaitForPlayerAsync(locomotion, player, cancellationToken);
                    continuedWithoutPlayer |= !caughtUp;
                }

                locomotion.SetTravelSubject(player);
                return ConvaiActionExecutionResult.Succeeded(
                    continuedWithoutPlayer
                        ? $"Reached '{targetObject.name}' after continuing ahead."
                        : $"Led the player to '{targetObject.name}'.");
            }
            finally
            {
                locomotion.ClearTravelSubject();
                if (_activeLocomotion == locomotion)
                    _activeLocomotion = null;
            }
        }

        private async Task<MoveOutcome> RunLegAsync(
            ConvaiNavMeshLocomotion locomotion,
            Vector3 destination,
            Transform player,
            bool mayWait,
            CancellationToken cancellationToken)
        {
            var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void HandleMoveEnded(bool arrived) => ended.TrySetResult(arrived);
            locomotion.MoveEnded += HandleMoveEnded;

            try
            {
                if (!locomotion.MoveTo(destination))
                    return MoveOutcome.NoPath;

                using (cancellationToken.Register(locomotion.Stop))
                {
                    while (!ended.Task.IsCompleted)
                    {
                        await ConvaiActionAsyncUtility.WaitSecondsAsync(CheckIntervalSeconds, cancellationToken);
                        if (!mayWait || player == null ||
                            FlatDistance(locomotion.transform.position, player.position) <= _waitWhenFartherThan)
                            continue;

                        locomotion.StopGracefully();
                        bool stoppedAtRequestedPoint = await ended.Task;
                        cancellationToken.ThrowIfCancellationRequested();
                        return stoppedAtRequestedPoint ? MoveOutcome.Arrived : MoveOutcome.WaitingForPlayer;
                    }

                    return await ended.Task ? MoveOutcome.Arrived : MoveOutcome.Interrupted;
                }
            }
            finally
            {
                locomotion.MoveEnded -= HandleMoveEnded;
            }
        }

        private async Task<bool> WaitForPlayerAsync(
            ConvaiNavMeshLocomotion locomotion,
            Transform player,
            CancellationToken cancellationToken)
        {
            if (_maximumWaitSeconds <= 0f)
                return false;

            float elapsed = 0f;
            while (player != null && FlatDistance(locomotion.transform.position, player.position) > _resumeWhenCloserThan)
            {
                float before = Time.realtimeSinceStartup;
                await ConvaiActionAsyncUtility.WaitSecondsAsync(CheckIntervalSeconds, cancellationToken);
                elapsed += Mathf.Max(0f, Time.realtimeSinceStartup - before);
                if (elapsed >= _maximumWaitSeconds)
                    return false;
            }

            return player != null;
        }

        private static float FlatDistance(Vector3 first, Vector3 second)
        {
            Vector3 difference = second - first;
            difference.y = 0f;
            return difference.magnitude;
        }

        private void OnDisable()
        {
            if (_activeLocomotion == null)
                return;
            _activeLocomotion.ClearTravelSubject();
            _activeLocomotion.Stop();
            _activeLocomotion = null;
        }

        private void OnValidate()
        {
            _arriveDistance = Mathf.Max(0f, _arriveDistance);
            _waitWhenFartherThan = Mathf.Max(1f, _waitWhenFartherThan);
            _resumeWhenCloserThan = Mathf.Clamp(_resumeWhenCloserThan, 0.5f, _waitWhenFartherThan - 0.1f);
            _maximumWaitSeconds = Mathf.Max(0f, _maximumWaitSeconds);
        }

        private enum MoveOutcome
        {
            Arrived,
            WaitingForPlayer,
            Interrupted,
            NoPath
        }
    }
}
