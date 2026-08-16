using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.Gaze.Core.Diagnostics;
using Convai.Modules.Gaze.Core.Policy;
using Convai.Modules.Gaze.Data;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Reorientation
{
    /// <summary>
    ///     Executes the feet rung of the actuator ladder: turns the whole body toward the
    ///     gaze target, through the registered <see cref="ICharacterReorientationHandler" />
    ///     (Body Animation's animated turn-in-place) when available, otherwise through the
    ///     procedural fallback driver.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>It no longer decides when to turn.</b> That verdict arrives as
    ///         <c>wantsFeet</c> from <see cref="Shift.GazeActuatorLadder" />, which forms it
    ///         from what the head and torso could not take and from the same shift clock the
    ///         other rungs read. This director previously carried its own angle threshold and
    ///         its own hysteresis hold, so a turn waited behind a hold that had already been
    ///         waited out elsewhere in the chain.
    ///     </para>
    ///     <para>
    ///         Head and eye gaze stay pinned on the world-space target during the whole turn —
    ///         the solver chain re-solves against the rotating root every frame, which is what
    ///         makes the turn read as "keeps looking at me while turning".
    ///     </para>
    /// </remarks>
    internal sealed class ReorientationDirector
    {
        /// <summary>
        ///     Cadence for steering an in-flight animated turn toward the live target: fast
        ///     enough to track a walking player, sparse enough to stay out of the trace.
        /// </summary>
        private const float ReaimIntervalSeconds = 0.15f;

        private readonly ProceduralReorientationDriver _procedural = new();
        private float _reaimTimer;
        private bool _fallbackLogged;
        private bool _handlerWasTurning;

        /// <summary>Whether any turn (animated or procedural) is currently in flight.</summary>
        public bool IsReorienting { get; private set; }

        public void Reset()
        {
            _reaimTimer = 0f;
            _procedural.Cancel();
            IsReorienting = false;
            _fallbackLogged = false;
            _handlerWasTurning = false;
        }

        /// <param name="facingReference">
        ///     The rig transform whose forward must end up on the target (what the player
        ///     sees); the yaw error and the fallback's completion are measured against it.
        /// </param>
        /// <param name="rotationRoot">
        ///     The character-root transform the fallback rotates — the same transform the
        ///     animated turn path rotates, so the two paths never de-synchronize parent
        ///     and child.
        /// </param>
        /// <param name="wantsFeet">
        ///     The actuator ladder's verdict: the head and torso have taken their shares and
        ///     what remains still needs the feet. This director no longer forms that opinion
        ///     itself — see the remarks on <see cref="Shift.GazeActuatorLadder" />.
        /// </param>
        public void Tick(
            ICharacterReorientationHandler handler,
            ConvaiGazeProfile profile,
            in GazeDirective directive,
            float targetYawError,
            bool wantsFeet,
            Transform facingReference,
            Transform rotationRoot,
            float deltaTime,
            GazeTrace trace)
        {
            if (profile == null || facingReference == null || rotationRoot == null) return;

            bool eligible = profile.EnableBodyTurn &&
                            directive.HasEngagedTarget &&
                            directive.AllowBodyTurn;

            // Keep driving an in-flight procedural turn to a clean stop even when the
            // target was just released — freezing mid-turn looks broken.
            if (_procedural.IsActive)
            {
                // Track the live target while swiveling — the procedural counterpart of
                // the animated turn's re-aim, so a walking player is landed on, not
                // where they stood when the swivel began.
                if (eligible)
                {
                    Vector3 liveSwivelDirection = directive.WorldPoint - facingReference.position;
                    liveSwivelDirection.y = 0f;
                    if (liveSwivelDirection.sqrMagnitude > 1e-6f)
                        _procedural.Retarget(liveSwivelDirection, profile);
                }

                bool still = _procedural.Tick(profile, deltaTime);
                if (!eligible && still)
                {
                    _procedural.Cancel();
                    trace?.State("Procedural body turn cancelled (target released).");
                    still = false;
                }

                IsReorienting = still || (handler?.IsReorienting ?? false);
                if (still) return;
            }

            bool handlerTurning = handler?.IsReorienting ?? false;
            bool handlerJustFinished = _handlerWasTurning && !handlerTurning;
            _handlerWasTurning = handlerTurning;
            IsReorienting = handlerTurning || _procedural.IsActive;

            if (!eligible) return;

            // An animated turn chasing a moving target can land short of (or past) the
            // live target — the clip's steerable window ends well before the clip does.
            // Anything between "close enough" and the next full-turn threshold would
            // otherwise stay uncorrected and read as over-turning followed by a hasty
            // head whip; a critically-damped settle swivel closes it smoothly instead.
            if (handlerJustFinished && !_procedural.IsActive)
            {
                float residual = Mathf.Abs(targetYawError);
                // Upper bound is "the ladder is not already asking for another turn": inside
                // that band the residual is too small to be worth a second stepping turn and
                // too large to leave, which is exactly what the settle swivel is for.
                if (residual > profile.BodyTurnCompletionToleranceDegrees && !wantsFeet)
                {
                    Vector3 settleDirection = directive.WorldPoint - facingReference.position;
                    settleDirection.y = 0f;
                    if (settleDirection.sqrMagnitude > 1e-6f)
                    {
                        _procedural.Begin(rotationRoot, facingReference, settleDirection, profile);
                        if (_procedural.IsActive)
                        {
                            IsReorienting = true;
                            trace?.Detail(
                                $"Animated turn landed {residual:F0}° off '{directive.TargetName}' — settling the residual procedurally.");
                            return;
                        }
                    }
                }
            }

            if (handlerTurning)
            {
                // Live re-aim: the target may keep moving while the animated turn plays.
                // Steering the in-flight turn keeps it landing on the live target instead
                // of where the target stood when it fired — which read as an overshoot
                // followed by a hasty correction turn.
                _reaimTimer += deltaTime;
                if (eligible && _reaimTimer >= ReaimIntervalSeconds)
                {
                    _reaimTimer = 0f;
                    Vector3 liveDirection = directive.WorldPoint - facingReference.position;
                    liveDirection.y = 0f;
                    if (liveDirection.sqrMagnitude > 1e-6f)
                        handler.TryReorient(liveDirection, "re-aim toward moving target");
                }

                return;
            }

            _reaimTimer = 0f;

            // The decision to turn is the actuator ladder's, not this director's. It fires when
            // the head and torso together have run out of room — a residual the ladder computes
            // after allocating their shares — rather than when the raw error crosses a fixed
            // angle. A fixed tripwire cannot tell a character whose head comfortably covers the
            // look from one already pinned at its limit; both used to wait for the same 68°.
            // The onset that used to be this director's own hysteresis hold now comes from the
            // same shift clock every other rung reads, which is what stops the waits stacking.
            if (!wantsFeet) return;

            Fire(handler, profile, in directive, targetYawError, facingReference, rotationRoot, trace);
        }

        private void Fire(
            ICharacterReorientationHandler handler,
            ConvaiGazeProfile profile,
            in GazeDirective directive,
            float targetYawError,
            Transform facingReference,
            Transform rotationRoot,
            GazeTrace trace)
        {
            Vector3 direction = directive.WorldPoint - facingReference.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;

            string reason = $"gaze yaw error {targetYawError:F0}° toward '{directive.TargetName}'";

            if (handler != null && handler.TryReorient(direction, reason))
            {
                IsReorienting = true;
                trace?.State($"Body turn requested from reorientation handler ({reason}).");
                return;
            }

            _procedural.Begin(rotationRoot, facingReference, direction, profile);
            if (_procedural.IsActive)
            {
                IsReorienting = true;
                if (!_fallbackLogged)
                {
                    _fallbackLogged = true;
                    trace?.State(
                        handler == null
                            ? $"No reorientation handler registered — using the procedural fallback turn ({reason})."
                            : $"Reorientation handler refused — using the procedural fallback turn ({reason}).");
                }
                else
                {
                    trace?.Detail($"Procedural body turn started ({reason}).");
                }
            }
        }
    }
}
