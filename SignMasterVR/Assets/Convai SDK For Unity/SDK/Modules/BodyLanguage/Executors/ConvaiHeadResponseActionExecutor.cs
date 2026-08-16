using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.BodyLanguage.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Executors
{
    /// <summary>
    ///     The character answers with its head: yes, no, or "hmm". A nod, a shake, or a considering
    ///     tilt — the small responses that make a listening character look like it is listening.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The gesture is layered on top of whatever the character is already doing rather than
    ///         replacing it. Breathing, weight shifts, and where the character is looking all
    ///         continue underneath, which is what keeps a nod from reading as a puppet jerk.
    ///     </para>
    ///     <para>
    ///         The action stays open until the gesture finishes, so a sequence can nod <em>then</em>
    ///         speak, in that order.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Nod Or Shake Head")]
    [ConvaiActionArchetype(
        "Nod Or Shake Head",
        ActionName = "Nod Or Shake Head",
        Description = "Answers with the head — a nod for yes, a shake for no, a tilt for 'let me think'.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        Parameters = new[] { "response,Choice,,yes|no|maybe", "intensity,Number" },
        ParameterDescriptions = new[]
        {
            "Use 'yes' for agreement, 'no' for disagreement, or 'maybe' for uncertainty. Always " +
            "provide one of these values.",
            "Strength of the head response from 0 (subtle) to 1 (fully expressed)."
        },
        RequiredPeerHint = "ConvaiBodyLanguageController")]
    public sealed class ConvaiHeadResponseActionExecutor : ConvaiCharacterActionExecutor<ConvaiBodyLanguageController>
    {
        [SerializeField]
        [Tooltip("Which response to give when the character does not name one. The character can ask " +
                 "for 'yes', 'no', or 'maybe' per call with the 'response' parameter.")]
        private HeadGestureKind _response = HeadGestureKind.Nod;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How big the movement is. Lower values read as a subtle acknowledgment, higher as " +
                 "an emphatic one.")]
        private float _intensity = 1f;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiBodyLanguageController bodyLanguage,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            HeadGestureKind response = ParseResponse(GetOverride(invocation, "response", string.Empty), _response);
            float intensity = Mathf.Clamp01(GetOverride(invocation, "intensity", _intensity));

            HeadGestureHandle handle = await RequestWaitingOutABusyHeadAsync(
                bodyLanguage, response, intensity, cancellationToken);

            if (!handle.IsActive)
            {
                return handle.Refusal == HeadGestureRefusal.Busy
                    ? ConvaiActionExecutionResult.Failed(
                        "The character was still finishing another head gesture and did not get to " +
                        "this one. Leave a moment between head responses, or send this one on its own.",
                        ConvaiActionFailureReason.Busy)
                    : ConvaiActionExecutionResult.Failed(
                        "This character cannot nod or shake its head — check that it has a Body " +
                        "Language profile and a usable rig.",
                        ConvaiActionFailureReason.InvalidState);
            }

            using (cancellationToken.Register(handle.Release))
            {
                await handle.Completion;
                cancellationToken.ThrowIfCancellationRequested();
                return ConvaiActionExecutionResult.Succeeded();
            }
        }

        /// <summary>
        ///     How long to keep trying when the character's head is busy, in seconds. A head gesture
        ///     runs for a little over a second, so a request that arrives on the tail of another one
        ///     is served by waiting rather than by refusing.
        /// </summary>
        private const float BusyGraceSeconds = 1.5f;

        /// <summary>How long to leave between attempts while waiting for the head to come free.</summary>
        private const float BusyRetryIntervalSeconds = 0.15f;

        /// <summary>
        ///     Asks for the gesture, and keeps asking for a moment when the only thing in the way is
        ///     the character's own previous gesture.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why waiting is the right answer and refusing is not.</b> "Ask a question, get a
        ///         nod, ask another, get a shake" is the most ordinary thing this behavior is used
        ///         for, and the second request lands while the first gesture is still unwinding. A
        ///         character that answers the first question and silently ignores the second reads as
        ///         broken, and no amount of message quality fixes that — the fix is to answer.
        ///     </para>
        ///     <para>
        ///         Only a busy head is waited out. A character that cannot gesture at all is refused
        ///         immediately: waiting would turn a clear setup message into a delayed one.
        ///     </para>
        /// </remarks>
        private static async Task<HeadGestureHandle> RequestWaitingOutABusyHeadAsync(
            ConvaiBodyLanguageController bodyLanguage,
            HeadGestureKind response,
            float intensity,
            CancellationToken cancellationToken)
        {
            HeadGestureHandle handle = bodyLanguage.Nod(response, intensity);

            for (float waited = 0f;
                 !handle.IsActive && handle.Refusal == HeadGestureRefusal.Busy && waited < BusyGraceSeconds;
                 waited += BusyRetryIntervalSeconds)
            {
                await ConvaiActionAsyncUtility.WaitSecondsAsync(BusyRetryIntervalSeconds, cancellationToken);
                handle = bodyLanguage.Nod(response, intensity);
            }

            return handle;
        }

        private void OnValidate() => _intensity = Mathf.Clamp01(_intensity);

        /// <summary>
        ///     Reads the requested response. The words are the ones a person — or a language model —
        ///     would actually use for the meaning, not the names of the motions: a character is asked
        ///     to say yes, not to pitch its head twice.
        /// </summary>
        internal static HeadGestureKind ParseResponse(string requested, HeadGestureKind authoredDefault)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return authoredDefault;

            return requested.Trim().ToLowerInvariant() switch
            {
                "yes" or "nod" or "agree" or "affirm" or "ok" => HeadGestureKind.Nod,
                "no" or "shake" or "disagree" or "refuse" or "deny" => HeadGestureKind.Shake,
                "maybe" or "tilt" or "think" or "consider" or "unsure" => HeadGestureKind.Tilt,
                _ => authoredDefault
            };
        }
    }
}
