using Convai.Modules.BodyLanguage.Data;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Policy
{
    /// <summary>
    ///     Smooths per-state <see cref="BodyLanguageStatePolicy" /> transitions so a dialogue
    ///     state change never snaps posture, breath, or gesture targets. Scalar
    ///     policy values blend exponentially over the profile's transition time; boolean gates
    ///     switch immediately (the downstream directors own their own ramps).
    /// </summary>
    /// <remarks>
    ///     Deterministic and allocation-free: identical (policy, deltaTime) sequences always
    ///     produce identical smoothed values. Modeled on the gaze module's policy engine.
    /// </remarks>
    internal sealed class BodyLanguagePolicyEngine
    {
        private BodyLanguageStatePolicy _current;
        private bool _initialized;
        private float _holdRemaining;

        /// <summary>Policy targeted on the last tick (for diagnostics).</summary>
        public BodyLanguageStatePolicy Target { get; private set; }

        /// <summary>The smoothed policy currently in effect.</summary>
        public BodyLanguageStatePolicy Current => _current;

        /// <summary>Whether a freeze hold is currently in effect (diagnostics).</summary>
        public bool IsHolding => _holdRemaining > 0f;

        /// <summary>
        ///     Freezes the smoothed scalar policy values for <paramref name="seconds" /> — the
        ///     "freeze 0.3s → re-engage" beat the Interrupted row calls for. During a
        ///     hold, <see cref="Tick" /> keeps returning the current scalars unchanged while
        ///     still snapping the boolean gates (so gesticulation still hard-stops the instant
        ///     the state changes); blending resumes when the hold elapses. A longer request never
        ///     shortens an in-flight hold.
        /// </summary>
        public void BeginHold(float seconds)
        {
            if (seconds > _holdRemaining) _holdRemaining = seconds;
        }

        /// <summary>
        ///     Advances the smoothed policy toward <paramref name="target" /> over
        ///     <paramref name="transitionSeconds" /> (time constant; 0 or less snaps) and
        ///     returns the blended result. The first tick snaps so a character never eases in
        ///     from a zeroed pose on enable.
        /// </summary>
        public BodyLanguageStatePolicy Tick(
            in BodyLanguageStatePolicy target, float transitionSeconds, float deltaTime)
        {
            Target = target;

            if (!_initialized || transitionSeconds <= 0f || deltaTime <= 0f)
            {
                _current = target;
                _initialized = true;
                _holdRemaining = 0f;
                return _current;
            }

            // Boolean gates and the state label always follow the target immediately — even
            // during a freeze hold, so gesticulation/fidgets hard-stop the instant the state
            // changes (a frozen character must not keep gesturing).
            _current.State = target.State;
            _current.GesticulationEnabled = target.GesticulationEnabled;
            _current.ListeningPostureEnabled = target.ListeningPostureEnabled;
            _current.FidgetsEnabled = target.FidgetsEnabled;

            if (_holdRemaining > 0f)
            {
                // Freeze: hold the scalar posture/breath values steady (the "hard pause" beat),
                // resume blending once the hold elapses.
                _holdRemaining -= deltaTime;
                return _current;
            }

            float alpha = 1f - Mathf.Exp(-deltaTime / transitionSeconds);

            _current.GesticulationIntensity += (target.GesticulationIntensity - _current.GesticulationIntensity) * alpha;
            _current.ListeningLeanIn += (target.ListeningLeanIn - _current.ListeningLeanIn) * alpha;
            _current.PostureOpennessBias += (target.PostureOpennessBias - _current.PostureOpennessBias) * alpha;
            _current.SagittalLeanBias += (target.SagittalLeanBias - _current.SagittalLeanBias) * alpha;
            _current.AmbientDrift += (target.AmbientDrift - _current.AmbientDrift) * alpha;
            _current.BreathRateCpm += (target.BreathRateCpm - _current.BreathRateCpm) * alpha;
            _current.BreathDepth += (target.BreathDepth - _current.BreathDepth) * alpha;
            _current.BreathIrregularity += (target.BreathIrregularity - _current.BreathIrregularity) * alpha;
            _current.FidgetRate += (target.FidgetRate - _current.FidgetRate) * alpha;

            return _current;
        }

        /// <summary>Restores the engine to its uninitialized state (next tick snaps).</summary>
        public void Reset()
        {
            _current = default;
            Target = default;
            _initialized = false;
            _holdRemaining = 0f;
        }
    }
}
