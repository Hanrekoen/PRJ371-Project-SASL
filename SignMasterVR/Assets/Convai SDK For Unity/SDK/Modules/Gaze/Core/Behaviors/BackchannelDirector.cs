using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.Gaze.Core.Behaviors
{
    /// <summary>
    ///     Active-listening head gesture: while the character listens it produces small
    ///     acknowledgment nods — one likely nod when <c>Listening</c> begins ("I'm with you")
    ///     and sparse nods on a randomized cadence afterwards. The result is a pitch-dominant
    ///     angular offset fed to the head/torso solver's gesture channel; nothing plays outside
    ///     <c>Listening</c> or while the gesture is suppressed (the character is producing
    ///     speech, or has no one to nod at).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The nod envelope is a procedural damped double-bob (two downward lobes, the
    ///         second ~55% of the first) that begins and ends exactly at rest, so it never
    ///         needs authored clips or curves and never pops the head goal.
    ///     </para>
    ///     <para>
    ///         The offset is applied to the bones post-spring, so this director guarantees a
    ///         continuous output: an active envelope is C¹-smooth by construction, and every
    ///         cancellation path (state exit, suppression) fades the residual offset to rest
    ///         at a bounded rate instead of stepping.
    ///     </para>
    ///     <para>
    ///         Suppression pauses without re-arming: only leaving <c>Listening</c> re-rolls
    ///         the acknowledgment nod, so a brief speech-energy flicker mid-listening cannot
    ///         fire spurious entry nods. <see cref="TriggerNod" /> is a public seam so a
    ///         future driver (e.g. player microphone energy) can request a nod on a point
    ///         without touching the scheduling here.
    ///     </para>
    /// </remarks>
    internal sealed class BackchannelDirector
    {
        // (1 - cos(4πp))/2 peaks at 1 for each lobe; multiplied by the exp(-1.2p) decay the
        // first lobe crests near p = 0.25 at ≈ 0.75. Dividing by that lifts the first bob back
        // to the configured peak amplitude and leaves the second lobe at ≈ 0.55 of it.
        private const float NodShapeNormalization = 1.34f;
        private const float NodDecay = 1.2f;

        /// <summary>
        ///     Fade rate (degrees/second) for a nod cancelled mid-flight. The offset feeds the
        ///     bones after the smoothing springs, so cancellations must ease out — a step here
        ///     would pop the head. ~45°/s clears a full-amplitude residual in under 0.1 s.
        /// </summary>
        private const float CancelFadeDegreesPerSecond = 45f;

        private bool _active;
        private float _elapsed;
        private float _countdown;
        private bool _wasListening;
        private float _intensity = 1f;
        private Vector2 _offset;
        private bool _startedThisTick;

        /// <summary>Current head gesture offset (yaw/pitch degrees, pitch-dominant); zero at rest.</summary>
        public Vector2 GestureOffset => _offset;

        /// <summary>Whether a nod envelope is currently playing (diagnostics/tests).</summary>
        public bool IsNodding => _active;

        /// <summary>
        ///     True only on the tick a nod begins (entry or cadence) — a one-shot pulse for
        ///     consumers that want to react to a nod starting (e.g. the brow-cue
        ///     coordinator), mirroring <c>InterruptionReactionDirector.WantsReacquisition</c>'s
        ///     single-tick pulse contract.
        /// </summary>
        public bool NodStartedThisTick => _startedThisTick;

        public void Reset()
        {
            _active = false;
            _elapsed = 0f;
            _countdown = 0f;
            _wasListening = false;
            _intensity = 1f;
            _offset = Vector2.zero;
            _startedThisTick = false;
        }

        /// <param name="profile">Tuning source; a null profile disables the director.</param>
        /// <param name="isListening">Whether the dialogue state is <c>Listening</c>.</param>
        /// <param name="suppressed">
        ///     Hard-pause without re-arming: the character is producing speech, or has no
        ///     engaged target to nod at. Any active nod fades out; the schedule freezes.
        /// </param>
        public void Tick(
            ConvaiGazeProfile profile,
            bool isListening,
            bool suppressed,
            float deltaTime,
            ref DeterministicEmbodimentRandom random)
        {
            _startedThisTick = false;

            bool canRun = profile != null && profile.EnableListeningNods && isListening;

            // Only leaving Listening re-arms the acknowledgment nod for the next entry;
            // suppression mid-listening must not re-roll it.
            if (!canRun)
                _wasListening = false;

            if (!canRun || suppressed)
            {
                if (_active)
                {
                    // The cancelled nod counts as delivered — resample the cadence so the
                    // schedule does not fire a make-up nod the instant suppression lifts.
                    _active = false;
                    _countdown = profile != null ? SampleInterval(profile, ref random) : 0f;
                }

                FadeOut(deltaTime);
                return;
            }

            if (!_wasListening)
            {
                _wasListening = true;
                if (random.Value < profile.AcknowledgeNodProbability)
                    StartNod(1f);
                else
                    _countdown = SampleInterval(profile, ref random);
            }

            if (_active)
            {
                _elapsed += deltaTime;
                float p = _elapsed / Mathf.Max(0.05f, profile.NodDurationSeconds);
                if (p >= 1f)
                {
                    // Shape(1) is exactly 0, so ending here is continuous.
                    _active = false;
                    _offset = Vector2.zero;
                    _countdown = SampleInterval(profile, ref random);
                }
                else
                {
                    _offset = new Vector2(0f, -profile.NodPitchDegrees * _intensity * Shape(p));
                }
                return;
            }

            // Idle between nods: finish any residual fade from a cancellation, run the cadence.
            FadeOut(deltaTime);
            _countdown -= deltaTime;
            if (_countdown <= 0f)
                StartNod(1f);
        }

        /// <summary>
        ///     Requests a nod at the given intensity (0..1) on the next tick. Public seam for
        ///     future drivers; still suppressed unless the character is actively listening.
        /// </summary>
        public void TriggerNod(float intensity01) => StartNod(Mathf.Clamp01(intensity01));

        private void StartNod(float intensity)
        {
            _active = true;
            _elapsed = 0f;
            _intensity = Mathf.Clamp01(intensity);
            _startedThisTick = true;
        }

        private void FadeOut(float deltaTime) =>
            _offset = Vector2.MoveTowards(_offset, Vector2.zero, CancelFadeDegreesPerSecond * deltaTime);

        private static float SampleInterval(ConvaiGazeProfile profile, ref DeterministicEmbodimentRandom random) =>
            random.Range(profile.ListeningNodIntervalMin, profile.ListeningNodIntervalMax);

        /// <summary>
        ///     Damped double-bob over normalized phase <paramref name="p" /> ∈ [0,1]: two
        ///     downward lobes eased to rest at both ends (value and first derivative zero), the
        ///     second ~55% of the first. Returns a non-negative 0..~1 magnitude. Internal so
        ///     the C¹ endpoints can be asserted directly.
        /// </summary>
        internal static float Shape(float p)
        {
            p = Mathf.Clamp01(p);
            float lobes = (1f - Mathf.Cos(p * 4f * Mathf.PI)) * 0.5f;
            float decay = Mathf.Exp(-NodDecay * p);
            return lobes * decay * NodShapeNormalization;
        }
    }
}
