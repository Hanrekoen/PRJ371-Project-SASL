using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyLanguage.Core.Policy;
using Convai.Modules.BodyLanguage.Core.Signals;
using Convai.Runtime.Utilities;
using UnityEngine;

namespace Convai.Modules.BodyLanguage.Data
{
    /// <summary>Verbosity gate for the body language diagnostics channel.</summary>
    public enum BodyLanguageTraceVerbosity
    {
        /// <summary>No trace output. Warnings and errors are still logged.</summary>
        Off = 0,

        /// <summary>Policy switches, degradations, lifecycle events. Recommended while tuning.</summary>
        State = 1,

        /// <summary>Adds director decisions, pulse verdicts, emotion blends.</summary>
        Detail = 2,

        /// <summary>Adds throttled per-tick value dumps. Logged, never recorded.</summary>
        Firehose = 3
    }

    /// <summary>
    ///     Per-<see cref="DialogueState" /> body language policy: how the body participates in
    ///     that conversational beat — gesticulation, listening posture, posture bias, breathing,
    ///     and fidgets. Unlisted states fall back to the Idle entry.
    /// </summary>
    [System.Serializable]
    public struct BodyLanguageStatePolicy
    {
        [Tooltip("Dialogue state this entry applies to. Unlisted states fall back to the Idle entry.")]
        public DialogueState State;

        [Tooltip("Whether co-speech gesticulation (head-beats, posture pulses) is eligible in this state.")]
        public bool GesticulationEnabled;

        [Range(0f, 1f)]
        [Tooltip("Overall gesticulation intensity 0–1 while enabled.")]
        public float GesticulationIntensity;

        [Tooltip("Whether the embodied-listening posture (lean-in, stillness) engages in this state.")]
        public bool ListeningPostureEnabled;

        [Range(0f, 1f)]
        [Tooltip("Lean-in fraction 0–1 of the profile's posture range while listening posture is engaged.")]
        public float ListeningLeanIn;

        [Range(-1f, 1f)]
        [Tooltip("Posture openness bias: positive opens/lifts the chest, negative rounds/closes it.")]
        public float PostureOpennessBias;

        [Range(-1f, 1f)]
        [Tooltip("Sagittal lean bias: positive leans toward the interlocutor, negative retracts.")]
        public float SagittalLeanBias;

        [Range(0f, 1f)]
        [Tooltip("How much the character sways on the spot in this state, 0–1. Set it to 0 to hold " +
                 "the character completely still here.")]
        public float AmbientDrift;

        [Range(4f, 30f)]
        [Tooltip("Breathing rate in cycles per minute for this state.")]
        public float BreathRateCpm;

        [Range(0f, 1f)]
        [Tooltip("Breathing depth 0–1 relative to the profile's breath calibration.")]
        public float BreathDepth;

        [Range(0f, 1f)]
        [Tooltip("Breathing irregularity 0–1: 0 is a steady rhythm, 1 is held/uneven breaths.")]
        public float BreathIrregularity;

        [Tooltip("Whether idle micro-fidgets are eligible in this state.")]
        public bool FidgetsEnabled;

        [Range(0f, 1f)]
        [Tooltip("Relative fidget rate 0–1 while fidgets are enabled.")]
        public float FidgetRate;
    }

    /// <summary>
    ///     Opt-in per-emotion modulation of the body language behavior: posture biases layered
    ///     on the state policy plus multiplicative scales on gesture and breath dynamics.
    /// </summary>
    [System.Serializable]
    public struct BodyLanguageEmotionModifier
    {
        [ConvaiEmotionLabel]
        [Tooltip("The emotion this modifier reacts to.")]
        public string EmotionLabel;

        [Range(-1f, 1f)]
        [Tooltip("Additional openness bias while this emotion is dominant (positive opens the chest).")]
        public float OpennessBias;

        [Range(-1f, 1f)]
        [Tooltip("Additional sagittal lean bias while this emotion is dominant (positive leans in).")]
        public float LeanBias;

        [Range(-1f, 1f)]
        [Tooltip("Shoulder tension bias while this emotion is dominant (positive raises/tenses).")]
        public float ShoulderTensionBias;

        [Range(0f, 2f)]
        [Tooltip("Multiplier on gesticulation intensity while this emotion is dominant.")]
        public float GestureIntensityScale;

        [Range(0f, 2f)]
        [Tooltip("Multiplier on gesticulation rate while this emotion is dominant.")]
        public float GestureRateScale;

        [Range(0f, 2f)]
        [Tooltip("Multiplier on breathing rate while this emotion is dominant.")]
        public float BreathRateScale;

        [Range(0f, 2f)]
        [Tooltip("Multiplier on breathing depth while this emotion is dominant.")]
        public float BreathDepthScale;
    }

