using Convai.Modules.BodyLanguage.Core.Policy;
using Convai.Modules.BodyLanguage.Data;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Behaviors
{
    /// <summary>
    ///     Cognition-tick POCO that turns the smoothed per-state policy plus emotion modulation
    ///     into the posture solver's continuous targets: openness, sagittal lean, and shoulder
    ///     tension. Targets move over seconds via an exponential slew — the spring
    ///     inside <see cref="Core.Pose.PostureSolver" /> adds the physical settle on top, so a
    ///     state or emotion change never snaps the pose.
    /// </summary>
    internal sealed class PostureDirector
    {
        private float _openness;
        private float _lean;
        private float _tension;
        private bool _initialized;

        /// <summary>Slewed openness target, -1..1.</summary>
        public float OpennessTarget => _openness;

        /// <summary>Slewed sagittal lean target, -1..1.</summary>
        public float LeanTarget => _lean;

        /// <summary>Slewed shoulder tension target, -1..1.</summary>
        public float TensionTarget => _tension;

        public void Reset()
        {
            _openness = 0f;
            _lean = 0f;
            _tension = 0f;
            _initialized = false;
        }

        /// <summary>
        ///     Advances the posture target toward the state policy's bias plus the blended
        ///     emotion bias, over <paramref name="targetSlewSeconds" /> (time constant; the
        ///     first tick snaps so a character never eases in from a zeroed pose on enable).
        /// </summary>
        public void Tick(
            in BodyLanguageStatePolicy statePolicy,
            EmotionBodyModulator emotion,
            float targetSlewSeconds,
            float deltaTime)
        {
            float opennessGoal = Mathf.Clamp(statePolicy.PostureOpennessBias + emotion.OpennessBias, -1f, 1f);
            float leanGoal = Mathf.Clamp(statePolicy.SagittalLeanBias + emotion.LeanBias, -1f, 1f);
            float tensionGoal = Mathf.Clamp(emotion.ShoulderTensionBias, -1f, 1f);

            if (!_initialized || targetSlewSeconds <= 0f || deltaTime <= 0f)
            {
                _openness = opennessGoal;
                _lean = leanGoal;
                _tension = tensionGoal;
                _initialized = true;
                return;
            }

            float alpha = 1f - Mathf.Exp(-deltaTime / targetSlewSeconds);
            _openness += (opennessGoal - _openness) * alpha;
            _lean += (leanGoal - _lean) * alpha;
            _tension += (tensionGoal - _tension) * alpha;
        }
    }
}
