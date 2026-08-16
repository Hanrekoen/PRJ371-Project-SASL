using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.BodyAnimation.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Executors
{
    /// <summary>
    ///     The character points at the thing the action names — "it's over there". The arm that
    ///     points is chosen from where the target actually is, the point rises, holds, and lowers,
    ///     and the rest of the body keeps doing what it was doing underneath.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Pointing pairs with looking: a character that points without looking reads as
    ///         distracted. You get that pairing for free — the actions dispatcher tells the
    ///         character's other systems what target this step acquired, and gaze turns toward it on
    ///         its own, as long as the dispatcher's performance reactions are left on.
    ///     </para>
    ///     <para>
    ///         <b>How long a point takes.</b> Three things add up, and only the middle one is
    ///         <see cref="_holdSeconds" />: the arm rises, holds, and lowers. The shipped pointing
    ///         clips run about five seconds with the apex halfway through, so the rise and the fall
    ///         are roughly two and a half seconds each on their own — a one-second hold still gives
    ///         a six-second gesture. <see cref="_gestureSpeed" /> shortens the rise and the fall, and
    ///         <see cref="_release" /> can drop the fall entirely. Both default to the original
    ///         behaviour, so an existing scene is unchanged until somebody asks for something else.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Point At Target")]
    [ConvaiActionArchetype(
        "Point At Target",
        ActionName = "Point At",
        Description = "Point at the target so the player can see which person, place, or object the " +
                      "character means. Use this together with speech when location or identity would " +
                      "otherwise be ambiguous.",
        TargetRequirement = ConvaiActionTargetRequirement.Either,
        RequiredPeerHint = "ConvaiBodyAnimationController",
        TimeoutSeconds = 15f,
        FailurePolicyOverride = ConvaiActionFailurePolicyOverride.ContinueBatch,
        FeaturedOrder = 5)]
    public sealed class ConvaiPointAtActionExecutor : ConvaiCharacterActionExecutor<ConvaiBodyAnimationController>
    {
        [SerializeField]
        [Min(0.1f)]
        [Tooltip("How long the point is held at full extension, in seconds. This is the pause in the " +
                 "middle — the arm still takes its own time to rise beforehand and to lower " +
                 "afterwards, so this is not the length of the gesture. Use Gesture Speed and " +
                 "Release to change those.")]
        private float _holdSeconds = 3f;

        [SerializeField]
        [Range(0.25f, 3f)]
        [Tooltip("How quickly the arm rises and lowers, as a multiple of the animation's own speed. " +
                 "The hold above is unaffected. Raise this when a point reads as laboured — the " +
                 "shipped pointing clips take about two and a half seconds each way at 1.")]
        private float _gestureSpeed = 1f;

        [SerializeField]
        [Tooltip("What happens when the hold ends. 'Play Tail' lowers the arm through the rest of the " +
                 "animation, which is the fullest-looking option and the slowest. 'Blend' drops the " +
                 "pose out instead, which ends the gesture roughly as soon as the hold does.")]
        private PointingReleaseStyle _release = PointingReleaseStyle.PlayTail;

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
                    "This action has nothing to point at. Pointing always needs a target — set the " +
                    "action's Target to Object, Character, or Either.");
            }

            float holdSeconds = Mathf.Max(0.1f, GetOverride(invocation, "duration", _holdSeconds));

            // Point at the interaction point when the target declares one — pointing at a door's
            // handle rather than at the middle of the door reads as knowing what you mean.
            Transform pointAt = ResolveTargetInteractionPoint(invocation) ?? targetObject.transform;

            // Played through the options overload rather than the plain one so the two settings
            // above reach the pointing layer. Both default to what the plain overload does, so a
            // behavior nobody has touched performs exactly as it did before.
            var options = PointingPlayOptions.Default;
            options.HoldSeconds = holdSeconds;
            options.Speed = _gestureSpeed;
            options.ReleaseStyle = _release;

            BodyAnimationPointingHandle handle = bodyAnimation.PointAt(pointAt, options);
            if (handle.Failed)
            {
                return ConvaiActionExecutionResult.Unhandled(
                    $"This character cannot point right now ({handle.FailureReason}). Its Animation Set " +
                    "needs pointing clips.");
            }

            using (cancellationToken.Register(handle.Release))
            {
                await handle.Completion;
                cancellationToken.ThrowIfCancellationRequested();
                return ConvaiActionExecutionResult.Succeeded();
            }
        }

        private void OnValidate()
        {
            _holdSeconds = Mathf.Max(0.1f, _holdSeconds);
            _gestureSpeed = Mathf.Clamp(_gestureSpeed, 0.25f, 3f);
        }
    }
}
