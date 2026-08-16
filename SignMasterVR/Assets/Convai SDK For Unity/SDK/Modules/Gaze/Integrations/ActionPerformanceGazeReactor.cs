using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.Gaze.Components;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Integrations
{
    /// <summary>
    ///     Realism-pack look-where-you-act glance : while a
    ///     targeted action step executes, holds a modest-priority gaze glance at the resolved
    ///     target through the controller's existing <see cref="ConvaiGazeController.GazeAt" /> API;
    ///     released as soon as the step's outcome is known.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The glance is off by default</b> (<c>Targeting → Look At Action Targets</c>).
    ///         Described in a sentence it sounds obviously right; on screen it reads as the
    ///         character being distracted by its own hands, because the look is decided by a step
    ///         boundary rather than by anything the character noticed, and it therefore lands at
    ///         moments a person would not have looked.
    ///     </para>
    ///     <para>
    ///         What is NOT behind that toggle is this type's other job: handing the step's target
    ///         to the travel director as what the journey is about. A walking character watches
    ///         the road and checks on where it is going whether or not it glances at what it acts
    ///         on, and those are the travel settings' business.
    ///     </para>
    ///     <para>
    ///         Registered by <see cref="ConvaiGazeController" /> itself on enable, unregistered on
    ///         disable — auto-wired, no user setup required. Inert unless the actions dispatcher's
    ///         Performance toggle is on and a reactor is registered, matching every other action
    ///         seam. Priority is deliberately lower than an explicit "Look At" action
    ///         (sustained mode, priority 10) so an authored gaze action always wins.
    ///     </para>
    ///     <para>
    ///         <b>A step that walks somewhere is the exception.</b> Holding the look for the whole
    ///         step is right for acting on a thing in reach and wrong for travelling to it — held
    ///         across a walk it becomes a head-locked stare at the destination for the entire
    ///         journey. So when travel begins, the hold is handed over: the same point becomes what
    ///         the journey is <em>about</em>, and the travel director glances at it periodically
    ///         while the character watches the road. Because this keys off the dispatcher's target
    ///         event rather than off any particular executor, a customer's own walk-somewhere
    ///         executor inherits the behavior without writing gaze code.
    ///     </para>
    /// </remarks>
    internal sealed class ActionPerformanceGazeReactor : IActionPerformanceReactor
    {
        private const int LookWhereYouActPriority = 2;

        private readonly ConvaiGazeController _controller;
        private GazeHandle _activeHandle;

        private Vector3 _stepTargetPoint;
        private bool _hasStepTarget;

        public ActionPerformanceGazeReactor(ConvaiGazeController controller) => _controller = controller;

        /// <summary>No batch-start reaction for Gaze; Body Language owns the acknowledgment nod.</summary>
        public void OnActionBatchStarted()
        {
        }

        /// <inheritdoc />
        public void OnActionTargetAcquired(string targetName, Vector3 worldPosition)
        {
            ReleaseHeldGaze();

            _stepTargetPoint = worldPosition;
            _hasStepTarget = true;

            // Already travelling when the step began: never take the stare in the first place.
            if (TryHandOverToTravel()) return;

            // The glance itself is opt-in, but everything above this line is not, and the order
            // is load-bearing. Recording the step's target and offering it to the travel director
            // is how a walking character knows what its journey is ABOUT — the thing it checks on
            // periodically while it watches the road. Gating the whole method on the toggle would
            // switch that off too, silently, and the symptom would be a character that walks
            // somewhere without ever glancing at where it is going.
            if (!LookAtActionTargetsEnabled) return;

            _activeHandle = _controller.GazeAt(worldPosition, new GazeOptions
            {
                Priority = LookWhereYouActPriority,
                HoldSeconds = 0f, // hold until released — see OnActionOutcome/ReleaseHeldGaze
                Engagement = 1f,
                AllowBodyTurn = false
            });
        }

        /// <summary>
        ///     Whether this character opts into the look-where-you-act glance. Read per event
        ///     rather than cached at registration, so changing the profile takes effect without
        ///     re-enabling the component.
        /// </summary>
        private bool LookAtActionTargetsEnabled
        {
            get
            {
                ConvaiGazeProfile profile = _controller.ActiveProfile;
                return profile != null && profile.EnableLookAtActionTargets;
            }
        }

        /// <summary>
        ///     Travel began while this step was running — the ordinary ordering for a walk, whose
        ///     executor asks for a path a frame or two after the dispatcher announces its target.
        ///     Raised by the controller on the rising edge of travel.
        /// </summary>
        public void OnTravelStarted()
        {
            if (!_hasStepTarget) return;

            ReleaseHeldGaze();
            TryHandOverToTravel();
        }

        /// <inheritdoc />
        public void OnActionOutcome(bool success)
        {
            ReleaseHeldGaze();
            _hasStepTarget = false;
        }

        /// <summary>Releases any held look-where-you-act glance. Safe to call when nothing is active.</summary>
        public void ReleaseHeldGaze()
        {
            _activeHandle?.Release();
            _activeHandle = null;
        }

        /// <summary>
        ///     Offers this step's target as the journey's subject. Returns whether the character is
        ///     travelling, which is also the answer to "should this step hold a stare".
        /// </summary>
        /// <remarks>
        ///     Nothing is provisioned here: a travel intent only exists once something has actually
        ///     moved the character, so a step that never travels finds none and keeps the original
        ///     held-gaze behavior exactly. An executor that declared its own subject (following a
        ///     person) is not overwritten — its subject is the longer-lived truth.
        /// </remarks>
        private bool TryHandOverToTravel()
        {
            ConvaiTravelIntent travel = _controller.GetComponent<ConvaiTravelIntent>();
            if (travel == null || !travel.IsTraveling) return false;

            if (!travel.HasSubject)
                travel.SetSubject(_stepTargetPoint);

            return true;
        }
    }
}
