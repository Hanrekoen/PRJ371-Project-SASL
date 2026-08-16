using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.BodyLanguage.Components;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Integrations
{
    /// <summary>
    ///     The acknowledgment beat: on action-batch start, requests a subtle scripted nod
    ///     through the controller's existing
    ///     <see cref="ConvaiBodyLanguageController.Nod" /> API — the "I heard you, I'm on it"
    ///     moment before the character acts.
    /// </summary>
    /// <remarks>
    ///     Registered by <see cref="ConvaiBodyLanguageController" /> itself on enable, unregistered
    ///     on disable — auto-wired, no user setup required. Inert unless the actions dispatcher's
    ///     Performance toggle is on and a reactor is registered, matching every other seam that
    ///     makes an action read as performed rather than merely executed.
    /// </remarks>
    internal sealed class ActionPerformanceNodReactor : IActionPerformanceReactor
    {
        private readonly ConvaiBodyLanguageController _controller;
        private readonly float _intensity;

        public ActionPerformanceNodReactor(ConvaiBodyLanguageController controller, float intensity)
        {
            _controller = controller;
            _intensity = intensity;
        }

        /// <inheritdoc />
        public void OnActionBatchStarted() => _controller.Nod(HeadGestureKind.Nod, _intensity);

        /// <summary>Look-where-you-act is Gaze's responsibility, not Body Language's.</summary>
        public void OnActionTargetAcquired(string targetName, Vector3 worldPosition)
        {
        }

        /// <summary>No per-outcome reaction for Body Language; Emotion owns the mood beat.</summary>
        public void OnActionOutcome(bool success)
        {
        }
    }
}
