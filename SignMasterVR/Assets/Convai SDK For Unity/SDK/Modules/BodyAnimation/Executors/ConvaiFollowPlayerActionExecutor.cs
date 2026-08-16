using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Convai.Modules.BodyAnimation.Components;
using Convai.Runtime.Actions;
using UnityEngine;

namespace Convai.Modules.BodyAnimation.Executors
{
    /// <summary>Whether the character should start following the player or stop.</summary>
    public enum ConvaiFollowMode
    {
        /// <summary>Starts following, and keeps following until told to stop.</summary>
        Follow = 0,

        /// <summary>Stops following and stands still.</summary>
        Stop = 1
    }

    /// <summary>
    ///     "Come with me." The character follows the player around, keeping a comfortable distance —
    ///     closing the gap when the player walks off, standing still when the player stops rather
    ///     than crowding them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This is the one behavior whose effect outlives the action that started it.</b>
    ///         Following has no natural end, so the action reports success as soon as the character
    ///         is following and the following itself continues afterwards. If the action stayed open
    ///         instead, it would sit there until it timed out and every later action would queue
    ///         behind it — the character would agree to follow and then be unable to do anything else.
    ///     </para>
    ///     <para>
    ///         Because it outlives its action, it is explicitly ended: send it again with
    ///         <c>stop</c>, or disable the character. It never leaks — the follow stops when the
    ///         component does.
    ///     </para>
    ///     <para>
    ///         <b>It gives way to any other walk.</b> Outliving its action means it is still running
    ///         while later actions arrive, and a follow that re-aims every quarter second would
    ///         otherwise overwrite the destination of every one of them: ask a following character to
    ///         walk somewhere and it would take two steps and come straight back. So while another
    ///         driver is moving the character, following stands down, and it resumes the moment that
    ///         move ends. Read as behaviour rather than as arbitration, this is also the truthful
    ///         version — someone walking with you who is asked to go and fetch something goes, and
    ///         then falls back in beside you.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Actions/Follow The Player")]
    [ConvaiActionArchetype(
        "Follow The Player",
        ActionName = "Follow The Player",
        Description = "Start following the player at a comfortable distance, keeping up as the " +
                      "player chooses the route, or stop following and stand still.",
        FeaturedDescription = "Follow the player at a comfortable distance, or stop and stay put.",
        TargetRequirement = ConvaiActionTargetRequirement.None,
        Parameters = new[] { "mode,Choice,,follow|stop" },
        ParameterDescriptions = new[]
        {
            "Use 'follow' when the player asks the character to come along, and 'stop' when they ask " +
            "the character to stay, wait, or stop following. Always provide one of these values."
        },
        RequiredPeerHint = "ConvaiNavMeshLocomotion",
        FeaturedOrder = 2)]
    public sealed class ConvaiFollowPlayerActionExecutor : ConvaiCharacterActionExecutor<ConvaiNavMeshLocomotion>
    {
        /// <summary>
        ///     How often the follow re-aims, in seconds. Re-pathing every frame is wasted work and
        ///     makes the character twitch as the destination jitters with the player's own steps;
        ///     a few times a second reads as attentive without either problem.
        /// </summary>
        private const float RepathIntervalSeconds = 0.25f;

        [SerializeField]
        [Tooltip("Whether this action starts following or stops. The character can ask for either per " +
                 "call with the 'mode' parameter ('follow' or 'stop').")]
        private ConvaiFollowMode _mode = ConvaiFollowMode.Follow;

        [SerializeField]
        [Min(0.5f)]
        [Tooltip("How close the character tries to stay, in metres. This is personal space — too " +
                 "small and the character treads on the player's heels.")]
        private float _followDistance = 2.2f;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("How far the player has to move beyond that distance before the character bothers to " +
                 "close the gap. Without this slack the character shuffles constantly.")]
        private float _slack = 0.8f;

        private Coroutine _following;