    /// <summary>
    ///     The single authoring asset for the Convai Body Language system: speech-pulse signal
    ///     tuning, per-dialogue-state policies, posture and breathing calibration, emotion
    ///     mapping, and diagnostics — one asset per character archetype.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConvaiBodyLanguageProfile",
        menuName = "Convai/Embodiment/Body Language Profile",
        order = 150)]
    public sealed class ConvaiBodyLanguageProfile : ScriptableObject
    {
        // No [Header] on serialized fields: ConvaiBodyLanguageProfileEditor draws every one of the
        // fields below into its own named sections and never falls back to the default inspector, so
        // a header here renders nowhere. They were not merely inert -- one of them read "State
        // Policies" against a section the editor titles "States", a disagreement no user could ever
        // have seen and nobody could catch by looking at the running Inspector. The section table in
        // the editor is the single source of the grouping.

        // ── Expressiveness ───────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("The single expressiveness dial: scales amplitude/frequency/richness " +
                 "coherently across the whole body language system. Natural is the shipped default.")]
        private ExpressivenessPreset expressivenessPreset = global::Convai.Domain.Embodiment.Semantics.ExpressivenessPreset.Natural;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Custom expressiveness value 0..1, used only when Expressiveness Preset is Custom.")]
        private float customExpressiveness = 0.5f;

        // ── Signals ──────────────────────────────────────────────────────────

        [SerializeField, Range(0.005f, 1f)]
        [Tooltip("Speech-energy envelope smoothing (seconds) while energy is rising.")]
        private float attackSeconds = 0.05f;

        [SerializeField, Range(0.01f, 2f)]
        [Tooltip("Speech-energy envelope smoothing (seconds) while energy is falling.")]
        private float releaseSeconds = 0.15f;

        [SerializeField, Range(0.2f, 30f)]
        [Tooltip("Time constant (seconds) of the slow adaptive baseline that tracks the resting noise level.")]
        private float baselineWindowSeconds = 2.5f;

        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("How far above the baseline the envelope must rise to count as a speech onset.")]
        private float onsetThresholdAboveBaseline = 0.12f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Fraction of the onset threshold the envelope must fall back below before a release fires (hysteresis).")]
        private float releaseHysteresisFraction = 0.5f;

        [SerializeField, Range(0.1f, 20f)]
        [Tooltip("Positive envelope derivative (units/second) that qualifies as an emphasis spike.")]
        private float emphasisDerivativeThreshold = 1.6f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Minimum seconds between two fired speech pulses, regardless of kind.")]
        private float refractorySeconds = 0.22f;

        [SerializeField, Range(0.1f, 10f)]
        [Tooltip("Cadence (seconds) of the sustain heartbeat while speech stays continuously active.")]
        private float sustainIntervalSeconds = 0.9f;

        // ── State policies ───────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Per-dialogue-state body language policy. Unlisted states fall back to the Idle entry.")]
        private List<BodyLanguageStatePolicy> statePolicies = BuildDefaultStatePolicies();

        [SerializeField, Range(0f, 20f)]
        [Tooltip("Seconds over which policy values blend when the dialogue state changes (0 snaps).")]
        private float policyTransitionSeconds = 0.4f;

        // ── Posture ──────────────────────────────────────────────────────────

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Spine rotation (degrees) that a full ±1 openness bias maps to.")]
        private float maxOpennessDegrees = 14f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Spine rotation (degrees) that a full ±1 sagittal lean bias maps to.")]
        private float maxLeanDegrees = 12f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Shoulder rotation (degrees) that a full ±1 shoulder tension bias maps to.")]
        private float maxTensionDegrees = 8f;

        [SerializeField, Range(0.5f, 20f)]
        [Tooltip("Posture spring sharpness — higher settles faster. Mirrors the gaze solver's spring recipe.")]
        private float postureSpringSharpness = 4f;

        [SerializeField, Range(10f, 720f)]
        [Tooltip("Hard angular speed clamp (degrees/second) for the posture spring.")]
        private float postureMaxAngularSpeed = 90f;

        [SerializeField, Range(0.1f, 10f)]
        [Tooltip("Seconds (time constant) over which the posture target slews toward a new state/emotion goal.")]
        private float postureTargetSlewSeconds = 1.5f;

        [SerializeField, Range(0.05f, 5f)]
        [Tooltip("Seconds over which the posture/breath master weight fades to zero on disable or full-body suppression — never a snap.")]
        private float postureFadeSeconds = 0.6f;

        [SerializeField, Range(1f, 15f)]
        [Tooltip("Spine rotation (degrees) that a full ±1 lateral weight-shift (fidget/thinking asymmetry) target maps to. Kept small — a weight shift should read, not perform.")]
        private float maxLateralShiftDegrees = 5f;

        // ── Stance & Sway ────────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Whether the periodic pelvis weight-shift program is enabled.")]
        private bool enableWeightShifts = true;

        [SerializeField, Range(6f, 90f)]
        [Tooltip("Mean seconds between weight-shift cycles (state-scaled — Listening/Speaking/Thinking run slower than this baseline).")]
        private float weightShiftIntervalSeconds = 20f;

        [SerializeField, Range(0f, 30f)]
        [Tooltip("Random variance (± seconds) applied to the weight-shift interval.")]
        private float weightShiftIntervalVarianceSeconds = 8f;

        [SerializeField, Range(0.8f, 5f)]
        [Tooltip("Seconds (time constant basis) over which a weight shift transfers to its new target.")]
        private float weightShiftTransferSeconds = 2.2f;

        [SerializeField, Range(0f, 6f)]
        [Tooltip("Lateral weight-shift travel (centimeters). Values above ~4cm need leg compensation to avoid foot slide.")]
        private float maxPelvisOffsetCentimeters = 3f;

        [SerializeField, Range(0f, 6f)]
        [Tooltip("Pelvis obliquity (hip-hike, degrees) at a full ±1 weight-shift.")]
        private float maxPelvisObliquityDegrees = 2.5f;

        [SerializeField, Range(0f, 8f)]
        [Tooltip("Pelvis yaw (degrees) at a full ±1 weight-shift.")]
        private float maxPelvisYawDegrees = 3f;

        [SerializeField]
        [Tooltip("Pins the feet during weight shifts via internal two-bone leg IK. Auto-inactive when the leg chain does not resolve.")]
        private bool enableLegCompensation = true;

        [SerializeField]
        [Tooltip("Whether the continuous band-limited postural sway is enabled.")]
        private bool enableAmbientSway = true;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Spine rotation (degrees) that a full ±1 postural sway sample maps to. Kept sub-degree by default — sway should read, not perform.")]
        private float maxSwayDegrees = 0.6f;

        // ── Head Gestures ────────────────────────────────────────────────────

        [SerializeField, Range(1f, 15f)]
        [Tooltip("Peak pitch (degrees) a full-intensity Nod gesture reaches. Kept conservative — the gaze consumer limit-compresses this further.")]
        private float headGestureNodMaxPitchDegrees = 8f;

        [SerializeField, Range(1f, 15f)]
        [Tooltip("Peak yaw (degrees) a full-intensity Shake gesture reaches.")]
        private float headGestureShakeMaxYawDegrees = 9f;

        [SerializeField, Range(1f, 15f)]
        [Tooltip("Peak roll (degrees) a full-intensity Tilt gesture reaches.")]
        private float headGestureTiltMaxRollDegrees = 6f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Minimum seconds after a head gesture completes before the next one may start.")]
        private float headGestureRefractorySeconds = 0.6f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Random variance (± seconds) applied to the refractory window so repeated gestures never read as a metronome.")]
        private float headGestureRefractoryVarianceSeconds = 0.25f;

        // ── Gesticulation ────────────────────────────────────────────────────

        [SerializeField, Range(0.3f, 3f)]
        [Tooltip("Minimum seconds between fast-channel co-speech beats, even during frequent emphasis pulses (distinct from the head-gesture refractory above).")]
        private float beatMinIntervalSeconds = 1.2f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Random variance (± seconds) added to the beat minimum interval — the anti-metronome knob so consecutive beat spacing is never near-constant.")]
        private float beatIntervalVarianceSeconds = 0.35f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Scales the head-gesture amplitude a fast-channel beat requests. Kept subtle by default — a co-speech beat is a small pitch accent, not a full nod.")]
        private float beatHeadIntensity = 0.5f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Scales how much the posture-pulse envelope adds on top of the continuous lean target on a beat.")]
        private float posturePulseAmplitude = 0.35f;

        [SerializeField, Range(0.02f, 0.2f)]
        [Tooltip("How fast an accent reaches its peak, in seconds. Keep it short — an accent that takes " +
                 "longer than about a tenth of a second stops reading as a response to the word " +
                 "being spoken.")]
        private float posturePulseAttackSeconds = 0.08f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("Posture-pulse decay time (seconds) back to the continuous lean target.")]
        private float posturePulseDecaySeconds = 0.35f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Multiplies a speech pulse's strength before it scales beat/posture-pulse amplitude — tune a character hotter or cooler without touching the signal analyzer.")]
        private float energyToIntensityGain = 1f;

        [SerializeField, Range(1f, 6f)]
        [Tooltip("Mean seconds between accents when nothing is publishing this character's speech " +
                 "energy — Lip Sync normally does. Deliberately slower than a real speech rhythm, " +
                 "because without that signal there is nothing to be in time with.")]
        private float statisticalCadenceIntervalSeconds = 2.5f;

        [SerializeField, Range(0f, 3f)]
        [Tooltip("Random variance (± seconds) applied to the statistical cadence interval.")]
        private float statisticalCadenceVarianceSeconds = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How much posture survives while another Convai module is using this character's " +
                 "upper body — walking, or an authored gesture. Gesture clips stop entirely, posture " +
                 "continues at this reduced weight, and breathing deliberately stays at full weight.")]
        private float upperBodySuppressionPostureWeight = 0.75f;

        [SerializeField, Range(0.5f, 10f)]
        [Tooltip("Minimum seconds between two meaning-carrying gestures, whether requested from your own " +
                 "code or driven by the conversation.")]
        private float semanticCueRefractorySeconds = 2.5f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Peak shoulder lift (degrees) of the procedural one-shot shrug triggered by an Uncertain gesture cue. Kept small — a shrug should read as a beat, not a shoulder heave.")]
        private float maxShrugDegrees = 4f;

        // ── Gesticulation: Hands ─────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Whether idle wrist/finger micro-motion is applied between authored gestures.")]
        private bool enableHandMicro = true;

        [SerializeField, Range(0f, 6f)]
        [Tooltip("Peak finger-proximal curl (degrees) of the idle hand micro-motion at full weight.")]
        private float maxFingerCurlDegrees = 2.5f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Peak wrist micro-motion (degrees) of the idle hand micro-motion at full weight.")]
        private float maxWristMicroDegrees = 2f;

        [SerializeField]
        [Tooltip("Generate conservative procedural arm/hand gestures when no Body Animation performer accepts a semantic cue.")]
        private bool enableProceduralGestureFallback = true;

        [SerializeField, Range(0.25f, 1.5f)]
        [Tooltip("Amplitude multiplier for procedural gesture fallback. Keep near 1; rig-independent rotations are intentionally conservative.")]
        private float proceduralGestureAmplitude = 1f;

        // ── Listening & Fidgets ──────────────────────────────────────────────

        [SerializeField, Range(1f, 10f)]
        [Tooltip("Mean seconds between fidget weight-shift cycles at full FidgetRate — higher FidgetRate shortens this (the gap, not the shift itself, scales with rate).")]
        private float fidgetGapSeconds = 3.5f;

        [SerializeField, Range(0.2f, 3f)]
        [Tooltip("Ease-in/ease-out duration (seconds) of a single fidget weight-shift.")]
        private float fidgetEaseSeconds = 0.9f;

        [SerializeField, Range(0.5f, 6f)]
        [Tooltip("Hold duration (seconds) at the peak of a fidget weight-shift before easing back.")]
        private float fidgetHoldSeconds = 2.2f;

        [SerializeField, Range(2f, 15f)]
        [Tooltip("Mean seconds between listening tilt-hold head gestures while embodied-listening posture is engaged.")]
        private float listeningTiltCadenceSeconds = 6f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Intensity (0..1) of the listening tilt-hold head gesture — kept gentle by default, a subtle attentive tilt, not a caricature.")]
        private float listeningTiltIntensity = 0.5f;

        // ── Breathing ────────────────────────────────────────────────────────

        [SerializeField, Range(0.2f, 6f)]
        [Tooltip("Chest/upper-chest expansion (degrees) a full-depth inhale maps to. Kept subtle by default — breath should read, not perform.")]
        private float maxBreathChestExpansionDegrees = 4.5f;

        [SerializeField, Range(0.1f, 4f)]
        [Tooltip("Shoulder lift (degrees) a full-depth inhale maps to.")]
        private float maxBreathShoulderLiftDegrees = 2.2f;

        [SerializeField]
        [Tooltip("Ducks procedural breathing against baked idle-clip breathing to prevent beat interference.")]
        private bool enableBreathAdaptiveLayering = true;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How level the head stays against breathing chest motion.")]
        private float breathHeadStabilization = 0.35f;

        [SerializeField]
        [Tooltip("Allow a catch-breath motion when the character is interrupted (opt-in).")]
        private bool enableCatchBreath = true;

        [SerializeField]
        [Tooltip("Allow a sigh-length breath motion when the conversation settles (opt-in, motion only — no audio).")]
        private bool enableSigh = true;

        [SerializeField]
        [Tooltip("Draw a brief deeper, faster inhale as the character begins to speak. On by default: a " +
                 "body that starts talking without first taking a breath is one of the clearest " +
                 "tells that a character is not alive.")]
        private bool enableInhaleBeforeSpeaking = true;

        [SerializeField, Range(0f, 1.5f)]
        [Tooltip("How much faster this character breathes when it has been moving hard — 0.4 means 40% " +
                 "faster at a full run. Needs the Convai Body Animation module to report the effort; " +
                 "without it this does nothing at all.")]
        private float exertionRateBoost = 0.4f;

        [SerializeField, Range(0f, 1.5f)]
        [Tooltip("How much deeper this character breathes when it has been moving hard — 0.5 means 50% " +
                 "deeper at a full run. Needs the Convai Body Animation module to report the effort; " +
                 "without it this does nothing at all.")]
        private float exertionDepthBoost = 0.5f;

        // ── Emotion ──────────────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Bias posture, gesture and breath by the character's current emotion. Harmless when " +
                 "no Emotion module is present — the reading stays neutral and every modifier " +
                 "resolves to no change.")]
        private bool enableEmotionModulation = true;

        [SerializeField]
        [Tooltip("Per-emotion modifiers applied while that emotion is dominant.")]
        private List<BodyLanguageEmotionModifier> emotionModifiers = BuildDefaultEmotionModifiers();

        [SerializeField]
        [Tooltip("For an emotion label that has no row in the table above, derive a modifier from its " +
                 "valence and arousal instead of ignoring it.")]
        private bool valenceArousalFallback = true;

        // ── Reactions ────────────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Whether one-shot bodily reactions are enabled — a startle flinch, an amused bounce — " +
                 "both when a sudden emotion triggers them and when your own code asks for one.")]
        private bool enableReactions = true;

        [SerializeField, Range(0f, 12f)]
        [Tooltip("Spine/shoulder rotation (degrees) a full-intensity startle flinch reaches.")]
        private float maxFlinchDegrees = 5f;

        [SerializeField, Range(0f, 4f)]
        [Tooltip("Chest rotation (degrees) a full-intensity amused bounce reaches.")]
        private float maxAmusementBounceDegrees = 1.2f;

        // ── Idle Presence ────────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Slowly drift breath depth, sway amplitude and fidget cadence together over several " +
                 "minutes, so a character left idle for a long time never settles into a visibly " +
                 "repeating baseline.")]
        private bool enableIdleMacroCycles = true;

        // ── Camera LOD ───────────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Scale sway and idle hand motion by how far the main camera is from the character: " +
                 "slightly larger at a distance so it still reads, slightly subtler in an extreme " +
                 "close-up. Neutral at a normal conversational distance. Never affects breathing, " +
                 "posture or gestures.")]
        private bool enableCameraDistanceLod = true;

        // ── Diagnostics ──────────────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Diagnostics verbosity for this character's body language trace.")]
        private BodyLanguageTraceVerbosity traceVerbosity = BodyLanguageTraceVerbosity.Off;

        // ── Public accessors ─────────────────────────────────────────────────

        /// <summary>The authored expressiveness preset. <c>ExpressivenessPreset.Custom</c> uses <see cref="CustomExpressiveness" />.</summary>
        public ExpressivenessPreset ExpressivenessPreset => expressivenessPreset;

        /// <summary>Custom expressiveness value 0..1, used only when <see cref="ExpressivenessPreset" /> is <c>ExpressivenessPreset.Custom</c>.</summary>
        public float CustomExpressiveness => customExpressiveness;

        /// <summary>Speech-energy envelope smoothing (seconds) while energy is rising.</summary>
        public float AttackSeconds => attackSeconds;

        /// <summary>Speech-energy envelope smoothing (seconds) while energy is falling.</summary>
        public float ReleaseSeconds => releaseSeconds;

        /// <summary>Time constant (seconds) of the slow adaptive baseline.</summary>
        public float BaselineWindowSeconds => baselineWindowSeconds;

        /// <summary>Envelope rise above baseline that counts as a speech onset.</summary>
        public float OnsetThresholdAboveBaseline => onsetThresholdAboveBaseline;

        /// <summary>Hysteresis fraction of the onset threshold for the release edge.</summary>
        public float ReleaseHysteresisFraction => releaseHysteresisFraction;

        /// <summary>Envelope derivative (units/second) that qualifies as an emphasis spike.</summary>
        public float EmphasisDerivativeThreshold => emphasisDerivativeThreshold;

        /// <summary>Minimum seconds between two fired speech pulses.</summary>
        public float RefractorySeconds => refractorySeconds;

        /// <summary>Cadence (seconds) of the sustain heartbeat during continuous speech.</summary>
        public float SustainIntervalSeconds => sustainIntervalSeconds;

        /// <summary>The authored per-state policy table. Unlisted states fall back to the Idle entry.</summary>
        public IReadOnlyList<BodyLanguageStatePolicy> StatePolicies => statePolicies;

        /// <summary>Seconds over which policy values blend on a dialogue state change (0 snaps).</summary>
        public float PolicyTransitionSeconds => policyTransitionSeconds;

        /// <summary>Spine rotation (degrees) mapped to a full ±1 openness bias.</summary>
        public float MaxOpennessDegrees => maxOpennessDegrees;

        /// <summary>Spine rotation (degrees) mapped to a full ±1 sagittal lean bias.</summary>
        public float MaxLeanDegrees => maxLeanDegrees;

        /// <summary>Shoulder rotation (degrees) mapped to a full ±1 shoulder tension bias.</summary>
        public float MaxTensionDegrees => maxTensionDegrees;

        /// <summary>Posture spring sharpness — higher settles faster.</summary>
        public float PostureSpringSharpness => postureSpringSharpness;

        /// <summary>Hard angular speed clamp (degrees/second) for the posture spring.</summary>
        public float PostureMaxAngularSpeed => postureMaxAngularSpeed;

        /// <summary>Seconds (time constant) over which the posture target slews toward a new goal.</summary>
        public float PostureTargetSlewSeconds => postureTargetSlewSeconds;

        /// <summary>Seconds over which the posture/breath master weight fades on disable or suppression.</summary>
        public float PostureFadeSeconds => postureFadeSeconds;

        /// <summary>Spine rotation (degrees) mapped to a full ±1 lateral weight-shift target.</summary>
        public float MaxLateralShiftDegrees => maxLateralShiftDegrees;

        /// <summary>Whether the periodic pelvis weight-shift program is enabled.</summary>
        public bool EnableWeightShifts => enableWeightShifts;

        /// <summary>Mean seconds between weight-shift cycles (state-scaled).</summary>
        public float WeightShiftIntervalSeconds => weightShiftIntervalSeconds;

        /// <summary>Random variance (± seconds) applied to the weight-shift interval.</summary>
        public float WeightShiftIntervalVarianceSeconds => weightShiftIntervalVarianceSeconds;

        /// <summary>Seconds over which a weight shift transfers to its new target.</summary>
        public float WeightShiftTransferSeconds => weightShiftTransferSeconds;

        /// <summary>Lateral weight-shift travel (centimeters).</summary>
        public float MaxPelvisOffsetCentimeters => maxPelvisOffsetCentimeters;

        /// <summary>Pelvis obliquity (hip-hike, degrees) at a full ±1 weight-shift.</summary>
        public float MaxPelvisObliquityDegrees => maxPelvisObliquityDegrees;

        /// <summary>Pelvis yaw (degrees) at a full ±1 weight-shift.</summary>
        public float MaxPelvisYawDegrees => maxPelvisYawDegrees;

        /// <summary>Whether feet are pinned during weight shifts via internal two-bone leg IK.</summary>
        public bool EnableLegCompensation => enableLegCompensation;

        /// <summary>Whether the continuous band-limited postural sway is enabled.</summary>
        public bool EnableAmbientSway => enableAmbientSway;

        /// <summary>Spine rotation (degrees) mapped to a full ±1 postural sway sample.</summary>
        public float MaxSwayDegrees => maxSwayDegrees;

        /// <summary>Peak pitch (degrees) a full-intensity Nod gesture reaches.</summary>
        public float HeadGestureNodMaxPitchDegrees => headGestureNodMaxPitchDegrees;

        /// <summary>Peak yaw (degrees) a full-intensity Shake gesture reaches.</summary>
        public float HeadGestureShakeMaxYawDegrees => headGestureShakeMaxYawDegrees;

        /// <summary>Peak roll (degrees) a full-intensity Tilt gesture reaches.</summary>
        public float HeadGestureTiltMaxRollDegrees => headGestureTiltMaxRollDegrees;

        /// <summary>Minimum seconds after a head gesture completes before the next one may start.</summary>
        public float HeadGestureRefractorySeconds => headGestureRefractorySeconds;

        /// <summary>Random variance (± seconds) applied to the head-gesture refractory window.</summary>
        public float HeadGestureRefractoryVarianceSeconds => headGestureRefractoryVarianceSeconds;

        /// <summary>Minimum seconds between fast-channel co-speech beats.</summary>
        public float BeatMinIntervalSeconds => beatMinIntervalSeconds;

        /// <summary>Random variance (± seconds) added to the beat minimum interval.</summary>
        public float BeatIntervalVarianceSeconds => beatIntervalVarianceSeconds;

        /// <summary>Scales the head-gesture amplitude a fast-channel beat requests.</summary>
        public float BeatHeadIntensity => beatHeadIntensity;

        /// <summary>Scales how much the posture-pulse envelope adds on top of the continuous lean target on a beat.</summary>
        public float PosturePulseAmplitude => posturePulseAmplitude;

        /// <summary>Posture-pulse rise time (seconds).</summary>
        public float PosturePulseAttackSeconds => posturePulseAttackSeconds;

        /// <summary>Posture-pulse decay time (seconds).</summary>
        public float PosturePulseDecaySeconds => posturePulseDecaySeconds;

        /// <summary>Multiplies a speech pulse's strength before it scales beat/posture-pulse amplitude.</summary>
        public float EnergyToIntensityGain => energyToIntensityGain;

        /// <summary>Mean seconds between fallback beats when no speech-energy provider is registered.</summary>
        public float StatisticalCadenceIntervalSeconds => statisticalCadenceIntervalSeconds;

        /// <summary>Random variance (± seconds) applied to the statistical cadence interval.</summary>
        public float StatisticalCadenceVarianceSeconds => statisticalCadenceVarianceSeconds;

        /// <summary>Posture weight fraction retained under UpperBody suppression (breathing stays at full weight).</summary>
        public float UpperBodySuppressionPostureWeight => upperBodySuppressionPostureWeight;

        /// <summary>Minimum seconds between semantic gesture-cue emissions.</summary>
        public float SemanticCueRefractorySeconds => semanticCueRefractorySeconds;

        /// <summary>Peak shoulder lift (degrees) of the procedural one-shot shrug triggered by an Uncertain gesture cue.</summary>
        public float MaxShrugDegrees => maxShrugDegrees;

        /// <summary>Whether idle wrist/finger micro-motion is applied between authored gestures.</summary>
        public bool EnableHandMicro => enableHandMicro;

        /// <summary>Peak finger-proximal curl (degrees) of the idle hand micro-motion at full weight.</summary>
        public float MaxFingerCurlDegrees => maxFingerCurlDegrees;

        /// <summary>Peak wrist micro-motion (degrees) of the idle hand micro-motion at full weight.</summary>
        public float MaxWristMicroDegrees => maxWristMicroDegrees;

        /// <summary>Whether rejected semantic cues may fall back to procedural arm/hand motion.</summary>
        public bool EnableProceduralGestureFallback => enableProceduralGestureFallback;

        /// <summary>Amplitude multiplier for procedural semantic fallback motion.</summary>
        public float ProceduralGestureAmplitude => proceduralGestureAmplitude;

        /// <summary>Mean seconds between fidget weight-shift cycles at full FidgetRate.</summary>
        public float FidgetGapSeconds => fidgetGapSeconds;

        /// <summary>Ease-in/ease-out duration (seconds) of a single fidget weight-shift.</summary>
        public float FidgetEaseSeconds => fidgetEaseSeconds;

        /// <summary>Hold duration (seconds) at the peak of a fidget weight-shift.</summary>
        public float FidgetHoldSeconds => fidgetHoldSeconds;

        /// <summary>Mean seconds between listening tilt-hold head gestures.</summary>
        public float ListeningTiltCadenceSeconds => listeningTiltCadenceSeconds;

        /// <summary>Intensity (0..1) of the listening tilt-hold head gesture.</summary>
        public float ListeningTiltIntensity => listeningTiltIntensity;

        /// <summary>Chest/upper-chest expansion (degrees) a full-depth inhale maps to.</summary>
        public float MaxBreathChestExpansionDegrees => maxBreathChestExpansionDegrees;

        /// <summary>Shoulder lift (degrees) a full-depth inhale maps to.</summary>
        public float MaxBreathShoulderLiftDegrees => maxBreathShoulderLiftDegrees;

        /// <summary>Whether procedural breathing ducks against baked idle-clip breathing to prevent beat interference.</summary>
        public bool EnableBreathAdaptiveLayering => enableBreathAdaptiveLayering;

        /// <summary>How level the head stays against breathing chest motion, 0..1.</summary>
        public float BreathHeadStabilization => breathHeadStabilization;

        /// <summary>Whether a catch-breath motion may play on interruption.</summary>
        public bool EnableCatchBreath => enableCatchBreath;

        /// <summary>Whether a sigh-length breath motion may play while settling.</summary>
        public bool EnableSigh => enableSigh;

        /// <summary>Whether a brief inhale plays as the character begins to speak.</summary>
        public bool EnableInhaleBeforeSpeaking => enableInhaleBeforeSpeaking;

        /// <summary>Additional breathing-rate multiplier at full locomotion exertion.</summary>
        public float ExertionRateBoost => exertionRateBoost;

        /// <summary>Additional breathing-depth multiplier at full locomotion exertion.</summary>
        public float ExertionDepthBoost => exertionDepthBoost;

        /// <summary>Whether the dominant emotion biases posture, gesture, and breath.</summary>
        public bool EnableEmotionModulation => enableEmotionModulation;

        /// <summary>Per-emotion modifiers applied while that emotion is dominant.</summary>
        public IReadOnlyList<BodyLanguageEmotionModifier> EmotionModifiers => emotionModifiers;

        /// <summary>Whether unlisted emotion labels derive a fallback modifier from valence/arousal.</summary>
        public bool ValenceArousalFallback => valenceArousalFallback;

        /// <summary>Whether one-shot bodily reactions (startle flinch, amused bounce) are enabled.</summary>
        public bool EnableReactions => enableReactions;

        /// <summary>Spine/shoulder rotation (degrees) a full-intensity startle flinch reaches.</summary>
        public float MaxFlinchDegrees => maxFlinchDegrees;

        /// <summary>Chest rotation (degrees) a full-intensity amused bounce reaches.</summary>
        public float MaxAmusementBounceDegrees => maxAmusementBounceDegrees;

        /// <summary>
        ///     Whether the idle macro-cycle drift is enabled: a very slow seeded drift that nudges
        ///     breath depth, sway amplitude and fidget cadence together over several minutes, so a
        ///     long idle never settles into a perceptibly looping baseline.
        /// </summary>
        public bool EnableIdleMacroCycles => enableIdleMacroCycles;

        /// <summary>
        ///     Whether the camera-distance amplitude LOD is enabled: scales sway amplitude and
        ///     idle hand-motion weight by distance from <c>Camera.main</c> — slightly larger far
        ///     away so the motion still reads, slightly subtler in an extreme close-up, neutral at
        ///     a normal conversational distance. Off, or with no camera resolved, the scale stays
        ///     at 1. Never touches breath rate/depth, posture or gestures — those carry meaning,
        ///     not texture.
        /// </summary>
        public bool EnableCameraDistanceLod => enableCameraDistanceLod;

        /// <summary>Diagnostics verbosity for this character's body language trace.</summary>
        public BodyLanguageTraceVerbosity TraceVerbosity => traceVerbosity;

        // ── Resolution helpers ───────────────────────────────────────────────

        /// <summary>
        ///     Resolves the effective expressiveness value 0..1: a fixed preset's
        ///     anchor (see <see cref="Core.Policy.ExpressivenessCurves.For" />), or
        ///     <see cref="CustomExpressiveness" /> when the preset is Custom.
        /// </summary>
        public float ResolveExpressiveness() =>
            expressivenessPreset == global::Convai.Domain.Embodiment.Semantics.ExpressivenessPreset.Custom
                ? Mathf.Clamp01(customExpressiveness)
                : ExpressivenessCurves.For(expressivenessPreset);

        /// <summary>
        ///     Resolves the policy for <paramref name="state" />, falling back to the Idle
        ///     entry (or a conservative built-in default) when the state is not authored.
        /// </summary>
        public BodyLanguageStatePolicy GetPolicy(DialogueState state)
        {
            BodyLanguageStatePolicy idleFallback = default;
            bool foundIdle = false;

            for (int i = 0; i < statePolicies.Count; i++)
            {
                BodyLanguageStatePolicy entry = statePolicies[i];
                if (entry.State == state) return Sanitize(entry);
                if (entry.State == DialogueState.Idle && !foundIdle)
                {
                    idleFallback = entry;
                    foundIdle = true;
                }
            }

            if (foundIdle) return Sanitize(idleFallback);

            return new BodyLanguageStatePolicy
            {
                State = state,
                GesticulationEnabled = false,
                GesticulationIntensity = 0f,
                ListeningPostureEnabled = false,
                ListeningLeanIn = 0f,
                PostureOpennessBias = 0f,
                SagittalLeanBias = 0f,
                AmbientDrift = 0.25f,
                BreathRateCpm = 13f,
                BreathDepth = 0.5f,
                BreathIrregularity = 0.1f,
                FidgetsEnabled = false,
                FidgetRate = 0f
            };
        }

        /// <summary>
        ///     Resolves the modifier for <paramref name="emotionLabel" /> (case-insensitive).
        ///     Returns <c>false</c> when modulation is disabled or the label is not authored.
        /// </summary>
        public bool TryGetEmotionModifier(string emotionLabel, out BodyLanguageEmotionModifier modifier)
        {
            modifier = default;
            if (!enableEmotionModulation || string.IsNullOrEmpty(emotionLabel)) return false;

            for (int i = 0; i < emotionModifiers.Count; i++)
            {
                if (string.Equals(emotionModifiers[i].EmotionLabel, emotionLabel,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    modifier = emotionModifiers[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Builds the analyzer configuration authored by the Signals section.</summary>
        internal SpeechPulseAnalyzerConfig BuildSignalConfig() => new()
        {
            AttackSeconds = attackSeconds,
            ReleaseSeconds = releaseSeconds,
            BaselineWindowSeconds = baselineWindowSeconds,
            OnsetThresholdAboveBaseline = onsetThresholdAboveBaseline,
            ReleaseHysteresisFraction = releaseHysteresisFraction,
            EmphasisDerivativeThreshold = emphasisDerivativeThreshold,
            RefractorySeconds = refractorySeconds,
            SustainIntervalSeconds = sustainIntervalSeconds
        };

        private static BodyLanguageStatePolicy Sanitize(BodyLanguageStatePolicy policy)
        {
            policy.GesticulationIntensity = Mathf.Clamp01(policy.GesticulationIntensity);
            policy.ListeningLeanIn = Mathf.Clamp01(policy.ListeningLeanIn);
            policy.PostureOpennessBias = Mathf.Clamp(policy.PostureOpennessBias, -1f, 1f);
            policy.SagittalLeanBias = Mathf.Clamp(policy.SagittalLeanBias, -1f, 1f);
            policy.AmbientDrift = Mathf.Clamp01(policy.AmbientDrift);
            policy.BreathRateCpm = Mathf.Clamp(policy.BreathRateCpm, 4f, 30f);
            policy.BreathDepth = Mathf.Clamp01(policy.BreathDepth);
            policy.BreathIrregularity = Mathf.Clamp01(policy.BreathIrregularity);
            policy.FidgetRate = Mathf.Clamp01(policy.FidgetRate);
            return policy;
        }

        private static BodyLanguageEmotionModifier Sanitize(BodyLanguageEmotionModifier modifier)
        {
            modifier.OpennessBias = Mathf.Clamp(modifier.OpennessBias, -1f, 1f);
            modifier.LeanBias = Mathf.Clamp(modifier.LeanBias, -1f, 1f);
            modifier.ShoulderTensionBias = Mathf.Clamp(modifier.ShoulderTensionBias, -1f, 1f);
            modifier.GestureIntensityScale = Mathf.Clamp(modifier.GestureIntensityScale, 0f, 2f);
            modifier.GestureRateScale = Mathf.Clamp(modifier.GestureRateScale, 0f, 2f);
            modifier.BreathRateScale = Mathf.Clamp(modifier.BreathRateScale, 0f, 2f);
            modifier.BreathDepthScale = Mathf.Clamp(modifier.BreathDepthScale, 0f, 2f);
            return modifier;
        }

        private void OnValidate()
        {
            customExpressiveness = Mathf.Clamp01(customExpressiveness);
            attackSeconds = Mathf.Clamp(attackSeconds, 0.005f, 1f);
            releaseSeconds = Mathf.Clamp(releaseSeconds, 0.01f, 2f);
            baselineWindowSeconds = Mathf.Clamp(baselineWindowSeconds, 0.2f, 30f);
            onsetThresholdAboveBaseline = Mathf.Clamp(onsetThresholdAboveBaseline, 0.01f, 1f);
            releaseHysteresisFraction = Mathf.Clamp(releaseHysteresisFraction, 0.05f, 1f);
            emphasisDerivativeThreshold = Mathf.Clamp(emphasisDerivativeThreshold, 0.1f, 20f);
            refractorySeconds = Mathf.Clamp(refractorySeconds, 0f, 2f);
            sustainIntervalSeconds = Mathf.Clamp(sustainIntervalSeconds, 0.1f, 10f);
            policyTransitionSeconds = Mathf.Clamp(policyTransitionSeconds, 0f, 20f);
            maxOpennessDegrees = Mathf.Clamp(maxOpennessDegrees, 1f, 30f);
            maxLeanDegrees = Mathf.Clamp(maxLeanDegrees, 1f, 30f);
            maxTensionDegrees = Mathf.Clamp(maxTensionDegrees, 1f, 30f);
            postureSpringSharpness = Mathf.Clamp(postureSpringSharpness, 0.5f, 20f);
            postureMaxAngularSpeed = Mathf.Clamp(postureMaxAngularSpeed, 10f, 720f);
            postureTargetSlewSeconds = Mathf.Clamp(postureTargetSlewSeconds, 0.1f, 10f);
            postureFadeSeconds = Mathf.Clamp(postureFadeSeconds, 0.05f, 5f);
            headGestureNodMaxPitchDegrees = Mathf.Clamp(headGestureNodMaxPitchDegrees, 1f, 15f);
            headGestureShakeMaxYawDegrees = Mathf.Clamp(headGestureShakeMaxYawDegrees, 1f, 15f);
            headGestureTiltMaxRollDegrees = Mathf.Clamp(headGestureTiltMaxRollDegrees, 1f, 15f);
            headGestureRefractorySeconds = Mathf.Clamp(headGestureRefractorySeconds, 0f, 5f);
            headGestureRefractoryVarianceSeconds = Mathf.Clamp(headGestureRefractoryVarianceSeconds, 0f, 2f);
            beatMinIntervalSeconds = Mathf.Clamp(beatMinIntervalSeconds, 0.3f, 3f);
            beatIntervalVarianceSeconds = Mathf.Clamp(beatIntervalVarianceSeconds, 0f, 2f);
            beatHeadIntensity = Mathf.Clamp01(beatHeadIntensity);
            posturePulseAmplitude = Mathf.Clamp01(posturePulseAmplitude);
            posturePulseAttackSeconds = Mathf.Clamp(posturePulseAttackSeconds, 0.02f, 0.2f);
            posturePulseDecaySeconds = Mathf.Clamp(posturePulseDecaySeconds, 0.1f, 1f);
            energyToIntensityGain = Mathf.Clamp(energyToIntensityGain, 0f, 2f);
            statisticalCadenceIntervalSeconds = Mathf.Clamp(statisticalCadenceIntervalSeconds, 1f, 6f);
            statisticalCadenceVarianceSeconds = Mathf.Clamp(statisticalCadenceVarianceSeconds, 0f, 3f);
            upperBodySuppressionPostureWeight = Mathf.Clamp01(upperBodySuppressionPostureWeight);
            semanticCueRefractorySeconds = Mathf.Clamp(semanticCueRefractorySeconds, 0.5f, 10f);
            maxShrugDegrees = Mathf.Clamp(maxShrugDegrees, 0f, 10f);
            maxFingerCurlDegrees = Mathf.Clamp(maxFingerCurlDegrees, 0f, 6f);
            maxWristMicroDegrees = Mathf.Clamp(maxWristMicroDegrees, 0f, 5f);
            proceduralGestureAmplitude = Mathf.Clamp(proceduralGestureAmplitude, 0.25f, 1.5f);
            fidgetGapSeconds = Mathf.Clamp(fidgetGapSeconds, 1f, 10f);
            fidgetEaseSeconds = Mathf.Clamp(fidgetEaseSeconds, 0.2f, 3f);
            fidgetHoldSeconds = Mathf.Clamp(fidgetHoldSeconds, 0.5f, 6f);
            listeningTiltCadenceSeconds = Mathf.Clamp(listeningTiltCadenceSeconds, 2f, 15f);
            listeningTiltIntensity = Mathf.Clamp01(listeningTiltIntensity);
            maxLateralShiftDegrees = Mathf.Clamp(maxLateralShiftDegrees, 1f, 15f);
            weightShiftIntervalSeconds = Mathf.Clamp(weightShiftIntervalSeconds, 6f, 90f);
            weightShiftIntervalVarianceSeconds = Mathf.Clamp(weightShiftIntervalVarianceSeconds, 0f, 30f);
            weightShiftTransferSeconds = Mathf.Clamp(weightShiftTransferSeconds, 0.8f, 5f);
            maxPelvisOffsetCentimeters = Mathf.Clamp(maxPelvisOffsetCentimeters, 0f, 6f);
            maxPelvisObliquityDegrees = Mathf.Clamp(maxPelvisObliquityDegrees, 0f, 6f);
            maxPelvisYawDegrees = Mathf.Clamp(maxPelvisYawDegrees, 0f, 8f);
            maxSwayDegrees = Mathf.Clamp(maxSwayDegrees, 0f, 2f);
            maxBreathChestExpansionDegrees = Mathf.Clamp(maxBreathChestExpansionDegrees, 0.2f, 6f);
            maxBreathShoulderLiftDegrees = Mathf.Clamp(maxBreathShoulderLiftDegrees, 0.1f, 4f);
            breathHeadStabilization = Mathf.Clamp01(breathHeadStabilization);
            exertionRateBoost = Mathf.Clamp(exertionRateBoost, 0f, 1.5f);
            exertionDepthBoost = Mathf.Clamp(exertionDepthBoost, 0f, 1.5f);
            maxFlinchDegrees = Mathf.Clamp(maxFlinchDegrees, 0f, 12f);
            maxAmusementBounceDegrees = Mathf.Clamp(maxAmusementBounceDegrees, 0f, 4f);

            if (statePolicies != null)
            {
                for (int i = 0; i < statePolicies.Count; i++)
                    statePolicies[i] = Sanitize(statePolicies[i]);
            }

            if (emotionModifiers != null)
            {
                for (int i = 0; i < emotionModifiers.Count; i++)
                    emotionModifiers[i] = Sanitize(emotionModifiers[i]);
            }
        }

        /// <summary>Creates the runtime default profile (never saved as an asset).</summary>
        public static ConvaiBodyLanguageProfile CreateDefault()
        {
            ConvaiBodyLanguageProfile instance = CreateInstance<ConvaiBodyLanguageProfile>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        /// <summary>
        ///     Shipped defaults: Idle keeps ambient drift and low fidgets; Listening
        ///     leans in and stills; Speaking gesticulates with speech-coupled breath; Thinking
        ///     closes slightly with irregular breath; Interrupted freezes; Settling relaxes.
        /// </summary>
        private static List<BodyLanguageStatePolicy> BuildDefaultStatePolicies() => new()
        {
            new BodyLanguageStatePolicy { State = DialogueState.Idle,        GesticulationEnabled = false, GesticulationIntensity = 0f,    ListeningPostureEnabled = false, ListeningLeanIn = 0f,   PostureOpennessBias = 0f,     SagittalLeanBias = 0f,     AmbientDrift = 0.9f, BreathRateCpm = 13f,   BreathDepth = 0.5f,  BreathIrregularity = 0.1f,  FidgetsEnabled = true,  FidgetRate = 0.25f },
            new BodyLanguageStatePolicy { State = DialogueState.Attending,   GesticulationEnabled = true,  GesticulationIntensity = 0.25f, ListeningPostureEnabled = false, ListeningLeanIn = 0f,   PostureOpennessBias = 0.1f,   SagittalLeanBias = 0.3f,   AmbientDrift = 0.7f, BreathRateCpm = 14.5f, BreathDepth = 0.55f, BreathIrregularity = 0.1f,  FidgetsEnabled = false, FidgetRate = 0f },
            new BodyLanguageStatePolicy { State = DialogueState.Listening,   GesticulationEnabled = false, GesticulationIntensity = 0f,    ListeningPostureEnabled = true,  ListeningLeanIn = 0.6f, PostureOpennessBias = 0.05f,  SagittalLeanBias = 0.35f,  AmbientDrift = 0.55f, BreathRateCpm = 11f,   BreathDepth = 0.45f, BreathIrregularity = 0.05f, FidgetsEnabled = true,  FidgetRate = 0.05f },
            new BodyLanguageStatePolicy { State = DialogueState.Thinking,    GesticulationEnabled = true,  GesticulationIntensity = 0.15f, ListeningPostureEnabled = false, ListeningLeanIn = 0f,   PostureOpennessBias = -0.1f,  SagittalLeanBias = -0.05f, AmbientDrift = 0.5f,  BreathRateCpm = 12f,   BreathDepth = 0.5f,  BreathIrregularity = 0.5f,  FidgetsEnabled = true,  FidgetRate = 0.4f },
            new BodyLanguageStatePolicy { State = DialogueState.Speaking,    GesticulationEnabled = true,  GesticulationIntensity = 0.8f,  ListeningPostureEnabled = false, ListeningLeanIn = 0f,   PostureOpennessBias = 0.38f,  SagittalLeanBias = 0.26f,  AmbientDrift = 0.7f,  BreathRateCpm = 14f,   BreathDepth = 0.6f,  BreathIrregularity = 0.2f,  FidgetsEnabled = false, FidgetRate = 0f },
            new BodyLanguageStatePolicy { State = DialogueState.Reacting,    GesticulationEnabled = true,  GesticulationIntensity = 1f,    ListeningPostureEnabled = false, ListeningLeanIn = 0f,   PostureOpennessBias = 0f,     SagittalLeanBias = 0f,     AmbientDrift = 0.3f, BreathRateCpm = 14f,   BreathDepth = 0.6f,  BreathIrregularity = 0.3f,  FidgetsEnabled = false, FidgetRate = 0f },
            new BodyLanguageStatePolicy { State = DialogueState.Interrupted, GesticulationEnabled = false, GesticulationIntensity = 0f,    ListeningPostureEnabled = false, ListeningLeanIn = 0f,   PostureOpennessBias = 0f,     SagittalLeanBias = 0f,     AmbientDrift = 0f,    BreathRateCpm = 15f,   BreathDepth = 0.5f,  BreathIrregularity = 0.4f,  FidgetsEnabled = false, FidgetRate = 0f },
            new BodyLanguageStatePolicy { State = DialogueState.Settling,    GesticulationEnabled = false, GesticulationIntensity = 0f,    ListeningPostureEnabled = false, ListeningLeanIn = 0f,   PostureOpennessBias = 0f,     SagittalLeanBias = -0.05f, AmbientDrift = 0.9f, BreathRateCpm = 13f,   BreathDepth = 0.5f,  BreathIrregularity = 0.15f, FidgetsEnabled = true,  FidgetRate = 0.15f },
        };

        /// <summary>
        ///     Hand-tuned big-six rows plus neutral: joy opens and lifts, sadness
        ///     rounds and slows, anger leans in and tenses, fear retracts with shallow fast
        ///     breath, surprise opens sharply, disgust recoils.
        /// </summary>
        private static List<BodyLanguageEmotionModifier> BuildDefaultEmotionModifiers() => new()
        {
            new BodyLanguageEmotionModifier { EmotionLabel = "neutral",  OpennessBias = 0f,    LeanBias = 0f,    ShoulderTensionBias = 0f,   GestureIntensityScale = 1f,    GestureRateScale = 1f,    BreathRateScale = 1f,    BreathDepthScale = 1f },
            new BodyLanguageEmotionModifier { EmotionLabel = "joy",      OpennessBias = 0.4f,  LeanBias = 0.1f,  ShoulderTensionBias = -0.3f, GestureIntensityScale = 1.25f, GestureRateScale = 1.15f, BreathRateScale = 1.05f, BreathDepthScale = 1.1f },
            new BodyLanguageEmotionModifier { EmotionLabel = "sadness",  OpennessBias = -0.5f, LeanBias = -0.2f, ShoulderTensionBias = -0.4f, GestureIntensityScale = 0.6f,  GestureRateScale = 0.7f,  BreathRateScale = 0.85f, BreathDepthScale = 0.8f },
            new BodyLanguageEmotionModifier { EmotionLabel = "anger",    OpennessBias = 0.1f,  LeanBias = 0.5f,  ShoulderTensionBias = 0.6f,  GestureIntensityScale = 1.4f,  GestureRateScale = 1.25f, BreathRateScale = 1.15f, BreathDepthScale = 1.1f },
            new BodyLanguageEmotionModifier { EmotionLabel = "fear",     OpennessBias = -0.4f, LeanBias = -0.3f, ShoulderTensionBias = 0.5f,  GestureIntensityScale = 0.8f,  GestureRateScale = 1.1f,  BreathRateScale = 1.3f,  BreathDepthScale = 0.7f },
            new BodyLanguageEmotionModifier { EmotionLabel = "surprise", OpennessBias = 0.5f,  LeanBias = -0.1f, ShoulderTensionBias = 0.4f,  GestureIntensityScale = 1.1f,  GestureRateScale = 1f,    BreathRateScale = 1.2f,  BreathDepthScale = 1.2f },
            new BodyLanguageEmotionModifier { EmotionLabel = "disgust",  OpennessBias = -0.2f, LeanBias = -0.4f, ShoulderTensionBias = 0.3f,  GestureIntensityScale = 0.9f,  GestureRateScale = 0.9f,  BreathRateScale = 1f,    BreathDepthScale = 0.9f },
        };
    }
}
