using Convai.Runtime.Embodiment;
using Convai.Modules.BodyLanguage.Core.Behaviors;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Core.Pose
{
    /// <summary>Per-frame input for <see cref="BreathSolver.Solve" />.</summary>
    internal struct BreathSolveInput
    {
        public float DeltaTime;

        /// <summary>Breathing rate in cycles per minute (director-smoothed; changes freely without a phase reset).</summary>
        public float RateCpm;

        /// <summary>Breathing depth 0..1.</summary>
        public float Depth;

        /// <summary>Breathing irregularity 0..1: per-cycle rate jitter plus, near 1, occasional hold plateaus.</summary>
        public float Irregularity;

        /// <summary>Master weight 0..1 — see <see cref="PostureSolveInput.MasterWeight" /> for the contract.</summary>
        public float MasterWeight;

        /// <summary>Chest/upper-chest expansion (degrees) a full-depth inhale maps to.</summary>
        public float MaxChestExpansionDegrees;

        /// <summary>Shoulder lift (degrees) a full-depth inhale maps to.</summary>
        public float MaxShoulderLiftDegrees;

        /// <summary>Phase-aware respiratory command currently owned by the breathing director.</summary>
        public BreathEventKind EventKind;
    }

    /// <summary>
    ///     Phase-continuous breathing oscillator: shapes a small additive chest/upper-chest
    ///     expansion swing plus a subtle shoulder lift, waveformed so inhale reads faster than
    ///     exhale (<c>BreathingDirector</c> is the target source). Rate, depth, and
    ///     irregularity are free-running inputs — none of them ever resets the phase, so state
    ///     changes and clip crossfades never pop the breath. This solver is a pure signal
    ///     shaper: it never touches a <see cref="Transform" />. <see cref="ChestSagittalDegrees" />,
    ///     <see cref="ShoulderLiftDegrees" /> (clavicle-led — the shoulders peak slightly before
    ///     the chest), and <see cref="ChestLateralDegrees" /> (a quarter-cycle-shifted lateral
    ///     ribcage component) are the shaped outputs; the owning controller's
    ///     <c>ProceduralPoseCompositor</c> is the single writer that turns them into bone deltas.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Irregularity is expressed as smooth per-cycle rate jitter drawn once per cycle
    ///         from a seeded <see cref="DeterministicEmbodimentRandom" /> stream (deterministic
    ///         given the same seed and cycle count) plus, at high irregularity, an occasional
    ///         held plateau at the top of the inhale — the "irregular, held" breath
    ///         Thinking row specifies.
    ///     </para>
    ///     <para>
    ///         The waveform itself is not a bare sine: inhale and exhale each ease with zero
    ///         velocity at both the trough and the peak, but inhale spends less of the cycle's
    ///         duration (<see cref="InhaleFraction" />) covering the same swing, so it still
    ///         reads faster than the exhale — without the velocity discontinuity a
    ///         start-fast ease would otherwise leave at the trough. Distribution across the
    ///         spine chain is the compositor's concern (it owns the chain's redistribution
    ///         weights), not this solver's.
    ///     </para>
    /// </remarks>
    internal sealed class BreathSolver
    {
        private const float TwoPi = Mathf.PI * 2f;

        /// <summary>Fraction of the cycle spent on the (faster) inhale half before easing into exhale.</summary>
        private const float InhaleFraction = 0.4f;

        /// <summary>Above this irregularity the top of the inhale may hold briefly before releasing.</summary>
        private const float HoldPlateauIrregularityThreshold = 0.55f;

        /// <summary>
        ///     Clavicle-lead phase offset (radians): the shoulders peak slightly BEFORE the
        ///     chest. Evaluating the shoulder waveform at <c>phase + lead</c>
        ///     means the shoulder reaches the same waveform value at an earlier <see cref="_phase" />
        ///     than the chest does — i.e. chronologically earlier. Feel-pass tunable.
        /// </summary>
        private const float ClavicleLeadRadians = 0.025f * TwoPi;

        /// <summary>Conservative share of authored expansion represented by generic spine rotation.</summary>
        private const float ChestRotationShare = 0.58f;
        private const float AccessoryMuscleStart = 0.62f;

        private float _phase;
        private int _cycleIndex;
        private float _cycleRateJitter;
        private float _holdRemaining;
        private bool _peakHoldEvaluated;
        private BreathEventKind _lastEventKind;
        private bool _eventInhaleActive;
        private float _eventInhaleElapsed;
        private float _eventInhaleDuration;
        private float _eventInhaleStartVolume;
        private float _lastVolume;
        private DeterministicEmbodimentRandom _random;
        private bool _randomSeeded;

        /// <summary>Current oscillator phase in radians, wrapped to <c>[0, 2π)</c>.</summary>
        public float Phase => _phase;

        /// <summary>
        ///     Current waveform value, -1 (full exhale) .. 1 (full inhale peak). Starts at -1
        ///     (the trough of the cycle) so the first solved frame never jumps from an artificial 0 —
        ///     matching what <see cref="Reset" /> restores.
        /// </summary>
        public float Waveform { get; private set; } = -1f;

        /// <summary>Completed breath cycles since the last <see cref="Reset" /> — advances the per-cycle irregularity draw.</summary>
        public int CycleIndex => _cycleIndex;

        /// <summary>Chest/upper-chest sagittal swing (degrees) this tick's waveform maps to — the shaped signal a compositor writes.</summary>
        public float ChestSagittalDegrees { get; private set; }

        /// <summary>Shoulder lift (degrees) this tick's waveform maps to — same sign on both shoulders; the shaped signal a compositor writes.</summary>
        public float ShoulderLiftDegrees { get; private set; }

        /// <summary>
        ///     Lateral ribcage swing (degrees) this tick — quarter-cycle out of phase with the
        ///     sagittal chest swing, so full-cycle breathing traces a subtle ellipse rather than
        ///     a pure fore-aft motion. Composed into the spine chain's lateral
        ///     channel alongside posture's own lateral output.
        /// </summary>
        public float ChestLateralDegrees { get; private set; }

        public void Seed(uint seed)
        {
            _random = new DeterministicEmbodimentRandom(seed);
            _randomSeeded = true;
        }

        public void Reset()
        {
            _phase = 0f;
            _cycleIndex = 0;
            _cycleRateJitter = 0f;
            _holdRemaining = 0f;
            _peakHoldEvaluated = false;
            _lastEventKind = BreathEventKind.None;
            _eventInhaleActive = false;
            _eventInhaleElapsed = 0f;
            _lastVolume = 0f;
            Waveform = -1f;
            ChestSagittalDegrees = 0f;
            ShoulderLiftDegrees = 0f;
            ChestLateralDegrees = 0f;
        }

        public void Solve(in BreathSolveInput input)
        {
            if (!_randomSeeded)
            {
                _random = new DeterministicEmbodimentRandom(0x8ADEA7u);
                _randomSeeded = true;
            }

            float dt = input.DeltaTime > 0f ? input.DeltaTime : 1f / 60f;
            float weight = Mathf.Clamp01(input.MasterWeight);
            float irregularity = Mathf.Clamp01(input.Irregularity);
            float depth = Mathf.Clamp01(input.Depth) * weight;

            BeginEventIfNeeded(input.EventKind);

            float volume;
            if (_eventInhaleActive)
            {
                _eventInhaleElapsed += dt;
                float eventT = Mathf.Clamp01(_eventInhaleElapsed / _eventInhaleDuration);
                volume = Mathf.Lerp(_eventInhaleStartVolume, 1f, SmootherStep(eventT));
                if (eventT >= 1f)
                {
                    _phase = TwoPi * InhaleFraction;
                    _peakHoldEvaluated = true;
                    _eventInhaleActive = false;
                }
            }
            else
            {
                AdvancePhase(input.RateCpm, irregularity, dt);
                volume = ShapeVolume(_phase);
            }
            _lastVolume = volume;
            Waveform = (volume * 2f - 1f) * depth;

            // Exhalation returns to neutral; it never rocks the chest through the authored pose.
            ChestSagittalDegrees = -volume * depth * input.MaxChestExpansionDegrees * ChestRotationShare;

            // Clavicle lead: the shoulders peak slightly before the chest — same depth scaling,
            // just a phase-shifted read of the same waveform shape.
            float shoulderVolume = ShapeVolume(WrapPhase(_phase + ClavicleLeadRadians));
            float accessoryDrive = SmootherStep(Mathf.InverseLerp(AccessoryMuscleStart, 1f, shoulderVolume));
            ShoulderLiftDegrees = accessoryDrive * depth * input.MaxShoulderLiftDegrees;

            // Generic spine rotation cannot represent rib-cage width safely. The old
            // phase-shifted lateral ellipse was the main oblique-view tremor source.
            ChestLateralDegrees = 0f;
        }

        private void BeginEventIfNeeded(BreathEventKind eventKind)
        {
            if (eventKind == _lastEventKind) return;
            _lastEventKind = eventKind;

            if (eventKind != BreathEventKind.CatchBreath &&
                eventKind != BreathEventKind.InhaleBeforeSpeaking &&
                eventKind != BreathEventKind.SpeechGapInhale &&
                eventKind != BreathEventKind.Sigh)
                return;

            _eventInhaleActive = true;
            _eventInhaleElapsed = 0f;
            _eventInhaleStartVolume = _lastVolume;
            _eventInhaleDuration = eventKind switch
            {
                BreathEventKind.CatchBreath => 0.22f,
                BreathEventKind.Sigh => 0.9f,
                BreathEventKind.SpeechGapInhale => 0.32f,
                _ => 0.34f
            };
        }

        /// <summary>
        ///     Advances phase from the rate input; irregularity draws a fresh per-cycle rate
        ///     multiplier on every phase wrap (deterministic given the seed and cycle count)
        ///     and, above the hold threshold, may pause phase advance briefly at the inhale
        ///     peak — a held breath, never a phase discontinuity.
        /// </summary>
        private void AdvancePhase(float rateCpm, float irregularity, float dt)
        {
            if (_holdRemaining > 0f)
            {
                _holdRemaining -= dt;
                return;
            }

            float rate = Mathf.Max(0.5f, rateCpm);
            float cyclesPerSecond = rate / 60f;
            float baseAngularSpeed = cyclesPerSecond * TwoPi;

            // Jitter scales the instantaneous rate by up to ±30% at full irregularity —
            // smooth because it only changes once per cycle, never mid-breath.
            float jitterScale = 1f + _cycleRateJitter * irregularity * 0.3f;
            float angularSpeed = baseAngularSpeed * Mathf.Max(0.1f, jitterScale);

            float previousPhase = _phase;
            float nextPhase = _phase + angularSpeed * dt;
            float inhalePeak = TwoPi * InhaleFraction;

            if (!_peakHoldEvaluated && previousPhase < inhalePeak && nextPhase >= inhalePeak)
            {
                _peakHoldEvaluated = true;
                float chance = Mathf.Max(0f, irregularity - HoldPlateauIrregularityThreshold) * 0.6f;
                if (_random.Value < chance)
                {
                    _phase = inhalePeak;
                    _holdRemaining = _random.Range(0.15f, 0.45f);
                    return;
                }
            }

            _phase = nextPhase;

            if (_phase >= TwoPi)
            {
                _phase %= TwoPi;
                _cycleIndex++;
                _cycleRateJitter = _random.Range(-1f, 1f);
                _peakHoldEvaluated = false;
            }
            else if (previousPhase > _phase)
            {
                // Guards against a pathological single-frame overshoot past a full cycle.
                _cycleIndex++;
            }
        }

        /// <summary>
        ///     Shapes the phase into a -1..1 waveform where inhale (the first
        ///     <see cref="InhaleFraction" /> of the cycle) reads faster than the exhale — not
        ///     because either half's ease has nonzero endpoint velocity, but because inhale
        ///     covers the same -1..+1 swing in less of the cycle's duration (0.4 vs 0.6). Both
        ///     halves use <see cref="EaseInOutQuad" />, which has zero derivative at both of its
        ///     own endpoints, so the waveform is velocity-continuous (C1-ish) at the trough
        ///     (cycle wrap 1→0) and at the peak (inhale→exhale handoff) alike — no per-cycle
        ///     "kick" into the inhale.
        /// </summary>
        private static float ShapeVolume(float phase)
        {
            float t = phase / TwoPi; // 0..1 across the whole cycle

            if (t < InhaleFraction)
            {
                float inhaleT = t / InhaleFraction; // 0..1 across the inhale
                return SmootherStep(inhaleT);
            }

            float exhaleT = (t - InhaleFraction) / (1f - InhaleFraction); // 0..1 across the exhale
            return 1f - SmootherStep(exhaleT);
        }

        /// <summary>Minimum-jerk interpolation with zero endpoint velocity and acceleration.</summary>
        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>Wraps a phase value (radians) to <c>[0, 2π)</c>.</summary>
        private static float WrapPhase(float phase)
        {
            float wrapped = phase % TwoPi;
            return wrapped < 0f ? wrapped + TwoPi : wrapped;
        }
    }
}
