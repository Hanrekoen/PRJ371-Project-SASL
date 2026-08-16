using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Statistical blink scheduler with a refractory window and gaze-shift triggering
    ///     (large saccades are often accompanied by a blink). Produces a normalized lid
    ///     weight consumed by the eyelid writer.
    /// </summary>
    internal sealed class BlinkDirector
    {
        private enum Phase
        {
            Waiting,
            Closing,
            Opening
        }

        /// <summary>
        ///     Duration (seconds) of the elevated-likelihood window opened by
        ///     <see cref="NotifyClusterCue" /> (end-of-utterance blink clustering) — within
        ///     the ~0.5-1 s spec range for a boundary-marking spike rather than a sustained
        ///     rate change.
        /// </summary>
        private const float ClusterWindowSeconds = 0.7f;

        private Phase _phase = Phase.Waiting;
        private float _countdown;
        private float _phaseTime;
        private float _sinceLastBlink;
        private float _clusterWindowRemaining;
        private float _pendingClusterDelay;

        /// <summary>Emotion-modulation hook: scales the blink frequency.</summary>
        public float RateScale { get; set; } = 1f;

        /// <summary>Current normalized lid weight (0 open, 1 closed).</summary>
        public float Weight { get; private set; }

        /// <summary>
        ///     True while a cognitive-boundary cluster window is elevating the spontaneous
        ///     blink rate. Internal seam for diagnostics/tests.
        /// </summary>
        internal bool ClusterWindowActive => _clusterWindowRemaining > 0f;


        public void Reset(ConvaiGazeProfile profile, ref DeterministicEmbodimentRandom random)
        {
            _phase = Phase.Waiting;
            _phaseTime = 0f;
            _sinceLastBlink = 999f;
            Weight = 0f;
            RateScale = 1f;
            _clusterWindowRemaining = 0f;
            _pendingClusterDelay = 0f;
            _countdown = profile != null ? SampleInterval(profile, ref random) : 4f;
        }

        /// <summary>
        ///     Opens (or extends) the cluster window immediately — used for cues that fire on
        ///     the boundary itself (Speaking-exit edge, an <c>isFinal</c> transcript).
        /// </summary>
        public void NotifyClusterCue() =>
            _clusterWindowRemaining = Mathf.Max(_clusterWindowRemaining, ClusterWindowSeconds);

        /// <summary>
        ///     Schedules the cluster window to open after <paramref name="delaySeconds" /> —
        ///     used for the player-VAD falling edge, which marks the boundary ~300 ms after the
        ///     player actually stops speaking.
        /// </summary>
        public void NotifyDelayedClusterCue(float delaySeconds) =>
            _pendingClusterDelay = Mathf.Max(_pendingClusterDelay, Mathf.Max(0f, delaySeconds));

        public void Tick(ConvaiGazeProfile profile, float deltaTime, ref DeterministicEmbodimentRandom random)
        {
            if (profile == null || !profile.EnableBlink)
            {
                Weight = 0f;
                return;
            }

            _sinceLastBlink += deltaTime;

            if (profile.EnableBlinkClustering)
            {
                TickClusterTimers(deltaTime);
            }
            else
            {
                // Disabling clustering at runtime immediately drops any pending/active cue —
                // there is nothing to "resume" if it is re-enabled later, which keeps the
                // disabled state fully inert rather than merely suppressed.
                _clusterWindowRemaining = 0f;
                _pendingClusterDelay = 0f;
            }

            switch (_phase)
            {
                case Phase.Waiting:
                    float rate = RateScale;
                    if (ClusterWindowActive)
                        rate *= Mathf.Clamp(profile.BlinkClusterRateMultiplier, 1f, 6f);
                    _countdown -= deltaTime * Mathf.Max(0.01f, rate);
                    if (_countdown <= 0f) StartBlink();
                    break;

                case Phase.Closing:
                    _phaseTime += deltaTime;
                    Weight = SmoothStep01(_phaseTime / Mathf.Max(0.01f, profile.BlinkCloseSeconds));
                    if (_phaseTime >= profile.BlinkCloseSeconds)
                    {
                        _phase = Phase.Opening;
                        _phaseTime = 0f;
                    }
                    break;

                case Phase.Opening:
                    _phaseTime += deltaTime;
                    Weight = 1f - SmoothStep01(_phaseTime / Mathf.Max(0.01f, profile.BlinkOpenSeconds));
                    if (_phaseTime >= profile.BlinkOpenSeconds)
                    {
                        _phase = Phase.Waiting;
                        _phaseTime = 0f;
                        Weight = 0f;
                        _sinceLastBlink = 0f;
                        _countdown = SampleInterval(profile, ref random);
                    }
                    break;
            }
        }

        /// <summary>
        ///     Offers a gaze-shift blink for a saccade of <paramref name="amplitudeDegrees" />.
        ///     Fires probabilistically, respecting the refractory window.
        /// </summary>
        public bool TryTriggerShiftBlink(
            ConvaiGazeProfile profile,
            float amplitudeDegrees,
            ref DeterministicEmbodimentRandom random)
        {
            if (profile == null || !profile.EnableBlink) return false;
            if (profile.GazeShiftBlinkThresholdDegrees <= 0f) return false;
            if (amplitudeDegrees < profile.GazeShiftBlinkThresholdDegrees) return false;
            if (_phase != Phase.Waiting) return false;
            if (_sinceLastBlink < profile.BlinkRefractorySeconds) return false;
            if (random.Value >= profile.GazeShiftBlinkProbability) return false;

            StartBlink();
            return true;
        }

        /// <summary>
        ///     Forces a blink for a scripted/reflex beat (e.g. the interruption startle
        ///     reaction) that must read every time it fires rather than being gated by the
        ///     gaze-shift probability roll. Still respects the phase and refractory window —
        ///     it never interrupts a blink already in flight and never fires faster than
        ///     <see cref="ConvaiGazeProfile.BlinkRefractorySeconds" />.
        /// </summary>
        public bool TryTriggerForcedBlink(ConvaiGazeProfile profile)
        {
            if (profile == null || !profile.EnableBlink) return false;
            if (_phase != Phase.Waiting) return false;
            if (_sinceLastBlink < profile.BlinkRefractorySeconds) return false;

            StartBlink();

            // A forced blink IS the deliberate boundary-marking blink the cluster spike exists
            // to produce — the window (and any still-pending delayed cue) is now redundant and,
            // left active, could let a spontaneous spike-rate blink land too close behind it
            // (e.g. at the 6x ceiling: 2.0s floor / 6 = 0.33s, inside the ~1s intended spacing).
            _clusterWindowRemaining = 0f;
            _pendingClusterDelay = 0f;

            return true;
        }

        private void StartBlink()
        {
            _phase = Phase.Closing;
            _phaseTime = 0f;
        }

        /// <summary>
        ///     Advances the pending-delay and cluster-window countdowns. Only called while
        ///     <see cref="ConvaiGazeProfile.EnableBlinkClustering" /> is on — see <see cref="Tick" />,
        ///     which clears both timers outright while the flag is off.
        /// </summary>
        private void TickClusterTimers(float deltaTime)
        {
            if (_pendingClusterDelay > 0f)
            {
                _pendingClusterDelay -= deltaTime;
                if (_pendingClusterDelay <= 0f)
                {
                    _pendingClusterDelay = 0f;
                    _clusterWindowRemaining = Mathf.Max(_clusterWindowRemaining, ClusterWindowSeconds);
                }
            }

            if (_clusterWindowRemaining > 0f)
                _clusterWindowRemaining = Mathf.Max(0f, _clusterWindowRemaining - deltaTime);
        }

        private static float SampleInterval(ConvaiGazeProfile profile, ref DeterministicEmbodimentRandom random)
        {
            float jitter = random.Range(-profile.BlinkIntervalJitter, profile.BlinkIntervalJitter);
            return Mathf.Max(profile.BlinkRefractorySeconds, profile.BlinkIntervalMean + jitter);
        }

        private static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
