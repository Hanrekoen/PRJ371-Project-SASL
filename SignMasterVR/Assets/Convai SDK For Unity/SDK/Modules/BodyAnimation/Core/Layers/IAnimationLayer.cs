using System;
using Convai.Domain.Embodiment.Readings;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using Convai.Modules.BodyAnimation.Core.Graph;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Core.Layers
{
    /// <summary>Stable port assignments on the root layer mixer.</summary>
    internal static class LayerPorts
    {
        public const int Locomotion = 0;
        public const int Talk = 1;
        public const int Action = 2;
        public const int Pointing = 3;

        /// <summary>
        ///     Additive walk-and-talk overlay (arms + hands), owned by the talk layer.
        ///     A separate port so moving speech never re-masks or re-modes a live port:
        ///     stationary ↔ moving is a weight crossfade between two pre-configured ports.
        /// </summary>
        public const int TalkMoving = 4;

        /// <summary>
        ///     Additive speech-rhythm beat overlay (arms + hands), owned by the talk layer
        ///    . A short one-shot gesture rides on top of whichever talk port is
        ///     currently visible instead of ever replacing it — always its own port so a beat
        ///     never re-masks or re-modes a live port.
        /// </summary>
        public const int TalkBeat = 5;

        public const int Count = 6;
    }

    /// <summary>Per-frame inputs shared by every layer.</summary>
    internal readonly struct LayerTickContext
    {
        public readonly float DeltaTime;
        public readonly DialogueState DialogueState;
        public readonly EmotionReading Emotion;
        public readonly float SpeechEnergy;    // [0..1], meaningful only when HasSpeechEnergy
        public readonly bool HasSpeechEnergy;  // a provider is registered
        public readonly bool IsMoving;         // locomotion is displacing the character

        /// <summary>
        ///     Conversational-motion-budget peer's reported intensity scale, 1 =
        ///     neutral. <see cref="TalkLayer" /> smooths its own envelope weight toward this so
        ///     an emotion-derived report visibly scales the talk overlay. Fed by the controller
        ///     from <c>ConversationalGesturePerformer.ReportedIntensityScale</c>; defaults to 1f
        ///     at every other (test) construction site.
        /// </summary>
        public readonly float ConversationalIntensityScale;

        /// <summary>
        ///     True while a peer layer (an action playing on the shared upper-body override
        ///     port, or an active pointing hold) already owns the arms this tick.
        ///     Computed by the controller from <c>ActionLayer.IsActive</c>/<c>PointingLayer.IsActive</c>
        ///     before the layer loop runs (one-tick-stale, same as <see cref="IsMoving" />).
        ///     <see cref="TalkLayer" />'s beat detector refuses a new onset while this is true
        ///     so a beat gesture never fights a real action or pointing hold for the arms.
        /// </summary>
        public readonly bool BeatSuppressedByPeers;

        /// <summary>
        ///     True while the controller's single per-tick <c>ConversationAnchorResolver</c>
        ///     resolved a conversation partner position this tick. Replaces three
        ///     independent <c>Camera.main</c> reads (social spacing, proximity expressiveness,
        ///     ambient suppression) with one resolution published here.
        /// </summary>
        public readonly bool HasConversationAnchor;

        /// <summary>World position of the resolved conversation anchor; meaningful only when
        /// <see cref="HasConversationAnchor" /> is true.</summary>
        public readonly UnityEngine.Vector3 ConversationAnchor;

        public LayerTickContext(
            float deltaTime,
            DialogueState dialogueState,
            in EmotionReading emotion,
            float speechEnergy,
            bool hasSpeechEnergy,
            bool isMoving,
            float conversationalIntensityScale = 1f,
            bool beatSuppressedByPeers = false,
            bool hasConversationAnchor = false,
            UnityEngine.Vector3 conversationAnchor = default)
        {
            DeltaTime = deltaTime;
            DialogueState = dialogueState;
            Emotion = emotion;
            SpeechEnergy = speechEnergy;
            HasSpeechEnergy = hasSpeechEnergy;
            IsMoving = isMoving;
            ConversationalIntensityScale = conversationalIntensityScale;
            BeatSuppressedByPeers = beatSuppressedByPeers;
            HasConversationAnchor = hasConversationAnchor;
            ConversationAnchor = conversationAnchor;
        }
    }

    /// <summary>
    ///     Shared services handed to every layer at build time: the graph, the root mixer,
    ///     content, tuning, diagnostics, and the transition-report hook that feeds both the
    ///     trace log and the controller's public <c>StateChanged</c> event.
    /// </summary>
    internal sealed class LayerRuntime
    {
        public PlayableGraph Graph;
        public LayerMixerHost Mixer;
        public ConvaiBodyAnimationSet Set;
        public ConvaiBodyAnimationConfig Config;
        public AnimTrace Trace;
        public int RandomSeed;
        public Action<AnimStateChange> StateChanged;

        /// <summary>
        ///     Multiplies every authored (clip-measured) distance and speed read by the
        ///     locomotion layer so a character built at a different scale than the sample rig
        ///     the content was analyzed on still lands its stops and covers the right ground.
        ///     1 = no correction (identical to an uncalibrated rig); resolved once per build by
        ///     <c>ConvaiBodyAnimationController.ResolveMotionScale</c>. Never touches yaw,
        ///     normalized time, or user-authored world quantities (config speeds, action
        ///     anchor tolerances) — only clip-measured metres.
        /// </summary>
        public float MotionScale = 1f;

        /// <summary>Character transform (root the locomotion rotates).</summary>
        public UnityEngine.Transform CharacterRoot;

        /// <summary>Animator the graph outputs to; bone queries (e.g. chest height). Null in tests.</summary>
        public UnityEngine.Animator Animator;

        /// <summary>
        ///     NavMesh movement authority (null when the character has no locomotion).
        ///     Stubbed in tests.
        /// </summary>
        public Core.Locomotion.ILocomotionDrive Locomotion;

        /// <summary>
        ///     True while the locomotion layer built against this runtime is the one allowed to
        ///     mutate the shared <see cref="Locomotion" /> drive (<c>EndManagedMotion</c>,
        ///     <c>FreezeAgent</c>, <c>RotationDrivenExternally</c>). During a set-swap handoff the
        ///     retiring runtime is flipped to false before the new runtime is created, so the
        ///     retiring <see cref="LocomotionLayer" />'s <c>Teardown</c> — which runs later, once
        ///     the handoff crossfade completes — can never unfreeze or stop a drive the live,
        ///     already-swapped-in layer is mid-turn on. Defaults to true: a runtime that
        ///     is never part of a handoff behaves exactly as before.
        /// </summary>
        public bool OwnsLocomotionDrive = true;

        /// <summary>Reports a transition to the trace log and the public event, identically.</summary>
        public void ReportTransition(
            string layer, string from, string to, string clip, float fadeSeconds, string reason)
        {
            var change = new AnimStateChange(layer, from, to, clip, fadeSeconds, reason);
            // The ToString is the expensive part of a transition report — skip building it
            // when the verbosity gate would drop the line anyway.
            if (Trace != null && Trace.Verbosity >= AnimTraceVerbosity.State)
                Trace.State(change.ToString());
            StateChanged?.Invoke(change);
        }
    }

    /// <summary>
    ///     One masked slice of the body animation stack. Layers own their sub-playables,
    ///     connect themselves to their mixer port during <see cref="Initialize" />, and
    ///     expose the weight the controller writes to the root mixer every tick.
    /// </summary>
    internal interface IAnimationLayer
    {
        string Name { get; }

        /// <summary>Weight the controller writes to this layer's mixer port each tick.</summary>
        float Weight { get; }

        /// <summary>Current state label for HUD/snapshot (e.g. "Idle", "Move", "Holding").</summary>
        string StateLabel { get; }

        /// <summary>Clip currently dominating the layer, for HUD/snapshot.</summary>
        string ActiveClipName { get; }

        /// <summary>Normalized time of the active clip, for HUD/snapshot.</summary>
        float ActiveNormalizedTime { get; }

        /// <summary>Builds sub-playables and connects them to the given root mixer port.</summary>
        void Initialize(LayerRuntime runtime, int port);

        void Tick(in LayerTickContext context);

        /// <summary>Releases layer state. Playables die with the graph; clear references here.</summary>
        void Teardown();
    }
}
