using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.Emotion.Components;
using UnityEngine;

namespace Convai.Modules.Emotion.Integrations
{
    /// <summary>
    ///     Realism-pack outcome mood beat : nudges the
    ///     character's mood briefly toward a "satisfied" or "frustrated" label (configurable on
    ///     <see cref="ConvaiEmotionController" />) after an action step's outcome, riding the
    ///     existing <c>SetMood</c>/<c>ClearMood</c> runtime mood layer rather than duplicating it.
    /// </summary>
    /// <remarks>
    ///     Registered by <see cref="ConvaiEmotionController" /> itself on enable, unregistered on
    ///     disable — auto-wired, no user setup required. Inert unless the actions dispatcher's
    ///     Performance toggle is on and a reactor is registered, matching every other seam in this
    ///     realism pack.
    /// </remarks>
    internal sealed class ActionPerformanceMoodReactor : IActionPerformanceReactor
    {
        private readonly ConvaiEmotionController _controller;

        public ActionPerformanceMoodReactor(ConvaiEmotionController controller) => _controller = controller;

        /// <summary>No batch-start reaction for Emotion; Body Language owns the acknowledgment nod.</summary>
        public void OnActionBatchStarted()
        {
        }

        /// <summary>Look-where-you-act is Gaze's responsibility, not Emotion's.</summary>
        public void OnActionTargetAcquired(string targetName, Vector3 worldPosition)
        {
        }

        /// <inheritdoc />
        public void OnActionOutcome(bool success) => _controller.ReactToActionOutcome(success);
    }
}
