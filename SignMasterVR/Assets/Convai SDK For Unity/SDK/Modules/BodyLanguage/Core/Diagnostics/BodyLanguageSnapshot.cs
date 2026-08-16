using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyLanguage.Core.Gestures;
using Convai.Modules.BodyLanguage.Data;

namespace Convai.Modules.BodyLanguage.Core.Diagnostics
{
    /// <summary>
    ///     Mutable, reusable capture of the body language runtime state for HUDs and tests.
    ///     Allocate once and refill via <c>ConvaiBodyLanguageController.CaptureSnapshot</c>.
    ///     Later phases extend this with live posture, breath, and gesture values.
    /// </summary>
    public sealed class BodyLanguageSnapshot
    {
        /// <summary>Dialogue state the policy engine acted on this frame.</summary>
        public DialogueState DialogueState;

        /// <summary>The smoothed policy currently in effect (scalar values mid-blend).</summary>
        public BodyLanguageStatePolicy ActivePolicy;

        /// <summary>The authored policy the engine is blending toward.</summary>
        public BodyLanguageStatePolicy TargetPolicy;

        /// <summary>Whether the module is inert (unusable rig — one error logged, no per-tick work).</summary>
        public bool IsInert;

        /// <summary>Whether the rig resolved a Spine bone (required).</summary>
        public bool HasSpine;

        /// <summary>Whether the rig resolved a Chest bone (optional).</summary>
        public bool HasChest;

        /// <summary>Whether the rig resolved an UpperChest bone (optional).</summary>
        public bool HasUpperChest;

        /// <summary>Whether the rig resolved both shoulder bones (optional).</summary>
        public bool HasShoulders;

        public bool HasProceduralArmChain;

        public bool HasProceduralFingerChain;

        /// <summary>Name of the effective profile at capture time.</summary>
        public string ProfileName = "-";

        /// <summary>Posture target this tick: openness, sagittal lean, shoulder tension (director output, -1..1).</summary>
        public float PostureOpennessTarget;

        /// <summary>See <see cref="PostureOpennessTarget" />.</summary>
        public float PostureLeanTarget;

        /// <summary>See <see cref="PostureOpennessTarget" />.</summary>
        public float PostureTensionTarget;

        /// <summary>Posture solver's current (spring-settled) values this tick, -1..1.</summary>
        public float PostureOpennessCurrent;

        /// <summary>See <see cref="PostureOpennessCurrent" />.</summary>
        public float PostureLeanCurrent;

        /// <summary>See <see cref="PostureOpennessCurrent" />.</summary>
        public float PostureTensionCurrent;

        /// <summary>Posture/breath master weight this tick (0 = no bone writes at all).</summary>
        public float MasterWeight;

        /// <summary>Breath oscillator phase this tick, radians in <c>[0, 2π)</c>.</summary>
        public float BreathPhase;

        /// <summary>Breath waveform value this tick, -1 (full exhale) .. 1 (full inhale peak), pre-depth-scale.</summary>
        public float BreathWaveform;

        /// <summary>Breathing rate target this tick, cycles per minute.</summary>
        public float BreathRateCpm;

        /// <summary>Breathing depth target this tick, 0..1.</summary>
        public float BreathDepth;

        /// <summary>
        ///     Current estimate of the animated pose's own baked slow torso oscillation
        ///     amplitude this tick, in degrees (adaptive layering).
        /// </summary>
        public float BreathBakedAmplitudeDegrees;

        /// <summary>
        ///     Procedural breath depth duck multiplier this tick, 1 = no duck (adaptive
        ///     layering); only applied to the solved breath depth when the profile's
        ///     <c>EnableBreathAdaptiveLayering</c> toggle is on.
        /// </summary>
        public float BreathDuckFactor;

        /// <summary>Whether a scripted head-gesture program (Nod/Shake/Tilt) is currently playing.</summary>
        public bool HeadGestureIsPlaying;

        /// <summary>The kind of the currently playing head-gesture program (only meaningful when <see cref="HeadGestureIsPlaying" />). Internal type — diagnostics/tests only.</summary>
        internal HeadGestureKind HeadGestureActiveKind;

        /// <summary>Normalized progress 0..1 of the currently playing head-gesture program.</summary>
        public float HeadGestureProgress;

        /// <summary>Number of consumers currently registered on the head-gesture channel.</summary>
        public int HeadGestureConsumerCount;

        /// <summary>
        ///     Whether the controller is self-actuating the head-gesture offset directly this
        ///     tick because no consumer is registered on the channel (fallback).
        /// </summary>
        public bool HeadGestureFallbackActive;

        /// <summary>
        ///     Posture-only suppression factor this tick, 0..1 — reduced under UpperBody
        ///     suppression while breath keeps the full <see cref="MasterWeight" />
        ///     ("posture at reduced weight, breath stays").
        /// </summary>
        public float PostureSuppressionWeight = 1f;

