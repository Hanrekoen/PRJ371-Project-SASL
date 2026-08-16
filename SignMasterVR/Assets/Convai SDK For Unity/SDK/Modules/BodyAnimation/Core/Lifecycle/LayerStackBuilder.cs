using System;
using System.Collections.Generic;
using Convai.Domain.Embodiment.Interfaces;
using Convai.Modules.BodyAnimation.Core.Diagnostics;
using Convai.Modules.BodyAnimation.Core.Graph;
using Convai.Modules.BodyAnimation.Core.Layers;
using Convai.Modules.BodyAnimation.Core.Locomotion;
using Convai.Modules.BodyAnimation.Core.Performance;
using Convai.Modules.BodyAnimation.Core.Policy;
using Convai.Modules.BodyAnimation.Data;
using UnityEngine;
using UnityEngine.Playables;

namespace Convai.Modules.BodyAnimation.Core.Lifecycle
{
    /// <summary>
    ///     The one place that builds a full masked layer stack — the mixer host, the
    ///     <see cref="LayerRuntime" />, the four layers, the gesture performer, the referential-
    ///     gesture director and the ambient-activity director — shared by
    ///     <c>ConvaiBodyAnimationController.BuildRuntime</c> (the first build) and
    ///     <c>TryBeginSetHandoff</c> (a live set swap).
    /// </summary>
    /// <remarks>
    ///     Before this existed the two call sites had already drifted: the handoff
    ///     path did not re-create the social-spacing policy, and separately did not re-push the
    ///     new config's walk/jog speeds to the locomotion drive. Routing both callers through this
    ///     one builder removes the social-spacing half of that class of bug by construction — see
    ///     <see cref="Args.SocialSpacing" /> — rather than by remembering to keep two call sites in
    ///     sync. The locomotion-speed half is a one-line fix at the handoff call site (locomotion
    ///     is not re-resolved on a handoff, only reconfigured, so it is not part of "building the
    ///     stack").
    ///     <para>
    ///     Deliberately NOT unified here — because it legitimately differs between a first build
    ///     and a handoff, not because it was missed: gesture-performer registration timing, the
    ///     root <c>Play</c>/<c>BeginRootHandoff</c> call, and the initial-tick priming (a full
    ///     zero-delta <c>EmbodimentTick</c> for a first build vs. a layers-only prime for a
    ///     handoff — see the controller's own comments at each call site).
    ///     </para>
    ///     <para>
    ///     The co-speech planner used to be listed here too, as an asymmetry this phase preserved
    ///     rather than fixed: only the handoff path created one, so a first build never published
    ///     <c>ICoSpeechPerformanceSource</c>. That was a defect, not a difference, and it is fixed —
    ///     both paths now call the controller's <c>RebuildCoSpeechPlanner</c>. It stays on the
    ///     controller rather than moving in here because the planner is a Context registration with
    ///     a token to withdraw, which is lifecycle, not layer-stack construction.
    ///     </para>
    /// </remarks>
    internal static class LayerStackBuilder
    {
        internal readonly struct Args
        {
            public readonly PlayableGraph Graph;
            public readonly ConvaiBodyAnimationSet Set;
            public readonly ConvaiBodyAnimationConfig Config;
            public readonly AnimTrace Trace;
            public readonly int RandomSeed;
            public readonly float MotionScale;
            public readonly Transform CharacterRoot;
            public readonly Animator Animator;
            public readonly ILocomotionDrive Locomotion;
            public readonly Action<AnimStateChange> OnStateChanged;
            public readonly Action<BodyAnimationActionEvent> OnActionEvent;
            public readonly Action<GestureCueKind, bool> OnGestureResolved;

            /// <summary>
            ///     Rebuilt here, identically, on every call. The bug this class exists to
            ///     remove was the handoff path skipping this exact reconstruction — see
            ///     <see cref="SocialSpacingRunner.Rebuild" />.
            /// </summary>
            public readonly SocialSpacingRunner SocialSpacing;

            public Args(
                PlayableGraph graph,
                ConvaiBodyAnimationSet set,
                ConvaiBodyAnimationConfig config,
                AnimTrace trace,
                int randomSeed,
                float motionScale,
                Transform characterRoot,
                Animator animator,
                ILocomotionDrive locomotion,
                Action<AnimStateChange> onStateChanged,
                Action<BodyAnimationActionEvent> onActionEvent,
                Action<GestureCueKind, bool> onGestureResolved,
                SocialSpacingRunner socialSpacing)
            {
                Graph = graph;
                Set = set;
                Config = config;
                Trace = trace;
                RandomSeed = randomSeed;
                MotionScale = motionScale;
                CharacterRoot = characterRoot;
                Animator = animator;
                Locomotion = locomotion;
                OnStateChanged = onStateChanged;
                OnActionEvent = onActionEvent;
                OnGestureResolved = onGestureResolved;
                SocialSpacing = socialSpacing;
            }
        }

