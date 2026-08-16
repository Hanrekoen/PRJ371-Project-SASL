using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Conversational face scanning: while gazing at the player (or another face), the
    ///     fixation point wanders in small saccades between implied face landmarks — left
    ///     eye, right eye, mouth — instead of freezing on one pixel. Output is a small
    ///     yaw/pitch offset (degrees) around the target point.
    /// </summary>
    /// <remarks>
    ///     Listener mouth-bias: while the player is speaking, human listeners fixate the
    ///     speaker's mouth more (speech comprehension). <see cref="Tick" /> takes the raw
    ///     player-speaking flag and smooths it into <see cref="MouthBiasFactor" /> (0→1 over
    ///     ~<see cref="MouthBiasBlendSeconds" />), which then scales up the mouth landmark's
    ///     selection weight in the weighted pick below. At a bias factor of exactly 0 (player
    ///     silent, blend settled) the mouth weight is exactly 1, so the pick is uniform and
    ///     each landmark is visited equally often. Note the never-repeat rule
    ///     bounds what any bias can achieve: the mouth is only reachable from an eye, so its
    ///     stationary share is capped below 1/2 (strength 2 → 0.4) even at full bias.
    /// </remarks>
    internal sealed class FaceScanDirector
    {
        /// <summary>Seconds for the mouth-bias factor to ramp fully in or out.</summary>
        private const float MouthBiasBlendSeconds = 0.5f;

        private const int MouthLandmarkIndex = 2;

        // Landmark triangle in normalized units of the scan radius:
        // slightly up-left (their right eye), up-right (their left eye), down-center (mouth).
        private static readonly Vector2[] Landmarks =
        {
            new(-0.55f, 0.35f),
            new(0.55f, 0.35f),
            new(0f, -1f)
        };

        private int _landmarkIndex = -1;
        private float _elapsed;
        private float _interval;
        private bool _wasActive;
        private float _mouthBiasFactor;

        /// <summary>Current landmark offset (degrees).</summary>
        public Vector2 Offset { get; private set; }

        /// <summary>
        ///     Smoothed 0→1 listener mouth-bias factor (0 = every landmark equally likely, 1 = full bias).
        /// </summary>
        public float MouthBiasFactor => _mouthBiasFactor;

        public void Reset()
        {
            _landmarkIndex = -1;
            _elapsed = 0f;
            _interval = 0f;
            _wasActive = false;
            _mouthBiasFactor = 0f;
            Offset = Vector2.zero;
        }

        /// <param name="profile">Gaze profile supplying face-scan and mouth-bias tuning.</param>
        /// <param name="deltaTime">Frame delta time (seconds).</param>
        /// <param name="active">Whether face scanning should run this tick.</param>
        /// <param name="playerSpeaking">Raw (unsmoothed) player-speaking flag for the mouth bias.</param>
        /// <param name="random">Shared deterministic RNG.</param>
        public void Tick(
            ConvaiGazeProfile profile,
            float deltaTime,
            bool active,
            bool playerSpeaking,
            ref DeterministicEmbodimentRandom random)
        {
            if (profile == null || !profile.EnableFaceScan || !active)
            {
                if (_wasActive) Reset();
                return;
            }

            if (!_wasActive)
            {
                _wasActive = true;
                _elapsed = 0f;
                _interval = 0f; // pick the first landmark immediately
            }

            // Smooth the raw speaking flag into a 0→1 bias factor over ~0.5s, every tick,
            // regardless of the landmark pick cadence below.
            float mouthBiasTarget = profile.EnableListenerMouthBias && playerSpeaking ? 1f : 0f;
            _mouthBiasFactor = Mathf.MoveTowards(
                _mouthBiasFactor,
                mouthBiasTarget,
                deltaTime / MouthBiasBlendSeconds);

            _elapsed += deltaTime;
            if (_elapsed < _interval) return;

            _elapsed = 0f;
            _interval = Mathf.Max(0.3f,
                profile.FaceScanIntervalMean +
                random.Range(-profile.FaceScanIntervalJitter, profile.FaceScanIntervalJitter));

            // Weighted pick with the never-repeat rule applied as *exclusion*: the current
            // landmark is removed from the candidate set before the draw. Redirecting a
            // colliding draw after the fact (e.g. rotating to the next index) would both
            // dilute the mouth bias and skew the two eyes asymmetrically, because the
            // redirect direction is fixed. Eyes always weight 1; the mouth weight scales up
            // to profile.ListenerMouthBiasStrength as the bias factor approaches 1, so at a
            // bias factor of exactly 0 the draw is uniform over the candidates — an even scan
            // of the whole face. Exactly one RNG draw per pick, no arrays/allocations.
            float mouthWeight = Mathf.Max(0f, 1f + _mouthBiasFactor * (profile.ListenerMouthBiasStrength - 1f));
            const float eyeWeight = 1f;

            int next;
            if (_landmarkIndex < 0)
            {
                // First pick after (re)activation: all three landmarks are candidates.
                float r = random.Value * (eyeWeight + eyeWeight + mouthWeight);
                if (r < eyeWeight) next = 0;
                else if (r < eyeWeight + eyeWeight) next = 1;
                else next = MouthLandmarkIndex;
            }
            else
            {
                // The two landmarks other than the current one; candidateA is always an eye.
                int candidateA = _landmarkIndex == 0 ? 1 : 0;
                int candidateB = _landmarkIndex == MouthLandmarkIndex ? 1 : MouthLandmarkIndex;
                float weightB = candidateB == MouthLandmarkIndex ? mouthWeight : eyeWeight;
                next = random.Value * (eyeWeight + weightB) < eyeWeight ? candidateA : candidateB;
            }

            _landmarkIndex = next;

            Offset = Landmarks[_landmarkIndex] * profile.FaceScanRadiusDegrees;
        }
    }
}
