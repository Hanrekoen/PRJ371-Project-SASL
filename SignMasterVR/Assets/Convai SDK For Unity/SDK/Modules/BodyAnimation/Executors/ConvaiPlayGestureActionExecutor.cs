using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Modules.BodyAnimation.Components;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using Convai.Modules.BodyAnimation.Data;
using Convai.Runtime.Actions;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Executors
{
    /// <summary>
    ///     Plays one of the character's own gestures by name — a wave, a shrug, a bow, whatever the
    ///     character's Animation Set contains. The gesture blends in over the character's current
    ///     posture and blends back out, so it reads as the same person moving rather than a clip
    ///     being swapped in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This behavior plays content, so it can only do what the character has been given. A
    ///         name with no matching gesture is declined rather than failed, so another behavior on
    ///         the character still gets its turn — and the Animation Set's actual gesture names are
    ///         logged once, at Detail tracing, so the fix is one console line away instead of a guess.
    ///     </para>
    ///     <para>
    ///         Characters that use a plain Animator Controller rather than the Body Animation module
    ///         want <c>Play Animator State</c> instead.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Play Gesture")]
    [ConvaiActionArchetype(
        "Play Gesture",
        ActionName = "Play Gesture",
        Description = "Play a gesture by name from the character's assigned Animation Set when a " +
                      "physical response naturally supports what the character is saying.",
        FeaturedDescription = "Play a gesture by name from the character's assigned Animation Set.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        Parameters = new[] { "gesture,String" },
        ParameterDescriptions = new[]
        {
            "Name the gesture to play using a gesture name or alias available in this character's " +
            "Animation Set. Always provide a gesture name."
        },
        RequiredPeerHint = "ConvaiBodyAnimationController",
        TimeoutSeconds = 15f,
        FailurePolicyOverride = ConvaiActionFailurePolicyOverride.ContinueBatch,
        FeaturedOrder = 4)]
    public sealed class ConvaiPlayGestureActionExecutor : ConvaiCharacterActionExecutor<ConvaiBodyAnimationController>
    {
        [SerializeField]
        [Tooltip("The gesture to play when the character does not name one. Matched against the " +
                 "Animation Set's gesture names and their aliases, ignoring capitalisation and spacing.")]
        private string _defaultGesture = string.Empty;

        [SerializeField]
        [Min(0f)]
        [Tooltip("How long to hold gestures that would otherwise continue indefinitely (a dance, a " +
                 "thinking pose), in seconds. 0 holds until something else stops it.")]
        private float _holdSeconds = 8f;

        private bool _loggedAvailableGestures;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiBodyAnimationController bodyAnimation,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            string gesture = ResolveGestureName(invocation, _defaultGesture);
            if (string.IsNullOrWhiteSpace(gesture))
            {
                // Two different situations, and telling somebody the wrong one sends them to the
                // wrong place: an action that never asks for a gesture needs one set here, while an
                // action that asks and was not answered needs its wording looked at. Saying "this
                // behavior has no default one" while a default sits in the Inspector is the kind of
                // message that costs an hour.
                return ConvaiActionExecutionResult.Unhandled(
                    DeclaredButNotSent(invocation, "gesture")
                        ? "This action asks the Convai Character which gesture to make, and it sent " +
                          "none. The behavior's Default Gesture is deliberately not used here — it " +
                          "answers which gesture this behavior is for, which is only a question when " +
                          "the action has no gesture parameter of its own. If this keeps happening, " +
                          "the parameter's wording may not make clear that a value is always needed."
                        : "No gesture was named, and this behavior has no default one.");
            }

            float holdSeconds = Mathf.Max(0f, GetOverride(invocation, "duration", _holdSeconds));

            BodyAnimationActionHandle handle = bodyAnimation.PlayAction(
                gesture, new ActionPlayOptions { HoldSeconds = holdSeconds });

            if (handle.Failed)
            {
                LogAvailableGesturesOnce(bodyAnimation, gesture);
                return ConvaiActionExecutionResult.Unhandled(
                    $"This character has no gesture called '{gesture}' ({handle.FailureReason}).");
            }

            using (cancellationToken.Register(handle.Stop))
            {
                bool finished = await handle.Completion;
                cancellationToken.ThrowIfCancellationRequested();

                return finished
                    ? ConvaiActionExecutionResult.Succeeded()
                    : ConvaiActionExecutionResult.Failed(
                        $"The '{gesture}' gesture was cut short by something else.",
                        ConvaiActionFailureReason.Interrupted);
            }
        }

        /// <summary>
        ///     Works out which gesture was asked for: the <c>gesture</c> parameter first, then this
        ///     behavior's default, and finally the action's own name — which is what makes a
        ///     one-action-per-gesture setup ("Wave", "Bow") work without any parameters at all.
        /// </summary>
        /// <remarks>
        ///     <b>An action that declares the parameter gets no default.</b> The default answers
        ///     "which gesture is this behavior for", which is only a question when the action has no
        ///     gesture parameter of its own. Where it does, a missing value is the Convai Character
        ///     failing to say which — and answering that with a stored gesture is how "thanks,
        ///     goodbye" became a hello wave on a live run, with nothing anywhere reporting it. See
        ///     <see cref="ConvaiActionExecutorBase.DeclaredButNotSent" />.
        /// </remarks>
        internal static string ResolveGestureName(ConvaiActionInvocation invocation, string defaultGesture)
        {
            string requested = GetOverride(invocation, "gesture", string.Empty);
            if (!string.IsNullOrWhiteSpace(requested))
                return requested;

            if (DeclaredButNotSent(invocation, "gesture"))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(defaultGesture))
                return defaultGesture;

            return invocation?.Command?.Name ?? string.Empty;
        }

        /// <summary>
        ///     Names the gestures the character actually has, once, and only when tracing is turned
        ///     up. Gated because the list can be long and the situation is usually a one-off typo;
        ///     logged at all because "no gesture called X" without "it has: Y, Z" leaves the author
        ///     guessing at content they cannot see from the action.
        /// </summary>
        private void LogAvailableGesturesOnce(ConvaiBodyAnimationController bodyAnimation, string requested)
        {
            if (_loggedAvailableGestures)
                return;

            AnimTraceVerbosity verbosity = bodyAnimation.Config != null
                ? bodyAnimation.Config.TraceVerbosity
                : AnimTraceVerbosity.Off;
            if (verbosity < AnimTraceVerbosity.Detail)
                return;

            _loggedAvailableGestures = true;
            ConvaiBodyAnimationSet set = bodyAnimation.AnimationSet;
            ConvaiLogger.Warning(
                $"[{nameof(ConvaiPlayGestureActionExecutor)}] No gesture matching '{requested}' in the " +
                $"Animation Set '{set?.DisplayName ?? "(none assigned)"}'.",
                LogCategory.Character);
        }

        private void OnValidate() => _holdSeconds = Mathf.Max(0f, _holdSeconds);
    }
}