        /// <summary>Current suppression reported by the conversational gesture performer (<see cref="GestureSuppression.None" /> when absent).</summary>
        public GestureSuppression GesticulationSuppression;

        /// <summary>The last semantic cue kind attempted via the internal <c>TryEmitCue</c> entry point.</summary>
        public GestureCueKind LastGestureCueKind;

        /// <summary>Whether the last semantic cue attempt was accepted by the conversational gesture performer.</summary>
        public bool LastGestureCueAccepted;

        /// <summary>Whether a rejected semantic cue is currently using procedural arm/hand fallback.</summary>
        public bool ProceduralGestureFallbackActive;

        /// <summary>Whether the fast channel is currently running on the no-provider statistical cadence fallback.</summary>
        public bool GesticulationStatisticalCadenceActive;

        /// <summary>Current posture-pulse envelope value (diagnostics; 0 at rest).</summary>
        public float GesticulationPosturePulseValue;

        /// <summary>
        ///     This tick's combined lateral weight-shift target (fidget program + Thinking
        ///     asymmetry bias, clamped -1..1) fed into the posture solve. Positive shifts
        ///     weight/side-bends toward the character's own right.
        /// </summary>
        public float PostureLateralShiftTarget;

        /// <summary>Posture solver's current (spring-settled) lateral weight-shift value this tick, -1..1.</summary>
        public float PostureLateralShiftCurrent;

        /// <summary>Fidget director's current weight-shift program value this tick, -1..1 (before the Thinking asymmetry bias is added).</summary>
        public float FidgetWeightShift;

        /// <summary>Listening-posture director's slewed lean-in bias this tick, 0..1.</summary>
        public float ListeningLeanIn;

        /// <summary>Listening-posture director's slewed stillness factor this tick, 0..1 (damps fidget amplitude).</summary>
        public float ListeningStillnessFactor;

        /// <summary>Whether the listening-posture director requested a tilt-hold head gesture this tick.</summary>
        public bool ListeningWantsTiltHold;

        /// <summary>Stance director's slewed pelvis lateral weight-shift value this tick, -1..1, +right.</summary>
        public float StanceLateral;

        /// <summary>Whether the pelvis is actively transferring toward a new stance target this tick.</summary>
        public bool StanceIsShifting;

        /// <summary>Postural sway director's combined sagittal sway sample this tick, -1..1.</summary>
        public float SwaySagittal;

        /// <summary>Postural sway director's combined lateral sway sample this tick, -1..1.</summary>
        public float SwayLateral;

        /// <summary>Whether leg compensation (two-bone leg IK re-pinning the feet) is active this tick.</summary>
        public bool LegCompensationActive;

        /// <summary>
        ///     Whether a <see cref="IConversationalMotionBudget" /> is registered this tick
        ///     — when <c>true</c>, suppression duck logic uses the budget's continuous
        ///     occupancy/hard-suppression negotiation instead of the older binary contract.
        /// </summary>
        public bool UsingMotionBudget;

        /// <summary>
        ///     The registered motion budget's <see cref="IConversationalMotionBudget.UpperBodyOccupancy01" />
        ///     this tick (0 when <see cref="UsingMotionBudget" /> is <c>false</c>).
        /// </summary>
        public float UpperBodyOccupancy;

        /// <summary>The reaction currently playing (<see cref="ReactionKind.None" /> when idle) — diagnostics. Internal type — diagnostics/tests only.</summary>
        internal ReactionKind ActiveReactionKind;

        /// <summary>Current flinch envelope value this tick, 0..1.</summary>
        public float ReactionFlinch;

        /// <summary>Current bounce envelope value this tick, signed roughly ±1.</summary>
        public float ReactionBounce;

        /// <summary>This tick's effective expressiveness 0..1 — override, else the profile's resolved value.</summary>
        public float Expressiveness = 0.5f;

        /// <summary>This tick's amplitude gain derived from <see cref="Expressiveness" />; 1 at Natural.</summary>
        public float AmplitudeGain = 1f;

        /// <summary>This tick's frequency gain derived from <see cref="Expressiveness" />; 1 at Natural.</summary>
        public float FrequencyGain = 1f;

        /// <summary>This tick's richness gain derived from <see cref="Expressiveness" />; 1 at Natural.</summary>
        public float RichnessGain = 1f;

        /// <summary>
        ///     Documented approximation (meters) of the spine-base→sternum lever arm this
        ///     character resolved at bind time (motion meter); 0 when unbound. See
        ///     <c>ProceduralPoseCompositor.SternumLeverMeters</c>.
        /// </summary>
        public float SternumLeverMeters;

