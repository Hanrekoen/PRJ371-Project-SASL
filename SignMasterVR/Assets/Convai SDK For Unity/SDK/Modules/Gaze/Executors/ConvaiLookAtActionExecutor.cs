using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.Gaze.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.Gaze.Executors
{
    /// <summary>How committed the character is to the thing it is looking at.</summary>
    public enum ConvaiGazeLookMode
    {
        /// <summary>A quick look and back — never turns the body, and does not interrupt what the eyes were doing.</summary>
        Glance = 0,

        /// <summary>A committed look the character holds, turning the body when the target is behind them.</summary>
        Sustained = 1
    }

    /// <summary>
    ///     Turns the character's attention to the thing the action points at. Eyes lead, the head
    ///     follows, and the body turns when the target is somewhere the head alone cannot reach —
    ///     the same anatomy the character uses when it looks at you, rather than a snap rotation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The action finishes as soon as the character's gaze visibly <em>arrives</em> on the
    ///         target, not when the hold ends. Looking at something reads as instant attention; if
    ///         the action stayed open for the whole hold, everything the character was about to say
    ///         or do next would queue up behind it. The hold continues on its own afterwards.
    ///     </para>
    ///     <para>
    ///         This is also the pattern to copy in a behavior of your own: ask for the gaze, wait for
    ///         it to settle so the character is visibly looking, <em>then</em> do the thing, and
    ///         release the gaze when you are done. That ordering is most of what makes an interaction
    ///         look deliberate instead of mechanical.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Look At Target")]
    [ConvaiActionArchetype(
        "Look At Target",
        ActionName = "Look At",
        Description = "Turn the character's attention to the target — eyes first, then head, then " +
                      "body when needed. Use this when the character mentions, examines, or is asked " +
                      "about a specific person, place, or object.",
        FeaturedDescription = "Turn naturally to look at a named person, place, or object.",
        TargetRequirement = ConvaiActionTargetRequirement.Either,
        RequiredPeerHint = "ConvaiGazeController",
        TimeoutSeconds = 10f,
        FailurePolicyOverride = ConvaiActionFailurePolicyOverride.ContinueBatch,
        FeaturedOrder = 3)]
    public sealed class ConvaiLookAtActionExecutor : ConvaiCharacterActionExecutor<ConvaiGazeController>
    {
        /// <summary>
        ///     Priority of a deliberately authored look. Above the character's own conversational
        ///     glancing so an authored "look at that" wins, and low enough that a later authored
        ///     attention action can take over from it.
        /// </summary>
        private const int SustainedPriority = 10;

        /// <summary>How long a glance lasts when no hold was asked for. Long enough to read as a look, short enough to stay a glance.</summary>
        private const float GlanceSeconds = 1.2f;

        [SerializeField]
        [Tooltip("Glance is a quick look and back. Sustained is a committed look the character holds, " +
                 "turning the body if the target is behind them. The character can ask for either per " +
                 "call with the 'mode' parameter.")]
        private ConvaiGazeLookMode _mode = ConvaiGazeLookMode.Sustained;

        [SerializeField]
        [Min(0f)]
        [Tooltip("How long to keep looking, in seconds, once the gaze arrives. 0 keeps looking until " +
                 "something else takes the character's attention.")]
        private float _holdSeconds = 2.5f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How intently to look. 1 is full attention; lower values read as a more casual look. " +
                 "Set explicitly so the action works even when the character is idle.")]
        private float _engagement = 1f;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiGazeController gaze,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            GameObject targetObject = ResolveTargetGameObject(invocation);
            if (targetObject == null)
            {
                return ConvaiActionExecutionResult.Unhandled(
                    "This action has nothing to look at. Looking always needs a target — set the " +
                    "action's Target to Object, Character, or Either.");
            }

            ConvaiGazeLookMode mode = ParseMode(GetOverride(invocation, "mode", string.Empty), _mode);
            float holdSeconds = Mathf.Max(0f, GetOverride(invocation, "holdSeconds", _holdSeconds));
            float engagement = Mathf.Clamp01(GetOverride(invocation, "engagement", _engagement));

            // The interaction point when the target declares one: looking at a chair's seat reads
            // very differently from looking at the origin of its bounding box.
            Transform lookAt = ResolveTargetInteractionPoint(invocation)
                               ?? ResolveEyeLevelForPlayer(gaze, targetObject)
                               ?? targetObject.transform;

            GazeHandle handle = mode == ConvaiGazeLookMode.Sustained
                ? gaze.GazeAt(lookAt, new GazeOptions
                {
                    Priority = SustainedPriority,
                    HoldSeconds = holdSeconds,
                    Engagement = engagement,
                    AllowBodyTurn = true
                })
                : gaze.GlanceAt(lookAt, holdSeconds > 0f ? holdSeconds : GlanceSeconds);

            if (handle == null)
            {
                return ConvaiActionExecutionResult.Failed(
                    "The gaze system would not take the request.", ConvaiActionFailureReason.InvalidState);
            }

            using (cancellationToken.Register(handle.Release))
            {
                bool arrived = await handle.Settled;
                cancellationToken.ThrowIfCancellationRequested();

                if (arrived)
                    return ConvaiActionExecutionResult.Succeeded();

                // A glance the character declined in order to keep looking at the person it is
                // talking to is the eye-contact lock working, not a look that went wrong. Reported
                // as unhandled with the setting named, so nobody goes hunting for a broken gaze
                // rig — and, under a batch that stops on failure, so it is not mistaken for one.
                return handle.Outcome == GazeOutcome.HeldEyeContactInstead
                    ? ConvaiActionExecutionResult.Unhandled(
                        "The character kept eye contact with the person it is talking to instead of " +
                        "glancing away. Turn off Lock Blocks Glances on the Gaze component if a " +
                        "glance should win, or ask for a sustained look rather than a glance.")
                    : ConvaiActionExecutionResult.Failed(
                        "Something took the character's attention before the look landed.",
                        ConvaiActionFailureReason.Interrupted);
            }
        }

        /// <summary>
        ///     Eye level for the player, when the thing being looked at <em>is</em> the player and the
        ///     action target did not say where to aim. Returns <c>null</c> for anything else.
        /// </summary>
        /// <remarks>
        ///     A player rig's root sits on the floor — a character controller's capsule is pivoted at
        ///     the feet, and so is almost every humanoid. Aiming at that transform makes the character
        ///     stare at the player's shoes, which is technically "looking at the player" and reads as
        ///     a bug to everyone who sees it. Worse, it disagreed with <c>Watch The Player</c>, so the
        ///     same person could be looked at in two different places depending on which action ran.
        ///     This resolves the one anchor the gaze system already uses for eye contact, so both
        ///     agree. An action target that declares its own interaction point always wins — the
        ///     author's aim beats this default.
        /// </remarks>
        private static Transform ResolveEyeLevelForPlayer(ConvaiGazeController gaze, GameObject targetObject)
        {
            Transform player = ConvaiPlayerBody.Resolve();
            if (player == null || player != targetObject.transform)
                return null;

            return gaze.TryGetPlayerAnchor(out Transform eyeAnchor) ? eyeAnchor : null;
        }

        /// <summary>Keeps both tuning values inside the range the Inspector advertises.</summary>
        private void OnValidate()
        {
            _holdSeconds = Mathf.Max(0f, _holdSeconds);
            _engagement = Mathf.Clamp01(_engagement);
        }

        /// <summary>
        ///     Reads the requested mode, accepting the everyday words a language model reaches for.
        ///     Anything unrecognised keeps the authored default rather than guessing.
        /// </summary>
        internal static ConvaiGazeLookMode ParseMode(string requested, ConvaiGazeLookMode authoredDefault)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return authoredDefault;

            return requested.Trim().ToLowerInvariant() switch
            {
                "glance" or "quick" or "brief" => ConvaiGazeLookMode.Glance,
                "sustained" or "hold" or "stare" or "watch" => ConvaiGazeLookMode.Sustained,
                _ => authoredDefault
            };
        }
    }
}