        internal sealed class Result
        {
            public LayerMixerHost Mixer;
            public LayerRuntime LayerRuntime;
            public LocomotionLayer LocomotionLayer;
            public TalkLayer TalkLayer;
            public ActionLayer ActionLayer;
            public PointingLayer PointingLayer;
            public ConversationalGesturePerformer GesturePerformer;
            public ReferentialGestureDirector ReferentialDirector;
            public AmbientActivityDirector AmbientDirector;
        }

        /// <summary>
        ///     Builds the stack and populates <paramref name="layers" /> (cleared first, then the
        ///     four layers added in port order) — the caller's own list is reused rather than a
        ///     new one allocated, matching the the previous behaviour exactly.
        /// </summary>
        internal static Result Build(in Args args, List<IAnimationLayer> layers)
        {
            var mixer = new LayerMixerHost(args.Graph, LayerPorts.Count);
            var layerRuntime = new LayerRuntime
            {
                Graph = args.Graph,
                Mixer = mixer,
                Set = args.Set,
                Config = args.Config,
                Trace = args.Trace,
                RandomSeed = args.RandomSeed,
                MotionScale = args.MotionScale,
                StateChanged = args.OnStateChanged,
                CharacterRoot = args.CharacterRoot,
                Animator = args.Animator,
                Locomotion = args.Locomotion
            };

            var locomotionLayer = new LocomotionLayer();
            var talkLayer = new TalkLayer();
            var actionLayer = new ActionLayer { LifecycleChanged = args.OnActionEvent };
            var pointingLayer = new PointingLayer();

            layers.Clear();
            layers.Add(locomotionLayer);   // LayerPorts.Locomotion
            layers.Add(talkLayer);         // LayerPorts.Talk
            layers.Add(actionLayer);       // LayerPorts.Action
            layers.Add(pointingLayer);     // LayerPorts.Pointing
            for (int i = 0; i < layers.Count; i++)
                layers[i].Initialize(layerRuntime, i);

            var gesturePerformer = new ConversationalGesturePerformer(args.Set, actionLayer, talkLayer, locomotionLayer);
            var referentialDirector = new ReferentialGestureDirector(args.Config, talkLayer, actionLayer, pointingLayer);
            referentialDirector.GestureResolved += args.OnGestureResolved;
            var ambientDirector = new AmbientActivityDirector(
                args.Config, actionLayer, args.Set, layerRuntime.CharacterRoot, args.Trace,
                unchecked((uint)(args.RandomSeed ^ 0x416D6269))); // "Ambi" salt

            args.SocialSpacing.Rebuild(
                args.Config.ComfortRadius, args.Config.ComfortHoldSeconds, args.Config.MaxRepositionsPerMinute);

            return new Result
            {
                Mixer = mixer,
                LayerRuntime = layerRuntime,
                LocomotionLayer = locomotionLayer,
                TalkLayer = talkLayer,
                ActionLayer = actionLayer,
                PointingLayer = pointingLayer,
                GesturePerformer = gesturePerformer,
                ReferentialDirector = referentialDirector,
                AmbientDirector = ambientDirector
            };
        }
    }
}
