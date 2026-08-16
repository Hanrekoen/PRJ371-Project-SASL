using System.Collections.Generic;
using Convai.Domain.Embodiment.Semantics;
using Convai.Domain.Embodiment.Readings;
using Convai.Modules.BodyAnimation.Core.Diagnostics;

namespace Convai.Modules.BodyAnimation
{
    /// <summary>Per-layer state exposed for HUDs and tests.</summary>
    public struct BodyAnimationLayerSnapshot
    {
        public string Name;
        public string State;
        public string Clip;
        public float Weight;
        public float DesiredWeight;
        public float EnvelopeWeight;
        public float ArbiterTargetWeight;
        public float FinalWeight;
        public string Owner;
        public string Mask;
        public bool Additive;
        public float NormalizedTime;
    }

    /// <summary>
    ///     Complete, allocation-friendly view of the body animation system for one frame:
    ///     layer weights and states, dialogue/locomotion inputs, and the recent transition
    ///     trace. Fill it via <c>ConvaiBodyAnimationController.CaptureSnapshot</c> — the HUD,
    ///     tests, and bug reports all read this single structure.
    /// </summary>
    public sealed class BodyAnimationSnapshot
    {
        public string Owner = string.Empty;
        public string SetName = string.Empty;

        public DialogueState DialogueState;
        public float SpeechEnergy;
        public CoSpeechQualityTier CoSpeechQuality;
        public CoSpeechPhrasePhase CoSpeechPhase;
        public int CoSpeechGeneration;
        public int CoSpeechGestureSequence;
        public string CoSpeechGesture = string.Empty;

        /// <summary>NavMeshAgent speed (m/s). 0 until the locomotion component is present.</summary>
        public float AgentSpeed;

        /// <summary>Effective animation cycle speed (m/s) after rate warping (foot-slide check).</summary>
        public float AnimationSpeed;

        /// <summary>Locomotion state machine label ("Idle", "Move", "Stop:LF", …).</summary>
        public string LocomotionState = string.Empty;
        public float DesiredSpeed;
        public float RemainingDistance;
        public float MotionPreviousNormalizedTime;
        public float MotionCurrentNormalizedTime;
        public float RateWarp;
        public float SharedGaitPhase;
        public int GraphPlayableCount;
        public float AppliedTurnYaw;
        public float ExpectedTurnYaw;
        public float HandoffMarker;
        public float StopDistanceError;

        public readonly List<BodyAnimationLayerSnapshot> Layers = new(6);
        public readonly List<AnimTraceEntry> RecentTrace = new(64);

        public void Clear()
        {
            Owner = string.Empty;
            SetName = string.Empty;
            DialogueState = DialogueState.Idle;
            SpeechEnergy = 0f;
            CoSpeechQuality = CoSpeechQualityTier.EnergyOnly;
            CoSpeechPhase = CoSpeechPhrasePhase.None;
            CoSpeechGeneration = 0;
            CoSpeechGestureSequence = 0;
            CoSpeechGesture = string.Empty;
            AgentSpeed = 0f;
            AnimationSpeed = 0f;
            LocomotionState = string.Empty;
            DesiredSpeed = 0f;
            RemainingDistance = 0f;
            MotionPreviousNormalizedTime = 0f;
            MotionCurrentNormalizedTime = 0f;
            RateWarp = 1f;
            SharedGaitPhase = 0f;
            GraphPlayableCount = 0;
            AppliedTurnYaw = 0f;
            ExpectedTurnYaw = 0f;
            HandoffMarker = 0f;
            StopDistanceError = 0f;
            Layers.Clear();
            RecentTrace.Clear();
        }
    }
}
