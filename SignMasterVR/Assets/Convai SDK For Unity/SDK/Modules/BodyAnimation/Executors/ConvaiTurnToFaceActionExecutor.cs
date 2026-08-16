using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.BodyAnimation.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Executors
{
    /// <summary>How the character gets round to facing the target.</summary>
    public enum ConvaiTurnStyle
    {
        /// <summary>
        ///     Plays the character's own turn-in-place animation, so the feet step round. Needs turn
        ///     clips in the Animation Set; without them the action declines.
        /// </summary>
        SteppingTurn = 0,

        /// <summary>
        ///     Rotates the character directly over a set duration. Needs no clips, never competes
        ///     with a locomotion animation, and is what many first-person and stylised games use.
        /// </summary>
        SmoothRotation = 1
    }

    /// <summary>
    ///     The character turns on the spot to face the thing the action names, stepping its feet
    ///     round rather than pivoting like a turret. This is the cheap half of walking over: most of
    ///     the time "look at the customer" means turning to face them, not crossing the room.
    /// </summary>
    /// <remarks>
    ///     Needs no NavMesh — it is a turn in place, so it works in scenes that have no pathfinding
    ///     set up at all. Reach for <c>Walk To Target</c> when the character actually has to travel.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Turn To Face Target")]
    [ConvaiActionArchetype(
        "Turn To Face Target",
        ActionName = "Turn To Face",
        Description = "Turn on the spot to face the target without walking toward it. Use this when " +
                      "the character should reorient its whole body while staying in place.",
        TargetRequirement = ConvaiActionTargetRequirement.Either,
        RequiredPeerHint = "ConvaiBodyAnimationController",
        TimeoutSeconds = 10f,
        FailurePolicyOverride = ConvaiActionFailurePolicyOverride.ContinueBatch,
        FeaturedOrder = 6)]
    public sealed class ConvaiTurnToFaceActionExecutor : ConvaiCharacterActionExecutor<ConvaiBodyAnimationController>
    {
        /// <summary>
        ///     Longest a turn is allowed to take before the action gives up waiting. A turn in place
        ///     is at most half a revolution of stepping; anything past this means the character is
        ///     being held still by something else, and holding the action open would stall the batch.
        /// </summary>
        private const float TurnTimeoutSeconds = 6f;

        [SerializeField]
        [Tooltip("Stepping Turn plays the character's own turn animation — grounded, but it needs " +
                 "turn clips and takes as long as they take. Smooth Rotation turns the character " +
                 "directly over the duration below, which needs no clips and never fights an " +
                 "animation. Neither is more correct; pick the one that suits your game.")]
        private ConvaiTurnStyle _turnStyle = ConvaiTurnStyle.SteppingTurn;

        [SerializeField]
        [Min(0.05f)]
        [Tooltip("How long a Smooth Rotation turn takes, in seconds. Ignored by Stepping Turn, " +
                 "which takes as long as its animation does.")]
        private float _smoothTurnSeconds = 0.5f;

        [SerializeField]
        [Range(0f, 45f)]
        [Tooltip("How close to facing the target counts as done, in degrees. A little slack here is " +
                 "what stops the character shuffling back and forth trying to be exact.")]
        private float _toleranceDegrees = 8f;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiBodyAnimationController bodyAnimation,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            GameObject targetObject = ResolveTargetGameObject(invocation);
            if (targetObject == null)
            {
                return ConvaiActionExecutionResult.Unhandled(
                    "This action has nothing to turn towards. Turning always needs a target — set the " +
                    "action's Target to Object, Character, or Either.");
            }

            Transform faceTowards = ResolveTargetInteractionPoint(invocation) ?? targetObject.transform;
            Transform body = bodyAnimation.transform;

            Vector3 direction = faceTowards.position - body.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return ConvaiActionExecutionResult.Succeeded(
                    $"Already standing at '{targetObject.name}'.");
            }

            if (AngleToTarget(body, faceTowards) <= _toleranceDegrees)
                return ConvaiActionExecutionResult.Succeeded($"Already facing '{targetObject.name}'.");

            if (_turnStyle == ConvaiTurnStyle.SmoothRotation)
            {
                await RotateSmoothlyAsync(body, faceTowards, cancellationToken);
                return ConvaiActionExecutionResult.Succeeded();
            }

            if (!bodyAnimation.FaceTowards(direction.normalized, "Turn To Face action"))
            {
                return ConvaiActionExecutionResult.Unhandled(
                    "This character cannot turn in place right now — its Animation Set needs turning " +
                    "clips. Set Turn Style to Smooth Rotation if this character has none.");
            }

            bool faced = await ConvaiActionAsyncUtility.WaitUntilAsync(
                () => AngleToTarget(body, faceTowards) <= _toleranceDegrees,
                cancellationToken,
                TurnTimeoutSeconds);

            return faced
                ? ConvaiActionExecutionResult.Succeeded()
                : ConvaiActionExecutionResult.Failed(
                    $"The character did not finish turning towards '{targetObject.name}'.",
                    ConvaiActionFailureReason.Timeout);
        }

        /// <summary>
        ///     Turns the character directly, re-aiming each frame so a target that moves mid-turn is
        ///     still faced at the end. Eased rather than linear, because a constant-speed turn starts
        ///     and stops abruptly and that is most of what makes direct rotation look robotic.
        /// </summary>
        private async Task RotateSmoothlyAsync(Transform body, Transform faceTowards, CancellationToken cancellationToken)
        {
            float turnSeconds = Mathf.Max(0.05f, _smoothTurnSeconds);
            Quaternion from = body.rotation;
            float elapsed = 0f;
            var clock = new ConvaiActionFrameClock();

            while (elapsed < turnSeconds)
            {
                elapsed += await clock.TickAsync(cancellationToken);
                if (body == null || faceTowards == null)
                    return;

                Vector3 toTarget = faceTowards.position - body.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f)
                    return;

                Quaternion to = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                body.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / turnSeconds));
            }
        }

        /// <summary>
        ///     Flat angle between where the character is facing and where the target is. Height is
        ///     ignored: a character does not lean back to face something above it.
        /// </summary>
        private static float AngleToTarget(Transform body, Transform target)
        {
            Vector3 toTarget = target.position - body.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
                return 0f;

            Vector3 facing = body.forward;
            facing.y = 0f;
            return Vector3.Angle(facing, toTarget);
        }

        private void OnValidate()
        {
            _toleranceDegrees = Mathf.Clamp(_toleranceDegrees, 0f, 45f);
            _smoothTurnSeconds = Mathf.Max(0.05f, _smoothTurnSeconds);
        }
    }
}