        /// <summary>
        ///     True only for the instant this component is issuing its own move, so the move it hears
        ///     about on <see cref="ConvaiNavMeshLocomotion.MoveStarted" /> can be told apart from
        ///     somebody else's. Without the distinction the follow would stand down for its own steps.
        /// </summary>
        private bool _selfDriving;

        /// <summary>
        ///     True while another driver owns the character's legs. Following keeps its state and
        ///     stops issuing moves rather than ending, because the visitor asked to be accompanied and
        ///     never withdrew that — the errand is an interruption, not a cancellation.
        /// </summary>
        private bool _standingDown;

        /// <inheritdoc />
        protected override bool RequiresTarget => false;

        /// <summary>Whether the character is following right now. Public so scene logic can show it.</summary>
        public bool IsFollowing => _following != null;

        /// <inheritdoc />
        protected override Task<ConvaiActionExecutionResult> ExecuteCoreAsync(
            ConvaiNavMeshLocomotion locomotion,
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            // An action that declares 'mode' is asking the Convai Character to choose, and the two
            // choices are opposites. Answering a missing one with this component's setting is how
            // "stop following me" became the character setting off after the visitor on a live run,
            // reported as a success. See ConvaiActionExecutorBase.DeclaredButNotSent.
            if (DeclaredButNotSent(invocation, "mode"))
            {
                return Task.FromResult(ConvaiActionExecutionResult.Unhandled(
                    "This action asks whether to follow or to stop, and the Convai Character sent " +
                    "neither. Following and stopping are opposites, so there is nothing safe to " +
                    "assume. If this keeps happening, the 'mode' parameter's wording may not make " +
                    "clear that a value is always needed."));
            }

            ConvaiFollowMode mode = ParseMode(GetOverride(invocation, "mode", string.Empty), _mode);

            if (mode == ConvaiFollowMode.Stop)
            {
                bool wasFollowing = IsFollowing;
                StopFollowing(locomotion);
                return Task.FromResult(ConvaiActionExecutionResult.Succeeded(
                    wasFollowing ? "Stopped following." : "Was not following."));
            }

            Transform player = ConvaiPlayerBody.Resolve();
            if (player == null)
            {
                return Task.FromResult(ConvaiActionExecutionResult.Failed(
                    "No player found in the scene. Add a Convai Player, or tag a camera as the main camera.",
                    ConvaiActionFailureReason.TargetMissing));
            }

            if (IsFollowing)
                return Task.FromResult(ConvaiActionExecutionResult.Succeeded("Already following."));

            // Walking with someone and never looking at them reads as escort duty. Naming the player
            // as what this journey is about is what turns it into company: the character watches
            // where it is going and checks on them as it walks.
            locomotion.SetTravelSubject(player);
            _cachedLocomotion = locomotion;
            locomotion.MoveStarted += HandleMoveStarted;
            locomotion.MoveEnded += HandleMoveEnded;
            _following = StartCoroutine(FollowRoutine(locomotion, player));
            return Task.FromResult(ConvaiActionExecutionResult.Succeeded("Following the player."));
        }

        /// <summary>
        ///     Whether the follow is currently letting another action drive. Internal seam so EditMode
        ///     tests can pin the hand-over down without a NavMesh, a player and a running coroutine.
        /// </summary>
        internal bool IsStandingDown => _standingDown;

        /// <summary>
        ///     Notices a move this component did not order and stands the follow down for its
        ///     duration, so the walk the visitor actually asked for is the one that happens.
        /// </summary>
        internal void HandleMoveStarted(Vector3 destination)
        {
            if (_selfDriving)
                return;

            _standingDown = true;
        }

        /// <summary>
        ///     Frees the follow once the other driver's move is over — whether it arrived or was cut
        ///     short, because either way nobody else is steering any more.
        /// </summary>
        /// <remarks>
        ///     Only the flag is cleared here. Restating the travel subject has to wait for the next
        ///     tick: this runs from inside the other driver's move-ended event, and that driver
        ///     clears the subject in the <c>finally</c> that follows — so a subject set here would be
        ///     wiped a moment later, and the character would resume the follow looking at nothing.
        /// </remarks>
        internal void HandleMoveEnded(bool arrived) => _standingDown = false;

