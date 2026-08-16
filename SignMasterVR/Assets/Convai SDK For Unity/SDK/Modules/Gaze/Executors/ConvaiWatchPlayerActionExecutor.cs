using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.Gaze.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.Gaze.Executors
{
    /// <summary>Whether the character should start watching the player or stop.</summary>
    public enum ConvaiWatchPlayerMode
    {
        /// <summary>Holds eye contact with the player until told to stop, or until something more important happens.</summary>
        Watch = 0,

        /// <summary>Lets go of an active watch and returns the character's attention to whatever it would do on its own.</summary>
        StopWatching = 1
    }

    /// <summary>
    ///     Makes the character hold eye contact with the player on request — "keep your eyes on me" —
    ///     and lets go again on request. This is a moment, not a setting: it is a held look with a
    ///     clear beginning and end, distinct from the character's own eye-contact behavior during
    ///     conversation.
    /// </summary>
    /// <remarks>
    ///     It deliberately does not change the character's Eye Contact setting. That setting is a
    ///     character-wide authoring choice, and reaching in to flip it would fight both the profile
    ///     and every other attention action, and would still be flipped when the moment had passed.
    ///     A held request is scoped, cancellable, and undone by simply letting go.
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Watch The Player")]
    [ConvaiActionArchetype(
        "Watch The Player",
        ActionName = "Watch The Player",
        Description = "Holds eye contact with the player until told to stop.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        Parameters = new[] { "mode,Choice,,watch|stop" },
        ParameterDescriptions = new[]
        {
            "Use 'watch' to keep the character's attention on the player, or 'stop' to release that " +
            "standing instruction. Always provide one of these values."
        },
        RequiredPeerHint = "ConvaiGazeController")]
    public sealed class ConvaiWatchPlayerActionExecutor : ConvaiCharacterActionExecutor<ConvaiGazeController>
    {
        /// <summary>
        ///     Deliberately <em>below</em> a one-off look (priority 10).
        /// </summary>
        /// <remarks>
        ///     A watch is a standing instruction, not an interruption. Ranking it above every other
        ///     attention action would mean that once the character was told to keep its eyes on you,
        ///     nothing could ever draw them away — "watch me" then "look at the crate" would do
        ///     nothing, which is exactly the bug this ranking avoids. Sitting underneath, the watch
        ///     is preempted by each explicit look and resumes when that look releases, which is what
        ///     a person does.
        /// </remarks>
        private const int WatchPriority = 5;

        [SerializeField]
        [Tooltip("Whether this action starts watching or stops. The character can ask for either per " +
                 "call with the 'mode' parameter ('watch' or 'stop').")]
        private ConvaiWatchPlayerMode _mode = ConvaiWatchPlayerMode.Watch;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How intently to watch. 1 is full attention.")]
        private float _engagement = 1f;

        private GazeHandle _activeWatch;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <inheritdoc />
        protected override async Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiGazeController gaze,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            ConvaiWatchPlayerMode mode = ParseMode(GetOverride(invocation, "mode", string.Empty), _mode);

            if (mode == ConvaiWatchPlayerMode.StopWatching)
            {
                bool wasWatching = _activeWatch != null;
                StopWatching();
                return ConvaiActionExecutionResult.Succeeded(
                    wasWatching ? "Stopped watching the player." : "Was not watching the player.");
            }

            // The gaze system's own player anchor, not the player GameObject: a first-person rig's
            // root sits on the floor, and aiming at it makes the character hold eye contact with the
            // player's shoes. This is the same anchor conversational eye contact uses, so a watch
            // and ordinary eye contact land in the same place.
            if (!gaze.TryGetPlayerAnchor(out Transform player) || player == null)
            {
                return ConvaiActionExecutionResult.Failed(
                    "No player found in the scene. Add a Convai Player, or tag a camera as the main camera.",
                    ConvaiActionFailureReason.TargetMissing);
            }

            float engagement = Mathf.Clamp01(GetOverride(invocation, "engagement", _engagement));

            StopWatching();
            GazeHandle handle = gaze.GazeAt(player, new GazeOptions
            {
                Priority = WatchPriority,
                HoldSeconds = 0f,
                Engagement = engagement,
                AllowBodyTurn = true
            });

            if (handle == null)
            {
                return ConvaiActionExecutionResult.Failed(
                    "The gaze system would not take the request.", ConvaiActionFailureReason.InvalidState);
            }

            _activeWatch = handle;

            using (cancellationToken.Register(() => ReleaseIfStillCurrent(handle)))
            {
                bool arrived = await handle.Settled;
                cancellationToken.ThrowIfCancellationRequested();

                if (!arrived)
                {
                    ReleaseIfStillCurrent(handle);
                    return ConvaiActionExecutionResult.Failed(
                        "Something took the character's attention before eye contact landed.",
                        ConvaiActionFailureReason.Interrupted);
                }

                return ConvaiActionExecutionResult.Succeeded("Watching the player.");
            }
        }

        private void OnValidate() => _engagement = Mathf.Clamp01(_engagement);

        /// <summary>
        ///     A watch outlives the action that started it, so it has to be let go when the component
        ///     goes away — otherwise disabling the character mid-watch would leave the gaze system
        ///     holding a request nothing can cancel.
        /// </summary>
        private void OnDisable() => StopWatching();

        private void ReleaseIfStillCurrent(GazeHandle handle)
        {
            if (_activeWatch != handle)
                return;

            handle.Release();
            _activeWatch = null;
        }

        private void StopWatching()
        {
            _activeWatch?.Release();
            _activeWatch = null;
        }

        /// <summary>Reads the requested mode, accepting the wordings a language model actually produces.</summary>
        internal static ConvaiWatchPlayerMode ParseMode(string requested, ConvaiWatchPlayerMode authoredDefault)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return authoredDefault;

            return requested.Trim().ToLowerInvariant() switch
            {
                "watch" or "start" or "look" => ConvaiWatchPlayerMode.Watch,
                "stop" or "stopwatching" or "stop_watching" or "release" or "away" => ConvaiWatchPlayerMode.StopWatching,
                _ => authoredDefault
            };
        }
    }
}
