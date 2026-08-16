using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.BodyAnimation.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Executors
{
    /// <summary>
    ///     The character goes back to where it started — behind the counter, back to its post, back
    ///     to the spot it was standing when the scene began. This is the undo for every action that
    ///     moved it, and it is what makes a scene worth playing twice.
    /// </summary>
    /// <remarks>
    ///     The home spot is remembered when the scene starts, before anything has had a chance to
    ///     move the character. Assign one explicitly when the character should return somewhere other
    ///     than where it happened to be placed.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Return To Start")]
    [ConvaiActionArchetype(
        "Return To Start",
        ActionName = "Return To Start",
        Description = "The character walks back to where it started, or to a spot you choose.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        RequiredPeerHint = "ConvaiNavMeshLocomotion")]
    public sealed class ConvaiReturnToStartActionExecutor : ConvaiCharacterActionExecutor<ConvaiNavMeshLocomotion>
    {
        [SerializeField]
        [Tooltip("Where 'back' is. Leave empty to use wherever the character was standing when the " +
                 "scene started.")]
        private Transform _homeSpot;

        [SerializeField]
        [Tooltip("Turn back to face the way it was originally facing after arriving. Without this a " +
                 "character returns to its post facing whatever direction it happened to walk in from.")]
        private bool _restoreFacing = true;

        [SerializeField]
        [Min(0.05f)]
        [Tooltip("How long that final turn takes, in seconds. It is a turn, not a snap — a character " +
                 "whose rotation changes in one frame reads as teleporting.")]
        private float _turnBackSeconds = 0.6f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("How close counts as home, in metres.")]
        private float _arriveDistance = 0.2f;

        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private bool _startRecorded;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <summary>
        ///     Records home before anything moves the character. Awake rather than Start so it is
        ///     captured before any behavior on the same character has had a chance to act.
        /// </summary>
        /// <remarks>
        ///     Reads the character's transform rather than this component's own, because a behavior
        ///     is allowed to sit on a child object that holds the character's action behaviors. This
        ///     used to read <c>transform</c> directly, which meant that on such a child the character
        ///     walked "home" to wherever that child object happened to be.
        /// </remarks>
        private void Awake()
        {
            Transform character = CharacterTransform;
            _startPosition = character.position;
            _startRotation = character.rotation;
            _startRecorded = true;
        }

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiNavMeshLocomotion locomotion,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            Vector3 home = _homeSpot != null
                ? _homeSpot.position
                : _startRecorded
                    ? _startPosition
                    : locomotion.transform.position;

            Vector3 gap = home - locomotion.transform.position;
            gap.y = 0f;
            if (gap.magnitude <= _arriveDistance)
            {
                await TurnBackAsync(locomotion, cancellationToken);
                return ConvaiActionExecutionResult.Succeeded("Already home.");
            }

            var arrival = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void HandleMoveEnded(bool arrived) => arrival.TrySetResult(arrived);
            locomotion.MoveEnded += HandleMoveEnded;

            try
            {
                if (!locomotion.MoveTo(home))
                {
                    return ConvaiActionExecutionResult.Failed(
                        "There is no way back to the starting spot. Check that it stands on the baked NavMesh.",
                        ConvaiActionFailureReason.PathBlocked);
                }

                using (cancellationToken.Register(locomotion.Stop))
                {
                    bool arrived = await arrival.Task;
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!arrived)
                    {
                        return ConvaiActionExecutionResult.Failed(
                            "The character was stopped before getting back.",
                            ConvaiActionFailureReason.Interrupted);
                    }

                    await TurnBackAsync(locomotion, cancellationToken);
                    return ConvaiActionExecutionResult.Succeeded();
                }
            }
            finally
            {
                locomotion.MoveEnded -= HandleMoveEnded;
            }
        }

        /// <summary>
        ///     Turns back to the original facing over <see cref="_turnBackSeconds" />, frame by frame.
        /// </summary>
        /// <remarks>
        ///     This used to assign the rotation in one frame, and it read exactly as badly as that
        ///     sounds — the character arrived and then flicked round, which is the single clearest
        ///     way to tell a player they are looking at a puppet. It is eased rather than linear so
        ///     the turn starts and finishes softly, and it is skipped entirely when the character is
        ///     already facing the right way, so a return that needs no turn does not stall.
        /// </remarks>
        private async Task TurnBackAsync(ConvaiNavMeshLocomotion locomotion, CancellationToken cancellationToken)
        {
            if (!_restoreFacing || !_startRecorded)
                return;

            Transform body = locomotion.transform;
            Quaternion from = body.rotation;
            Quaternion to = _homeSpot != null ? _homeSpot.rotation : _startRotation;

            if (Quaternion.Angle(from, to) <= FacingToleranceDegrees)
                return;

            float turnSeconds = Mathf.Max(0.05f, _turnBackSeconds);
            float elapsed = 0f;
            var clock = new ConvaiActionFrameClock();

            while (elapsed < turnSeconds)
            {
                elapsed += await clock.TickAsync(cancellationToken);
                if (body == null)
                    return;

                body.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / turnSeconds));
            }

            if (body != null)
                body.rotation = to;
        }

        /// <summary>Close enough to the original facing that turning would read as fidgeting.</summary>
        private const float FacingToleranceDegrees = 5f;

        private void OnValidate()
        {
            _arriveDistance = Mathf.Max(0f, _arriveDistance);
            _turnBackSeconds = Mathf.Max(0.05f, _turnBackSeconds);
        }
    }
}