        private IEnumerator FollowRoutine(ConvaiNavMeshLocomotion locomotion, Transform player)
        {
            var wait = new WaitForSeconds(RepathIntervalSeconds);
            var stoodDown = false;

            while (player != null)
            {
                // Standing down is checked here rather than at the callback so a move that starts and
                // ends between two ticks costs nothing: the follow simply finds itself free again.
                if (_standingDown)
                {
                    stoodDown = true;
                    yield return wait;
                    continue;
                }

                if (stoodDown)
                {
                    // Back on our own feet, and a tick after the errand's own cleanup — late enough
                    // that restating who this journey is about survives.
                    stoodDown = false;
                    locomotion.SetTravelSubject(player);
                }

                Vector3 gap = player.position - locomotion.transform.position;
                gap.y = 0f;
                float distance = gap.magnitude;

                if (distance > _followDistance + _slack)
                {
                    Vector3 standingSpot = player.position - gap.normalized * _followDistance;

                    _selfDriving = true;
                    locomotion.MoveTo(standingSpot);
                    _selfDriving = false;
                }
                else if (distance < _followDistance - _slack && locomotion.IsMoving)
                {
                    // Close enough. Stopping rather than creeping the last few centimetres is what
                    // makes the character look like it decided to stop, not like it ran out of path.
                    // Gracefully, because this is a decision and not an interruption: cancelling the
                    // path outright halts the animation on the spot while the agent coasts on, and
                    // the character slides to a halt with its feet already standing still.
                    locomotion.StopGracefully();
                }

                yield return wait;
            }

            _following = null;
        }

        /// <summary>
        ///     Ends the follow if the component is switched off or destroyed. Without this, disabling
        ///     a following character would leave its locomotion mid-path with nothing left to stop it.
        /// </summary>
        private void OnDisable()
        {
            if (_following == null)
                return;

            StopCoroutine(_following);
            _following = null;

            if (!TryResolvePeer(ref _cachedLocomotion, out ConvaiNavMeshLocomotion locomotion)) return;

            Unlisten(locomotion);
            locomotion.ClearTravelSubject();
            locomotion.Stop();
        }

        private ConvaiNavMeshLocomotion _cachedLocomotion;

        private void StopFollowing(ConvaiNavMeshLocomotion locomotion)
        {
            if (_following != null)
            {
                StopCoroutine(_following);
                _following = null;
            }

            Unlisten(locomotion);
            locomotion.ClearTravelSubject();

            // "Stop following me" is a decision the character carries out, not a plug being
            // pulled: it walks its last stride out. The subscriptions are already dropped above,
            // so the run-out's own MoveEnded is nobody's business but the locomotion's.
            locomotion.StopGracefully();
        }

        /// <summary>
        ///     Drops the move subscriptions and the stand-down state together. Kept in one place
        ///     because a follow that ends while standing down would otherwise leave the flag set, and
        ///     the next follow would start already convinced somebody else was steering.
        /// </summary>
        private void Unlisten(ConvaiNavMeshLocomotion locomotion)
        {
            if (locomotion != null)
            {
                locomotion.MoveStarted -= HandleMoveStarted;
                locomotion.MoveEnded -= HandleMoveEnded;
            }

            _standingDown = false;
            _selfDriving = false;
        }

        private void OnValidate()
        {
            _followDistance = Mathf.Max(0.5f, _followDistance);
            _slack = Mathf.Max(0.1f, _slack);
        }

        /// <summary>Reads the requested mode, accepting the wordings a language model actually produces.</summary>
        internal static ConvaiFollowMode ParseMode(string requested, ConvaiFollowMode authoredDefault)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return authoredDefault;

            return requested.Trim().ToLowerInvariant() switch
            {
                "follow" or "start" or "come" or "accompany" => ConvaiFollowMode.Follow,
                "stop" or "stay" or "wait" or "halt" or "stopfollowing" or "stop_following" => ConvaiFollowMode.Stop,
                _ => authoredDefault
            };
        }
    }
}