        /// <summary>The spine chain's combined sagittal swing (degrees) actually written on the last apply (motion meter).</summary>
        public float AppliedSpineSagittalDegrees;

        /// <summary>Combined lateral counterpart of <see cref="AppliedSpineSagittalDegrees" />.</summary>
        public float AppliedSpineLateralDegrees;

        /// <summary>This tick's breath-only chest sagittal swing (degrees), pre-composition with posture/sway/reactions (motion meter).</summary>
        public float BreathAppliedSagittalDegrees;

        /// <summary>This tick's applied pelvis lateral weight-shift travel (centimeters), post-expressiveness-gain and master weight (motion meter).</summary>
        public float StanceLateralCentimeters;

        /// <summary>This tick's applied pelvis obliquity (hip-hike, degrees). See <see cref="StanceLateralCentimeters" />.</summary>
        public float StanceObliquityDegrees;

        /// <summary>
        ///     The idle macro-cycle director's current combined drift this tick, -1..1
        ///     — 0 when <c>EnableIdleMacroCycles</c> is off (the enable envelope has
        ///     settled to zero). Feeds small multipliers on breath depth, sway amplitude, and
        ///     fidget cadence; diagnostics only.
        /// </summary>
        public float MacroCycleEnergy;

        /// <summary>
        ///     This tick's blended physiological arousal, 0..1 — 0.5 is neutral.
        ///     Modulates sway amplitude and fidget cadence; does not modulate breath
        ///     rate (already emotion-scaled elsewhere) or the public <c>BodyLanguageReading</c>.
        /// </summary>
        public float ArousalLevel = 0.5f;

        /// <summary>
        ///     This tick's applied camera-distance amplitude LOD scale — 1 = no-op.
        ///     Multiplies sway amplitude and hand-micro weight only.
        /// </summary>
        public float CameraLodScale = 1f;

        /// <summary>Recent trace log copied from the ring buffer (oldest first).</summary>
        public readonly List<BodyLanguageTraceEntry> RecentTrace = new(64);

        /// <summary>Resets all fields to their defaults.</summary>
        public void Clear()
        {
            DialogueState = DialogueState.Idle;
            ActivePolicy = default;
            TargetPolicy = default;
            IsInert = false;
            HasSpine = false;
            HasChest = false;
            HasUpperChest = false;
            HasShoulders = false;
            HasProceduralArmChain = false;
            HasProceduralFingerChain = false;
            ProfileName = "-";
            PostureOpennessTarget = 0f;
            PostureLeanTarget = 0f;
            PostureTensionTarget = 0f;
            PostureOpennessCurrent = 0f;
            PostureLeanCurrent = 0f;
            PostureTensionCurrent = 0f;
            MasterWeight = 0f;
            BreathPhase = 0f;
            BreathWaveform = 0f;
            BreathRateCpm = 0f;
            BreathDepth = 0f;
            BreathBakedAmplitudeDegrees = 0f;
            BreathDuckFactor = 1f;
            HeadGestureIsPlaying = false;
            HeadGestureActiveKind = default;
            HeadGestureProgress = 0f;
            HeadGestureConsumerCount = 0;
            HeadGestureFallbackActive = false;
            PostureSuppressionWeight = 1f;
            GesticulationSuppression = GestureSuppression.None;
            LastGestureCueKind = GestureCueKind.None;
            LastGestureCueAccepted = false;
            ProceduralGestureFallbackActive = false;
            GesticulationStatisticalCadenceActive = false;
            GesticulationPosturePulseValue = 0f;
            PostureLateralShiftTarget = 0f;
            PostureLateralShiftCurrent = 0f;
            FidgetWeightShift = 0f;
            ListeningLeanIn = 0f;
            ListeningStillnessFactor = 0f;
            ListeningWantsTiltHold = false;
            StanceLateral = 0f;
            StanceIsShifting = false;
            SwaySagittal = 0f;
            SwayLateral = 0f;
            LegCompensationActive = false;
            UsingMotionBudget = false;
            UpperBodyOccupancy = 0f;
            ActiveReactionKind = default;
            ReactionFlinch = 0f;
            ReactionBounce = 0f;
            Expressiveness = 0.5f;
            AmplitudeGain = 1f;
            FrequencyGain = 1f;
            RichnessGain = 1f;
            SternumLeverMeters = 0f;
            AppliedSpineSagittalDegrees = 0f;
            AppliedSpineLateralDegrees = 0f;
            BreathAppliedSagittalDegrees = 0f;
            StanceLateralCentimeters = 0f;
            StanceObliquityDegrees = 0f;
            MacroCycleEnergy = 0f;
            ArousalLevel = 0.5f;
            CameraLodScale = 1f;
            RecentTrace.Clear();
        }
    }
}
