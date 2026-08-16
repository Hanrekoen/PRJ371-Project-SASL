using Convai.Modules.Gaze.Core.Policy;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Fixational eye micro-behavior: slow drift, discrete micro-saccades, and a tiny
    ///     tremor — the difference between living eyes and a dead stare. Produces a small
    ///     yaw/pitch offset (degrees) that rides on top of fixations and pursuit.
    /// </summary>
    internal sealed class FixationMicroMotion
    {
        private Vector2 _microSaccadeOffset;
        private float _microCountdown;
        private float _driftPhaseX;
        private float _driftPhaseY;
        private float _time;

        /// <summary>Current combined offset (degrees).</summary>
        public Vector2 Offset { get; private set; }

        public void Reset(ref DeterministicEmbodimentRandom random)
        {
            _microSaccadeOffset = Vector2.zero;
            _microCountdown = 0f;
            _driftPhaseX = random.Range(0f, 100f);
            _driftPhaseY = random.Range(0f, 100f);
            _time = 0f;
            Offset = Vector2.zero;
        }

        /// <summary>
        ///     Ticks the fixation micro-behavior for one frame.
        /// </summary>
        /// <param name="tempoScale">
        ///     Emotion-driven saccade tempo multiplier (<see cref="EmotionGazeModulator.SaccadeTempoScale" />,
        ///     1 = unmodified). Scales how fast the micro-saccade dwell countdown depletes —
        ///     &gt;1 (quicker tempo, e.g. joy) shortens dwell between micro-saccades for a more
        ///     lively fixation; &lt;1 (slower tempo, e.g. sadness) lengthens it.
        /// </param>
        public void Tick(ConvaiGazeProfile profile, float deltaTime, float tempoScale, ref DeterministicEmbodimentRandom random)
        {
            if (profile == null)
            {
                Offset = Vector2.zero;
                return;
            }

            _time += deltaTime;

            // Slow ocular drift on two incommensurate frequencies (never visibly loops).
            float w = 2f * Mathf.PI * profile.FixationDriftFrequency;
            var drift = new Vector2(
                Mathf.Sin(_time * w + _driftPhaseX),
                Mathf.Sin(_time * w * 0.83f + _driftPhaseY)) * profile.FixationDriftDegrees;

            // Discrete micro-saccades: small jumps to a new offset that then holds. Tempo
            // scales the countdown's depletion rate, not the sampled interval itself, so the
            // authored mean/jitter stay meaningful and only the effective pacing shifts.
            _microCountdown -= deltaTime * Mathf.Max(0.01f, tempoScale);
            if (_microCountdown <= 0f)
            {
                _microCountdown = Mathf.Max(0.1f,
                    profile.MicroSaccadeIntervalMean +
                    random.Range(-profile.MicroSaccadeIntervalJitter, profile.MicroSaccadeIntervalJitter));

                float angle = random.Range(0f, 2f * Mathf.PI);
                float magnitude = random.Range(0.3f, 1f) * profile.MicroSaccadeAmplitudeDegrees;
                _microSaccadeOffset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * magnitude;
            }

            Offset = drift + _microSaccadeOffset;
        }
    }
}
